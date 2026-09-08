using System;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Rings the focused person, animal, relic, or item row of
    /// <c>Dialog_ChooseThingsForNewColony</c> (the Archonexus new-colony selection
    /// screen). Vanilla lays each row out inside <c>DoRow</c>'s own balanced group, which is
    /// geometry no capture pass records, so the ring stays on the direct-draw shape
    /// <see cref="ConfirmDialogScope"/>/<see cref="ScheduleScope"/>'s own row patches use
    /// rather than on <c>ScreenScope.FocusedContentRect</c>: match identity against the
    /// scope's live focus and call <see cref="FocusRing.Draw"/> right there.
    ///
    /// <c>DoRow</c> wraps its whole body in a balanced BeginGroup(rect)/EndGroup(), so by
    /// the time this postfix runs (after the method returns) GUI.matrix is back to the
    /// enclosing scroll-view group -- the same space <c>rect</c> was passed in, so drawing
    /// it directly here is valid (no VisibleScreenRect conversion needed; FocusRing.Draw
    /// operates in the current GUI matrix like any other Widgets call).
    /// </summary>
    [HarmonyPatch(typeof(Dialog_ChooseThingsForNewColony), "DoRow")]
    internal static class ArchonexusRowDrawPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Dialog_ChooseThingsForNewColony __instance, Rect rect, Thing t)
        {
            try
            {
                ArchonexusColonyScope scope = FocusStack.Top as ArchonexusColonyScope;
                if (scope == null || !scope.Owns(__instance))
                {
                    return;
                }
                if (t == null || !ReferenceEquals(t, scope.FocusedThing))
                {
                    return;
                }
                FocusRing.Draw(rect);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Archonexus row draw error", ex);
            }
        }
    }
}
