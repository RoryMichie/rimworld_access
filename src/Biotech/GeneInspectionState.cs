using System;
using System.Linq;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Lifecycle and identity for baby gene inspection (ITab_GenesPregnancy / ITab_Genes):
    /// which pawn or gene-set holder is being inspected, the tree DATA built for it, and the
    /// opening/closing announcements. This is a facade — a data-and-lifecycle surface the
    /// Harmony patch drives — not a navigator.
    ///
    /// <b>What moved.</b> The retired <c>TreeNavigationHelper</c>
    /// instance, its five format callbacks, the thirteen router forwarders the focus scope
    /// used to call, the typeahead accessor, and the Page Up/Down gene scan all now live in
    /// <see cref="RimWorldAccess.Shell.GeneInspectionScope"/>, which rides
    /// <c>TreeModel</c> and the shared announcement composer directly. This class keeps
    /// <see cref="IsActive"/>, <see cref="Open"/>, <see cref="OpenForGeneSetHolder"/>,
    /// <see cref="Close"/> and <see cref="CloseInspection"/> — every member
    /// <c>GeneInspectionPatch</c> and the scope's own Escape claim call — and hands each
    /// freshly built root to the scope through <c>OpenTree</c>.
    /// </summary>
    public static class GeneInspectionState
    {
        public static bool IsActive { get; private set; } = false;

        private static Pawn currentPawn = null;
        private static HediffWithParents currentPregnancy = null;
        private static GeneSetHolderBase currentHolder = null;

        /// <summary>The tree handed to the scope, kept so a scope pushed later can adopt it.</summary>
        private static InspectionTreeItem treeRoot = null;

        /// <summary>
        /// Opens the gene inspection accessibility state for a pregnant pawn.
        /// </summary>
        /// <param name="pawn">The pregnant pawn to inspect</param>
        public static void Open(Pawn pawn)
        {
            try
            {
                if (pawn == null)
                    return;

                // Find the pregnancy hediff
                var pregnancy = pawn.health?.hediffSet?.hediffs
                    .OfType<HediffWithParents>()
                    .FirstOrDefault();

                if (pregnancy == null || pregnancy.geneSet == null)
                {
                    TolkHelper.Speak("RimWorldAccess.Biotech.GeneInspection.NoPregnancy".Loc(), SpeechPriority.High);
                    return;
                }

                currentPawn = pawn;
                currentPregnancy = pregnancy;
                IsActive = true;

                // Get parent names for context
                string motherName = pregnancy.Mother?.LabelShort;
                string fatherName = pregnancy.Father?.LabelShort;

                // Build the tree
                var rootItem = GeneTreeBuilder.BuildTree(pregnancy.geneSet, motherName, fatherName);
                PresentTree(rootItem);

                SoundDefOf.TabOpen.PlayOneShotOnCamera();
                AnnounceOpening(rootItem);
            }
            catch (Exception ex)
            {
                Log.Error($"[GeneInspectionState] Error opening: {ex.Message}");
                Close();
            }
        }

        /// <summary>
        /// Opens the gene inspection accessibility state for a GeneSetHolderBase item
        /// (embryo, genepack, or xenogerm).
        /// </summary>
        public static void OpenForGeneSetHolder(GeneSetHolderBase holder)
        {
            try
            {
                if (holder == null || holder.GeneSet == null)
                    return;

                currentPawn = null;
                currentPregnancy = null;
                currentHolder = holder;
                IsActive = true;

                // Get parent names for embryos
                string motherName = null;
                string fatherName = null;
                if (holder is HumanEmbryo embryo)
                {
                    try
                    {
                        motherName = embryo.Mother?.LabelShort;
                        fatherName = embryo.Father?.LabelShort;
                    }
                    catch { /* CompHasPawnSources may not be available */ }
                }

                // Build the tree
                var rootItem = GeneTreeBuilder.BuildTree(holder.GeneSet, motherName, fatherName);

                // Override root label based on item type
                int geneCount = holder.GeneSet.GenesListForReading?.Count ?? 0;
                string countStr = GeneTreeBuilder.GeneCountSuffix(geneCount);
                string xenotype = holder.GeneSet.Label;
                bool hasXenotype = !string.IsNullOrEmpty(xenotype) && xenotype != "ERR";

                if (holder is HumanEmbryo)
                    rootItem.Label = hasXenotype
                        ? "RimWorldAccess.Biotech.Gene.RootEmbryoGenesWithXenotype".Translate(xenotype, countStr).ToString()
                        : "RimWorldAccess.Biotech.Gene.RootEmbryoGenes".Translate(countStr).ToString();
                else if (holder is Xenogerm xg && !string.IsNullOrEmpty(xg.xenotypeName))
                    rootItem.Label = "RimWorldAccess.Biotech.Gene.RootXenogermGenes".Translate(xg.xenotypeName, countStr).ToString();
                else if (holder is Genepack)
                    rootItem.Label = hasXenotype
                        ? "RimWorldAccess.Biotech.Gene.RootGenepackWithXenotype".Translate(xenotype, countStr).ToString()
                        : "RimWorldAccess.Biotech.Gene.RootGenepackGenes".Translate(countStr).ToString();
                else
                    rootItem.Label = hasXenotype
                        ? "RimWorldAccess.Biotech.Gene.RootGenesWithXenotype".Translate(xenotype, countStr).ToString()
                        : "RimWorldAccess.Biotech.Gene.RootGenes".Translate(countStr).ToString();

                PresentTree(rootItem);

                SoundDefOf.TabOpen.PlayOneShotOnCamera();
                AnnounceOpening(rootItem);
            }
            catch (Exception ex)
            {
                Log.Error($"[GeneInspectionState] Error opening for GeneSetHolder: {ex.Message}");
                Close();
            }
        }

        /// <summary>
        /// Closes the gene inspection accessibility state.
        /// </summary>
        public static void Close()
        {
            IsActive = false;
            currentPawn = null;
            currentPregnancy = null;
            currentHolder = null;
            treeRoot = null;
            Shell.GeneInspectionScope.Live?.ClearTree();
        }

        /// <summary>
        /// Closes the gene inspection (Escape key).
        /// </summary>
        public static void CloseInspection()
        {
            if (!IsActive)
                return;

            // Also close the visual tab if open
            if (currentPawn != null)
            {
                var pane = Find.MainTabsRoot?.OpenTab?.TabWindow as MainTabWindow_Inspect;
                if (pane != null)
                {
                    // Close the genes tab
                    pane.CloseOpenTab();
                }
            }

            Close();
            SoundDefOf.Click.PlayOneShotOnCamera();
            TolkHelper.Speak("RimWorldAccess.Biotech.GeneInspection.Closed".Loc());
        }

        #region Private Methods

        /// <summary>
        /// Hands a freshly built tree to the scope. <c>GeneInspectionScopeMirror</c> creates
        /// its scope during the first OnGUI pass, long before a player can open a genes tab,
        /// so the instance effectively always exists here; <see cref="NotifyScopeAttached"/>
        /// covers the theoretical open-before-first-reconcile ordering.
        /// </summary>
        private static void PresentTree(InspectionTreeItem root)
        {
            treeRoot = root;
            Shell.GeneInspectionScope.Live?.OpenTree(root);
        }

        /// <summary>Called from the scope's OnPush: adopt the current tree if it has not already.</summary>
        internal static void NotifyScopeAttached(Shell.GeneInspectionScope scope)
        {
            if (IsActive && treeRoot != null)
            {
                scope.EnsureTree(treeRoot);
            }
        }

        /// <summary>
        /// Announces the opening of the gene inspection.
        /// </summary>
        private static void AnnounceOpening(InspectionTreeItem root)
        {
            if (root == null)
                return;

            string rootLabel = root.Label.StripTags();

            // Build opening announcement with first item
            var sb = new System.Text.StringBuilder();
            sb.Append(rootLabel);
            sb.Append(". ");

            // Announce the first item
            var firstItem = Shell.GeneInspectionScope.Live?.FirstVisibleItem;
            if (firstItem != null)
            {
                string firstLabel = firstItem.Label.StripTags();
                string state = ExpansionStateWord(firstItem);
                sb.Append("RimWorldAccess.Biotech.GeneInspection.FirstGene".Translate(firstLabel));
                if (!string.IsNullOrEmpty(state))
                    sb.Append($" {state}");
                sb.Append(". ");
            }

            sb.Append("RimWorldAccess.Biotech.GeneInspection.NavHint".Translate());
            TolkHelper.SpeakData(sb.ToString());
        }

        /// <summary>
        /// The bare branch-state word for the opening line. Reads the shared tree vocabulary
        /// keys directly so this one sentence cannot drift from the row announcements the
        /// composer speaks from its own copy of the same words.
        /// </summary>
        private static string ExpansionStateWord(InspectionTreeItem item)
        {
            if (!item.IsExpandable)
                return "";
            return (item.IsExpanded
                ? "RimWorldAccess.Tree.StateExpanded"
                : "RimWorldAccess.Tree.StateCollapsed").Translate().ToString();
        }

        #endregion
    }
}
