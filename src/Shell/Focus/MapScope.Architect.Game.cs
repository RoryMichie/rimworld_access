using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Ambient map claim: Tab opens/cancels/closes the architect
    /// tree menu (retired side door <c>ArchitectMenuPatch.Prefix</c>'s Tab
    /// branch). Map-only, verbatim from the legacy handler — the side door
    /// itself yielded on the world map (<c>if (WorldNavigationState.IsActive)
    /// return;</c>), so unlike the QuickInfo cluster this claim is
    /// NOT duplicated onto <see cref="WorldScope"/>.
    ///
    /// The gate is the legacy handler's own gate PORTED VERBATIM, including the
    /// 0.3s double-press cooldown (moved from an instance field on the old
    /// Harmony patch class to a static field here) and the legacy handler's own
    /// explicit <c>KeyboardHelper.IsAnyAccessibilityMenuActive()</c> check —
    /// unlike the sibling ambient claims, no modal-guard substitution is
    /// needed here because the legacy code ALREADY tested the full helper directly
    /// (not just the narrower !AnyLiveModal shell-transition term), so there
    /// is nothing to substitute. The cooldown lives in the `when:` gate, not
    /// the handler body: on legacy, a cooldown-blocked press returned WITHOUT
    /// calling Event.current.Use(), so the key fell through un-consumed to
    /// lower-priority handlers — putting the check in the gate (rather than
    /// the handler) reproduces that exactly (claim never matches while on
    /// cooldown, so Dispatch keeps walking/falls through instead of
    /// consuming).
    ///
    /// <b>The three-way branch below is provably unreachable today</b> (kept
    /// anyway, verbatim, per the design lock): <c>ArchitectState.IsActive</c>
    /// is itself one of <see cref="ShellGuards.MenuOwnsInput"/>'s
    /// own OR terms, and <c>IsInPlacementMode</c> implies <c>IsActive</c>
    /// (CurrentMode != Inactive) — so by the time this claim's gate has
    /// already confirmed <c>!ShellGuards.MenuOwnsInput()</c>,
    /// <c>ArchitectState.IsActive</c> (and therefore <c>IsInPlacementMode</c>)
    /// must already be false, meaning the Cancel()/Reset() branches can never
    /// fire under the current shape of that helper. This exact relationship
    /// held on the RETIRED rung too (its own second guard was the identical
    /// <c>if (KeyboardHelper.IsAnyAccessibilityMenuActive()) return;</c>), so
    /// porting the gate verbatim reproduces the same (un)reachability
    /// byte-for-byte — not a behavior change, just an inherited observation
    /// worth flagging.
    /// </summary>
    public sealed partial class MapScope
    {
        private static float lastArchitectKeyTime = 0f;
        private const float ArchitectKeyCooldown = 0.3f;

        private void RegisterArchitectClaims()
        {
            Claim("map.architect.toggle", OnToggleArchitect, when: ArchitectToggleLive);
        }

        /// <summary>The retired Tab rung's gate, verbatim (including the cooldown — see class remarks).</summary>
        private static bool ArchitectToggleLive()
        {
            return Time.time - lastArchitectKeyTime >= ArchitectKeyCooldown
                && Find.CurrentMap != null
                && MapNavigationState.IsInitialized
                && !WorldNavigationState.IsActive
                && (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion)
                && !WindowlessFloatMenuState.IsActive
                && !ShellGuards.MenuOwnsInput();
        }

        /// <summary>
        /// The legacy handler's three-way branch, ported verbatim (see class
        /// remarks on reachability).
        /// </summary>
        private static void OnToggleArchitect(KeyEventSnapshot e)
        {
            lastArchitectKeyTime = Time.time;

            if (ArchitectState.IsInPlacementMode)
            {
                ArchitectState.Cancel();
                return;
            }

            if (ArchitectState.IsActive)
            {
                ArchitectState.Reset();
                TolkHelper.Speak("RimWorldAccess.Building.Architect.MenuClosed".Loc());
                return;
            }

            ArchitectMenuPatch.OpenArchitectTreeMenu();
        }
    }
}
