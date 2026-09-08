using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The skeleton every RimTalk text dialog repeats on top of
    /// <see cref="ScreenScope"/>: the dialog handle, the
    /// <see cref="TextFieldEditSession"/> and its pop teardown, the foreign-window modality
    /// predicate, browse-type-to-edit, and the one-shot open-then-row announcement flush behind
    /// its readiness guard. Each scope declares only what its own window does differently: its
    /// draw pass and capture addressing, its focus ring, its edit session's spec and vehicles,
    /// its rows, and its opening line. Navigation, typeahead and announcement composition are
    /// the chassis's.
    ///
    /// ONE content region per dialog, holding that window's elements in its own draw order —
    /// these windows have no footer of their own to capture, so their buttons are rows like
    /// everything else (each still activated through the real widget's own vehicle) and
    /// <see cref="ScreenScope.IncludeActionsRegion"/> is off.
    ///
    /// WHY THE ANNOUNCEMENT FLUSH STAYS: the chassis speaks on focus, from the dispatcher pass,
    /// but these windows' rows are only readable once the window's own draw has captured them
    /// (the persona editor's text lives in a captured Widgets.TextArea, not in a field this scope
    /// can read), and neither utterance may go out while a foreign window above owns input or
    /// while the legacy keyboard overlay masks everything beneath it. So the flush rides each
    /// scope's GUI pass and the chassis's automatic entry announcement stands down for that focus
    /// (<see cref="ScreenScope.SuppressNextEntryAnnouncement"/>). The readiness guard lives here
    /// rather than in each scope because it is a SHELL contract, not a per-dialog one: three
    /// synchronized copies meant a shell-side fix could reach two of them and silently miss the
    /// third.
    /// </summary>
    public abstract class RimTalkTextDialogScopeBase : ScreenScope
    {
        protected const float FocusRingExpand = 2f;

        protected readonly Window dialog;
        protected readonly TextFieldEditSession session = new TextFieldEditSession();

        protected bool pendingAnnounce;
        private bool announcedOpen;

        protected RimTalkTextDialogScopeBase(Window dialog)
        {
            this.dialog = dialog;

            RegisterPopTeardown(session.CancelIfActive);
        }

        public override bool IsModal
        {
            get { return !TextDialogShared.ForeignWindowAbove(dialog); }
        }

        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        /// <summary>Every element these windows draw is a content row of its own — see the class header.</summary>
        protected override bool IncludeActionsRegion
        {
            get { return false; }
        }

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        internal bool Owns(Window window)
        {
            return ReferenceEquals(window, dialog);
        }

        /// <summary>
        /// The live scope of type <typeparamref name="T"/> owning <paramref name="window"/>, from
        /// anywhere on the focus stack — what each dialog's draw patch resolves with. A modal edit
        /// session pushes the text-input scope ABOVE this one, so a top-of-stack lookup would stop
        /// running the whole per-pass body (mirror, ring, raw-poll mask) for exactly the frames the
        /// player is typing; see <see cref="TextDialogShared.ScopeOwning{T}"/>.
        /// </summary>
        internal static T OwningScope<T>(Window window) where T : RimTalkTextDialogScopeBase
        {
            return TextDialogShared.ScopeOwning<T>(
                window, delegate(T scope, Window owned) { return scope.Owns(owned); });
        }

        public override void OnFocus()
        {
            // The window's own draw pass owns this focus's first utterance — see the class header.
            SuppressNextEntryAnnouncement();
            base.OnFocus();
            pendingAnnounce = true;
        }

        /// <summary>The claim predicate every row shares: a foreign window above this one owns input.</summary>
        protected bool NotForeign()
        {
            return !TextDialogShared.ForeignWindowAbove(dialog);
        }

        /// <summary>The focused row's index within this dialog's one content region, or -1 while it holds nothing.</summary>
        protected int CurrentIndex
        {
            get
            {
                ListModel region = Model.CurrentRegion;
                return region == null || region.IsEmpty ? -1 : region.Index;
            }
        }

        /// <summary>
        /// Speaks the pending row, preceded once by the window's own opening context. Called from
        /// each scope's own GUI pass so both utterances ride the dialog's draw rather than the
        /// dispatcher, and gated on this scope actually owning the ear (see the class header).
        /// </summary>
        protected void FlushPendingAnnouncement()
        {
            if (!pendingAnnounce || TextDialogShared.ForeignWindowAbove(dialog)
                || ShellDispatcherPatch.LegacyKeyboardOverlayActive())
            {
                return;
            }

            pendingAnnounce = false;
            if (!announcedOpen)
            {
                announcedOpen = true;
                string opened = ComposeOpenedAnnouncement();
                if (!string.IsNullOrEmpty(opened))
                {
                    TolkHelper.SpeakData(opened);
                }
            }
            AnnounceCurrentItem();
        }

        /// <summary>First-focus context for this window, spoken once before the first row.</summary>
        protected abstract string ComposeOpenedAnnouncement();

        /// <summary>
        /// Enter/Space on a row, once the shared predicate has cleared it: a foreign window above
        /// this one owns input, so no row's own vehicle may fire beneath it (the guard every one of
        /// these scopes used to carry on its own Activate claims, now stated once).
        /// </summary>
        protected sealed override void ActivateContentItem(int region, int index)
        {
            if (!NotForeign())
            {
                return;
            }
            ActivateRow(index);
        }

        /// <summary>Enter/Space on the row at <paramref name="index"/>, through that row's own vanilla vehicle.</summary>
        protected abstract void ActivateRow(int index);

        /// <summary>Whether the row at <paramref name="index"/> is this window's editable text row.</summary>
        protected abstract bool IsTextRow(int index);

        /// <summary>Opens the text row's edit session, bound to that row's own vanilla vehicle.</summary>
        protected abstract void BeginEdit(bool announcePrompt);

        /// <summary>The edit session's exit callback: the row re-reads itself on the next GUI pass, where its captured value is fresh again.</summary>
        protected virtual void ReAnnounceRow()
        {
            pendingAnnounce = true;
        }

        /// <summary>
        /// Browse-type-to-edit: a printable key on this window's text row opens its edit session
        /// and inserts the character; anything else falls through to the chassis's typeahead.
        /// While editing, the dispatcher routes characters straight to the controller, so this is
        /// browse-only.
        /// </summary>
        public override bool HandleChar(char c)
        {
            if (TextDialogShared.ForeignWindowAbove(dialog))
            {
                return false;
            }
            if (!session.Editing && IsTextRow(CurrentIndex)
                && !char.IsControl(c) && !char.IsWhiteSpace(c))
            {
                BeginEdit(announcePrompt: false);
                session.FeedChar(c);
                return true;
            }
            return base.HandleChar(c);
        }
    }
}
