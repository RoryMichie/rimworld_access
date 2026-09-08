using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The keyboard scope for the windowless "virtual" float menu
    /// (<see cref="WindowlessFloatMenuState"/>). Modal, gated bare on
    /// <c>WindowlessFloatMenuState.IsActive</c>, and reconciled LAST OF ALL MIRRORS so its Push
    /// floats it above everything else (see <see cref="FloatMenuOverlayScopeMirror"/>).
    ///
    /// Real <c>FloatMenu</c>/<c>FloatMenuMap</c>/<c>FloatMenuWorld</c> windows belong to the
    /// window-attached <c>FloatMenuScope</c> instead; <c>DialogInterceptionPatch</c>'s redirects
    /// only decide which system a menu enters, never how its keys route afterwards.
    ///
    /// One content region over the state's own option list, so navigation, typeahead and
    /// announcement composition are the chassis's. <c>SelectedIndex</c> remains the selection datum
    /// the visual twin's focus ring and the quest/area menus read; this scope publishes its cursor
    /// into it on every landing.
    ///
    /// Enter stamps <c>MarkAcceptConsumed</c> first, then runs the state's activation vehicle,
    /// which Closes BEFORE running the option's action so the action may open the next menu. Escape
    /// stamps <c>MarkCancelConsumed</c> first, then plays FloatMenu_Cancel, calls Cancel(), resets
    /// ArchitectState where applicable, and picks one of three closing announcements (see
    /// <see cref="OnCancel"/>). Clearing a live search ahead of all that is the chassis's own Escape
    /// claim, registered first.
    ///
    /// The stamps are LOAD-BEARING: these menus sit over real dialogs (trade, caravan, rituals,
    /// bills), and the same physical Enter/Escape must not also reach the dialog's own vanilla
    /// handler. Since this scope has no attached window, the accept/cancel routers take their
    /// "an overlay scope owns it outright" branch.
    ///
    /// Card-over-foreign-menu precedence lives in the mirror's gate,
    /// <c>!(InfoCardState.IsActive &amp;&amp; !InfoCardState.OwnsFloatMenu)</c>: Alt+I opens an info
    /// card WITHOUT closing the menu, and this scope's per-pass Push would otherwise re-float above
    /// the window-attached InfoCardScope and leave the fresh card deaf. The scope stands down while
    /// a card it does not own is open and is re-pushed over the still-open menu when the card
    /// closes; a card the menu DOES own stays float-scope-driven so
    /// <see cref="InfoCardState.ReleaseFloatMenu"/>'s Escape branch fires.
    /// </summary>
    public sealed class FloatMenuOverlayScope : ScreenScope
    {
        /// <summary>
        /// The option list this cursor was last snapped to, by reference. <see cref="SyncToState"/>
        /// compares against it to notice a fresh menu (or a drill-in submenu, which never pops this
        /// scope) and follow the state's own starting index.
        /// </summary>
        private List<FloatMenuOption> knownOptions;

        public FloatMenuOverlayScope()
        {
            Claim(SharedMenuGrammar.Info, delegate { WindowlessFloatMenuState.TryOpenInfoCardForSelected(); });
            // Shift+Enter: a float menu has no proceed button, so the chord keeps its vanilla
            // shift-click meaning — ExecuteSelected reads the live shift bit and queues the order.
            // The base's SearchSettle claim shares the chord and takes it first during a search.
            Claim(SharedMenuGrammar.ActivateDefault, e => ActivateCurrent(),
                when: () => !TypeaheadHasActiveSearch);
            Claim(SharedMenuGrammar.Cancel, OnCancel, when: () => !TypeaheadHasActiveSearch);
        }

        public override string Name
        {
            get { return "float-menu-overlay"; }
        }

        public override bool IsLive
        {
            get { return true; }
        }

        /// <summary>
        /// Unconditional cancel ownership, load-bearing rather than defensive. A windowless float
        /// menu sits over real dialogs whose <c>closeOnAccept</c>/<c>closeOnCancel</c> default true,
        /// and the frame stamps cover only their own frame while the focused dialog's deferred
        /// <c>InnerWindowOnGUI</c> re-test can fire on a LATER frame (QA R6), closing the dialog
        /// behind the menu's back and discarding the player's work. Owning accept and cancel makes
        /// the accept/cancel routers block every underlying window for as long as this overlay is
        /// top, which is frame-independent. Coverage is continuous: the instant the mirror pops this
        /// overlay, the scope beneath becomes top. The base makes cancel typeahead-conditional,
        /// which a windowless overlay cannot afford.
        /// </summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        /// <summary>A float menu draws no button row of its own.</summary>
        protected override bool IncludeActionsRegion
        {
            get { return false; }
        }

        /// <summary>
        /// The star key is consume-only, never a search character: routing '*' into the search would
        /// look for a literal asterisk in every option label and speak an unwanted "no matches".
        /// Required, not tidy — without it the char reaches the search through the IME channel.
        /// </summary>
        public override bool HandleChar(char c)
        {
            if (c == '*')
            {
                return true;
            }
            return base.HandleChar(c);
        }

        public override void OnPush()
        {
            base.OnPush();
            // Long-lived singleton: the cursor is re-derived from the state's starting index on the
            // next sync, and the state's own Open speaks the opening announcement (title folded into
            // the first option as ONE utterance), so the chassis must not add a second.
            knownOptions = null;
            SuppressNextEntryAnnouncement();
        }

        public override void OnPop()
        {
            base.OnPop();
            knownOptions = null;
        }

        /// <summary>
        /// Ticked by <see cref="FloatMenuOverlayScopeMirror"/> every dispatcher pass while the menu
        /// is live; this scope has no draw pass of its own. Follows the state's selected row
        /// whenever it differs from the cursor, covering a fresh open, a drill-in submenu (which
        /// never pops this scope), and a selection moved by something else. Convergent rather than a
        /// tug of war: every landing publishes the cursor into the state first
        /// (<see cref="OnCursorSettled"/>), so they differ only when the other side moved. Silent.
        /// </summary>
        internal void SyncToState()
        {
            List<FloatMenuOption> options = WindowlessFloatMenuState.CurrentOptions;
            bool swapped = !ReferenceEquals(options, knownOptions);
            knownOptions = options;
            int selected = WindowlessFloatMenuState.SelectedIndex;
            RefreshModel();
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty)
            {
                return;
            }
            if (swapped || selected != region.Index)
            {
                region.MoveTo(selected);
            }
        }

        // Content: the state's own option list.

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        /// <summary>Reuses the shared "Menu" phrase; a single-region screen only ever speaks it on a region frame.</summary>
        protected override string ContentRegionName(int region)
        {
            return "RimWorldAccess.MainMenu.MenuRegion".Translate().ToString();
        }

        protected override int ContentItemCount(int region)
        {
            List<FloatMenuOption> options = WindowlessFloatMenuState.CurrentOptions;
            return options == null ? 0 : options.Count;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            List<FloatMenuOption> options = WindowlessFloatMenuState.CurrentOptions;
            if (options == null || index < 0 || index >= options.Count)
            {
                return new ElementDescription();
            }
            return WindowlessFloatMenuState.DescribeOption(options[index]);
        }

        /// <summary>
        /// The bare option label, not the composed description: the haystack is rebuilt per row per
        /// keystroke, and describing a row runs the option's own dynamic tooltip getter.
        /// </summary>
        protected override string ContentRowSearchText(int region, int row)
        {
            List<FloatMenuOption> options = WindowlessFloatMenuState.CurrentOptions;
            return options != null && row >= 0 && row < options.Count ? (options[row].Label ?? "") : "";
        }

        /// <summary>
        /// Enter: the state's own activation vehicle, the one the twin's mouse click runs. It Closes
        /// BEFORE running the option's action, so the action may open the next menu.
        /// </summary>
        protected override void ActivateContentItem(int region, int index)
        {
            WindowlessFloatMenuState.ActivateIndex(index);
        }

        /// <summary>Keeps the state's selection — which the visual twin's focus ring and auto-scroll read — equal to this scope's cursor on every landing.</summary>
        protected override void OnCursorSettled(int region, int index)
        {
            WindowlessFloatMenuState.PublishSelectedIndex(index);
        }

        /// <summary>
        /// The menu has no window, and the twin drawing it is an ImmediateWindow, which
        /// <see cref="ShellGuards.NonImmediateWindowUnderPointer"/> skips by design; handing the
        /// twin's window to <see cref="PointerRouting.PointerOwnedBy"/> asks the right question —
        /// the pointer is inside the twin and no real window sits over it.
        /// </summary>
        protected override Window PointerSurface
        {
            get { return base.PointerSurface ?? FloatMenuTwin.LiveWindow; }
        }

        private protected override void CollectRouteCandidates(
            List<PointerHitCandidate> candidates, List<RouteTarget> targets)
        {
            base.CollectRouteCandidates(candidates, targets);
            IReadOnlyList<PointerHitCandidate> hits = FloatMenuTwin.RowHits;
            for (int i = 0; i < hits.Count; i++)
            {
                candidates.Add(hits[i]);
                targets.Add(new RouteTarget { Region = 0, Index = i });
            }
        }

        private void OnCancel(KeyEventSnapshot e)
        {
            ShellFrameStamps.MarkCancelConsumed();

            SoundDefOf.FloatMenu_Cancel.PlayOneShotOnCamera();
            WindowlessFloatMenuState.Cancel();

            // Escaping a drill-in submenu whose close callback re-opened its parent is a step BACK,
            // not a close: the parent's own first-row announcement is the one utterance, so none of
            // the "menu closed" wording applies and this scope stays live.
            if (WindowlessFloatMenuState.IsActive)
            {
                return;
            }

            if (ArchitectState.IsActive && !ArchitectState.IsInPlacementMode)
            {
                ArchitectState.Reset();
            }

            if (InfoCardState.OwnsFloatMenu)
            {
                InfoCardState.ReleaseFloatMenu();
                TolkHelper.Speak("RimWorldAccess.Inspection.InfoCard.MenuClosed".Loc());
            }
            else if (AssignMenuState.IsActive)
            {
                AssignMenuState.AnnounceCurrentCell(includeItemName: false);
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Input.Close.MenuClosed".Loc());
            }
        }
    }

    /// <summary>
    /// The quest reward-menu coexistence layer: a small NON-modal scope reconciled AFTER
    /// <see cref="FloatMenuOverlayScope"/> so it sits above it, claiming only Escape (in the
    /// item-inspection sub-mode) and Alt+I and letting everything else continue the dispatch walk
    /// to the overlay beneath — only a modal scope stops that walk.
    /// Gated on <c>QuestMenuState.IsActive &amp;&amp; HasActiveRewardMenu &amp;&amp;
    /// WindowlessFloatMenuState.IsActive</c>; <see cref="QuestMenuScopeMirror"/>'s own
    /// <c>!WindowlessFloatMenuState.IsActive</c> pop condition stays, so QuestMenuScope stands down
    /// while its reward-choice menu is up and this layer fills the gap.
    ///
    /// The reward choices ARE <see cref="WindowlessFloatMenuState"/>'s option list, so this scope
    /// presents that list as its single region and hands the navigation chords back:
    /// <see cref="ContentItemOwnsNavigationKeys"/> is true and <see cref="EnableRegionCycling"/>
    /// false. Enter, which the chassis claims unconditionally, routes to the same activation vehicle
    /// the scope beneath would have run rather than a second copy of the choose logic.
    /// </summary>
    public sealed class QuestRewardOverlayScope : ScreenScope
    {
        public QuestRewardOverlayScope()
        {
            Claim(SharedMenuGrammar.Cancel, OnCancel, when: InItemInspection);
            Claim(SharedMenuGrammar.Info, delegate { QuestMenuState.OpenItemInspectionForCurrentChoice(); },
                when: () => !QuestMenuState.IsInItemInspectionMenu);
        }

        public override string Name
        {
            get { return "quest-reward-overlay"; }
        }

        public override bool IsLive
        {
            get { return true; }
        }

        public override bool IsModal
        {
            get { return false; }
        }

        /// <summary>Escape belongs to this scope only in the item-inspection sub-mode; in reward-choice mode it is the overlay beneath that answers.</summary>
        public override bool OwnsCancel
        {
            get { return InItemInspection(); }
        }

        protected override bool IncludeActionsRegion
        {
            get { return false; }
        }

        protected override bool EnableRegionCycling
        {
            get { return false; }
        }

        /// <summary>The reward rows are navigated by the overlay beneath — see the class remarks.</summary>
        protected override bool ContentItemOwnsNavigationKeys(int region, int index)
        {
            return true;
        }

        public override void OnPush()
        {
            base.OnPush();
            // Pushed over an already-announced menu: the rows were spoken by the overlay beneath.
            SuppressNextEntryAnnouncement();
        }

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        /// <summary>Reuses the shared "Menu" phrase; a single-region screen only ever speaks it on a region frame.</summary>
        protected override string ContentRegionName(int region)
        {
            return "RimWorldAccess.MainMenu.MenuRegion".Translate().ToString();
        }

        protected override int ContentItemCount(int region)
        {
            List<FloatMenuOption> options = WindowlessFloatMenuState.CurrentOptions;
            return options == null ? 0 : options.Count;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            List<FloatMenuOption> options = WindowlessFloatMenuState.CurrentOptions;
            if (options == null || index < 0 || index >= options.Count)
            {
                return new ElementDescription();
            }
            return WindowlessFloatMenuState.DescribeOption(options[index]);
        }

        /// <summary>
        /// Enter: inspect in the item-inspection sub-mode, otherwise run the menu's own activation
        /// on the LIVE selection — this cursor never moves (the rows are navigated beneath), so the
        /// state's selection is the only honest source for which row Enter means.
        /// </summary>
        protected override void ActivateContentItem(int region, int index)
        {
            if (InItemInspection())
            {
                QuestMenuState.InspectCurrentItem();
                return;
            }
            WindowlessFloatMenuState.ExecuteSelected();
        }

        /// <summary>Keeps the state's selection — which InspectCurrentItem and the visual twin read — equal to wherever a pointer route landed this cursor.</summary>
        protected override void OnCursorSettled(int region, int index)
        {
            WindowlessFloatMenuState.PublishSelectedIndex(index);
        }

        /// <summary>Alt+Shift+J routes to the menu rows the overlay beneath draws — see <see cref="FloatMenuOverlayScope.PointerSurface"/>.</summary>
        protected override Window PointerSurface
        {
            get { return base.PointerSurface ?? FloatMenuTwin.LiveWindow; }
        }

        private protected override void CollectRouteCandidates(
            List<PointerHitCandidate> candidates, List<RouteTarget> targets)
        {
            base.CollectRouteCandidates(candidates, targets);
            IReadOnlyList<PointerHitCandidate> hits = FloatMenuTwin.RowHits;
            for (int i = 0; i < hits.Count; i++)
            {
                candidates.Add(hits[i]);
                targets.Add(new RouteTarget { Region = 0, Index = i });
            }
        }

        private static bool InItemInspection()
        {
            return QuestMenuState.IsInItemInspectionMenu;
        }

        private static void OnCancel(KeyEventSnapshot e)
        {
            ShellFrameStamps.MarkCancelConsumed();
            QuestMenuState.ReturnToRewardChoiceMenu();
        }
    }

    /// <summary>
    /// Reconciles the float-menu family: FloatMenuOverlayScope first, QuestRewardOverlayScope
    /// second so its later Push lands above. Must be the LAST mirror call in the dispatcher pass so
    /// its Push always wins while live, whatever else was live a moment ago.
    /// </summary>
    internal static class FloatMenuOverlayScopeMirror
    {
        private static readonly FloatMenuOverlayScope floatMenu = new FloatMenuOverlayScope();
        private static readonly QuestRewardOverlayScope questReward = new QuestRewardOverlayScope();

        public static void Reconcile()
        {
            // The second term keeps a card opened OVER a foreign float menu (Alt+I does not close
            // the menu) in charge of the keyboard: standing down lets the window-attached
            // InfoCardScope drive, and the card's Escape lets this mirror re-push the float scope
            // over the still-open menu next pass. A card the menu itself owns stays float-scope-
            // driven so its dedicated Escape branch fires. The quest mini-scope shares the term so
            // its own claims cannot steal the card's keys either.
            bool floatMenuDrives = WindowlessFloatMenuState.IsActive
                && !(InfoCardState.IsActive && !InfoCardState.OwnsFloatMenu);
            ReconcileOne(floatMenu, floatMenuDrives);
            if (floatMenuDrives)
            {
                // Windowless equivalent of a draw-pass MirrorLive: follow the state's own selection
                // whenever it hands us a different option list.
                floatMenu.SyncToState();
            }
            ReconcileOne(questReward,
                floatMenuDrives && QuestMenuState.IsActive && QuestMenuState.HasActiveRewardMenu);
        }

        private static void ReconcileOne(FocusScope scope, bool live)
        {
            if (live)
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
