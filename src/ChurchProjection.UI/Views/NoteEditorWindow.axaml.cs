using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ChurchProjection.UI.ViewModels.Operator;

namespace ChurchProjection.UI.Views;

public partial class NoteEditorWindow : Window
{
    private NoteEditorViewModel? _vm;

    public NoteEditorWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Opened += OnOpened;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnOpened(object? sender, EventArgs e)
    {
        _vm?.RefreshSlides();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null) _vm.CloseRequested -= Close;
        _vm = DataContext as NoteEditorViewModel;
        if (_vm is not null) _vm.CloseRequested += Close;
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.CloseRequested -= Close;
            _vm.Dispose();
        }
        base.OnClosed(e);
    }
}
