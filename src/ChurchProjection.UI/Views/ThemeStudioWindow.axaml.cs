using Avalonia.Controls;
using Avalonia.Input;
using ChurchProjection.UI.ViewModels;
using System.Windows.Input;

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

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete && e.Key != Key.Back) return;
        if (e.Source is TextBox) return;

        if (DataContext is ThemeStudioViewModel vm)
        {
            ((ICommand)vm.DeleteSelectedCommand).Execute(null);
            e.Handled = true;
        }
    }
}
