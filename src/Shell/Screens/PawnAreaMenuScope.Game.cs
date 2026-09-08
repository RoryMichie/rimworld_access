using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The keyboard focus scope for the pawn area-assignment picker: one flat content region
    /// ("Area") listing Manage Areas, Unrestricted, then every assignable area. The mod owns no
    /// window here — the surface is the windowless <see cref="PawnAreaMenuState"/> state machine —
    /// so the scope rides the focus stack through <see cref="PawnAreaMenuScopeMirror"/>.
    ///
    /// Deliberately bypasses the base's shared typeahead engine
    /// (<see cref="ScreenScope.EnableTypeahead"/> stays false, with
    /// <see cref="CharSink"/>/<see cref="HandleChar"/> overridden) and the generic composer: this
    /// screen's announcements are bespoke translated phrases — cell counts, a "(current)" suffix,
    /// distinct Manage-Areas/Unrestricted/Area formats — that the composer grammar cannot produce.
    ///
    /// The Manage-Areas drill-in needs no mirror gate: <see cref="OpenManageAreas"/> calls
    /// <see cref="Close"/> before raising the real <see cref="Dialog_ManageAreas"/>.
    /// </summary>
    public sealed class PawnAreaMenuScope : ScreenScope, ICharSink
    {
        private const int ManageAreasIndex = 0;
        private const int UnrestrictedIndex = 1;
        private static readonly object ManageAreasSentinel = new object();

        private Pawn targetPawn;
        private readonly List<object> areaOptions = new List<object>();
        private readonly TypeaheadSearchHelper typeaheadHelper = new TypeaheadSearchHelper();
        private bool announcedOpen;

        public PawnAreaMenuScope()
        {
            // This scope owns Escape unconditionally (clear an active search, else cancel the
            // menu); the base's typeahead-gated Cancel claim can never win with
            // EnableTypeahead false.
            Claim(SharedMenuGrammar.Cancel, delegate { HandleCancelKey(); });
            Claim(SharedMenuGrammar.SearchBackspace, delegate { HandleSearchBackspace(); },
                when: () => typeaheadHelper.HasActiveSearch);
        }

        public override string Name
        {
            get { return "pawn-area-menu"; }
        }

        public override bool OwnsCancel
        {
            get { return true; }
        }

        public override ICharSink CharSink
        {
            get { return this; }
        }

        /// <summary>Bypasses the base's shared typeahead engine entirely — see the class remarks.</summary>
        public override bool HandleChar(char c)
        {
            if (!TypeaheadMatcher.AcceptsSearchChar(c, typeaheadHelper.HasActiveSearch))
            {
                return false;
            }
            HandleTypeahead(c);
            return true;
        }

        // ScreenScope content contract.

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        /// <summary>Reuses vanilla's own Allowed Area column label — no new translation key.</summary>
        protected override string ContentRegionName(int region)
        {
            PawnColumnDef def = DefDatabase<PawnColumnDef>.GetNamedSilentFail("AllowedArea");
            return def != null ? def.LabelCap.Resolve() : "Area";
        }

        protected override int ContentItemCount(int region)
        {
            return areaOptions.Count;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            if (index >= 0 && index < areaOptions.Count)
            {
                d.Label = LabelFor(areaOptions[index]);
            }
            return d;
        }

        protected override void ActivateContentItem(int region, int index)
        {
            Confirm();
        }

        /// <summary>
        /// Populates the map's assignable areas plus the Manage Areas/Unrestricted sentinels once
        /// per open; OnPush clears the cache so a fresh open rebuilds.
        /// </summary>
        protected override void RefreshContent()
        {
            if (areaOptions.Count > 0)
                return;
            Map map = Find.CurrentMap;
            if (map?.areaManager == null)
                return;

            areaOptions.Add(ManageAreasSentinel);
            areaOptions.Add(null); // Unrestricted
            areaOptions.AddRange(map.areaManager.AllAreas.Where(a => a.AssignableAsAllowed()));
        }

        // Lifecycle.

        public override void OnPush()
        {
            base.OnPush();
            targetPawn = PawnAreaMenuState.TargetPawn;
            areaOptions.Clear();
            typeaheadHelper.ClearSearch();
            announcedOpen = false;
        }

        /// <summary>
        /// Parks the cursor on the pawn's current area and speaks the opening announcement, once
        /// per open.
        /// </summary>
        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen)
                return;
            announcedOpen = true;
            if (areaOptions.Count == 0 || targetPawn == null)
                return;

            Area currentArea = targetPawn.playerSettings?.AreaRestrictionInPawnCurrentMap;
            int initialIndex;
            if (currentArea == null)
            {
                initialIndex = UnrestrictedIndex;
            }
            else
            {
                int index = areaOptions.IndexOf(currentArea);
                initialIndex = index >= 0 ? index : UnrestrictedIndex;
            }
            Model.CurrentRegion?.MoveTo(initialIndex);

            string currentAreaName = currentArea?.Label ?? "RimWorldAccess.Pawns.Area.Unrestricted".Translate().ToString();
            TolkHelper.Speak("RimWorldAccess.Pawns.Area.OpenAnnouncement".Loc(targetPawn.LabelShort, currentAreaName));
            AnnounceCurrentSelection();
        }

        // Navigation.

        protected override void MoveItem(int delta)
        {
            RefreshModel();
            if (typeaheadHelper.HasActiveSearch && !typeaheadHelper.HasNoMatches)
            {
                int match = delta > 0
                    ? typeaheadHelper.GetNextMatch(Model.CurrentRegion.Index)
                    : typeaheadHelper.GetPreviousMatch(Model.CurrentRegion.Index);
                if (match >= 0)
                {
                    MenuHelper.SoundMatchMove(Model.CurrentRegion.Index, match, delta);
                    Model.CurrentRegion.MoveTo(match);
                    AnnounceWithSearch();
                }
                return;
            }
            if (areaOptions.Count == 0)
                return;
            Model.CurrentRegion.MoveBy(delta);
            AnnounceCurrentSelection();
        }

        protected override void MoveItemEdge(bool first)
        {
            RefreshModel();
            if (areaOptions.Count == 0)
                return;
            if (typeaheadHelper.HasActiveSearch && !typeaheadHelper.HasNoMatches)
            {
                int match = first ? typeaheadHelper.GetFirstMatch() : typeaheadHelper.GetLastMatch();
                Model.CurrentRegion.MoveTo(match);
                AnnounceWithSearch();
                return;
            }
            Model.CurrentRegion.MoveTo(first ? 0 : areaOptions.Count - 1);
            typeaheadHelper.ClearSearch();
            AnnounceCurrentSelection();
        }

        // Confirm / Cancel / Escape / typeahead.

        private void Confirm()
        {
            if (targetPawn == null || targetPawn.playerSettings == null)
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.Area.PawnGone".Loc());
                Close();
                return;
            }

            ListModel region = Model.CurrentRegion;
            int selectedIndex = region == null ? -1 : region.Index;

            if (selectedIndex == ManageAreasIndex)
            {
                OpenManageAreas();
                return;
            }

            if (selectedIndex >= 0 && selectedIndex < areaOptions.Count)
            {
                object focused = areaOptions[selectedIndex];
                Area area = focused as Area; // null for Unrestricted

                // MUTATION-C: mirrors InspectPaneFiller.DrawAreaAllowed's gate (RimWorld/InspectPaneFiller.cs:143)
                // and its bare-field write via AreaUtility.MakeAllowedAreaListFloatMenu's selAction delegate;
                // no gated setter exists on AreaRestrictionInPawnCurrentMap. PawnAreaMenuState.Open's
                // CanEditAllowedArea gate (mirroring PawnColumnWorker_AllowedArea.DoCell) already refused
                // entry for a non-player-faction, mutant-that-doesn't-respect-areas, or overseer-less
                // mechanoid pawn before targetPawn was set.
                targetPawn.playerSettings.AreaRestrictionInPawnCurrentMap = area;

                string areaName = area?.Label ?? "RimWorldAccess.Pawns.Area.Unrestricted".Translate().ToString();
                TolkHelper.Speak("RimWorldAccess.Pawns.Area.Applied".Loc(targetPawn.LabelShort, areaName));
                Close();
            }
        }

        private void Cancel()
        {
            TolkHelper.Speak("RimWorldAccess.Pawns.Area.Cancelled".Loc());
            Close();
        }

        private void HandleCancelKey()
        {
            if (typeaheadHelper.HasActiveSearch)
            {
                typeaheadHelper.ClearSearchAndAnnounce();
                AnnounceCurrentSelection();
                return;
            }
            Cancel();
        }

        private void HandleSearchBackspace()
        {
            List<string> labels = GetOptionLabels();
            if (typeaheadHelper.ProcessBackspace(labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    Model.CurrentRegion?.MoveTo(newIndex);
                }
                AnnounceWithSearch();
            }
        }

        private void HandleTypeahead(char c)
        {
            List<string> labels = GetOptionLabels();
            if (typeaheadHelper.ProcessCharacterInput(c, labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    Model.CurrentRegion?.MoveTo(newIndex);
                    AnnounceWithSearch();
                }
            }
            else
            {
                typeaheadHelper.SpeakNoMatches();
            }
        }

        private void OpenManageAreas()
        {
            Map map = Find.CurrentMap;
            if (map == null)
                return;

            Close();
            // Vanilla's own Manage areas... delegate (AreaUtility); ManageAreasScope drives it.
            Find.WindowStack.Add(new Dialog_ManageAreas(map));
        }

        // Labels and announcements.

        private static string LabelFor(object option)
        {
            if (option == ManageAreasSentinel)
                return "RimWorldAccess.Pawns.Area.ManageAreas".Translate();
            if (option == null)
                return "RimWorldAccess.Pawns.Area.Unrestricted".Translate();
            if (option is Area area)
                return area.Label;
            return "RimWorldAccess.Pawns.Area.UnknownLabel".Translate();
        }

        private List<string> GetOptionLabels()
        {
            var labels = new List<string>();
            foreach (object option in areaOptions)
            {
                labels.Add(LabelFor(option));
            }
            return labels;
        }

        /// <summary>
        /// The area the cursor is browsing, for the map's area-cells draw. Reads the model without
        /// RefreshModel: the caller is a per-frame map draw, not this scope's OnGUI pass, so a
        /// refresh here would recompute content every frame. Null for Manage Areas and Unrestricted.
        /// </summary>
        internal Area FocusedAreaForDisplay()
        {
            ListModel region = Model.CurrentRegion;
            if (region == null || areaOptions.Count == 0 || region.Index < 0 || region.Index >= areaOptions.Count)
                return null;
            return areaOptions[region.Index] as Area;
        }

        private void AnnounceCurrentSelection()
        {
            RefreshModel();
            ListModel region = Model.CurrentRegion;
            if (region == null || areaOptions.Count == 0 || region.Index < 0 || region.Index >= areaOptions.Count)
                return;

            string position = MenuHelper.FormatPosition(region.Index, areaOptions.Count);
            object focused = areaOptions[region.Index];

            if (focused == ManageAreasSentinel)
            {
                string announcement = string.IsNullOrEmpty(position)
                    ? "RimWorldAccess.Pawns.Area.ManageAreas".Translate().ToString()
                    : "RimWorldAccess.Pawns.Area.ManageAreasWithPosition".Translate(position).ToString();
                TolkHelper.SpeakData(announcement);
            }
            else
            {
                Area area = focused as Area;
                string areaName = area?.Label ?? "RimWorldAccess.Pawns.Area.Unrestricted".Translate().ToString();

                string currentSuffix = "";
                if (targetPawn?.playerSettings != null)
                {
                    Area pawnArea = targetPawn.playerSettings.AreaRestrictionInPawnCurrentMap;
                    if (area == pawnArea) // works for both null==null and area==area
                        currentSuffix = "RimWorldAccess.Pawns.Area.CurrentSuffix".Translate();
                }

                string content;
                if (area != null)
                {
                    int cellCount = area.TrueCount;
                    content = "RimWorldAccess.Pawns.Area.AreaWithCells".Translate(areaName, currentSuffix, cellCount);
                    area.MarkForDraw();
                }
                else
                {
                    content = "RimWorldAccess.Pawns.Area.AreaBare".Translate(areaName, currentSuffix);
                }

                string announcement = string.IsNullOrEmpty(position)
                    ? content
                    : "RimWorldAccess.Pawns.Area.WithPosition".Translate(content, position).ToString();
                TolkHelper.SpeakData(announcement);
            }
        }

        private void AnnounceWithSearch()
        {
            RefreshModel();
            ListModel region = Model.CurrentRegion;
            if (region == null || areaOptions.Count == 0 || region.Index < 0 || region.Index >= areaOptions.Count)
                return;

            object focused = areaOptions[region.Index];

            if (focused == ManageAreasSentinel)
            {
                TolkHelper.SpeakData(typeaheadHelper.BuildItemAnnouncement("RimWorldAccess.Pawns.Area.ManageAreas".Translate()));
            }
            else
            {
                Area area = focused as Area;
                string areaName = area?.Label ?? "RimWorldAccess.Pawns.Area.Unrestricted".Translate().ToString();
                TolkHelper.SpeakData(typeaheadHelper.BuildItemAnnouncement(areaName));
            }
        }

        private void Close()
        {
            PawnAreaMenuState.Close();
        }
    }

    /// <summary>
    /// Keeps <see cref="PawnAreaMenuScope"/> in lockstep with
    /// <see cref="PawnAreaMenuState.IsActive"/>, reconciled every OnGUI pass. Reconcile order does
    /// not matter: the only opener is Alt+A on the bare map, so this screen never coexists with the
    /// inspection, bills, or storage families. Stands down while an info card is open.
    /// </summary>
    internal static class PawnAreaMenuScopeMirror
    {
        private static readonly PawnAreaMenuScope scope = new PawnAreaMenuScope();

        public static void Reconcile()
        {
            if (PawnAreaMenuState.IsActive && !InfoCardState.IsActive)
            {
                FocusStack.Push(scope);
            }
            else
            {
                FocusStack.Pop(scope);
            }
        }
    }
}
