using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages keyboard navigation for forbid/unforbid controls (CompForbiddable).
    /// Allows toggling forbidden status via keyboard shortcuts.
    /// </summary>
    public static class ForbidControlState
    {
        private static CompForbiddable forbiddable = null;
        private static Building building = null;
        private static bool isActive = false;

        public static bool IsActive => isActive;

        /// <summary>Live forbidden flag for ForbidControlScope's checkbox row (read fresh each announce, never cached).</summary>
        public static bool Forbidden => forbiddable != null && forbiddable.Forbidden;

        public static void Open(Building targetBuilding)
        {
            if (!GuardHelper.RequireBuilding(targetBuilding)) return;

            CompForbiddable comp = targetBuilding.TryGetComp<CompForbiddable>();
            if (comp == null)
            {
                TolkHelper.Speak("RimWorldAccess.Building.Forbid.NoForbidComponent".Loc(), SpeechPriority.High);
                return;
            }

            building = targetBuilding;
            forbiddable = comp;
            isActive = true;
            MapNavigationState.SuppressMapNavigation = true;

            AnnounceCurrentStatus();
        }

        public static void Close()
        {
            forbiddable = null;
            building = null;
            isActive = false;
            MapNavigationState.SuppressMapNavigation = false;
        }

        /// <summary>
        /// MUTATION vehicle B: CompForbiddable.Forbidden (decompiled
        /// RimWorld/CompForbiddable.cs:14-46) is a real self-gating setter,
        /// not a bare field — it early-outs on a same-value write and, when
        /// spawned, performs vanilla's own side effects (listerHaulables/
        /// listerMergeables forbidden-notify, a door's reachability-cache
        /// clear, UpdateOverlayHandle). The one thing this skips versus
        /// vanilla's real Command_Toggle.toggleAction is
        /// PlayerKnowledgeDatabase.KnowledgeDemonstrated — tutorial-system
        /// telemetry only, no gameplay effect, omitted deliberately. The
        /// sound cue and re-announce now live in ForbidControlScope, which
        /// speaks the toggle-doctrine state-change fragment for its checkbox
        /// row instead of this method's old full-status re-announce.
        /// </summary>
        public static void ToggleForbidden()
        {
            if (forbiddable == null || building == null)
                return;

            forbiddable.Forbidden = !forbiddable.Forbidden;
        }

        public static void AnnounceCurrentStatus()
        {
            if (forbiddable == null || building == null)
                return;

            string status = forbiddable.Forbidden
                ? "RimWorldAccess.Building.Forbid.StatusForbidden".Translate()
                : "RimWorldAccess.Building.Forbid.StatusAllowed".Translate();

            TolkHelper.Speak("RimWorldAccess.Building.Forbid.LabelStatus".Loc(building.LabelCap, status));
        }

        public static void AnnounceDetailedStatus()
        {
            if (forbiddable == null || building == null)
                return;

            var b = new AnnouncementBuilder();
            b.Add(building.LabelCap);

            if (forbiddable.Forbidden)
            {
                b.Add("RimWorldAccess.Building.Forbid.DetailStatusForbidden".Translate());
                b.Add("RimWorldAccess.Building.Forbid.DetailNoInteract".Translate());
                b.Add("RimWorldAccess.Building.Forbid.DetailNoHaulUseEquip".Translate());
            }
            else
            {
                b.Add("RimWorldAccess.Building.Forbid.DetailStatusAllowed".Translate());
                b.Add("RimWorldAccess.Building.Forbid.DetailCanInteract".Translate());
            }

            TolkHelper.SpeakData(b.Build());
        }
    }
}
