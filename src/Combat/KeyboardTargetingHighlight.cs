using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Draws the game's own targeting highlight (range ring, target bracket, AOE
    /// field) at the keyboard cursor while an ability, weapon or jump is being
    /// aimed. Vanilla's targeting draw is not mouse-bound by design, only by
    /// argument: <c>Targeter.TargeterUpdate</c> computes
    /// <c>CurrentTargetUnderMouse()</c> and passes it to
    /// <c>targetingSource.DrawHighlight(target)</c> (decompiled
    /// RimWorld/Targeter.cs:283-307). Both branches take the target as a parameter, so the whole range
    /// ring, target highlight and explosion field come from vanilla's own code called with the keyboard
    /// cell instead. Nothing here computes a radius or draws a ring.
    /// </summary>
    internal static class KeyboardTargetingHighlight
    {
        // Targeter.highlightAction is private (decompiled RimWorld/Targeter.cs:31);
        // read-only reflection per the hard rules, cached once.
        private static readonly System.Reflection.FieldInfo HighlightActionField =
            AccessTools.Field(typeof(Targeter), "highlightAction");

        internal static void Draw(Map map)
        {
            try
            {
                DrawInner(map);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Keyboard targeting highlight error", ex);
            }
        }

        private static void DrawInner(Map map)
        {
            // Weapon-fire targeting (Command_VerbTarget through the gizmo menu) is
            // STATELESS on the mod side — none of the five targeting states goes
            // active, and TargetingPatch.HasTargetingContext arms only for ranged
            // commands with a known range — so the five-state gate memo decision T2
            // proposed can never draw for it (live-verified: SMG targeting with all
            // five states false). Any live Targeter session in this mod is aimed by
            // the keyboard cursor, so the session itself is the gate.
            if (Find.Targeter == null || !Find.Targeter.IsTargeting)
                return;

            if (!MapNavigationState.IsInitialized)
                return;
            IntVec3 cell = MapNavigationState.CurrentCursorPosition;
            if (!cell.InBounds(map))
                return;

            // Same expression the Enter path uses to resolve the keyboard target,
            // duplicated rather than refactored (src/Combat/TargetingPatch.cs:123-132
            // is the source of truth; see the InspectPaneLink.SyncSelection
            // precedent, src/Shell/Focus/InspectPaneLink.Game.cs:89-99). Unifying
            // the two is a Wave-5 cleanup, not a pixels-only change.
            ITargetingSource source = Find.Targeter.targetingSource; // public field
            LocalTargetInfo target = LocalTargetInfo.Invalid;
            if (source != null)
            {
                // thingsOnly: true because GenUI.TargetsAt otherwise falls back to
                // the real UI.MouseCell() (TargetingPatch.cs:120-122).
                target = GenUI.TargetsAt(cell.ToVector3Shifted(), source.targetParams, thingsOnly: true, source)
                              .FirstOrFallback(LocalTargetInfo.Invalid);
            }
            if (!target.IsValid)
                target = new LocalTargetInfo(cell);

            if (source != null)
            {
                // Vanilla's own public interface method; on a Verb this draws the
                // radius ring, the target highlight and the AOE field (decompiled
                // Verse/Verb.cs:676-702).
                source.DrawHighlight(target);
                return;
            }

            // Targeter.BeginTargeting(TargetingParameters, Action<LocalTargetInfo>, ...)
            // shape: highlightAction is vanilla's own delegate (category A) when set,
            // else GenDraw.DrawTargetHighlight is exactly what TargeterUpdate calls in
            // the same situation (decompiled RimWorld/Targeter.cs:297-300).
            Action<LocalTargetInfo> highlightAction =
                HighlightActionField?.GetValue(Find.Targeter) as Action<LocalTargetInfo>;
            if (highlightAction != null)
            {
                highlightAction(target);
            }
            else
            {
                GenDraw.DrawTargetHighlight(target);
            }
        }
    }
}
