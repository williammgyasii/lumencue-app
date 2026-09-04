namespace ChurchProjection.Core.Services;

public enum WorkspaceSelectionCause
{
    OperatorClick,
    ModeRestore,
    ListRebuild
}

/// <summary>
/// Whether a list selection may go live. Restore and rebuild never send live;
/// an operator click follows <see cref="LiveClickPolicy"/>.
/// </summary>
public static class WorkspaceSelectionPolicy
{
    public static bool MaySendLive(WorkspaceSelectionCause cause, bool singleClickGoesLive)
        => MaySendLive(cause, isDoubleClick: false, singleClickGoesLive);

    public static bool MaySendLive(
        WorkspaceSelectionCause cause,
        bool isDoubleClick,
        bool singleClickGoesLive)
        => cause == WorkspaceSelectionCause.OperatorClick
           && LiveClickPolicy.GoesLive(isDoubleClick, singleClickGoesLive);
}
