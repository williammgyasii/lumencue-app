using Avalonia.ReactiveUI;
using ChurchProjection.UI.ViewModels;

namespace ChurchProjection.UI.Views;

public partial class ProjectorWindow : ReactiveWindow<ProjectorViewModel>
{
    public ProjectorWindow()
    {
        InitializeComponent();
    }
}
