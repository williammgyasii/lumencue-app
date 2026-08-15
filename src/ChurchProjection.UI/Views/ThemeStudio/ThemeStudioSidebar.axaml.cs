using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ChurchProjection.UI.ViewModels;

namespace ChurchProjection.UI.Views.ThemeStudio;

public partial class ThemeStudioSidebar : UserControl
{
    public ThemeStudioSidebar() => InitializeComponent();

    private async void OnImportDesignClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ThemeStudioViewModel vm) return;
        var window = TopLevel.GetTopLevel(this) as Window;
        if (window is null) return;

        try
        {
            var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
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
            if (string.IsNullOrEmpty(path)) return;

            using var bitmap = new Avalonia.Media.Imaging.Bitmap(path);
            var size = bitmap.PixelSize;
            await vm.ImportDesignAsync(path, size.Width, size.Height);
        }
        catch (System.Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to import lower-third design");
        }
    }

    private void OnObjectDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: LayoutObjectItem item } container && item.CanRename)
        {
            item.BeginEdit();
            Dispatcher.UIThread.Post(() =>
            {
                var box = container.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
                box?.Focus();
                box?.SelectAll();
            }, DispatcherPriority.Input);
        }
    }

    private void OnRenameKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: LayoutObjectItem item }) return;
        if (e.Key == Key.Enter) { item.CommitEdit(); e.Handled = true; }
        else if (e.Key == Key.Escape) { item.CancelEdit(); e.Handled = true; }
    }

    private void OnRenameCommit(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: LayoutObjectItem item })
            item.CommitEdit();
    }
}
