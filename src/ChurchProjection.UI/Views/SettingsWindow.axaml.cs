using Avalonia.Controls;
using Avalonia.Interactivity;
using ChurchProjection.Core.Services;

namespace ChurchProjection.UI.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        CanResize = SettingsFormChrome.CanResize;
        Width = SettingsFormChrome.WindowWidth;
        Height = SettingsFormChrome.WindowHeight;
        MinWidth = SettingsFormChrome.WindowWidth;
        MinHeight = SettingsFormChrome.WindowHeight;
        MaxWidth = SettingsFormChrome.WindowWidth;
        MaxHeight = SettingsFormChrome.WindowHeight;
    }

    private void OnDoneClicked(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
