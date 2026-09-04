using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ChurchProjection.Core.Models.Projection;
using ChurchProjection.UI.Services;
using ChurchProjection.UI.Services.Video;
using ReactiveUI;
using Serilog;

namespace ChurchProjection.UI.ViewModels.Operator;

/// <summary>
/// The operator's swappable background palette. Wraps <see cref="ILiveBackgroundService"/> as a
/// bindable list of tiles; clicking a tile swaps the live media layer underneath the text without
/// touching the active theme.
/// </summary>
public sealed class BackgroundsViewModel : ReactiveObject, IDisposable
{
    private readonly ILiveBackgroundService _service;
    private readonly CompositeDisposable _subs = new();
    private bool _hasSelection;
    private bool _themeAcceptsLiveSelection;

    public ObservableCollection<BackgroundTileViewModel> Items { get; } = [];

    public ReactiveCommand<BackgroundTileViewModel, Unit> SelectCommand { get; }
    public ReactiveCommand<BackgroundTileViewModel, Unit> RemoveCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearCommand { get; }

    public BackgroundsViewModel(ILiveBackgroundService service)
    {
        _service = service;

        SelectCommand = ReactiveCommand.Create<BackgroundTileViewModel>(t =>
        {
            if (!_themeAcceptsLiveSelection) return;
            _service.Select(t.Model);
        });
        RemoveCommand = ReactiveCommand.CreateFromTask<BackgroundTileViewModel>(t => _service.RemoveAsync(t.Model));
        ClearCommand = ReactiveCommand.Create(() => _service.Select(null));

        _service.ItemsChanged
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(Rebuild)
            .DisposeWith(_subs);

        _service.SelectedChanged
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(ApplySelectionHighlight)
            .DisposeWith(_subs);
    }

    /// <summary>
    /// Placeholder themes take a live background; solid/image/key themes do not.
    /// When false, clicks are ignored and no tile shows a selection ring.
    /// </summary>
    public bool ThemeAcceptsLiveSelection
    {
        get => _themeAcceptsLiveSelection;
        set
        {
            if (!this.RaiseAndSetIfChanged(ref _themeAcceptsLiveSelection, value))
                return;
            ApplySelectionHighlight(_service.Selected);
        }
    }

    /// <summary>True when a background is live (enables the "None" / clear control).</summary>
    public bool HasSelection
    {
        get => _hasSelection;
        set => this.RaiseAndSetIfChanged(ref _hasSelection, value);
    }

    /// <summary>Adds a media file picked by the view's file dialog.</summary>
    public Task AddAsync(string path) => _service.AddAsync(path);

    private void ApplySelectionHighlight(LiveBackground? sel)
    {
        var id = _themeAcceptsLiveSelection ? sel?.Id : null;
        foreach (var t in Items) t.IsSelected = t.Model.Id == id;
        HasSelection = _themeAcceptsLiveSelection && sel is not null;
    }

    private void Rebuild(IReadOnlyList<LiveBackground> items)
    {
        var selId = _themeAcceptsLiveSelection ? _service.Selected?.Id : null;
        foreach (var t in Items) t.Dispose();
        Items.Clear();
        foreach (var i in items)
            Items.Add(new BackgroundTileViewModel(i) { IsSelected = i.Id == selId });
    }

    public void Dispose()
    {
        foreach (var t in Items) t.Dispose();
        _subs.Dispose();
    }
}

/// <summary>One thumbnail tile in the backgrounds palette.</summary>
public sealed class BackgroundTileViewModel : ReactiveObject, IDisposable
{
    private bool _isSelected;
    private Bitmap? _thumbnail;
    private readonly Bitmap? _imageThumbnail;
    private IVideoFramePlayer? _preview;
    private bool _gotStill;

    public BackgroundTileViewModel(LiveBackground model)
    {
        Model = model;

        if (model.Kind == LiveBackgroundKind.Image && File.Exists(model.Path))
        {
            try
            {
                using var fs = File.OpenRead(model.Path);
                _imageThumbnail = Bitmap.DecodeToWidth(fs, BackgroundTilePreview.MaxWidth);
                Thumbnail = _imageThumbnail;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Could not build thumbnail for {Path}", model.Path);
            }
            return;
        }

        var request = BackgroundTilePreview.RequestFor(model);
        if (request is null || !File.Exists(model.Path))
            return;

        try
        {
            _preview = VideoFramePlayerFactory.Start(request, OnPreviewFrame);
            if (!_preview.IsRunning)
            {
                _preview.Dispose();
                _preview = null;
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not start motion preview for {Path}", model.Path);
            _preview?.Dispose();
            _preview = null;
        }
    }

    public LiveBackground Model { get; }
    public string Name => Model.Name;
    public bool IsVideo => Model.Kind == LiveBackgroundKind.Video;

    public Bitmap? Thumbnail
    {
        get => _thumbnail;
        private set
        {
            this.RaiseAndSetIfChanged(ref _thumbnail, value);
            this.RaisePropertyChanged(nameof(HasThumbnail));
        }
    }

    public bool HasThumbnail => Thumbnail is not null;

    public bool IsSelected
    {
        get => _isSelected;
        set => this.RaiseAndSetIfChanged(ref _isSelected, value);
    }

    private void OnPreviewFrame(Bitmap frame)
    {
        if (_gotStill) return;
        _gotStill = true;

        try
        {
            using var ms = new MemoryStream();
            frame.Save(ms);
            ms.Position = 0;
            Thumbnail = new Bitmap(ms);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not freeze background thumbnail for {Path}", Model.Path);
            Thumbnail = frame;
            return;
        }

        var player = _preview;
        _preview = null;
        Dispatcher.UIThread.Post(() => player?.Dispose(), DispatcherPriority.Background);
    }

    public void Dispose()
    {
        _preview?.Dispose();
        _preview = null;
        SafeBitmapDisposal.Retire(_imageThumbnail);
        if (!ReferenceEquals(_thumbnail, _imageThumbnail))
            SafeBitmapDisposal.Retire(_thumbnail);
    }
}
