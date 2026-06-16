using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ChurchProjection.UI.Views;

public partial class SongImportWindow : Window
{
    public SongImportWindow()
    {
        InitializeComponent();
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
