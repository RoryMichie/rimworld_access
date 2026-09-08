using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Shared tree navigation logic for faction screens.
    /// Used only by FactionTabState (the in-game Factions tab); FactionLandingState
    /// uses FactionLandingScope's own TreeRegionScope tree instead (blueprint
    /// ruling D15).
    /// Wraps TreeNavigationHelper with faction-specific behavior.
    /// </summary>
    internal class FactionTreeNavigation
    {
        private readonly TreeNavigationHelper treeNav = new TreeNavigationHelper("FactionTree");

        public bool HasActiveSearch => treeNav.HasActiveSearch;

        /// <summary>
        /// The wrapped tree, exposed for shell-scope routers (FactionLandingState's
        /// per-action routers delegate to the tree's own public router methods).
        /// </summary>
        internal TreeNavigationHelper Tree => treeNav;

        public FactionTreeNavigation()
        {
            treeNav.OnInfo = HandleInfoCard;
        }

        /// <summary>
        /// Initializes the tree from a faction list. Builds tree, flattens,
        /// resets selection to 0, and announces opening.
        /// </summary>
        public void Initialize(List<Faction> factions)
        {
            var rootItem = FactionHelper.BuildFactionTree(factions);
            treeNav.Initialize(rootItem);
            AnnounceOpening(factions.Count);
        }

        /// <summary>
        /// Resets all tree state.
        /// </summary>
        public void Reset()
        {
            treeNav.Reset();
        }

        #region Announcements

        private void AnnounceOpening(int factionCount)
        {
            if (factionCount > 0 && treeNav.Count > 0)
            {
                var sb = new StringBuilder(
                    "RimWorldAccess.Factions.Tab.OpeningWithCount".Translate(factionCount).ToString());
                var firstItem = treeNav.VisibleItems[0];
                FactionHelper.AppendSentence(sb, firstItem.Label);

                sb.Append(TreeNavigationHelper.FormatExpansionSuffix(firstItem, includeChildCount: true));

                var (pos, total) = treeNav.GetSiblingPosition(firstItem);
                string position = MenuHelper.FormatPosition(pos - 1, total);
                if (!string.IsNullOrEmpty(position))
                    FactionHelper.AppendSentence(sb, position);

                TolkHelper.SpeakData(sb.ToString());
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Factions.Tab.OpeningNoFactions".Loc());
            }
        }

        #endregion

        #region Info Card

        private bool HandleInfoCard(InspectionTreeItem item)
        {
            Faction faction = GetSelectedFaction();
            if (faction == null)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("RimWorldAccess.Factions.Tab.NoFactionSelected".Loc());
                return true;
            }
            Find.WindowStack.Add(new Dialog_InfoCard(faction));
            return true;
        }

        /// <summary>
        /// Finds the Faction object for the current selection by walking up the tree
        /// until a node with Data of type Faction is found.
        /// </summary>
        private Faction GetSelectedFaction()
        {
            var item = treeNav.SelectedItem;
            while (item != null)
            {
                if (item.Data is Faction f) return f;
                item = item.Parent;
            }
            return null;
        }

        #endregion
    }
}
