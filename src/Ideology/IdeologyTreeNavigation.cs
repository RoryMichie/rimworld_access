using System.Collections.Generic;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Tree navigation logic for the ideology details panel.
    /// Wraps TreeNavigationHelper with ideology-specific behavior:
    /// smart label truncation, "Inspectable." suffix, ritual sound preview.
    ///
    /// <b>One consumer left: <c>IdeosDuringLandingScope</c>.</b> The tree migration
    /// moved the in-game viewer and the Archonexus dialog onto
    /// <see cref="IdeoDetailsTreeRegion"/>, which drives the shared <c>TreeModel</c> from a
    /// conforming <c>ScreenScope</c> content region. That landing dialog is still a plain
    /// <c>FocusScope</c> with a hand-rolled two-panel state machine and no content regions at
    /// all, so it has nothing to hang the shared contract on; converting it is a separate job.
    /// Until then this class stays, unchanged in behaviour, driving that one screen.
    ///
    /// The ritual-sound preview itself has already moved: the <c>Sustainer</c> is a single
    /// static on <see cref="IdeoDetailsTreeRegion"/> shared by every host, so a preview started
    /// here is stopped by whichever host closes first, exactly as before.
    /// </summary>
    internal class IdeologyTreeNavigation
    {
        private readonly TreeNavigationHelper treeNav = new TreeNavigationHelper("IdeologyTree");

        public bool HasActiveSearch => treeNav.HasActiveSearch;

        /// <summary>
        /// The wrapped tree, exposed for shell-scope routers — mirrors
        /// <see cref="FactionTreeNavigation.Tree"/> verbatim.
        /// </summary>
        internal TreeNavigationHelper Tree => treeNav;

        public IdeologyTreeNavigation()
        {
            treeNav.FormatItemAnnouncement = FormatItemAnnouncement;
            treeNav.FormatStateChangeAnnouncement = FormatStateChangeAnnouncement;
            treeNav.FormatSearchAnnouncement = FormatSearchAnnouncement;
            treeNav.OnActivate = HandleActivate;
            treeNav.OnInfo = HandleInfoCard;
            // Page Up/Down (ideologyTab.jumpToPreviousSection/jumpToNextSection, already claimed by
            // IdeologyTabScope) jump between this tree's level-0 section headers (Overview, Style
            // Categories, Factions, Memes, each precept category, etc. — see
            // IdeologyHelper.BuildIdeologyTree's own "Level 0 nodes are section headers" doc comment).
            // Without this the claim above is a dead reject-sound no-op.
            treeNav.IsSectionBoundary = item => item.IndentLevel == 0;
        }

        /// <summary>
        /// Initializes the tree from an ideology. Builds tree, flattens,
        /// resets selection to 0, and announces opening.
        /// </summary>
        public void Initialize(Ideo ideo)
        {
            var rootItem = IdeologyHelper.BuildIdeologyTree(ideo);
            treeNav.Initialize(rootItem);
            AnnounceOpening(ideo);
        }

        /// <summary>
        /// Resets all tree state.
        /// </summary>
        public void Reset()
        {
            IdeoDetailsTreeRegion.StopRitualSound();
            treeNav.Reset();
        }

        // Expose for callers that need it
        public void AnnounceCurrentItem() => treeNav.ReannounceCurrentItem();

        /// <summary>
        /// Feeds a typeahead character to the tree directly. Needed by directly-patched hosts (e.g.
        /// the builder's read-only ideo browser) where the shell's character dispatch
        /// never runs, so the tree would otherwise be deaf to typeahead.
        /// </summary>
        public void HandleTypeaheadCharacter(char c) => treeNav.HandleTypeaheadCharacter(c);

        #region Announcement Formatters

        private string FormatItemAnnouncement(InspectionTreeItem item)
        {
            // Smart label: expanded nodes use short name, collapsed/leaf use full label
            string label;
            if (item.IsExpandable && item.IsExpanded)
            {
                int sepIdx = item.Label.IndexOf(". ");
                label = sepIdx > 0 ? item.Label.Substring(0, sepIdx) : item.Label;
            }
            else
            {
                label = item.Label.TrimEnd('.', '!', '?');
            }

            string stateIndicator = TreeNavigationHelper.FormatExpansionSuffix(item, includeChildCount: true);

            var (position, total) = treeNav.GetSiblingPosition(item);
            string positionPart = MenuHelper.FormatPosition(position - 1, total);
            string positionSection = string.IsNullOrEmpty(positionPart)
                ? "" : $". {positionPart}";

            string levelSuffix = MenuHelper.GetLevelSuffix("IdeologyTree", item.IndentLevel);
            string inspectable = GetInspectableDefs(item).Count > 0 ? "RimWorldAccess.InfoCard.Inspectable".Translate().ToString() : "";

            return $"{label}{stateIndicator}{positionSection}{levelSuffix}{inspectable}";
        }

        /// <summary>
        /// Short announcement after expand/collapse: just label + state.
        /// </summary>
        private string FormatStateChangeAnnouncement(InspectionTreeItem item)
        {
            int sepIdx = item.Label.IndexOf(". ");
            string shortLabel = sepIdx > 0 ? item.Label.Substring(0, sepIdx) : item.Label.TrimEnd('.', '!', '?');

            return $"{shortLabel}{TreeNavigationHelper.FormatExpansionSuffix(item, includeChildCount: true)}";
        }

        private string FormatSearchAnnouncement(InspectionTreeItem item, TypeaheadSearchHelper typeahead)
        {
            int searchSepIdx = item.IsExpandable ? item.Label.IndexOf(". ") : -1;
            string label = searchSepIdx > 0 ? item.Label.Substring(0, searchSepIdx) : item.Label.TrimEnd('.', '!', '?');

            string stateIndicator = "";
            if (item.IsExpandable)
                stateIndicator = item.IsExpanded ? ", " + (string)"RimWorldAccess.Tree.StateExpanded".Translate() : ", " + (string)"RimWorldAccess.Tree.StateCollapsed".Translate();

            return typeahead.BuildItemAnnouncement($"{label}{stateIndicator}");
        }

        private void AnnounceOpening(Ideo ideo)
        {
            if (treeNav.Count > 0)
            {
                var firstItem = treeNav.VisibleItems[0];

                string stateIndicator = TreeNavigationHelper.FormatExpansionSuffix(firstItem, includeChildCount: true);

                var (pos, total) = treeNav.GetSiblingPosition(firstItem);
                string position = MenuHelper.FormatPosition(pos - 1, total);

                string inspectable = GetInspectableDefs(firstItem).Count > 0 ? "RimWorldAccess.InfoCard.Inspectable".Translate().ToString() : "";
                TolkHelper.SpeakData($"{firstItem.Label}{stateIndicator}. {position}{inspectable}");
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Ideology.Tree.Empty".Loc(ideo.name, "NoneLower".Translate()));
            }
        }

        #endregion

        #region Custom Actions

        private bool HandleActivate(InspectionTreeItem item)
        {
            // Ritual sound toggle
            if (item.Data is SoundDef soundDef)
            {
                IdeoDetailsTreeRegion.ToggleRitualSound(soundDef);
                return true;
            }
            // Reform action — open the accessible reform dialog on top of whichever host is
            // currently showing this tree (vehicle A). Host-agnostic: this tree is shared by the
            // in-game viewer (a real window now — IdeologyViewerScreenScope),
            // IdeosDuringLandingScope, and the Archonexus read-only
            // viewer, so nothing here may assume or close any one host's own window/state; stopping
            // any live ritual-sound preview before the new dialog opens is the one universally
            // correct side effect (previously piggybacked on the now-deleted IdeologyTabState.Close).
            if (item.Data is IdeoReformState.ReformActionMarker reformMarker)
            {
                IdeoDetailsTreeRegion.StopRitualSound();
                Find.WindowStack.Add(new RimWorld.Dialog_ReformIdeo(reformMarker.Ideo));
                return true;
            }
            return false; // Fall back to default expand/collapse toggle
        }

        #endregion

        #region Info Card

        private bool HandleInfoCard(InspectionTreeItem item)
        {
            var defs = GetInspectableDefs(item);
            if (defs.Count == 0)
            {
                InfoCardState.SpeakNoInfoCardAvailable();
                return true;
            }

            if (defs.Count == 1)
            {
                InfoCardState.OpenInfoCardForDef(defs[0]);
                return true;
            }

            // Multiple defs — present selection menu
            var options = new List<FloatMenuOption>();
            foreach (var def in defs)
            {
                var captured = def;
                string label = def.label?.CapitalizeFirst() ?? def.defName;
                options.Add(new FloatMenuOption(label, () => InfoCardState.OpenInfoCardForDef(captured)));
            }
            TolkHelper.Speak("RimWorldAccess.InfoCard.ChooseItemToInspect".Loc());
            WindowlessFloatMenuState.Open(options, false);
            return true;
        }

        /// <summary>
        /// Walks up the tree from <paramref name="item"/> to find inspectable Defs.
        /// Supports both single Def and List&lt;Def&gt; stored in Data. MemeDef is excluded like
        /// SoundDef -- vanilla never opens Dialog_InfoCard for a MemeDef (no SpecialDisplayStats
        /// override, and Dialog_ChooseMemes has zero Dialog_InfoCard references), so a meme node
        /// yields no target here and Alt+I speaks the standard refusal instead of opening or
        /// speaking a fabricated card; the meme's full detail content is already on its own row
        /// (IdeologyHelper.BuildMemeDetailLines).
        /// </summary>
        private List<Def> GetInspectableDefs(InspectionTreeItem item)
        {
            var node = item;
            var rootItem = treeNav.RootItem;
            while (node != null && node != rootItem)
            {
                if (node.Data is Def def && !(def is SoundDef) && !(def is MemeDef))
                    return new List<Def> { def };
                if (node.Data is List<Def> defs && defs.Count > 0)
                    return defs;
                node = node.Parent;
            }
            return new List<Def>();
        }

        #endregion
    }
}
