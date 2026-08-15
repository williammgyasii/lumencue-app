using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using ChurchProjection.UI.ViewModels;

namespace ChurchProjection.UI.Views.ThemeStudio;

public partial class ThemeStudioCanvas : UserControl
{
    public ThemeStudioCanvas() => InitializeComponent();

    private void OnEditorHostSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is ThemeStudioViewModel vm)
            vm.SetViewport(e.NewSize.Width - 40, e.NewSize.Height - 40);
    }

    private void OnCanvasBackgroundPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ThemeStudioViewModel vm && ReferenceEquals(e.Source, sender))
            vm.SelectThemeBackground();
    }

    private void OnRegionBoxPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ThemeStudioViewModel vm && sender is Control { Tag: string tag }
            && Enum.TryParse<RegionKind>(tag, out var kind))
        {
            vm.SelectedRegion = kind;
            e.Handled = true;
        }
    }

    private void OnShapeBoxPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ThemeStudioViewModel vm && sender is Control { Tag: int index })
        {
            vm.SelectShape(index);
            e.Handled = true;
        }
    }

    private void OnMoveThumb(object? sender, VectorEventArgs e)
    {
        if (DataContext is ThemeStudioViewModel vm)
            vm.MoveSelected(e.Vector.X, e.Vector.Y);
    }

    private void OnResizeThumb(object? sender, VectorEventArgs e)
    {
        if (DataContext is ThemeStudioViewModel vm && sender is Thumb { Tag: string handle })
            vm.ResizeSelected(handle, e.Vector.X, e.Vector.Y);
    }
}
