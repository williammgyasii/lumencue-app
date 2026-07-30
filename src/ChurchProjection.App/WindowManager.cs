using System.Reactive.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using ChurchProjection.Core.Models.Slides;
using ChurchProjection.Core.Services;
using ChurchProjection.Infrastructure.Data;
using ChurchProjection.UI.Services;
using ChurchProjection.UI.ViewModels;
using ChurchProjection.UI.Views;
using ReactiveUI;
using Serilog;

namespace ChurchProjection.App;

/// <summary>
/// Owns the operator window plus one projector window per active screen, and routes ProPresenter.
/// Every screen renders the single program feed; screens differ only by their name and assigned view
/// (theme), and media can be targeted at one screen or all of them.
/// </summary>
public class WindowManager
{
    private const string LayoutKey = "output_layout";
    private const string PpOutputKey = "propresenter";
    private const string NdiOutputKey = "ndi";
    private const string WindowedKey = "windowed";

    private readonly OperatorViewModel _operatorVm;
    private readonly IProjectionService _projection;
    private readonly IThemeService _themes;
    private readonly IProPresenterService _proPresenter;
    private readonly SettingsRepository _settings;
    private readonly ILiveBackgroundService _liveBackground;
    private readonly IAnnouncementService _announcements;
    private readonly ILayerService _layers;
    private readonly INdiOutputService _ndi;

    private readonly Dictionary<string, DisplayWindow> _windows = [];
    private IDisposable? _ppFeedSub;
    private ProjectorViewModel? _ndiViewModel;
    private bool _screenOutputEnabled = true;

    private OperatorWindow? _operatorWindow;
    private bool _restoring;

    public WindowManager(
        OperatorViewModel operatorVm,
        IProjectionService projection,
        IThemeService themes,
        IProPresenterService proPresenter,
        SettingsRepository settings,
        ILiveBackgroundService liveBackground,
        IAnnouncementService announcements,
        ILayerService layers,
        INdiOutputService ndi)
    {
        _operatorVm = operatorVm;
        _projection = projection;
        _themes = themes;
        _proPresenter = proPresenter;
        _settings = settings;
        _liveBackground = liveBackground;
        _announcements = announcements;
        _layers = layers;
        _ndi = ndi;
    }

    public async void ShowAll()
    {
        _operatorWindow = new OperatorWindow { DataContext = _operatorVm };
        _operatorWindow.Show();

        await SetupOutputsAsync();

        _operatorWindow.Closed += (_, _) =>
        {
            foreach (var w in _windows.Values.ToList())
                w.Window.Close();
            _windows.Clear();
        };

        Log.Information("All windows launched");
    }

    /// <summary>Closes the operator window (and, via its Closed handler, all projector windows). Used on sign-out.</summary>
    public void CloseAll()
    {
        _operatorWindow?.Close();
        _operatorWindow = null;
    }

    private async Task SetupOutputsAsync()
    {
        if (_operatorWindow is null) return;

        var displays = BuildDisplayOptions(_operatorWindow.Screens);

        _operatorVm.Outputs.Clear();
        foreach (var d in displays)
        {
            var kind = d.IsWindowedPreview ? OutputKind.Windowed : OutputKind.Display;
            _operatorVm.Outputs.Add(new OutputRow(d.Key, kind, d.Name, d, _operatorVm.ThemeOptions));
        }
        _operatorVm.Outputs.Add(new OutputRow(PpOutputKey, OutputKind.ProPresenter, "ProPresenter", null, _operatorVm.ThemeOptions));
        _operatorVm.Outputs.Add(new OutputRow(NdiOutputKey, OutputKind.Ndi, "NDI (OBS)", null, _operatorVm.ThemeOptions));

        var saved = await LoadLayoutAsync();
        var ndiSourceName = await _settings.GetAsync("ndi_source_name");
        if (!string.IsNullOrWhiteSpace(ndiSourceName))
            _ndi.SourceName = ndiSourceName;
        ApplySavedLayout(saved, displays);

        // React to every screen's on/off, view (theme), and name changes.
        foreach (var row in _operatorVm.Outputs)
        {
            var local = row;
            local.WhenAnyValue(x => x.IsActive, x => x.SelectedThemeOption, x => x.Name)
                .Skip(1)
                .Subscribe(_ => OnOutputChanged(local));
        }

        // Master screen-output kill-switch: hide/show all of this app's projector windows without
        // touching each output's configured channel.
        _screenOutputEnabled = _operatorVm.ScreenOutputEnabled;
        _operatorVm.WhenAnyValue(x => x.ScreenOutputEnabled)
            .Skip(1)
            .Subscribe(enabled =>
            {
                _screenOutputEnabled = enabled;
                foreach (var row in _operatorVm.Outputs.Where(o => o.Kind is OutputKind.Display or OutputKind.Windowed))
                    ApplyDisplay(row);
                var ndiRow = _operatorVm.Outputs.FirstOrDefault(o => o.Kind == OutputKind.Ndi);
                if (ndiRow is not null) ApplyNdi(ndiRow);
                Log.Information("Screen output {State}", enabled ? "enabled" : "disabled");
            });

        ApplyAllOutputs();
    }

    private void ApplySavedLayout(List<OutputState> saved, List<DisplayOption> displays)
    {
        _restoring = true;

        var anyDisplayActive = false;
        foreach (var row in _operatorVm.Outputs)
        {
            var state = saved.FirstOrDefault(s => s.Key == row.Key);
            if (!string.IsNullOrWhiteSpace(state?.Name))
                row.Name = state.Name;
            row.SelectedThemeOption = string.IsNullOrEmpty(state?.ThemeOverride)
                ? OutputRow.FollowContent
                : state.ThemeOverride;
            if (state is not null)
            {
                row.IsActive = state.Active;
                if (state.Active && row.Kind != OutputKind.ProPresenter)
                    anyDisplayActive = true;
            }
        }

        // First run: if ProPresenter is already configured, default its output on (migrates the
        // previous standalone ProPresenter toggle).
        var ppRow = _operatorVm.Outputs.FirstOrDefault(o => o.Kind == OutputKind.ProPresenter);
        if (ppRow is not null && saved.All(s => s.Key != PpOutputKey)
            && !string.IsNullOrWhiteSpace(_proPresenter.Settings.MessageName))
        {
            ppRow.IsActive = true;
        }

        // First run (no display saved active): default a sensible single Main screen so there is
        // always something projecting.
        if (!anyDisplayActive && saved.Count == 0)
        {
            var physical = _operatorVm.Outputs
                .FirstOrDefault(o => o.Kind == OutputKind.Display && o.Display is { } d && !d.Name.Contains("(primary)"));
            var fallback = physical
                           ?? _operatorVm.Outputs.FirstOrDefault(o => o.Kind == OutputKind.Windowed);
            if (fallback is not null) fallback.IsActive = true;
        }

        _restoring = false;
    }

    private void OnOutputChanged(OutputRow row)
    {
        if (_restoring) return;
        ApplyOutput(row);
        _ = SaveLayoutAsync();
    }

    private void ApplyAllOutputs()
    {
        foreach (var row in _operatorVm.Outputs)
            ApplyOutput(row);
    }

    private void ApplyOutput(OutputRow row)
    {
        switch (row.Kind)
        {
            case OutputKind.ProPresenter:
                ApplyProPresenter(row);
                break;
            case OutputKind.Ndi:
                ApplyNdi(row);
                break;
            default:
                ApplyDisplay(row);
                break;
        }
    }

    private void ApplyDisplay(OutputRow row)
    {
        if (row.Display is null) return;

        if (!row.IsActive || !_screenOutputEnabled)
        {
            if (_windows.Remove(row.Key, out var existing))
            {
                existing.Window.Close();
                existing.ViewModel.Dispose();
            }
            return;
        }

        if (_windows.TryGetValue(row.Key, out var dw))
        {
            // Already open: swap the view (theme override) live, no rebind — the feed never changes.
            if (dw.ThemeOverride != row.ThemeOverride)
            {
                dw.ViewModel.SetThemeOverride(row.ThemeOverride);
                _windows[row.Key] = dw with { ThemeOverride = row.ThemeOverride };
                Log.Information("Screen '{Name}' → view {View}", row.Name, row.ThemeOverride ?? "(follow content)");
            }
            return;
        }

        var vm = new ProjectorViewModel(_projection, _themes, row.ThemeOverride, _liveBackground, _announcements, row.Key, _layers);
        var window = new ProjectorWindow { DataContext = vm };
        window.Show();
        PositionWindow(window, row.Display);
        _windows[row.Key] = new DisplayWindow(window, vm, row.ThemeOverride);

        Log.Information("Screen '{Name}' → view {View}", row.Name, row.ThemeOverride ?? "(follow content)");
    }

    private void ApplyProPresenter(OutputRow row)
    {
        _ppFeedSub?.Dispose();
        _ppFeedSub = null;

        if (!row.IsActive)
        {
            _ = _proPresenter.ClearAsync();
            return;
        }

        _ppFeedSub = _projection.CurrentSlide
            .Subscribe(slide =>
            {
                if (slide.Type == SlideType.Blank)
                    _ = _proPresenter.ClearAsync();
                else
                    _ = _proPresenter.ShowAsync(slide.Title, slide.Body);
            });

        Log.Information("ProPresenter output → program feed");
    }

    private void ApplyNdi(OutputRow row)
    {
        _ndi.Stop();
        _ndiViewModel?.Dispose();
        _ndiViewModel = null;

        if (!row.IsActive || !_screenOutputEnabled)
            return;

        if (!_ndi.IsAvailable)
        {
            Log.Warning("NDI output requested but unavailable: {Reason}", _ndi.UnavailableReason);
            return;
        }

        _ndiViewModel = new ProjectorViewModel(
            _projection, _themes, row.ThemeOverride, _liveBackground, _announcements, NdiOutputKey, _layers);
        _ndi.Start(_ndiViewModel);
        Log.Information("NDI output → program feed as '{Source}'", _ndi.SourceName);
    }

    private void PositionWindow(ProjectorWindow window, DisplayOption opt)
    {
        if (opt.IsWindowedPreview)
        {
            window.WindowState = WindowState.Normal;
            window.SystemDecorations = SystemDecorations.Full;
            window.ShowInTaskbar = true;
            window.Title = "LumenCue — Projector Preview";
            window.Width = opt.Width;
            window.Height = opt.Height;
            window.Position = new PixelPoint(opt.X, opt.Y);
            return;
        }

        window.WindowState = WindowState.Normal;
        window.SystemDecorations = SystemDecorations.None;
        window.ShowInTaskbar = false;
        window.Position = new PixelPoint(opt.X, opt.Y);
        window.WindowState = WindowState.FullScreen;
    }

    private List<DisplayOption> BuildDisplayOptions(Screens screens)
    {
        var all = screens.All.ToList();
        var primary = screens.Primary;

        Log.Information("Detected {Count} display(s)", all.Count);

        var options = new List<DisplayOption>();
        for (var i = 0; i < all.Count; i++)
        {
            var s = all[i];
            var b = s.Bounds;
            var isPrimary = primary is not null && s.Equals(primary);
            var name = $"Display {i + 1} — {b.Width}×{b.Height}{(isPrimary ? " (primary)" : "")}";
            options.Add(new DisplayOption(name, b.X, b.Y, b.Width, b.Height));
        }

        options.Add(new DisplayOption("Windowed preview", 80, 80, 960, 540, IsWindowedPreview: true));
        return options;
    }

    private async Task<List<OutputState>> LoadLayoutAsync()
    {
        try
        {
            var json = await _settings.GetAsync(LayoutKey);
            if (!string.IsNullOrWhiteSpace(json))
                return JsonSerializer.Deserialize<List<OutputState>>(json) ?? [];
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load output layout");
        }
        return [];
    }

    private async Task SaveLayoutAsync()
    {
        try
        {
            var state = _operatorVm.Outputs
                .Select(o => new OutputState { Key = o.Key, Active = o.IsActive, Name = o.Name, ThemeOverride = o.ThemeOverride })
                .ToList();
            await _settings.SetAsync(LayoutKey, JsonSerializer.Serialize(state));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to save output layout");
        }
    }

    private sealed record DisplayWindow(ProjectorWindow Window, ProjectorViewModel ViewModel, string? ThemeOverride);

    private sealed class OutputState
    {
        public string Key { get; set; } = string.Empty;
        public bool Active { get; set; }
        public string? Name { get; set; }
        public string? ThemeOverride { get; set; }
    }
}
