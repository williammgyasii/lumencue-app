using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ChurchProjection.Core.Models.Theme;
using ChurchProjection.UI.ViewModels;
using System.Linq;

namespace ChurchProjection.UI.Views.ThemeStudio;

public partial class ThemeStudioInspector : UserControl
{
    public ThemeStudioInspector() => InitializeComponent();

    private Window? OwnerWindow => TopLevel.GetTopLevel(this) as Window;

    private async void OnBrowseImageClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not ThemeStudioViewModel vm || OwnerWindow is null) return;
        var path = await PickImageAsync(OwnerWindow, "Select background image");
        if (string.IsNullOrEmpty(path)) return;
        vm.BackgroundImagePath = path;
        vm.BackgroundKind = ThemeBackgroundKind.Image;
    }

    private async void OnBrowseShapeImageClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not ThemeStudioViewModel vm || OwnerWindow is null) return;
        var path = await PickImageAsync(OwnerWindow, "Select shape image");
        if (!string.IsNullOrEmpty(path)) vm.SelShapeImagePath = path;
    }

    private void OnClearShapeImageClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ThemeStudioViewModel vm) vm.SelShapeImagePath = null;
    }

    private async void OnBrowseRegionImageClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not ThemeStudioViewModel vm || OwnerWindow is null) return;
        var path = await PickImageAsync(OwnerWindow, "Select caption-box image");
        if (!string.IsNullOrEmpty(path)) vm.SelRegionBgImagePath = path;
    }

    private void OnClearRegionImageClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ThemeStudioViewModel vm) vm.SelRegionBgImagePath = null;
    }

    private static async Task<string?> PickImageAsync(Window window, string title)
    {
        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Images")
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.webp"],
                },
            ],
        });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }
}
