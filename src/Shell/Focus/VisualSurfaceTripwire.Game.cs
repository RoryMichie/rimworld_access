#if DEBUG
using System.Collections.Generic;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Focus-indicator-law tripwire: a live MODAL scope owning the keyboard while nothing
    /// of it is on screen leaves the sighted viewer with no cursor to follow, which is how
    /// the bill editor shipped invisible for six weeks. Every such scope now names itself
    /// in the QA flight recorder, once per launch, so the violation is impossible to miss.
    ///
    /// A scope declares its surface either by owning a window through
    /// <see cref="ScopeForWindow"/> or by answering
    /// <see cref="FocusScope.HasOnScreenSurface"/> — the second being for scopes that draw
    /// their own overlay or anchor a vanilla window they do not formally own. A scope that
    /// does neither is either missing its declaration or genuinely invisible; both are
    /// worth a line.
    /// </summary>
    internal static class VisualSurfaceTripwire
    {
        private static FocusScope lastChecked;
        private static readonly HashSet<string> screamedNames = new HashSet<string>();

        /// <summary>
        /// Called once per OnGUI pass from the tail of the dispatcher's mirror reconcile,
        /// so the stack it reads is the settled one for the frame — a window opened in the
        /// same call that pushed the scope is already in the WindowStack by then.
        /// </summary>
        internal static void Check()
        {
            FocusScope top = FocusStack.Top;
            if (top == null || ReferenceEquals(top, lastChecked))
            {
                return;
            }
            lastChecked = top;

            if (!top.IsModal || !top.IsLive)
            {
                return;
            }
            if (ScopeForWindow.WindowOf(top) != null || top.HasOnScreenSurface)
            {
                return;
            }
            if (!screamedNames.Add(top.Name))
            {
                return;
            }

            ShellDev.QARecord("visual", "VIOLATION: modal scope " + top.Name + " has no on-screen surface");
        }
    }
}
#endif
