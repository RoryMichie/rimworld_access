using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patches for Dialog_FormCaravan: lifecycle for CaravanFormationState, the Escape/Enter
    /// blockers CaravanFormationScope depends on, and the keyboard-mode indicator.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_FormCaravan))]
    public static class CaravanFormationPatch
    {
        /// <summary>
        /// Blocks vanilla's Escape handling over caravan dialogs while a typeahead search is live.
        /// Patches the base Window class, not the dialog.
        /// </summary>
        [HarmonyPatch(typeof(Window), "OnCancelKeyPressed")]
        [HarmonyPrefix]
        public static bool Window_OnCancelKeyPressed_Prefix(Window __instance)
        {
            // Confirmation dialogs are real windows, and the WindowStack's own top-down cancel walk
            // lets the dialog on top catch Escape before anything beneath it.

            if (__instance is Dialog_FormCaravan || __instance is Dialog_SplitCaravan)
            {
                // Deliberately narrow. Quantity/stat-breakdown overlays need no term here: both are
                // modal scopes whose Cancel claims stamp ShellFrameStamps.MarkCancelConsumed, which
                // the general WindowCancelKeyRouterPatch already consults. A WindowlessInspectionState
                // term must not come back either — it left Escape dead for a dialog opened from the
                // inspection tree (see LordJobDialogScope.IsLive).
                if (__instance is Dialog_FormCaravan && CaravanFormationState.HasActiveTypeahead)
                {
                    return false;
                }
                if (__instance is Dialog_SplitCaravan && SplitCaravanState.HasActiveTypeahead)
                {
                    return false;
                }
            }
            return true; // Let original method run
        }

        /// <summary>
        /// Blocks vanilla's Enter handling for Dialog_SplitCaravan, which inherits Window's own
        /// OnAcceptKeyPressed with closeOnAccept=true and would otherwise close on Enter.
        /// </summary>
        [HarmonyPatch(typeof(Window), "OnAcceptKeyPressed")]
        [HarmonyPrefix]
        public static bool Window_OnAcceptKeyPressed_Prefix(Window __instance)
        {
            if (__instance is Dialog_SplitCaravan && SplitCaravanState.IsActive)
            {
                return false; // Skip original - our SplitCaravanState handles Enter key
            }
            return true; // Let original method run
        }

        /// <summary>
        /// Decides whether CaravanFormationScope engages for this open. The scope factory attaches
        /// and announces on its own; this patch's only job is marking the two opens where the dialog
        /// is not yet ready to be driven, which the factory answers with no scope at all.
        /// </summary>
        [HarmonyPatch("PostOpen")]
        [HarmonyPostfix]
        public static void PostOpen_Postfix(Dialog_FormCaravan __instance)
        {
            StopStaleRoutePlannerSession(__instance);

            // The world-map "Form caravan" flow opens the route planner explicitly and activates
            // later, so suppress this open.
            if (CaravanFormationState.PendingRoutePlannerOpen)
            {
                CaravanFormationState.PendingRoutePlannerOpen = false;
                CaravanFormationState.MarkSuppressed();
                return;
            }

            // A non-reform dialog opened from a colony building gizmo starts with no destination, and
            // vanilla's PostOpen already started the route planner and pulled this dialog off the
            // stack. Route the player into the planner; the dialog reopens with a valid destination
            // and the scope attached to that open engages normally.
            if (ShouldChooseRouteFirst(__instance))
            {
                CaravanFormationState.BeginColonyOriginatedRouteChoice(__instance);
                return;
            }

        }

        /// <summary>
        /// Mirrors <c>WorldRoutePlanner.Start</c>'s own entry guard for the opens where no Start runs
        /// at all: with Vehicle Framework installed the route-first handoff is transpiled away, so a
        /// planner session left active by an abandoned caravan flow survives into the new dialog and
        /// its stale dialog reference hijacks the next route confirmation. Never stops a session
        /// vanilla just bound to THIS dialog.
        /// </summary>
        private static void StopStaleRoutePlannerSession(Dialog_FormCaravan __instance)
        {
            WorldRoutePlanner planner = Find.WorldRoutePlanner;
            if (planner == null || !planner.Active)
                return;

            FieldInfo dialogField = AccessTools.Field(typeof(WorldRoutePlanner), "currentFormCaravanDialog");
            if (dialogField == null)
                return;
            if (ReferenceEquals(dialogField.GetValue(planner), __instance))
                return;

            planner.Stop();
        }

        /// <summary>
        /// True when a freshly-opened caravan dialog still needs a destination route before it can be
        /// sent. Excludes reform dialogs and a dialog reopening after route confirmation.
        /// </summary>
        private static bool ShouldChooseRouteFirst(Dialog_FormCaravan dialog)
        {
            // A re-entrant PostOpen while already choosing a route for this dialog.
            if (CaravanFormationState.IsColonyRouteChoiceActive)
                return false;

            WorldRoutePlanner planner = Find.WorldRoutePlanner;
            if (planner == null || !planner.Active || !planner.FormingCaravan)
                return false; // Route planner isn't driving this dialog.

            if (IsReform(dialog))
                return false; // Reform caravans run their own (often route-free) flow.

            // A chosen destination means the dialog is reopening after route confirmation.
            FieldInfo destField = VanillaAccess.GetField(typeof(Dialog_FormCaravan), "destinationTile");
            if (destField == null)
                return false;
            PlanetTile destination = (PlanetTile)destField.GetValue(dialog);
            return !destination.Valid;
        }

        /// <summary>Reads the dialog's private reform flag.</summary>
        private static bool IsReform(Dialog_FormCaravan dialog)
        {
            FieldInfo reformField = AccessTools.Field(typeof(Dialog_FormCaravan), "reform");
            return reformField != null && (bool)reformField.GetValue(dialog);
        }

        /// <summary>
        /// Tears the state down on close, unless the route planner merely has the dialog off the
        /// stack. Stops a still-active route planner, and announces cancellation unless a send was
        /// attempted, which the game announces itself.
        /// </summary>
        [HarmonyPatch("PostClose")]
        [HarmonyPostfix]
        public static void PostClose_Postfix(Dialog_FormCaravan __instance)
        {
            // The dialog reopens once the route is chosen, so keep the state while choosingRoute.
            if (__instance.choosingRoute)
                return;

            // Close resets this.
            bool wasSendAttempted = CaravanFormationState.SendAttempted;

            CaravanFormationState.Close();

            // Vanilla starts WorldRoutePlanner in PostOpen for every caravan dialog but never stops
            // it on close after a successful send, leaving a stale dialog reference that soft-locks
            // Enter on the world map.
            if (Find.WorldRoutePlanner != null && Find.WorldRoutePlanner.Active)
            {
                Find.WorldRoutePlanner.Stop();
            }

            // A successful send is announced by the game itself.
            if (!wasSendAttempted)
            {
                TolkHelper.Speak("RimWorldAccess.Caravan.Form.FormationCancelled".Loc());
            }
        }

        /// <summary>
        /// Cleans up state when the route planner stops. Announces nothing — PostClose does that —
        /// which avoids false "cancelled" calls when the planner stops during its own startup timing.
        /// </summary>
        [HarmonyPatch("Notify_NoLongerChoosingRoute")]
        [HarmonyPostfix]
        public static void Notify_NoLongerChoosingRoute_Postfix(Dialog_FormCaravan __instance)
        {
            // Only when the dialog is not being reopened.
            if (!Find.WindowStack.IsOpen(__instance))
            {
                CaravanFormationState.Close();
            }
        }

        /// <summary>
        /// Blocks the dialog's own OnAcceptKeyPressed while the shell drives it; without this, Enter
        /// triggers TrySend and its validation errors.
        /// </summary>
        [HarmonyPatch("OnAcceptKeyPressed")]
        [HarmonyPrefix]
        public static bool OnAcceptKeyPressed_Prefix()
        {
            // Let through the reentrant call CaravanFormationScope.Send makes itself.
            if (CaravanFormationState.SendingFromOurCode)
            {
                return true;
            }
            if (CaravanFormationState.IsActive)
            {
                return false; // Skip original method
            }
            return true; // Let original method run
        }


        /// <summary>Draws the keyboard-mode indicator; input belongs to CaravanFormationScope.</summary>
        [HarmonyPatch("DoWindowContents")]
        [HarmonyPostfix]
        public static void DoWindowContents_Postfix(Dialog_FormCaravan __instance, Rect inRect)
        {
            if (!CaravanFormationState.IsActive)
                return;

            DrawKeyboardModeIndicator(inRect);
        }

        /// <summary>Draws the "keyboard mode active" indicator at the top of the dialog.</summary>
        private static void DrawKeyboardModeIndicator(Rect inRect)
        {
            float indicatorWidth = 250f;
            float indicatorHeight = 30f;
            Rect indicatorRect = new Rect(inRect.x + 10f, inRect.y + 10f, indicatorWidth, indicatorHeight);

            Color backgroundColor = new Color(0.2f, 0.4f, 0.6f, 0.85f);
            Widgets.DrawBoxSolid(indicatorRect, backgroundColor);

            Color borderColor = new Color(0.4f, 0.6f, 1.0f, 1.0f);
            Widgets.DrawBox(indicatorRect, 1);

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(indicatorRect, (string)"RimWorldAccess.Caravan.Form.KeyboardModeActive".Translate());

            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;

            float instructionsY = indicatorRect.yMax + 5f;
            float instructionsWidth = 500f;
            float instructionsHeight = 60f;
            Rect instructionsRect = new Rect(inRect.x + 10f, instructionsY, instructionsWidth, instructionsHeight);

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperLeft;

            string instructions = "RimWorldAccess.Caravan.Form.KeyboardInstructions".Translate();

            Widgets.Label(instructionsRect, instructions);

            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }
    }
}
