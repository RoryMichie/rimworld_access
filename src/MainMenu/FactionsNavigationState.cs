using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>Outcome of a faction-delete request (the scope composes the spoken result).</summary>
    internal enum FactionDeleteResult
    {
        Removed,
        LockedByScenario,
        TutorialBlocked,
        NotFound,
    }

    /// <summary>One visible faction row: its def, its ACTUAL index in the page's faction list, and its scenario-lock state.</summary>
    internal struct FactionRowInfo
    {
        public FactionDef Def;
        public int ListIndex;
        public bool Locked;
    }

    /// <summary>
    /// Headless state helper for the faction configuration of
    /// Page_CreateWorldParams. Since the migration to
    /// <see cref="RimWorldAccess.Shell.WorldParamsScreenScope"/>, the Factions
    /// region owns the selection cursor, typeahead, and every row announcement,
    /// so this class keeps only what the scope and the add-menu overlay ride as
    /// game vehicles: the visible-row query (paired with each row's real list
    /// index and lock state), the delete/reset mutation cores, the MUTATION-C
    /// warning mirror, and the forwarders onto <see cref="FactionAddMenuState"/>
    /// (the add menu's own navigation and typeahead forwarders are retired:
    /// WorldParamsAddFactionScope owns them now).
    /// Reflected access into the shared page instance goes through
    /// <see cref="WorldParamsPageBridge"/>.
    ///
    /// RETIRED (replaced the old FocusScope + state-machine): the selectedIndex
    /// cursor, the faction-list typeahead, the
    /// Activate/Deactivate section toggle, NavigateUp/Down/Home/End,
    /// AnnounceCurrentFaction/AnnounceFactionWithSearch/AnnounceWarnings, and
    /// NotifyFactionsChangedExternally (its only jobs — re-clamp the now-removed
    /// cursor and re-announce via the removed announce path — are subsumed by the
    /// scope's region model, which rebuilds its faction rows and re-announces the
    /// current row after every reset).
    /// </summary>
    public static class FactionsNavigationState
    {
        public static bool IsAddMenuOpen => FactionAddMenuState.IsOpen;

        // ===== LIFECYCLE =====

        public static void Reset()
        {
            WorldParamsPageBridge.Unbind();
            FactionAddMenuState.Reset();
        }

        // ===== FACTION LIST QUERY =====

        /// <summary>
        /// The visible faction rows in draw order, each paired with its ACTUAL
        /// index in the page's faction list (the index vanilla's own DoRow
        /// removes by) and its scenario-lock state. The scope builds one row per
        /// entry; the delete chord passes back the chosen row's
        /// <see cref="FactionRowInfo.ListIndex"/>.
        /// </summary>
        internal static List<FactionRowInfo> GetVisibleFactionRows()
        {
            var rows = new List<FactionRowInfo>();
            var factions = WorldParamsPageBridge.Factions;
            for (int i = 0; i < factions.Count; i++)
            {
                if (!factions[i].displayInFactionSelection)
                {
                    continue;
                }
                rows.Add(new FactionRowInfo
                {
                    Def = factions[i],
                    ListIndex = i,
                    Locked = IsFactionLocked(factions[i]),
                });
            }
            return rows;
        }

        // ===== FACTION LIST ACTIONS =====

        /// <summary>
        /// The kept delete mutation core (lock check + tutorial gate +
        /// RemoveAt), refactored to take the ACTUAL faction-list index of the
        /// cursor's row so it removes exactly that occurrence — the same
        /// RemoveAt(index) vanilla's WorldFactionsUIUtility.DoRow performs
        /// (decompiled :185-190), not the first def match the retired
        /// cursor-reading version used. The scope announces the outcome.
        /// </summary>
        internal static FactionDeleteResult DeleteFactionAt(int listIndex)
        {
            var factions = WorldParamsPageBridge.Factions;
            if (listIndex < 0 || listIndex >= factions.Count)
            {
                return FactionDeleteResult.NotFound;
            }

            FactionDef faction = factions[listIndex];

            if (IsFactionLocked(faction))
            {
                return FactionDeleteResult.LockedByScenario;
            }

            if (!TutorSystem.AllowAction("ConfiguringWorldFactions"))
            {
                return FactionDeleteResult.TutorialBlocked;
            }

            factions.RemoveAt(listIndex);
            return FactionDeleteResult.Removed;
        }

        // ===== RESET =====

        /// <summary>
        /// Rides Page_CreateWorldParams's own private ResetFactionCounts() (Category B
        /// via reflection -- no public wrapper exists) -- vanilla's "Reset factions"
        /// button, including its Tick_Tiny click sound (Page_CreateWorldParams.cs:186-190).
        /// Rebuilds the whole faction list from each FactionDef's
        /// startingCountAtWorldCreation; the scope refreshes its rows and re-announces.
        /// </summary>
        public static void ResetFactions()
        {
            if (!WorldParamsPageBridge.IsBound) return;

            WorldParamsPageBridge.ResetFactionCounts();
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();

            TolkHelper.Speak("RimWorldAccess.Factions.ResetDone".Loc());
        }

        // ===== ADD MENU (forwarders onto FactionAddMenuState) =====

        public static void OpenAddMenu() => FactionAddMenuState.Open();

        public static void CloseAddMenu()
        {
            FactionAddMenuState.Close();
            // Re-read whichever Factions-region row the cursor rests on, so focus
            // is clear when the overlay goes away (the scope owns the cursor now).
            RimWorldAccess.Shell.WorldParamsScreenScope.Active?.ReannounceCurrent();
        }

        public static void AddMenuConfirm(int index) => FactionAddMenuState.Confirm(index);

        // ===== HELPERS =====

        internal static bool IsFactionLocked(FactionDef faction)
        {
            // Check scenario parts for preventRemovalOfFaction
            // During world creation, Current.Game.Scenario should be set
            Scenario scenario = Current.Game?.Scenario;
            if (scenario == null) return false;

            foreach (ScenPart part in scenario.AllParts)
            {
                if (part.def.preventRemovalOfFaction == faction)
                {
                    return true;
                }
            }
            return false;
        }

        // MUTATION-C: mirrors WorldFactionsUIUtility.DoWindowContents's warning block
        // (RimWorld.Planet.WorldFactionsUIUtility.cs:97-129) -- no invokable vehicle
        // exists (it builds a StringBuilder for direct IMGUI drawing), so the warning
        // set and its Odyssey-gated duplicates are hand-copied here. Matched by
        // FactionDefOf, not defName strings -- string matching on defName is exactly
        // the "don't match on translated/identity strings" trap this codebase's
        // doctrine warns about (FactionDefOf survives renames/mod overrides the same
        // way vanilla's own check does).
        internal static List<string> GetCurrentWarnings()
        {
            var warnings = new List<string>();
            var factions = WorldParamsPageBridge.Factions;
            int visibleCount = factions.Count(x => !x.hidden);

            if (visibleCount == 0)
            {
                warnings.Add("RimWorldAccess.Factions.WarningNoFactions".Translate());
                return warnings;
            }

            if (ModsConfig.RoyaltyActive && !factions.Contains(FactionDefOf.Empire))
            {
                warnings.Add("RimWorldAccess.Factions.WarningMissingEmpire".Translate());
            }

            if (!factions.Contains(FactionDefOf.Mechanoid))
            {
                warnings.Add("RimWorldAccess.Factions.WarningMissingMechanoid".Translate());
            }

            if (!factions.Contains(FactionDefOf.Insect))
            {
                warnings.Add("RimWorldAccess.Factions.WarningMissingInsect".Translate());
            }

            // Odyssey shows a SECOND, Odyssey-specific warning for the same two
            // factions -- vanilla duplicates rather than replaces the base ones
            // (WorldFactionsUIUtility.cs:118-128).
            if (ModsConfig.OdysseyActive)
            {
                if (!factions.Contains(FactionDefOf.Mechanoid))
                {
                    warnings.Add("RimWorldAccess.Factions.WarningMissingMechanoidOdyssey".Translate());
                }
                if (!factions.Contains(FactionDefOf.Insect))
                {
                    warnings.Add("RimWorldAccess.Factions.WarningMissingInsectOdyssey".Translate());
                }
            }

            return warnings;
        }
    }
}
