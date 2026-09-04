using ChurchProjection.Core.Services;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

public class WorkspaceSelectionPolicyTests
{
    [Fact]
    public void Mode_restore_never_goes_live_even_when_single_click_goes_live()
    {
        Assert.False(WorkspaceSelectionPolicy.MaySendLive(
            WorkspaceSelectionCause.ModeRestore, singleClickGoesLive: true));
    }

    [Fact]
    public void List_rebuild_never_goes_live_even_when_single_click_goes_live()
    {
        Assert.False(WorkspaceSelectionPolicy.MaySendLive(
            WorkspaceSelectionCause.ListRebuild, singleClickGoesLive: true));
    }

    [Fact]
    public void Operator_click_follows_the_live_click_policy()
    {
        Assert.Equal(
            LiveClickPolicy.GoesLive(isDoubleClick: false, singleClickGoesLive: false),
            WorkspaceSelectionPolicy.MaySendLive(
                WorkspaceSelectionCause.OperatorClick, singleClickGoesLive: false));
        Assert.Equal(
            LiveClickPolicy.GoesLive(isDoubleClick: false, singleClickGoesLive: true),
            WorkspaceSelectionPolicy.MaySendLive(
                WorkspaceSelectionCause.OperatorClick, singleClickGoesLive: true));
        Assert.Equal(
            LiveClickPolicy.GoesLive(isDoubleClick: true, singleClickGoesLive: false),
            WorkspaceSelectionPolicy.MaySendLive(
                WorkspaceSelectionCause.OperatorClick, isDoubleClick: true, singleClickGoesLive: false));
    }
}
