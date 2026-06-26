using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ChurchProjection.UI.ViewModels.Operator;

namespace ChurchProjection.UI.Views;

public partial class NoteEditWindow : Window
{
    private NoteEditViewModel? _vm;

    public NoteEditWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (_vm is not null) _vm.CloseRequested -= Close;
        _vm = DataContext as NoteEditViewModel;
        if (_vm is not null) _vm.CloseRequested += Close;
    }
}
