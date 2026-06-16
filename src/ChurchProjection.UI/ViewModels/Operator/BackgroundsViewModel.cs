using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using ChurchProjection.Core.Models.Projection;
using ChurchProjection.UI.Services;
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

    public ObservableCollection<BackgroundTileViewModel> Items { get; } = [];

    public ReactiveCommand<BackgroundTileViewModel, Unit> SelectCommand { get; }
    public ReactiveCommand<BackgroundTileViewModel, Unit> RemoveCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearCommand { get; }

    public BackgroundsViewModel(ILiveBackgroundService service)
    {
        _service = service;

        SelectCommand = ReactiveCommand.Create<BackgroundTileViewModel>(t => _service.Select(t.Model));
        RemoveCommand = ReactiveCommand.CreateFromTask<BackgroundTileViewModel>(t => _service.RemoveAsync(t.Model));
        ClearCommand = ReactiveCommand.Create(() => _service.Select(null));

        _service.ItemsChanged
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(Rebuild)
            .DisposeWith(_subs);

        _service.SelectedChanged
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(sel =>
            {
                var id = sel?.Id;
                foreach (var t in Items) t.IsSelected = t.Model.Id == id;
                HasSelection = sel is not null;
            })
            .DisposeWith(_subs);
    }

    /// <summary>True when a background is live (enables the "None" / clear control).</summary>
    public bool HasSelection
    {
        get => _hasSelection;
        set => this.RaiseAndSetIfChanged(ref _hasSelection, value);
    }

    /// <summary>Adds a media file picked by the view's file dialog.</summary>
    public Task AddAsync(string path) => _service.AddAsync(path);

    private void Rebuild(IReadOnlyList<LiveBackground> items)
    {
        var selId = _service.Selected?.Id;
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

    public BackgroundTileViewModel(LiveBackground model)
    {
        Model = model;

        if (model.Kind == LiveBackgroundKind.Image && File.Exists(model.Path))
        {
            try
            {
                using var fs = File.OpenRead(model.Path);
                Thumbnail = Bitmap.DecodeToWidth(fs, 240);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Could not build thumbnail for {Path}", model.Path);
            }
        }
    }

    public LiveBackground Model { get; }
    public string Name => Model.Name;
    public bool IsVideo => Model.Kind == LiveBackgroundKind.Video;
    public Bitmap? Thumbnail { get; }
    public bool HasThumbnail => Thumbnail is not null;

    public bool IsSelected
    {
        get => _isSelected;
        set => this.RaiseAndSetIfChanged(ref _isSelected, value);
    }

    public void Dispose() => Thumbnail?.Dispose();
}
