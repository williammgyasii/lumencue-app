using Avalonia.Controls;

namespace ChurchProjection.UI.Views;

public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
    }

    /// <summary>Updates the small status line under the progress bar (e.g. "Loading library…").</summary>
    public void SetStatus(string message)
    {
        if (this.FindControl<TextBlock>("StatusText") is { } status)
            status.Text = message;
    }

    /// <summary>Advances the determinate progress bar (0–100). The bar animates smoothly to the
    /// new value via its XAML transition, so stepping through milestones reads as a real load.</summary>
    public void SetProgress(double percent)
    {
        if (this.FindControl<ProgressBar>("Bar") is { } bar)
            bar.Value = System.Math.Clamp(percent, 0, 100);
    }
}
