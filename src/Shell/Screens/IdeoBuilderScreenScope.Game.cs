using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Focus scope for <c>Page_ConfigureIdeo</c> and its Fluid subclass (the worldgen
    /// custom-ideoligion creation hub), window-attached via
    /// <see cref="ScopeForWindow.RegisterHierarchy"/> so the Fluid subclass needs no registration.
    ///
    /// Two typed regions. Ideoligion holds the section editor, built by
    /// <see cref="RimWorldAccess.IdeoBuilderHelper.BuildSections"/> for whichever ideo vanilla's
    /// details pane is drawing (<see cref="DisplayIdeo"/>), interleaved with the dev toggles, the
    /// fluid development-points row, the symbol/precept randomizers, the description lock, the DEV
    /// debug buttons, and a read-only validation/impact row. Every displayed ideo is FULLY editable,
    /// not just the page's own: both <c>DoIdeos</c> overrides pass <c>onlyEditIdeo: null</c>, so no
    /// row is ever forced Disabled for lack of ownership. The validation row alone is always about
    /// the PAGE's own ideo — the advance gate cares what will actually be submitted.
    /// Ideoligions holds one RadioButton row per <c>IdeosInViewOrder</c> (cursor movement live-syncs
    /// vanilla's selection silently; Enter switches the Ideoligion region's content) plus the
    /// Create/Load/Delete/Save rows, which mirror vanilla's own availability: Create and Load vanish
    /// once the player's primary ideo is exclusively theirs, and always on the Fluid page.
    ///
    /// <see cref="CaptureWindowButtons"/> is false — the page draws its own ButtonTexts, which the
    /// section rows would otherwise be miscounted alongside — so the Buttons region declares
    /// Back/Next/Randomize-all/Save itself.
    ///
    /// Escape is claimed unconditionally (search-clear first, else <see cref="EscapeBack"/>, which
    /// preserves the discard-confirmation flow); Next routes through
    /// <see cref="RimWorldAccess.IdeoBuilderHubPatch.TryDoNext"/>. Both wrap the reflected call in
    /// <see cref="BackRequested"/>/<see cref="AdvanceRequested"/> try/finally, read by the DoBack and
    /// DoNext guards to block the raw <c>Page.DoBack</c>/<c>DoNext</c> polls while this scope or one
    /// of the windowless overlay editors is live and the flag is unset.
    ///
    /// The overlay editors and Dialog_ChooseMemes mask this scope by ordinary stack layering, so
    /// <see cref="OnFocus"/> fires again when one closes — that is where the return-from-child
    /// rebuild and re-announce belongs.
    /// </summary>
    public sealed class IdeoBuilderScreenScope : ScreenScope
    {
        private enum Region { Editor = 0, List = 1 }

        private enum ListRowKind { Ideo, CreateCustom, LoadExisting, DeleteCustom, Save }

        private sealed class ListRow
        {
            public ListRowKind Kind;
            public Ideo Ideo;
        }

        /// <summary>
        /// True only while this scope's own Next is driving the vanilla gate-and-advance, so the
        /// Page.DoNext guard lets that one call through while still blocking the raw poll.
        /// </summary>
        internal static bool AdvanceRequested;

        /// <summary>
        /// True only while this scope's own confirmed Back is driving the vanilla DoBack, read by the
        /// Page.DoBack guard the same way.
        /// </summary>
        internal static bool BackRequested;

        /// <summary>The single live instance, for <see cref="NotifyIdeoEdited"/> — at most one Page_ConfigureIdeo is ever open.</summary>
        private static IdeoBuilderScreenScope active;

        private readonly Page_ConfigureIdeo page;
        private readonly bool fluid;
        private readonly List<ScreenAction> actions = new List<ScreenAction>(3);
        private readonly IdeoEditorRegionCore editorCore;
        private readonly List<ListRow> listRows = new List<ListRow>();
        private bool announcedOpen;

        public IdeoBuilderScreenScope(Page_ConfigureIdeo page)
        {
            this.page = page;
            fluid = page is Page_ConfigureFluidIdeo;
            active = this;
            RimWorldAccess.IdeoEditNotifyHub.Register(NotifyIdeoEdited);

            editorCore = new IdeoEditorRegionCore(new IdeoEditorRegionCore.Options
            {
                DisplayIdeo = () => DisplayIdeo,
                RefreshModel = RefreshModel,
                AnnounceCurrentItem = AnnounceCurrentItem,
                IncludeDevToggles = true,
                IncludeFluidDevPoints = true,
                IncludeDebugButtons = true,
                IncludeSection = null,
                ValidationOrImpactText = BuildValidationOrImpactText,
            });

            Claim(SharedMenuGrammar.Cancel, e => EscapeBack(), when: () => !TypeaheadHasActiveSearch);
            Claim(SharedMenuGrammar.Info, OnInfo);
            Claim("ideoBuilder.continueNext", OnContinueNext);
            Claim("ideoBuilder.randomizeAll", e => RandomizeAllAction());
            Claim("ideoBuilder.saveToFile", e => SaveToFileAction());
        }

        public override string Name
        {
            get { return "ideo-builder-hub"; }
        }

        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        protected internal override Window OwnedWindow
        {
            get { return page; }
        }

        /// <summary>The page draws its own Back/Next/Randomize-all ButtonTexts; declare them instead so the section rows beneath aren't miscounted as buttons.</summary>
        protected override bool CaptureWindowButtons
        {
            get { return false; }
        }

        /// <summary>Surface anything vanilla or a mod draws that the typed regions do not present.</summary>
        protected override bool IncludeCapturedExtrasRegion
        {
            get { return true; }
        }

        public override void OnPush()
        {
            base.OnPush();
            IdeoBoxDrawPatch.AddInterest();
            IdeoBoxDrawPatch.FollowTarget = FollowTargetRect;
        }

        public override void OnPop()
        {
            IdeoBoxDrawPatch.FollowTarget = null;
            IdeoBoxDrawPatch.RemoveInterest();
            base.OnPop();
            if (ReferenceEquals(active, this))
            {
                active = null;
                RimWorldAccess.IdeoEditNotifyHub.Unregister(NotifyIdeoEdited);
            }
        }

        /// <summary>
        /// Notifies this host that a sub-editor changed the ideo. Called by
        /// <see cref="RimWorldAccess.IdeoSymbolEditState"/>'s shared <c>AfterEdit</c> when this scope
        /// is the live one, and after Randomize All.
        /// </summary>
        internal static void NotifyIdeoEdited()
        {
            NotifyIdeoEdited(true);
        }

        /// <summary>
        /// The hub's registered <see cref="RimWorldAccess.IdeoEditNotifyHub"/> callback.
        /// <paramref name="announce"/> false refreshes the rows silently.
        /// </summary>
        internal static void NotifyIdeoEdited(bool announce)
        {
            if (active == null)
            {
                return;
            }
            active.RefreshModel();
            if (announce)
            {
                active.AnnounceCurrentItem();
            }
        }

        protected override void RefreshContent()
        {
            editorCore.Rebuild();
            BuildListRows();
        }

        /// <summary>
        /// The ideo the Ideoligion region currently shows — every row and action below operates on
        /// THIS, never unconditionally on <c>page.ideo</c>.
        /// Read from vanilla's own decision rather than a local mirror: the details pane draws
        /// <c>selected ?? mouseoverIdeo ?? FallbackSelectedIdeo</c>, so reading <c>selected</c>
        /// directly keeps the typed rows, the extras haystack and the pixels on screen the same
        /// ideoligion by construction. A local mirror instead let the drawn pane and the typed rows
        /// describe two different ideos, dumping the drawn one's strings into Additional controls.
        /// <c>mouseoverIdeo</c> is hover-only and never exposed; <c>page.ideo</c> is the fallback.
        /// </summary>
        private Ideo DisplayIdeo
        {
            get
            {
                Ideo selected = IdeoUIUtility.selected;
                if (selected != null && Find.IdeoManager.IdeosListForReading.Contains(selected))
                {
                    return selected;
                }
                return page.ideo;
            }
        }

        private void BuildListRows()
        {
            listRows.Clear();
            foreach (Ideo ideo in Find.IdeoManager.IdeosInViewOrder)
            {
                listRows.Add(new ListRow { Kind = ListRowKind.Ideo, Ideo = ideo });
            }
            // The fluid page passes showCreateIdeoButton: false unconditionally; the fixed page
            // passes !PlayerPrimaryIdeoNotShared, so Create (and the LoadExisting nested inside it)
            // show only while the player's primary ideo is still shared. DeleteCustom's gate is the
            // opposite condition, so the two groups are mutually exclusive.
            if (!fluid)
            {
                bool showCreateIdeoButton = !IdeoUIUtility.PlayerPrimaryIdeoNotShared;
                if (showCreateIdeoButton)
                {
                    listRows.Add(new ListRow { Kind = ListRowKind.CreateCustom });
                    if (GenFilePaths.AllCustomIdeoFiles.Any())
                    {
                        listRows.Add(new ListRow { Kind = ListRowKind.LoadExisting });
                    }
                }
                if (IdeoUIUtility.PlayerPrimaryIdeoNotShared)
                {
                    listRows.Add(new ListRow { Kind = ListRowKind.DeleteCustom });
                }
            }
            listRows.Add(new ListRow { Kind = ListRowKind.Save });
        }

        protected override int ContentRegionCount
        {
            get { return 2; }
        }

        protected override string ContentRegionName(int region)
        {
            return (Region)region == Region.Editor
                ? (string)"RimWorldAccess.Ideology.Builder.EditorRegion".Translate()
                : (string)"RimWorldAccess.Ideology.Builder.ListRegion".Translate();
        }

        protected override int ContentItemCount(int region)
        {
            return (Region)region == Region.Editor ? editorCore.RowCount : listRows.Count;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            return (Region)region == Region.Editor ? editorCore.Describe(index) : DescribeListRow(index);
        }

        protected override void ActivateContentItem(int region, int index)
        {
            if ((Region)region == Region.Editor)
            {
                editorCore.Activate(index);
            }
            else
            {
                ActivateListRow(index);
            }
        }

        /// <summary>List-region cursor movement live-syncs vanilla's own <c>selected</c> static, silently.</summary>
        protected override void MoveItem(int delta)
        {
            base.MoveItem(delta);
            if ((Region)Model.RegionIndex == Region.List)
            {
                SelectCurrentListIdeoSilently();
            }
        }

        protected override void MoveItemEdge(bool first)
        {
            base.MoveItemEdge(first);
            if ((Region)Model.RegionIndex == Region.List)
            {
                SelectCurrentListIdeoSilently();
            }
        }

        protected override void OnRegionChanged(MoveResult result)
        {
            base.OnRegionChanged(result);
            if ((Region)Model.RegionIndex == Region.List)
            {
                SelectCurrentListIdeoSilently();
            }
        }

        private void SelectCurrentListIdeoSilently()
        {
            ListModel region = Model.Region((int)Region.List);
            if (region == null || region.IsEmpty || region.Index < 0 || region.Index >= listRows.Count)
            {
                return;
            }
            ListRow row = listRows[region.Index];
            if (row.Kind != ListRowKind.Ideo || IdeoUIUtility.selected == row.Ideo)
            {
                return;
            }
            if (TutorSystem.AllowAction("ConfiguringIdeo"))
            {
                IdeoUIUtility.SetSelected(row.Ideo);
                // Selection IS what the details pane draws, so the Ideoligion region's rows must be
                // re-derived in the same step or Tab lands on rows describing the ideoligion that
                // just left the screen. Silent: the caller owns the announcement.
                RefreshModel();
            }
        }

        /// <summary>
        /// Worldgen's validation-row text policy: a validation error takes priority, else the impact
        /// readout plus any non-blocking precept warning. Kept here rather than in
        /// <see cref="IdeoEditorRegionCore"/> because it always reads <c>page.ideo</c>, never the
        /// displayed ideo, and the core must never reference <c>Page_ConfigureIdeo</c>.
        /// </summary>
        private string BuildValidationOrImpactText()
        {
            Ideo ideo = page.ideo;
            if (ideo == null)
            {
                return "";
            }
            string err = IdeoBuilderHelper.BuildValidationSummary(ideo);
            if (!string.IsNullOrEmpty(err))
            {
                return err;
            }
            var parts = new List<string>();
            List<MemeDef> normals = ideo.memes.Where(m => m.category == MemeCategory.Normal).ToList();
            if (normals.Count > 0)
            {
                int impact = IdeoBuilderHelper.ImpactOf(normals);
                string impactLabel = IdeoImpactUtility.OverallImpactLabel(impact);
                parts.Add(((string)"IdeoImpact".Translate()) + ": " + impactLabel);
            }
            string warning = IdeoBuilderHelper.BuildPlayerWarning(ideo);
            if (!string.IsNullOrEmpty(warning))
            {
                parts.Add(warning);
            }
            return string.Join(". ", parts);
        }

        /// <summary>
        /// The Editor row the cursor rests on, or -1 when it is anywhere else. The List region needs
        /// no ring of its own: cursor movement there live-syncs <c>IdeoUIUtility.selected</c> and
        /// vanilla draws <c>Widgets.DrawHighlightSelected</c> on exactly that row.
        /// </summary>
        private int FocusedEditorRow()
        {
            if ((Region)Model.RegionIndex != Region.Editor)
            {
                return -1;
            }
            ListModel region = Model.CurrentRegion;
            return region != null && !region.IsEmpty ? region.Index : -1;
        }

        protected internal override Rect FocusedContentRect()
        {
            int row = FocusedEditorRow();
            return row >= 0 ? editorCore.RowScreenRect(row) : default(Rect);
        }

        private Rect? FollowTargetRect()
        {
            int row = FocusedEditorRow();
            return row >= 0 ? editorCore.RowRawRect(row) : null;
        }

        protected override void OnCursorSettled(int region, int index)
        {
            base.OnCursorSettled(region, index);
            if ((Region)region == Region.Editor)
            {
                IdeoBoxDrawPatch.RequestFollow();
            }
        }

        private ElementDescription DescribeListRow(int index)
        {
            var d = new ElementDescription();
            if (index < 0 || index >= listRows.Count)
            {
                return d;
            }
            ListRow row = listRows[index];
            switch (row.Kind)
            {
                case ListRowKind.Ideo:
                    d.Label = IdeologyHelper.BuildIdeoListAnnouncement(row.Ideo);
                    d.Role = ElementRole.RadioButton;
                    d.Selected = ReferenceEquals(DisplayIdeo, row.Ideo);
                    if (ReferenceEquals(row.Ideo, page.ideo))
                    {
                        d.Extras = (string)"RimWorldAccess.Ideology.Builder.Status.Yours".Translate();
                    }
                    return d;
                case ListRowKind.CreateCustom:
                    d.Label = ((string)"CreateCustom".Translate()) + "...";
                    d.Role = ElementRole.Button;
                    return d;
                case ListRowKind.LoadExisting:
                    d.Label = ((string)"LoadExisting".Translate()) + "...";
                    d.Role = ElementRole.Button;
                    return d;
                case ListRowKind.DeleteCustom:
                    d.Label = (string)"DeleteCustom".Translate();
                    d.Role = ElementRole.Button;
                    return d;
                case ListRowKind.Save:
                    d.Label = (string)"Save".Translate();
                    d.Role = ElementRole.Button;
                    return d;
                default:
                    return d;
            }
        }

        private void ActivateListRow(int index)
        {
            if (index < 0 || index >= listRows.Count)
            {
                return;
            }
            ListRow row = listRows[index];
            switch (row.Kind)
            {
                case ListRowKind.Ideo:
                    ActivateListIdeoRow(row.Ideo);
                    return;
                case ListRowKind.CreateCustom:
                    ActivateCreateCustom();
                    return;
                case ListRowKind.LoadExisting:
                    Find.WindowStack.Add(new Dialog_IdeoList_Load(delegate (Ideo loaded)
                    {
                        page.SelectOrMakeNewIdeo(loaded);
                    }));
                    return;
                case ListRowKind.DeleteCustom:
                    ActivateDeleteCustom();
                    return;
                case ListRowKind.Save:
                    // Vanilla's own Save button saves whichever ideo the details pane currently
                    // shows, not unconditionally page.ideo.
                    IdeoEditorCommands.SaveIdeoligion(DisplayIdeo);
                    return;
            }
        }

        /// <summary>
        /// Enter on an ideoligion-list row. Deliberately does NOT mirror vanilla's mouse click, which
        /// calls <c>SelectOrMakeNewIdeo</c> for any row: arrowing through the list would silently
        /// reassign the player's own ideoligion on every step. Enter only moves the cursor into the
        /// Ideoligion region for the selected ideo, which stays fully editable.
        /// </summary>
        private void ActivateListIdeoRow(Ideo ideo)
        {
            if (!TutorSystem.AllowAction("ConfiguringIdeo"))
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }
            IdeoUIUtility.SetSelected(ideo);
            SwitchToEditorRegion();
        }

        private void ActivateCreateCustom()
        {
            page.SelectOrMakeNewIdeo();
            IdeoUIUtility.SetSelected(page.ideo);
            SwitchToEditorRegion();
        }

        private void ActivateDeleteCustom()
        {
            // Mirrors IdeoUIUtility.DoIdeoList's DeleteCustom button body.
            Ideo removeCustomIdeo = Faction.OfPlayer.ideos.PrimaryIdeo;
            if (removeCustomIdeo == null)
            {
                return;
            }
            Faction.OfPlayer.ideos.SetPrimary(Find.IdeoManager.IdeosInViewOrder.First(newIdeo => newIdeo != removeCustomIdeo));
            IdeoUIUtility.SetSelected(Faction.OfPlayer.ideos.PrimaryIdeo);
            page.SelectOrMakeNewIdeo(IdeoUIUtility.selected);
            Find.IdeoManager.Remove(removeCustomIdeo);
            SwitchToEditorRegion();
        }

        /// <summary>Rebuilds region content and jumps to the top of the Ideoligion region with a full structural re-announce.</summary>
        private void SwitchToEditorRegion()
        {
            RefreshModel();
            Model.MoveToRegion((int)Region.Editor);
            ListModel region = Model.Region((int)Region.Editor);
            if (region != null && !region.IsEmpty)
            {
                region.MoveFirst();
            }
            AnnounceRegion();
        }

        // Buttons region: Back / Next / Randomize all ride the vanilla page vehicle.

        /// <summary>Next is this page's default proceed button.</summary>
        protected override string DefaultAcceptActionId
        {
            get { return "ideoBuilder.continueNext"; }
        }

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                actions.Add(new ScreenAction("Back".Translate(), BackAction, SharedMenuGrammar.Cancel));
                actions.Add(new ScreenAction("Next".Translate(), NextAction, "ideoBuilder.continueNext"));
                actions.Add(new ScreenAction("RandomizeAll".Translate(), RandomizeAllAction, "ideoBuilder.randomizeAll"));
                actions.Add(new ScreenAction("Save".Translate(), SaveToFileAction, "ideoBuilder.saveToFile"));
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
            IdeoBuilderHubPatch.TryDoBack(page);
        }

        private void OnContinueNext(KeyEventSnapshot e)
        {
            ShellFrameStamps.MarkAcceptConsumed();
            NextAction();
        }

        private void NextAction()
        {
            IdeoBuilderHubPatch.TryDoNext(page);
        }

        private void RandomizeAllAction()
        {
            // Vanilla's "Randomize all" is a page bottom button, not part of the details pane, and
            // its handler always reads page.ideo regardless of what the panes show. No reject: while
            // the player browses another ideoligion that view is simply left unchanged, exactly as a
            // sighted player would see.
            if (page.ideo == null)
            {
                return;
            }
            IdeoBuilderHubPatch.TryRandomizeAll(page);
        }

        private void SaveToFileAction()
        {
            // Toolbar-level, like RandomizeAllAction: always the page's own ideo. The List region's
            // own Save row mirrors vanilla's DisplayIdeo-scoped button instead.
            if (page.ideo == null)
            {
                return;
            }
            IdeoEditorCommands.SaveIdeoligion(page.ideo);
        }

        // Alt+I drill-in: only Editor-region section rows carry inspectable defs.

        private void OnInfo(KeyEventSnapshot e)
        {
            RefreshModel();
            List<Def> defs = CurrentRowInspectableDefs();
            if (defs.Count == 0)
            {
                InfoCardState.SpeakNoInfoCardAvailable();
                return;
            }
            if (defs.Count == 1)
            {
                InfoCardState.OpenInfoCardForDef(defs[0]);
                return;
            }
            var options = new List<FloatMenuOption>();
            foreach (Def def in defs)
            {
                Def captured = def;
                string label = def.label != null ? def.label.CapitalizeFirst() : def.defName;
                options.Add(new FloatMenuOption(label, delegate { InfoCardState.OpenInfoCardForDef(captured); }));
            }
            TolkHelper.Speak("RimWorldAccess.InfoCard.ChooseItemToInspect".Loc());
            WindowlessFloatMenuState.Open(options, false);
        }

        private List<Def> CurrentRowInspectableDefs()
        {
            if ((Region)Model.RegionIndex != Region.Editor)
            {
                return new List<Def>();
            }
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty || region.Index < 0 || region.Index >= editorCore.RowCount)
            {
                return new List<Def>();
            }
            return editorCore.InspectableDefs(region.Index);
        }

        // Captured-extras suppression.

        protected override IEnumerable<string> AdditionalPresentedTexts
        {
            get
            {
                yield return (string)"CustomizeIdeoligion".Translate();
                // The page title and the list panel's first section header render adjacent
                // and fuse into one captured row — one composite line covers that fusion.
                yield return (string)"CustomizeIdeoligion".Translate() + " "
                    + (string)"CustomIdeoligionSectionHeader".Translate();
                // In the shared helper: vanilla draws this caption for any editMode != None, so the
                // in-game viewer's dev-edit-on state needs it too.
                foreach (string text0 in IdeoDetailsPresentedTexts.EditModeCaption(editAffordancesVisible: true))
                {
                    yield return text0;
                }
                // The list panel's two section headers, mirrored by the Ideoligions region itself.
                yield return (string)"CustomIdeoligionSectionHeader".Translate();
                yield return (string)"FactionIdeoligionSectionHeader".Translate();
                // The details pane draws its own bare Load/Save buttons in addition to the list
                // panel's LoadExisting — same actions as this scope's rows, different literal text.
                yield return (string)"Load".Translate();
                // The ritual-ambience-preview ButtonImage is ProgramState.Entry-gated in vanilla, so
                // it is worldgen-only and its two texture-name states stay inline here rather than
                // moving to the shared IdeoDetailsPresentedTexts helper. The description-lock icon
                // below is gated on editMode alone, so every edit-affordance host draws it.
                yield return "PreviewSound_NotPlaying";
                yield return "PreviewSound_Playing";
                foreach (string text in IdeoDetailsPresentedTexts.DescriptionLockIconStates(editAffordancesVisible: true))
                {
                    yield return text;
                }
                // Belt-and-suspenders for the per-category randomize button label.
                yield return (string)"RandomizePrecepts".Translate();
                // The per-category "Add ..." buttons: each category's add flow lives inside this
                // scope's section editors, so the pane buttons are deliberate mirrors. In the shared
                // helper because every host's typed Section rows carry the FACET name, not this text.
                foreach (string text1 in IdeoDetailsPresentedTexts.AddPreceptButtonLabels(editAffordancesVisible: true))
                {
                    yield return text1;
                }
                // "AddDeity"/"RandomizeDeities", drawn by IdeoFoundation_Deity.DoInfo.
                foreach (string text2 in IdeoDetailsPresentedTexts.DeitySectionButtonLabels(editAffordancesVisible: true))
                {
                    yield return text2;
                }
                // The ideo vanilla's details pane is actually drawing: reading page.ideo here leaves
                // every string of a browsed foreign ideoligion unmirrored.
                Ideo ideo = DisplayIdeo;
                if (ideo != null)
                {
                    // The ideo-keyed composites shared with every DoIdeoDetails-drawing host.
                    foreach (string text in IdeoDetailsPresentedTexts.ForIdeo(ideo))
                    {
                        yield return text;
                    }
                }
            }
        }

        public override void OnFocus()
        {
            base.OnFocus();
            // Re-arm: a sub-editor opened from a section row registers its own follow target and
            // clears it again on pop, so regaining focus is where this page takes it back.
            IdeoBoxDrawPatch.FollowTarget = FollowTargetRect;
            if (announcedOpen)
            {
                // Returning from a child window, dialog or overlay editor.
                AnnounceCurrentItem();
                return;
            }
            announcedOpen = true;
            string tabCount = TabCountFragment();
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(tabCount))
            {
                sb.Append(tabCount).Append(". ");
            }
            sb.Append((string)"CustomizeIdeoligion".Translate());
            if (page.ideo != null && !page.ideo.name.NullOrEmpty())
            {
                sb.Append(". ").Append(page.ideo.name);
            }
            if (listRows.Count(r => r.Kind == ListRowKind.Ideo) > 1)
            {
                sb.Append(". ").Append((string)"RimWorldAccess.Ideology.Builder.TabForIdeoList".Translate());
            }
            TolkHelper.SpeakData(sb.ToString(), SpeechPriority.High);
            AnnounceCurrentItem();
        }
    }
}
