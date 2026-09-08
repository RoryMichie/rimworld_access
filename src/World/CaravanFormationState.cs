using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Static bridge between the caravan-formation Harmony patches, the ambient guards reading its
    /// liveness, and the live <see cref="Shell.CaravanFormationScope"/> that owns the tab, cursor,
    /// search and announce state.
    /// The colony-originated destination sub-flow lives here, not on the scope, because it runs while
    /// <see cref="Dialog_FormCaravan"/> is off the WindowStack — ScopeForWindow attaches only to a
    /// window actually on the stack, so that sub-flow must outlive any one scope.
    /// </summary>
    public static class CaravanFormationState
    {
        internal static Shell.CaravanFormationScope ActiveScope;

        private static Dialog_FormCaravan currentDialog;
        private static bool pendingRoutePlannerOpen;
        private static Map routeChoiceOriginMap;
        private static bool sendAttempted;
        private static bool suppressNextActivation;

        /// <summary>True only while the formation-tabs scope is live and driving the dialog.</summary>
        public static bool IsActive => ActiveScope != null && ActiveScope.IsLive;

        /// <summary>Whether a send was attempted; PostClose picks its announcement from this.</summary>
        public static bool SendAttempted => sendAttempted;

        /// <summary>Restores normal cancel behaviour after an abandoned send.</summary>
        public static void ResetSendAttempted()
        {
            sendAttempted = false;
        }

        /// <summary>Whether typeahead is active; the OnCancelKeyPressed patch blocks the close if so.</summary>
        public static bool HasActiveTypeahead => ActiveScope != null && ActiveScope.HasActiveTypeahead;

        /// <summary>Whether the player is choosing a route for a caravan started from a colony building gizmo.</summary>
        public static bool IsColonyRouteChoiceActive => routeChoiceOriginMap != null;

        /// <summary>Gets the colony map a colony-originated route choice was started from, or null. Read before stopping the route planner, since stopping it resets this state.</summary>
        public static Map RouteChoiceOriginMap => routeChoiceOriginMap;

        /// <summary>Set before adding the dialog to the stack when the route planner should open first.</summary>
        public static bool PendingRoutePlannerOpen
        {
            get => pendingRoutePlannerOpen;
            set => pendingRoutePlannerOpen = value;
        }

        /// <summary>
        /// Bypass flag for <see cref="CaravanFormationPatch.OnAcceptKeyPressed_Prefix"/> while the
        /// scope's own Send() calls the dialog's real OnAcceptKeyPressed directly.
        /// </summary>
        public static bool SendingFromOurCode { get; internal set; }

        /// <summary>Triggers caravan reformation from the current map.</summary>
        public static void TriggerReformation()
        {
            if (!GuardHelper.RequireMap(out Map currentMap, SpeechPriority.High)) return;

            if (currentMap.IsPlayerHome)
            {
                TolkHelper.Speak("RimWorldAccess.Caravan.Form.CannotReformFromHome".Loc(), SpeechPriority.High);
                return;
            }

            MapParent mapParent = currentMap.Parent;
            if (mapParent == null)
            {
                TolkHelper.Speak("RimWorldAccess.Caravan.Form.MapHasNoParent".Loc(), SpeechPriority.High);
                return;
            }

            FormCaravanComp formCaravanComp = mapParent.GetComponent<FormCaravanComp>();
            if (formCaravanComp == null)
            {
                TolkHelper.Speak("RimWorldAccess.Caravan.Form.MapDoesNotSupport".Loc(), SpeechPriority.High);
                return;
            }

            // FormCaravanComp.CanReformNow() is vanilla's own combined gate, so a map with no free
            // colonists left is refused exactly as vanilla's gizmo refuses to appear.
            if (!formCaravanComp.CanReformNow())
            {
                // countDormantPawnsAsHostile: true, matching vanilla's own disabled-reason check —
                // a dormant threat still blocks reformation and must be named as the reason.
                if (GenHostility.AnyHostileActiveThreatToPlayer(currentMap, countDormantPawnsAsHostile: true))
                {
                    TolkHelper.Speak("RimWorldAccess.Caravan.Form.CannotReformEnemiesPresent".Loc(), SpeechPriority.High);
                }
                else
                {
                    TolkHelper.Speak("RimWorldAccess.Caravan.Form.CannotReformAtThisTime".Loc(), SpeechPriority.High);
                }
                return;
            }

            Dialog_FormCaravan reformDialog = new Dialog_FormCaravan(currentMap, reform: true);
            Find.WindowStack.Add(reformDialog);

            TolkHelper.Speak("RimWorldAccess.Caravan.Form.OpeningReformation".Loc(), SpeechPriority.Normal);
        }

        /// <summary>
        /// Routes the player into the world route planner for a caravan dialog opened from a colony
        /// building gizmo. PostOpen already started the planner and took the dialog off the stack, so
        /// this only moves keyboard focus to the world map; confirming reopens the dialog with a
        /// fresh scope, and cancelling restores the colony map through the planner's own handling.
        /// </summary>
        public static void BeginColonyOriginatedRouteChoice(Dialog_FormCaravan dialog)
        {
            if (dialog == null)
                return;

            Map originMap = VanillaAccess.GetField(typeof(Dialog_FormCaravan), "map")?.GetValue(dialog) as Map;
            routeChoiceOriginMap = originMap ?? Find.CurrentMap;
            currentDialog = dialog;
            MarkSuppressed();

            CameraJumper.TryShowWorld();
            if (!WorldNavigationState.IsActive)
            {
                WorldNavigationState.Open(WorldNavContext.InGame);
            }

            string addWaypointsPrompt = "RoutePlannerAddOneOrMoreWaypoints".Translate();
            TolkHelper.SpeakData((string)"RimWorldAccess.Caravan.Form.ChooseDestinationPrompt".Translate(addWaypointsPrompt));
        }

        /// <summary>
        /// Restores the colony map view after a colony-originated route choice ends; a no-op for
        /// caravans formed from the world map.
        /// </summary>
        private static void EndColonyOriginatedRouteChoice()
        {
            if (routeChoiceOriginMap == null)
                return;

            Map origin = routeChoiceOriginMap;
            routeChoiceOriginMap = null;

            if (WorldNavigationState.IsActive)
            {
                WorldNavigationState.Close();
            }

            CameraJumper.TryHideWorld();

            if (origin != null && Find.Maps.Contains(origin))
            {
                Current.Game.CurrentMap = origin;
            }
        }

        /// <summary>Records that Send() was called, before the game's own OnAcceptKeyPressed runs.</summary>
        internal static void NoteSendAttempted()
        {
            sendAttempted = true;
        }

        /// <summary>
        /// Marks the two opens that must get NO scope at all — route-planner-driven, or a
        /// colony-originated destination choice — and is consumed once by the scope factory.
        /// </summary>
        internal static void MarkSuppressed()
        {
            suppressNextActivation = true;
        }

        /// <summary>True and clears the flag exactly once. The Dialog_FormCaravan factory answers it with no scope at all: vanilla's PostOpen hands the dialog to the route planner and pulls it off the WindowStack inside Add, so any scope attached there would announce a screen the player never sees.</summary>
        internal static bool ConsumeSuppression()
        {
            bool wasSuppressed = suppressNextActivation;
            suppressNextActivation = false;
            return wasSuppressed;
        }

        /// <summary>
        /// A freshly-constructed scope is genuinely engaging: it becomes the active scope and
        /// restores the colony map when this open follows a colony-originated destination choice.
        /// </summary>
        internal static void NotifyScopeAttached(Shell.CaravanFormationScope scope)
        {
            EndColonyOriginatedRouteChoice();
            ActiveScope = scope;
        }

        internal static void NotifyScopeDetached(Shell.CaravanFormationScope scope)
        {
            if (ActiveScope == scope)
            {
                ActiveScope = null;
            }
        }

        /// <summary>Closes keyboard navigation once the dialog's own session is over.</summary>
        public static void Close()
        {
            currentDialog = null;
            routeChoiceOriginMap = null;
            pendingRoutePlannerOpen = false;
            suppressNextActivation = false;
            sendAttempted = false;
        }
    }
}
