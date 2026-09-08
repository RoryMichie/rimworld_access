namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The widget the hover hit test last resolved, published so the pointer-routing
    /// chord (menus.routeToPointer) can match the pointer to a model row by real object
    /// identity instead of by geometry. Passive: hover writes, routing reads, and hover's
    /// announce-only contract is untouched — nothing here moves a cursor.
    ///
    /// The record self-invalidates without a timer. Every CapturedWidget is freshly
    /// allocated per recording call (WidgetCapture has no pool), so a widget from an
    /// earlier pass is simply absent from the current pass's rows and the reference
    /// search misses. Routing then falls back to its rect tier.
    /// </summary>
    internal static class HoverLanding
    {
        /// <summary>The widget under the pointer at the last resolved hover hit, or null.</summary>
        internal static CapturedWidget Widget { get; private set; }

        internal static void Record(CapturedWidget widget)
        {
            Widget = widget;
        }

        internal static void Clear()
        {
            Widget = null;
        }
    }
}
