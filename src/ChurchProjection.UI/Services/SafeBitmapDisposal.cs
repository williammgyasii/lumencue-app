using System;
using Avalonia.Threading;

namespace ChurchProjection.UI.Services;

/// <summary>
/// Disposes bitmaps that were (or may still be) bound to an on-screen <c>Image.Source</c> without
/// racing Avalonia's render thread. Disposing a bound bitmap synchronously frees its native surface
/// while the compositor may still be painting it, which throws a NullReferenceException inside
/// <c>Image.Render</c>. Posting the dispose at <see cref="DispatcherPriority.Background"/> defers it
/// until after the pending render commit has swapped to the new source, so the old bitmap is no
/// longer in use.
/// </summary>
internal static class SafeBitmapDisposal
{
    public static void Retire(IDisposable? bitmap)
    {
        if (bitmap is null) return;

        // Always defer to the UI dispatcher at Background priority (below Render), so the dispose runs
        // after the compositor has committed the frame that swapped away from this bitmap.
        Dispatcher.UIThread.Post(() =>
        {
            try { bitmap.Dispose(); }
            catch (Exception) { /* the renderer may have just released it; ignore */ }
        }, DispatcherPriority.Background);
    }
}
