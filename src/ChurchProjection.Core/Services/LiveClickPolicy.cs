namespace ChurchProjection.Core.Services;

/// <summary>
/// Click vs double-click for sending a card to the program output.
/// Default: single-click selects/previews, double-click goes live.
/// The operator can opt into ProPresenter-style single-click-goes-live in Settings.
/// </summary>
public static class LiveClickPolicy
{
    public static bool GoesLive(bool isDoubleClick, bool singleClickGoesLive)
        => isDoubleClick || singleClickGoesLive;
}
