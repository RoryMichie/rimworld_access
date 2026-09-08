using System;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Shared successor to the per-feature "in flight" flags listed in
    /// <see cref="RimWorldAccess.DialogInterceptionPatch"/> (GizmoNavigationState,
    /// TransportPodLaunchState, WindowlessFloatMenuState, ExternalWorldTargeting,
    /// InspectTabCaptureService, MenuNavigationState, CharEditorCompat.DelegateInFlight):
    /// a custom compat scope that reflectively invokes a mod delegate which may open a
    /// vanilla <c>FloatMenu</c> wraps that invoke in <see cref="Run"/> instead of growing
    /// yet another bespoke flag. A real FloatMenu opened this way anchors at the physical
    /// mouse and self-dismisses within a frame if the mouse is far away (vanilla's
    /// vanishIfMouseDistant check), so a keyboard user would never reach it without this
    /// redirect into the windowless menu path.
    /// </summary>
    internal static class ScopeDelegateGuard
    {
        internal static bool InFlight { get; private set; }

        /// <summary>Runs <paramref name="action"/> with <see cref="InFlight"/> raised; exceptions propagate after it's cleared.</summary>
        internal static void Run(Action action)
        {
            InFlight = true;
            // A mod delegate invoked on the player's own
            // behalf can open a plain, non-absorbing Window (the same reasoning
            // as GenericWindowScope.PostActivate's twin remarks) — arm the
            // watch so ScopeForWindow.GenericReaderEligible does not refuse it
            // and leave the player with dead silence.
            ScopeForWindow.ArmDeliberateGenericAttach();
            try
            {
                action();
            }
            finally
            {
                InFlight = false;
            }
        }
    }
}
