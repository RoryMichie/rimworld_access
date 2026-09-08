using RimWorld.Planet;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The two deferred gizmo openers: Shift+&lt;letter&gt; hotkey activation and the G key.
    /// <see cref="GizmoHotkeyShiftPatch"/> is untouched by either — it has zero coupling to them.
    ///
    /// <b>Shift+letter — map-only ambient claim.</b> Not duplicated onto <see cref="WorldScope"/>,
    /// since its gate already carries <c>!WorldRendererUtility.WorldSelected</c>. The modifier guard
    /// is subsumed by the 26 Shift+Letter chords' own exact-modifier <see cref="KeyChord"/> matching
    /// (Ctrl/Alt+Shift+Letter cannot match a chord with Ctrl=Alt=false).
    /// Blocked behavior is SILENT fallthrough, so every gate term lives in the <c>when:</c> predicate.
    /// The dry <c>Find.Selector != null</c> term is part of that:
    /// <see cref="RimWorldAccess.GizmoNavigationState.TryHotkeyActivate"/> returns silently only when
    /// the selector or map is null — every other path, "no gizmo matches this letter" included, plays
    /// a reject sound and speaks. Because a claim consumes once <c>when:</c> matches, reproducing the
    /// silent case needs this side-effect-free check in the gate rather than calling the real method
    /// twice.
    ///
    /// <b>G — both ambient scopes via a shared registrar.</b> ONE handler
    /// (<see cref="OnOpenGizmoNav(KeyEventSnapshot)"/>) registered from both
    /// <see cref="MapScope"/> and <see cref="WorldScope"/> keeps the map-vs-world decision in a single
    /// function; the world branch carries no <c>MapNavigationState.IsInitialized</c> check, which only
    /// ever gated the colony-map branch.
    /// The gate CANNOT be a plain <c>!ShellGuards.MenuOwnsInput()</c>: placement and review are
    /// input owners yet LIVE states for gizmos — vanilla keeps the gizmo bar clickable while a
    /// designator is selected — and <see cref="PlacementScopeMirror"/>/
    /// <see cref="ViewingModeScopeMirror"/> stand their scopes down for the gizmo drill-in and
    /// re-float them after.
    /// </summary>
    public sealed partial class MapScope
    {
        private void RegisterGizmoOpenerClaims()
        {
            Claim("map.gizmo.hotkeyActivate", OnActivateGizmoHotkey, when: ShiftLetterActivateLive);
            RegisterGizmoOpenClaim(this);
        }

        /// <summary>The shared registrar for map.gizmo.open; called from MapScope's constructor and from WorldScope's.</summary>
        internal static void RegisterGizmoOpenClaim(FocusScope scope)
        {
            scope.RegisterExternalClaim("map.gizmo.open", OnOpenGizmoNav, when: GizmoOpenLive);
        }

        /// <summary>The hotkey gate, including the StatBreakdownState term and the TryHotkeyActivate dry check (see class remarks).</summary>
        private static bool ShiftLetterActivateLive()
        {
            return Current.ProgramState == ProgramState.Playing
                && Find.CurrentMap != null
                && !WorldRendererUtility.WorldSelected
                && MapNavigationState.IsInitialized
                // Placement and review are the input owners gizmos stay live under (vanilla
                // keeps the gizmo bar clickable); their mirrors stand down for the drill-in.
                && (!ShellGuards.MenuOwnsInput()
                    || FocusStack.Top is PlacementScope || FocusStack.Top is ViewingModeScope)
                && !StatBreakdownState.IsActive
                && (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion)
                && Find.Selector != null;
        }

        private static void OnActivateGizmoHotkey(KeyEventSnapshot e)
        {
            GizmoNavigationState.TryHotkeyActivate(e.Key);
        }

        /// <summary>The G gate; placement and review are live states for gizmos (see class remarks).</summary>
        private static bool GizmoOpenLive()
        {
            return (FocusStack.Top is PlacementScope || FocusStack.Top is ViewingModeScope
                    || !ShellGuards.MenuOwnsInput())
                && !StatBreakdownState.IsActive;
        }

        /// <summary>
        /// The ProgramState/window-motion check, then the world-vs-map branch.
        /// </summary>
        private static void OnOpenGizmoNav(KeyEventSnapshot e)
        {
            if (Current.ProgramState != ProgramState.Playing)
                return;
            if (Find.WindowStack != null && Find.WindowStack.WindowsPreventCameraMotion)
                return;

            if (WorldRendererUtility.WorldSelected)
            {
                GizmoNavigationState.OpenFromWorldObjects();
                return;
            }

            if (Find.CurrentMap != null && MapNavigationState.IsInitialized)
            {
                if ((GizmoNavigationState.PawnJustSelected || MultiSelectState.IsMultiSelectActive) &&
                    Find.Selector != null && Find.Selector.NumSelected > 0)
                {
                    GizmoNavigationState.Open();
                }
                else
                {
                    IntVec3 cursorPosition = MapNavigationState.CurrentCursorPosition;
                    GizmoNavigationState.OpenAtCursor(cursorPosition, Find.CurrentMap);
                }
            }
        }
    }
}
