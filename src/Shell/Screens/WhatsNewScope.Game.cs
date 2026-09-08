using System.Collections.Generic;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The keyboard focus scope for the What's New reader: the windowless, read-only announcement
    /// buffer shown at the main menu after a mod update and reopenable from the main and pause menus.
    /// The mod owns no window, so the scope rides the focus stack through
    /// <see cref="WhatsNewScopeMirror"/>.
    ///
    /// The content region is the shared big-text buffer (<see cref="TextBufferScope"/>): a header line
    /// plus one read-only line per content line, with no section headings, so the buffer's Page
    /// Up/Page Down jumps stay unclaimed here. The Buttons region is rebuilt fresh on every describe,
    /// since the Jump and Mark-all buttons come and go with the unread count.
    ///
    /// <see cref="EnableTypeahead"/> stays false: this screen has no search grammar. The scope stays
    /// modal with a null <see cref="CharSink"/>, and a modal scope with no sink still BLOCKS
    /// characters from reaching sinks beneath it — which is what stops letters typed into the open
    /// reader from leaking into the main menu's typeahead underneath.
    ///
    /// This scope is the single key router for the reader in both program states.
    /// FocusStack.AnyLiveModal is the first term of ShellGuards.MenuOwnsInput, which gates the
    /// dispatcher's modal swallow, so unclaimed keys are eaten here. WhatsNewState additionally
    /// remains a member of that helper in its own right: do not remove the membership.
    ///
    /// Being windowless, the scope self-claims Cancel rather than relying on a real window's own
    /// Escape-close chain, and stamps the ShellFrameStamps on Cancel and Activate. The stamps are
    /// defensive — the reader never has a real window beneath it — but make that true by construction.
    /// </summary>
    public sealed class WhatsNewScope : TextBufferScope
    {
        private int lastSeenOpenGeneration = -1;
        private bool announcedOpen;

        public WhatsNewScope()
        {
            Claim(SharedMenuGrammar.Cancel, OnCancel);
        }

        public override string Name
        {
            get { return "whats-new"; }
        }

        /// <summary>No owning Window exists for this windowless overlay — self-claim Escape.</summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        /// <summary>No vanilla window backs this reader — nothing for ButtonTextCapture to scrape; every button is a DeclaredAction.</summary>
        protected override bool CaptureWindowButtons
        {
            get { return false; }
        }

        /// <summary>Down past the last changelog line continues into the Buttons region; Up from the first button returns to the last line.</summary>
        protected override bool ContentFlowsToActions(int region)
        {
            return true;
        }

        /// <summary>One block of prose ending in its buttons: Down reaches them, Tab has nowhere else to go.</summary>
        protected override bool ActionsRegionInTabCycle
        {
            get { return false; }
        }

        // ------------------------------------------------------------------
        // Big-text buffer: the header line, then one read-only line per announcement content line. No
        // line is marked as a section start, so the shared Page Up/Page Down jumps stay unclaimed.
        // ------------------------------------------------------------------

        protected override string TextRegionName
        {
            get { return "RimWorldAccess.OverlayMigration.WhatsNew.RegionName".Translate().ToString(); }
        }

        protected override void BuildTextBuffer()
        {
            AddLine(WhatsNewState.HeaderAnnouncement());
            for (int i = 0; i < WhatsNewState.LineCount; i++)
            {
                AddLine(WhatsNewState.LineAt(i));
            }
        }

        // ------------------------------------------------------------------
        // Buttons region: Jump-to-next and Mark-all-read (both conditional on remaining unread), Open
        // Changelog, Close, Don't show again. Rebuilt every describe, since the first two come and go
        // with the unread count.
        // ------------------------------------------------------------------

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get { return BuildActions(); }
        }

        private List<ScreenAction> BuildActions()
        {
            var actions = new List<ScreenAction>();
            int remaining = WhatsNewState.UnreadCount();
            int nextUnread = WhatsNewState.OldestUnreadIndex();

            if (nextUnread >= 0)
            {
                actions.Add(new ScreenAction(
                    "RimWorldAccess.WhatsNew.Button.NextAnnouncement".Translate(remaining).ToString(),
                    delegate { JumpToNext(nextUnread); }));

                // The mark-all button appears only when at least one OTHER unread announcement
                // exists, so this count is always plural.
                actions.Add(new ScreenAction(
                    "RimWorldAccess.WhatsNew.Button.MarkAllRead".Translate(WhatsNewState.CatalogCount).ToString(),
                    MarkAllRead));
            }

            actions.Add(new ScreenAction(
                "RimWorldAccess.WhatsNew.Button.OpenChangelog".Translate().ToString(),
                WhatsNewState.OpenChangelog));
            actions.Add(new ScreenAction(
                "RimWorldAccess.WhatsNew.Button.Close".Translate().ToString(),
                delegate { WhatsNewState.CloseMenu(); }));
            actions.Add(new ScreenAction(
                "RimWorldAccess.WhatsNew.Button.DontShowAgain".Translate().ToString(),
                WhatsNewState.SuppressAutoPopup));
            return actions;
        }

        private void JumpToNext(int index)
        {
            WhatsNewState.ShowAnnouncement(index);
            RefreshModel();
            Model.MoveToRegion(0);
            Model.CurrentRegion?.MoveFirst();
            AnnounceCurrentItem();
        }

        private void MarkAllRead()
        {
            WhatsNewState.MarkAllRead();
            RefreshModel();
            // The Jump/Mark-all buttons just disappeared: land on the first remaining button.
            Model.MoveToRegion(ContentRegionCount);
            Model.CurrentRegion?.MoveFirst();
            AnnounceCurrentItem();
        }

        // ------------------------------------------------------------------
        // Lifecycle: a one-shot opening announcement, guarded by the state's open generation so a
        // pop and re-push does not repeat it.
        // ------------------------------------------------------------------

        public override void OnPush()
        {
            base.OnPush();
            int generation = WhatsNewState.OpenGeneration;
            if (generation == lastSeenOpenGeneration)
                return;
            lastSeenOpenGeneration = generation;
            announcedOpen = false;
        }

        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen)
                return;
            announcedOpen = true;

            RefreshModel();
            Model.CurrentRegion?.MoveFirst();
            AnnounceCurrentItem();
        }

        private void OnCancel(KeyEventSnapshot e)
        {
            ShellFrameStamps.MarkCancelConsumed();
            WhatsNewState.CloseMenu();
        }
    }

    /// <summary>
    /// Keeps <see cref="WhatsNewScope"/> in lockstep with <see cref="WhatsNewState.IsActive"/>, a
    /// bare IsActive gate. Entry-safe: the flag is a plain bool with no vanilla getter chain behind it.
    /// Reconciled last of the ordinary mirrors, so its per-pass Push re-floats the reader above every
    /// other mirrored and window-attached scope whenever it is open.
    /// <see cref="LearningHelperScopeMirror"/> is reconciled immediately before it; the two can never
    /// actually coexist, since each is a consume-all overlay while active and the learning helper's
    /// opener needs Find.World, which is null at the bare main menu.
    /// </summary>
    internal static class WhatsNewScopeMirror
    {
        private static readonly WhatsNewScope scope = new WhatsNewScope();

        public static void Reconcile()
        {
            if (WhatsNewState.IsActive)
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
