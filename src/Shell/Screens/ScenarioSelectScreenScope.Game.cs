using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Steamworks;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Focus scope for the scenario selection page (<see cref="Page_SelectScenario"/>),
    /// window-attached via ShellBootstrap. Two regions, re-derived every RefreshContent cycle:
    /// Scenarios (a RadioButton row per visible <see cref="Scenario"/> in vanilla's order, with
    /// "(none)" placeholders read-only; Enter or arrowing onto a row writes the page's private
    /// <c>curScen</c>, an invalid scenario through <c>PreLoadUtility.CheckVersionAndLoad</c>
    /// first) and Details (a tree panel of read-only rows for <c>curScen</c>, not the cursor's
    /// scenario, under a name-header prefix row).
    /// <see cref="CaptureWindowButtons"/> is false because the page's content draws non-toolbar
    /// buttons, so the toolbar is declared instead.
    /// "Open Steam Workshop" is deliberately NOT Steam-gated: vanilla draws it unconditionally
    /// and <c>SteamUtility.OpenUrl</c> falls back to <c>Application.OpenURL</c>.
    /// Back and Next run the page's real reflected gates behind the static
    /// <see cref="BackRequested"/>/<see cref="AdvanceRequested"/> flags; the guard twins in
    /// src/MainMenu/ScenarioSelectionPatch.cs block the page's raw keyboard poll while this
    /// scope is the live top and the flag is unset.
    /// </summary>
    public sealed class ScenarioSelectScreenScope : TreeRegionScope
    {
        private enum RegionKind { Scenarios, Details }

        private sealed class ScenarioRow
        {
            /// <summary>Null only for a placeholder ("(none)") row.</summary>
            public Scenario Scenario;
            public ScenarioCategory Category;
            /// <summary>True on the FIRST row of a CustomLocal/SteamWorkshop category; folds the header into the Label.</summary>
            public bool CategoryBoundary;
        }

        private static readonly AccessTools.FieldRef<Page_SelectScenario, Scenario> curScenField =
            AccessTools.FieldRefAccess<Page_SelectScenario, Scenario>("curScen");

        private static readonly MethodInfo canDoNextMethod = AccessTools.Method(typeof(Page_SelectScenario), "CanDoNext");
        private static readonly MethodInfo doNextMethod = AccessTools.Method(typeof(Page), "DoNext");
        private static readonly MethodInfo canDoBackMethod = AccessTools.Method(typeof(Page), "CanDoBack");
        private static readonly MethodInfo doBackMethod = AccessTools.Method(typeof(Page), "DoBack");
        private static readonly MethodInfo goToScenarioEditorMethod = AccessTools.Method(typeof(Page_SelectScenario), "GoToScenarioEditor");

        /// <summary>True only while Next drives the vanilla gate-and-advance; read by the CanDoNext guard twin.</summary>
        internal static bool AdvanceRequested;

        /// <summary>True only while Back runs its vanilla gate; read by the CanDoBack guard twin.</summary>
        internal static bool BackRequested;

        private readonly Page_SelectScenario page;
        private readonly List<RegionKind> activeRegions = new List<RegionKind>();
        private readonly List<ScenarioRow> scenarioRows = new List<ScenarioRow>();
        private readonly List<ScreenAction> actions = new List<ScreenAction>();

        private Scenario lastDetailScenario;
        private bool announcedOpen;

        public ScenarioSelectScreenScope(Page_SelectScenario page)
        {
            this.page = page;
            activeRegions.Add(RegionKind.Scenarios);
            activeRegions.Add(RegionKind.Details);

            Claim(SharedMenuGrammar.Cancel, e => EscapeBack(), when: () => !TypeaheadHasActiveSearch);
            Claim("scenarioSelect.editScenario", e => ActivateEditScenario());
            Claim("scenarioSelect.next", e => NextAction());
            Claim("scenarioSelect.contextMenu", e => ActivateContextMenu(), when: ScenariosRegionLive);
        }

        public override string Name
        {
            get { return "scenario-select"; }
        }

        protected internal override Window OwnedWindow
        {
            get { return page; }
        }

        /// <summary>The page's content draws non-toolbar buttons, so the toolbar is declared instead.</summary>
        protected override bool CaptureWindowButtons
        {
            get { return false; }
        }

        protected override bool IncludeCapturedExtrasRegion
        {
            get { return true; }
        }

        protected override void RefreshContent()
        {
            base.RefreshContent();
            RefreshScenarioRows();
            Scenario curScen = curScenField(page);
            if (Tree.Root == null || !ReferenceEquals(curScen, lastDetailScenario))
            {
                lastDetailScenario = curScen;
                SetTreeRoot(BuildDetailRoot(curScen));
            }
        }

        private static InspectionTreeItem BuildDetailRoot(Scenario curScen)
        {
            InspectionTreeItem root = ScenarioNavigationState.BuildDetailTree(curScen);
            if (root.Children.Count == 0)
            {
                root.Children.Add(new InspectionTreeItem
                {
                    Label = "RimWorldAccess.ScenarioSelect.NoAdditionalDetails".Translate(),
                    IndentLevel = 0,
                    IsExpandable = false,
                    Parent = root,
                });
            }
            return root;
        }

        private void RefreshScenarioRows()
        {
            scenarioRows.Clear();
            AppendCategoryRows(ScenarioCategory.FromDef);
            AppendCategoryRows(ScenarioCategory.CustomLocal);
            AppendCategoryRows(ScenarioCategory.SteamWorkshop);
        }

        private void AppendCategoryRows(ScenarioCategory category)
        {
            // Vanilla's per-category filter (Page_SelectScenario.ListScenariosOnListing :100-123).
            List<Scenario> visible = ScenarioLister.ScenariosInCategory(category).Where(s => s.showInUI).ToList();
            bool boundary = category != ScenarioCategory.FromDef;
            if (visible.Count == 0)
            {
                scenarioRows.Add(new ScenarioRow { Scenario = null, Category = category, CategoryBoundary = boundary });
                return;
            }
            bool first = true;
            foreach (Scenario scen in visible)
            {
                scenarioRows.Add(new ScenarioRow { Scenario = scen, Category = category, CategoryBoundary = boundary && first });
                first = false;
            }
        }

        private protected override void CollectRouteCandidates(
            List<PointerHitCandidate> candidates, List<RouteTarget> targets)
        {
            int region = activeRegions.IndexOf(RegionKind.Scenarios);
            if (region < 0)
                return;
            for (int i = 0; i < scenarioRows.Count; i++)
            {
                ScenarioRowRingPatch.RowGeometry.AddCandidate(scenarioRows[i].Scenario, region, i, candidates, targets);
            }
        }

        protected override int ContentRegionCount
        {
            get { return activeRegions.Count; }
        }

        protected override int TreeRegionIndex
        {
            get { return activeRegions.IndexOf(RegionKind.Details); }
        }

        protected override string TreeRegionLabel
        {
            get { return "RimWorldAccess.ScenarioSelect.DetailsLabel".Translate(); }
        }

        protected override string ContentRegionName(int region)
        {
            return activeRegions[region] == RegionKind.Scenarios
                ? (string)"RimWorldAccess.ScenarioSelect.ScenariosRegion".Translate()
                : TreeRegionLabel;
        }

        protected override int PrefixRowCountFor(int region)
        {
            return activeRegions[region] == RegionKind.Scenarios ? scenarioRows.Count : 1;
        }

        protected override ElementDescription DescribePrefixRow(int region, int index)
        {
            if (activeRegions[region] == RegionKind.Scenarios)
            {
                return DescribeScenarioRow(index);
            }
            Scenario curScen = curScenField(page);
            return new ElementDescription
            {
                Label = curScen != null ? curScen.name : "",
                ReadOnly = true,
            };
        }

        protected override void ActivatePrefixRow(int region, int index)
        {
            if (activeRegions[region] == RegionKind.Scenarios)
            {
                ActivateScenarioRow(index);
                return;
            }
            AnnounceCurrentItem();
        }

        protected override ElementDescription DescribeTreeNode(InspectionTreeItem item)
        {
            var d = new ElementDescription { Label = item.Label };
            if (item.IsExpandable)
            {
                d.Role = ElementRole.TreeItem;
                d.Expanded = item.IsExpanded;
            }
            else
            {
                d.ReadOnly = true;
            }
            return d;
        }

        protected override void ActivateTreeNode(InspectionTreeItem item)
        {
            if (item.IsExpandable)
            {
                PerformActivateExpandToggle(item);
                return;
            }
            AnnounceCurrentItem();
        }

        /// <summary>
        /// The scenario under the keyboard cursor, for <see cref="ScenarioRowRingPatch"/>; null
        /// outside the Scenarios region and on "(none)" placeholder rows.
        /// </summary>
        internal Scenario FocusedScenario
        {
            get
            {
                if (Model.RegionIndex != activeRegions.IndexOf(RegionKind.Scenarios))
                    return null;
                ListModel region = Model.CurrentRegion;
                int index = region == null ? -1 : region.Index;
                return index >= 0 && index < scenarioRows.Count ? scenarioRows[index].Scenario : null;
            }
        }

        private bool ScenariosRegionLive()
        {
            RefreshModel();
            return Model.RegionIndex == activeRegions.IndexOf(RegionKind.Scenarios) && scenarioRows.Count > 0;
        }

        private static string CategoryHeader(ScenarioCategory category)
        {
            switch (category)
            {
                case ScenarioCategory.CustomLocal: return (string)"ScenariosCustom".Translate();
                case ScenarioCategory.SteamWorkshop: return (string)"ScenariosSteamWorkshop".Translate();
                default: return "";
            }
        }

        private ElementDescription DescribeScenarioRow(int index)
        {
            var d = new ElementDescription();
            if (index < 0 || index >= scenarioRows.Count) return d;
            ScenarioRow row = scenarioRows[index];

            if (row.Scenario == null)
            {
                d.Role = ElementRole.None;
                d.ReadOnly = true;
                string placeholder = "(" + "NoneLower".Translate() + ")";
                d.Label = row.CategoryBoundary
                    ? (string)"RimWorldAccess.ScenarioSelect.CategoryPrefix".Translate(CategoryHeader(row.Category), placeholder)
                    : placeholder;
                return d;
            }

            Scenario scen = row.Scenario;
            d.Role = ElementRole.RadioButton;
            d.Selected = ReferenceEquals(curScenField(page), scen);
            d.Label = row.CategoryBoundary
                ? (string)"RimWorldAccess.ScenarioSelect.CategoryPrefix".Translate(CategoryHeader(row.Category), scen.name)
                : scen.name;

            var extras = new List<string>();
            string summary = scen.GetSummary();
            if (!string.IsNullOrEmpty(summary)) extras.Add(summary);
            if (!scen.valid) extras.Add((string)"ScenPart_Error".Translate());
            if (scen.enabled && scen.CanToUploadToWorkshop()) extras.Add((string)"CanBeUpdatedOnWorkshop".Translate());
            d.Extras = string.Join(". ", extras);
            return d;
        }

        protected override void ActivateContentItem(int region, int index)
        {
            if (activeRegions[region] == RegionKind.Scenarios)
                ActivateScenarioRow(index);
            else
                AnnounceCurrentItem(); // Details rows are read-only; expand/collapse is Left/Right only.
        }

        private void ActivateScenarioRow(int index)
        {
            if (index < 0 || index >= scenarioRows.Count) return;
            ScenarioRow row = scenarioRows[index];
            if (row.Scenario == null)
            {
                AnnounceCurrentItem();
                return;
            }
            Scenario scen = row.Scenario;
            if (ReferenceEquals(curScenField(page), scen))
            {
                AnnounceCurrentItem();
                return;
            }
            if (!scen.valid)
            {
                // As in DoScenarioListEntry's invalid-scenario branch (:194-199), the
                // version-mismatch dialog chain owns the mutation: only its completion
                // callback commits curScen.
                PreLoadUtility.CheckVersionAndLoad(scen.File.FullName, ScribeMetaHeaderUtility.ScribeHeaderMode.Scenario, delegate
                {
                    curScenField(page) = scen;
                    AnnounceScenarioSelectedStateChange();
                });
                return;
            }
            CommitScenarioSelection(scen);
            AnnounceScenarioSelectedStateChange();
        }

        /// <summary>
        /// MUTATION-C: mirrors DoScenarioListEntry's ButtonInvisible branch (decompiled
        /// :189-204) — no gated vanilla setter exists for the private curScen field. Shared by
        /// Enter's valid-scenario branch and the cursor-landing auto-select so both write the
        /// selection exactly one way.
        /// </summary>
        private void CommitScenarioSelection(Scenario scen)
        {
            curScenField(page) = scen;
        }

        /// <summary>
        /// Arrowing onto a scenario selects it, no Enter needed. Only the VALID branch
        /// auto-fires: an invalid scenario opens a confirmation chain, which must never appear
        /// just because the cursor passed over the row.
        /// </summary>
        protected override void OnCursorSettled(int region, int index)
        {
            // region can be the Buttons or captured-extras region, past activeRegions' end.
            if (region < 0 || region >= activeRegions.Count || activeRegions[region] != RegionKind.Scenarios)
                return;
            if (index < 0 || index >= scenarioRows.Count)
                return;
            Scenario scen = scenarioRows[index].Scenario;
            if (scen == null || !scen.valid || ReferenceEquals(curScenField(page), scen))
                return;
            CommitScenarioSelection(scen);
        }

        private void AnnounceScenarioSelectedStateChange()
        {
            var d = new ElementDescription { Selected = true };
            TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(d, TranslatedShellVocabulary.Instance));
        }

        private void ActivateContextMenu()
        {
            RefreshModel();
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty) return;
            int idx = region.Index;
            if (idx < 0 || idx >= scenarioRows.Count) return;
            ScenarioRow row = scenarioRows[idx];
            if (row.Scenario == null) return;

            List<FloatMenuOption> options = BuildContextMenuOptions(row.Scenario, idx);
            if (options.Count > 0)
            {
                WindowlessFloatMenuState.Open(options, colonistOrders: false);
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.ScenarioSelect.NoContextActions".Loc());
            }
        }

        private List<FloatMenuOption> BuildContextMenuOptions(Scenario scen, int rowIndex)
        {
            var options = new List<FloatMenuOption>();
            // Vanilla skips every icon action for a disabled scenario (:148-151).
            if (!scen.enabled) return options;

            if (scen.Category == ScenarioCategory.CustomLocal)
            {
                options.Add(new FloatMenuOption((string)"Delete".Translate(), () =>
                    ScenarioNavigationState.DeleteSelectedScenario(scen, () => RebuildAfterRowMutation(rowIndex))));
            }
            if (scen.Category == ScenarioCategory.SteamWorkshop)
            {
                options.Add(new FloatMenuOption((string)"Unsubscribe".Translate(), () =>
                    ScenarioNavigationState.UnsubscribeSelectedScenario(scen, () => RebuildAfterRowMutation(rowIndex))));
            }
            if (scen.GetPublishedFileId() != PublishedFileId_t.Invalid)
            {
                options.Add(new FloatMenuOption((string)"WorkshopPage".Translate(), () =>
                    SteamUtility.OpenWorkshopPage(scen.GetPublishedFileId())));
            }
            return options;
        }

        /// <summary>
        /// Rebuilds the Scenarios region after a delete/unsubscribe, restoring the cursor by
        /// SCENARIO IDENTITY: <c>curScen</c>'s row, else vanilla's <c>EnsureValidSelection</c>
        /// fallback row, else clamped to the pre-mutation row.
        /// </summary>
        private void RebuildAfterRowMutation(int previousRowIndex)
        {
            RefreshModel();
            Scenario current = curScenField(page);
            if (current == null || !ScenarioLister.ScenarioIsListedAnywhere(current))
            {
                // MUTATION-C: mirrors Page_SelectScenario.EnsureValidSelection (decompiled :239-245).
                curScenField(page) = ScenarioLister.ScenariosInCategory(ScenarioCategory.FromDef).FirstOrDefault();
                RefreshModel();
            }

            Scenario target = curScenField(page);
            int found = -1;
            for (int i = 0; i < scenarioRows.Count; i++)
            {
                if (scenarioRows[i].Scenario != null && ReferenceEquals(scenarioRows[i].Scenario, target))
                {
                    found = i;
                    break;
                }
            }

            ListModel region = Model.CurrentRegion;
            if (region != null && !region.IsEmpty)
            {
                region.MoveTo(found >= 0 ? found : Math.Min(previousRowIndex, region.Count - 1));
            }
            AnnounceCurrentItem();
        }

        /// <summary>Next is this page's default/proceed button for Enter double-press.</summary>
        protected override string DefaultAcceptActionId
        {
            get { return "scenarioSelect.next"; }
        }

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                actions.Add(new ScreenAction("Back".Translate(), BackAction, SharedMenuGrammar.Cancel));
                actions.Add(new ScreenAction("Next".Translate(), NextAction, "scenarioSelect.next"));
                actions.Add(new ScreenAction("ScenarioEditor".Translate(), ActivateEditScenario, "scenarioSelect.editScenario"));
                actions.Add(new ScreenAction("OpenSteamWorkshop".Translate(), ActivateOpenSteamWorkshop));
                return actions;
            }
        }

        private void EscapeBack()
        {
            ShellFrameStamps.MarkCancelConsumed();
            BackAction();
        }

        private void BackAction()
        {
            // Mirrors Page.DoBottomButtons' Back branch (Page.cs:60-63): gate, then DoBack.
            BackRequested = true;
            try
            {
                if ((bool)canDoBackMethod.Invoke(page, null))
                    doBackMethod.Invoke(page, null);
            }
            finally
            {
                BackRequested = false;
            }
        }

        private void NextAction()
        {
            // Mirrors the Next branch (Page.cs:69-71): CanDoNext, which also runs
            // BeginScenarioConfiguration, then DoNext.
            AdvanceRequested = true;
            try
            {
                if ((bool)canDoNextMethod.Invoke(page, null))
                    doNextMethod.Invoke(page, null);
            }
            finally
            {
                AdvanceRequested = false;
            }
        }

        private void ActivateEditScenario()
        {
            Scenario curScen = curScenField(page);
            if (curScen == null) return;
            // Read-only re-derivation of CanEditScenario (:50-61), only to pick the announcement.
            bool canEditDirectly = curScen.Category == ScenarioCategory.CustomLocal || curScen.CanToUploadToWorkshop();
            string announcement = canEditDirectly
                ? (string)"RimWorldAccess.ScenarioSelect.EditingScenario".Translate(curScen.name)
                : (string)"RimWorldAccess.ScenarioSelect.EditingCopyOf".Translate(curScen.name);
            // The page's private GoToScenarioEditor() builds, pushes and closes for us
            // (Page_SelectScenario.cs:63-69), so no gate is hand-copied here.
            goToScenarioEditorMethod.Invoke(page, null);
            TolkHelper.SpeakData(announcement);
        }

        private void ActivateOpenSteamWorkshop()
        {
            // Vanilla's inline list button (:90-93); deliberately ungated, see the class remarks.
            SteamUtility.OpenSteamWorkshopPage();
            // A sighted player sees the OS switch focus; without this line there is no signal.
            TolkHelper.Speak("RimWorldAccess.ScenarioSelect.OpenedSteamWorkshop".Loc());
        }

        /// <summary>
        /// Strings this scope already presents another way, folded out of the captured-extras
        /// diff: icon-button fallback labels, the tip icons, and the page title.
        /// </summary>
        protected override IEnumerable<string> AdditionalPresentedTexts
        {
            get
            {
                yield return (string)"ChooseScenario".Translate();
                yield return (string)"Delete".Translate();
                yield return (string)"Unsubscribe".Translate();
                yield return (string)"WorkshopPage".Translate();

                // The info pane's body captures as ONE fused row phrased vanilla's way, so the
                // containment diff needs vanilla's composite verbatim (Scenario.cs:197-226).
                Scenario infoScen = curScenField(page);
                if (infoScen != null)
                {
                    yield return infoScen.name;
                    yield return infoScen.GetFullInformationText();
                }
            }
        }

        public override void OnPush()
        {
            base.OnPush();
            announcedOpen = false;
            lastDetailScenario = null;
            ResetTree();
        }

        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen) return;
            announcedOpen = true;
            string tabCount = TabCountFragment();
            string title = (string)"ChooseScenario".Translate();
            TolkHelper.SpeakData(string.IsNullOrEmpty(tabCount) ? title : tabCount + ". " + title);
            AnnounceCurrentItem();
        }
    }

    /// <summary>
    /// Paints the shared focus ring on vanilla's scenario row, reusing the rect
    /// <c>DoScenarioListEntry</c> was handed (Page_SelectScenario.cs:125). Rows match by
    /// <see cref="Scenario"/> reference; the ring never writes <c>curScen</c>.
    /// </summary>
    [HarmonyPatch(typeof(Page_SelectScenario), "DoScenarioListEntry")]
    internal static class ScenarioRowRingPatch
    {
        /// <summary>Every scenario's row rect, so Alt+Shift+J can route to the row under the mouse.</summary>
        internal static readonly RowGeometryCache RowGeometry = new RowGeometryCache();

        [HarmonyPostfix]
        public static void Postfix(Rect rect, Scenario scen)
        {
            try
            {
                if (Event.current.type != EventType.Repaint)
                {
                    return;
                }
                ScenarioSelectScreenScope scope = FocusStackLookup.TopmostOfType<ScenarioSelectScreenScope>();
                if (scope == null || scen == null)
                {
                    return;
                }
                RowGeometry.Record(scen, rect);
                if (scen != scope.FocusedScenario)
                {
                    return;
                }
                FocusRing.Draw(rect.ContractedBy(1f));
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Scenario row ring error", ex);
            }
        }
    }
}
