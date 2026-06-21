using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ChurchProjection.Core.Models.Theme;
using ChurchProjection.UI.ViewModels;

namespace ChurchProjection.UI.Views;

public partial class ThemeStudioWindow : Window
{
    private ThemeStudioViewModel? _vm;

    public ThemeStudioWindow()
    {
        InitializeComponent();
        Closed += (_, _) => (DataContext as ThemeStudioViewModel)?.Dispose();
        DataContextChanged += OnDataContextChanged;
        KeyDown += OnKeyDown;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (_vm is not null) _vm.CloseRequested -= Close;
        _vm = DataContext as ThemeStudioViewModel;
        if (_vm is not null) _vm.CloseRequested += Close;
    }

    // Keep the editor canvas as large as the available space allows.
    private void OnEditorHostSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is ThemeStudioViewModel vm)
            vm.SetViewport(e.NewSize.Width - 40, e.NewSize.Height - 40);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete && e.Key != Key.Back) return;
        // Don't hijack the key while the user is editing text (e.g. the theme name box).
        if (e.Source is TextBox) return;

        if (DataContext is ThemeStudioViewModel vm)
        {
            ((System.Windows.Input.ICommand)vm.DeleteSelectedCommand).Execute(null);
            e.Handled = true;
        }
    }

    // Click a region box on the layout canvas to select it for editing.
    private void OnRegionBoxPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ThemeStudioViewModel vm && sender is Control { Tag: string tag }
            && System.Enum.TryParse<RegionKind>(tag, out var kind))
            vm.SelectedRegion = kind;
    }

    // Drag the body of the selection box to move the region.
    private void OnMoveThumb(object? sender, VectorEventArgs e)
    {
        if (DataContext is ThemeStudioViewModel vm)
            vm.MoveSelected(e.Vector.X, e.Vector.Y);
    }

    // Drag a corner/edge handle to resize the region.
    private void OnResizeThumb(object? sender, VectorEventArgs e)
    {
        if (DataContext is ThemeStudioViewModel vm && sender is Thumb { Tag: string handle })
            vm.ResizeSelected(handle, e.Vector.X, e.Vector.Y);
    }

    // Import a church's designed lower-third graphic as a new theme: pick the file, read its real
    // pixel size (so the importer can map it onto 1920x1080 without shrinking), then hand it to the
    // view model which copies it into the asset store and builds the theme.
    private async void OnImportDesignClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ThemeStudioViewModel vm)
            return;

        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Import lower-third design",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Images")
                    {
                        Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.webp"],
                    },
                ],
            });

            var path = files.FirstOrDefault()?.TryGetLocalPath();
            if (string.IsNullOrEmpty(path))
                return;

            using var bitmap = new Avalonia.Media.Imaging.Bitmap(path);
            var size = bitmap.PixelSize;
            await vm.ImportDesignAsync(path, size.Width, size.Height);
        }
        catch (System.Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to import lower-third design");
        }
    }

    private async void OnBrowseImageClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ThemeStudioViewModel vm)
            return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select background image",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Images")
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.webp"],
                },
            ],
        });

        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(path))
        {
            vm.BackgroundImagePath = path;
            // Picking an image implies the user wants an image background.
            vm.BackgroundKind = ThemeBackgroundKind.Image;
        }
    }

    private async void OnBrowseShapeImageClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ThemeStudioViewModel vm)
            return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select shape image",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Images")
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.webp"],
                },
            ],
        });

        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(path))
            vm.SelShapeImagePath = path;
    }

    private void OnClearShapeImageClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ThemeStudioViewModel vm)
            vm.SelShapeImagePath = null;
    }

    private async void OnBrowseRegionImageClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ThemeStudioViewModel vm)
            return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select caption-box image",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Images")
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.webp"],
                },
            ],
        });

        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(path))
            vm.SelRegionBgImagePath = path;
    }

    private void OnClearRegionImageClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ThemeStudioViewModel vm)
            vm.SelRegionBgImagePath = null;
    }
}
