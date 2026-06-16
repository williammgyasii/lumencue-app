using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ChurchProjection.UI.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    private void OnDoneClicked(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
