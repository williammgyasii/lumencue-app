namespace ChurchProjection.Core.Services;

/// <summary>
/// Operator chrome: a normal desktop window (title bar, drag, resize), not a kiosk shell.
/// </summary>
public static class OperatorWindowChrome
{
    public static bool CanResize => true;
    public static bool ExtendClientArea => false;
    public static bool StartsMaximized => false;
}
