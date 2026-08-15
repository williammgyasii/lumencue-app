using Avalonia.Controls;
using Avalonia.Interactivity;
using ChurchProjection.UI.ViewModels.Planning;

namespace ChurchProjection.UI.Views;

public partial class SongEditorWindow : Window
{
    private SongEditorViewModel? _vm;

    public SongEditorWindow()
    {
        InitializeComponent();
        Closed += (_, _) => (DataContext as SongEditorViewModel)?.Dispose();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (_vm is not null) _vm.CloseRequested -= Close;
        _vm = DataContext as SongEditorViewModel;
        if (_vm is not null) _vm.CloseRequested += Close;
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e) => Close();

    private void OnTitleLostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SongEditorViewModel vm)
            vm.NormalizeTitle();
    }

    private void OnMoveUpClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SongEditorViewModel vm && (sender as Control)?.DataContext is SongSectionVm s)
            vm.MoveUpCommand.Execute(s).Subscribe();
    }

    private void OnMoveDownClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SongEditorViewModel vm && (sender as Control)?.DataContext is SongSectionVm s)
            vm.MoveDownCommand.Execute(s).Subscribe();
    }

    private void OnDeleteClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SongEditorViewModel vm && (sender as Control)?.DataContext is SongSectionVm s)
            vm.DeleteSectionCommand.Execute(s).Subscribe();
    }
}
