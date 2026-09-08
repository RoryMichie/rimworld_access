using System;
using System.Collections;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Keyboard support for RimWorld Together's chat panel, <c>RTClient.Dialogs.DLG_Chat</c>: a
    /// non-modal, draggable, unpausing window opened and closed from the mod's own HUD chat toggle
    /// that streams live multiplayer text into a scrolling log with a send field.
    ///
    /// THREE regions: History (every entry of the mod's 100-line <c>ChatMessages</c> ring, read
    /// fresh every refresh — never from captured label rows, which cover only the visible scrolled
    /// band), Message (one row, the input field), and the automatic Buttons region renamed
    /// "Toolbar", which carries this window's captured buttons followed by the declared
    /// Send/Pin/Sounds actions in <see cref="ScreenScope"/>'s fixed order.
    ///
    /// MESSAGE REGION AUTO-EDITS: arriving on it opens the modal edit session immediately, so
    /// tabbing into the field drops straight into typing rather than a browse-mode row. See
    /// <see cref="OnRegionChanged"/> and the <see cref="AnnounceRegion"/> override that skips the
    /// ordinary landing announcement in favor of the edit session's own prompt. Tab/Shift+Tab leave
    /// the field WITHOUT sending; Enter confirms and sends.
    ///
    /// RAW ENTER POLL: <c>DLG_Chat.CheckForEnterKey</c> polls <c>Event.current.keyCode</c>
    /// unconditionally at the end of every <c>DoWindowContents</c> pass and sends itself. It is
    /// masked unconditionally by <see cref="RimworldTogetherChatScopeCompat"/>'s draw-pass guard —
    /// this scope is the only thing that ever sends.
    ///
    /// NON-MODAL COEXISTENCE: <c>absorbInputAroundWindow</c> is false and the map runs underneath,
    /// so <see cref="IsModal"/> is <c>!TextDialogShared.ForeignWindowAbove(dialog)</c> and a
    /// genuinely scopeless foreign window stacked above is never masked. Every other RTClient
    /// dialog inherits <c>DLG_Base</c>'s absorbing default and gets the generic reader's own modal
    /// scope, so none is "foreign" in the sense this guard cares about.
    ///
    /// LIVE GROWTH UNDER THE CURSOR: the mod appends (and evicts index 0 past 100 entries) at any
    /// time, so <see cref="RefreshHistoryRows"/> re-finds the focused message by string REFERENCE
    /// rather than index — an eviction shifts every remaining index.
    ///
    /// <see cref="RimworldTogetherChatCompat"/> already speaks each arriving message while the
    /// window is open; this scope never also announces arrivals.
    /// </summary>
    public sealed class RimworldTogetherChatScope : ScreenScope
    {
        private const int HistoryRegion = 0;
        private const int MessageRegion = 1;

        private readonly Window dialog;
        private readonly TextFieldEditSession session = new TextFieldEditSession();
        private readonly List<ScreenAction> actions = new List<ScreenAction>();

        // rawMessages holds the SAME string references DLG_Chat.ChatMessages holds, so identity
        // survives an eviction; historyRows is the tag-stripped display text at the same indices.
        private readonly List<string> rawMessages = new List<string>();
        private readonly List<string> historyRows = new List<string>();

        private int lastAnnouncedPlayerCount = int.MinValue;

        public RimworldTogetherChatScope(Window dialog)
        {
            this.dialog = dialog;
            Claim(SharedMenuGrammar.Cancel, OnCancelClaim);

            RegisterPopTeardown(session.CancelIfActive);
        }

        public override string Name
        {
            get { return "rimworldtogether-chat"; }
        }

        /// <summary>See the class remarks' NON-MODAL COEXISTENCE section.</summary>
        public override bool IsModal
        {
            get { return !TextDialogShared.ForeignWindowAbove(dialog); }
        }

        /// <summary>
        /// Escape closes the panel. DLG_Base leaves <c>closeOnCancel</c> false and this window
        /// draws no close control of its own, so without this claim the modal swallow eats Escape
        /// and a keyboard player cannot leave the panel.
        /// </summary>
        public override bool OwnsCancel
        {
            get { return !TextDialogShared.ForeignWindowAbove(dialog); }
        }

        /// <summary>Vehicle A: the exact call the mod's own chat-icon toggle makes to close the panel.</summary>
        private void OnCancelClaim(KeyEventSnapshot e)
        {
            ShellFrameStamps.MarkCancelConsumed();
            dialog.Close(true);
        }

        internal bool Owns(Window window)
        {
            return ReferenceEquals(window, dialog);
        }

        protected override int ContentRegionCount
        {
            get { return 2; }
        }

        protected override string ContentRegionName(int region)
        {
            return region == HistoryRegion
                ? "RimWorldAccess.Compat.RimworldTogether.Chat.HistoryRegionName".Translate()
                : "RimWorldAccess.Compat.RimworldTogether.Chat.MessageRegionName".Translate();
        }

        /// <summary>The automatic Buttons region, renamed to match what a sighted player would call this row of icons.</summary>
        protected override string ActionsRegionName
        {
            get { return "RimWorldAccess.Compat.RimworldTogether.Chat.ToolbarRegionName".Translate().ToString(); }
        }

        /// <summary>
        /// DLG_Chat draws its clear-history button as a bare "C" with no tooltip, so name it for
        /// its function.
        /// </summary>
        protected override string CapturedButtonLabel(int captureIndex, string rawLabel)
        {
            return rawLabel == "C"
                ? "RimWorldAccess.Compat.RimworldTogether.Chat.ClearLabel".Translate().ToString()
                : rawLabel;
        }

        /// <summary>
        /// History always reports at least one row (the "no messages yet" placeholder) so it stays
        /// reachable by Tab before the first message arrives, rather than vanishing from navigation.
        /// </summary>
        protected override int ContentItemCount(int region)
        {
            return region == HistoryRegion ? Math.Max(historyRows.Count, 1) : 1;
        }

        protected override void RefreshContent()
        {
            RefreshHistoryRows();
        }

        /// <summary>See the class remarks' LIVE GROWTH UNDER THE CURSOR section.</summary>
        private void RefreshHistoryRows()
        {
            string focusedRaw = null;
            if (Model.RegionIndex == HistoryRegion)
            {
                ListModel region = Model.CurrentRegion;
                if (region != null && region.Index >= 0 && region.Index < rawMessages.Count)
                {
                    focusedRaw = rawMessages[region.Index];
                }
            }

            IList raw = RimworldTogetherChatScopeCompat.GetChatMessages();
            rawMessages.Clear();
            historyRows.Clear();
            if (raw != null)
            {
                for (int i = 0; i < raw.Count; i++)
                {
                    string s = raw[i] as string ?? "";
                    rawMessages.Add(s);
                    historyRows.Add(s.StripTags());
                }
            }

            if (focusedRaw == null)
            {
                return;
            }
            int newIndex = -1;
            for (int i = 0; i < rawMessages.Count; i++)
            {
                if (ReferenceEquals(rawMessages[i], focusedRaw))
                {
                    newIndex = i;
                    break;
                }
            }
            if (newIndex < 0)
            {
                return;
            }
            ListModel current = Model.CurrentRegion;
            if (current != null && current.Index != newIndex)
            {
                current.MoveTo(newIndex);
            }
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            if (region == MessageRegion)
            {
                d.Role = ElementRole.TextField;
                d.Label = FieldCaption();
                string value = RimworldTogetherChatScopeCompat.GetCurrentChatInput();
                if (string.IsNullOrEmpty(value))
                {
                    d.ValueBlank = true;
                }
                else
                {
                    d.Value = value;
                }
                return d;
            }

            d.Role = ElementRole.None;
            d.ReadOnly = true;
            d.Label = historyRows.Count == 0
                ? (string)"RimWorldAccess.Compat.RimworldTogether.Chat.EmptyHistory".Translate()
                : (index >= 0 && index < historyRows.Count ? historyRows[index] : "");
            return d;
        }

        /// <summary>Read-only rows, no per-message context menu: the mod gives none.</summary>
        protected override void ActivateContentItem(int region, int index)
        {
            if (region == MessageRegion)
            {
                BeginEdit(announcePrompt: true);
                return;
            }
            AnnounceCurrentItem();
        }

        /// <summary>See the class remarks' MESSAGE REGION AUTO-EDITS section.</summary>
        protected override void OnRegionChanged(MoveResult result)
        {
            if (Model.RegionIndex == MessageRegion)
            {
                BeginEdit(announcePrompt: true);
            }
        }

        /// <summary>
        /// Skips the ordinary landing announcement for Message while the edit session it just
        /// opened is live, since that session already spoke its own prompt, and folds a changed
        /// player count into History's landing announcement.
        /// </summary>
        protected override void AnnounceRegion()
        {
            if (Model.RegionIndex == MessageRegion && session.Editing)
            {
                return;
            }
            if (Model.RegionIndex == HistoryRegion)
            {
                string playerCount = PlayerCountPrefixIfChanged();
                if (!string.IsNullOrEmpty(playerCount))
                {
                    TolkHelper.SpeakData(playerCount, SpeechPriority.Normal);
                }
            }
            base.AnnounceRegion();
        }

        /// <summary>Null when unchanged since this last fired, or when the session has no player count yet.</summary>
        private string PlayerCountPrefixIfChanged()
        {
            int count = RimworldTogetherChatScopeCompat.GetCurrentServerPlayers();
            if (count == lastAnnouncedPlayerCount)
            {
                return null;
            }
            lastAnnouncedPlayerCount = count;
            if (count < 0)
            {
                return null;
            }
            return count > 1
                ? (string)"RimWorldAccess.Compat.RimworldTogether.Chat.PlayerCountPlural".Translate(count)
                : (string)"RimWorldAccess.Compat.RimworldTogether.Chat.PlayerCountSingular".Translate(count);
        }

        private static string FieldCaption()
        {
            return "RimWorldAccess.Compat.RimworldTogether.Chat.MessageFieldCaption".Translate();
        }

        private void BeginEdit(bool announcePrompt)
        {
            string current = RimworldTogetherChatScopeCompat.GetCurrentChatInput();

            // No length cap beyond the mod's own 512, from DrawInput's `text.Length <= 512` guard.
            var spec = new TextFieldSpec(labelKey: "RimWorldAccess.TextInput.LabelDefault", maxLength: 512, minLength: 0);

            session.EnterEdit(
                current,
                spec,
                FieldCaption(),
                ApplyValue,
                ReAnnounceRow,
                announcePrompt,
                onConfirm: ConfirmSend,
                onTabExit: HandleTabExit,
                silentConfirmExit: true);
        }

        private void ApplyValue(string value)
        {
            RimworldTogetherChatScopeCompat.SetCurrentChatInput(value ?? "");
        }

        /// <summary>
        /// Enter sends and STAYS in the field, re-entering the session silently, so the only
        /// feedback is the message's own arriving echo. Tab/Shift+Tab and Escape are the ways out.
        /// </summary>
        private void ConfirmSend()
        {
            SendCurrentMessage();
            BeginEdit(announcePrompt: false);
        }

        private void ReAnnounceRow()
        {
            AnnounceCurrentItem();
        }

        /// <summary>Tab/Shift+Tab leave the field (value already live-mirrored) and move to the next/previous region — Tab forward to Toolbar, Shift+Tab back to History.</summary>
        private void HandleTabExit(bool shiftHeld)
        {
            MoveRegion(!shiftHeld);
        }

        /// <summary>
        /// Vehicle A: the exact two statements <c>DLG_Chat.CheckForEnterKey</c>'s raw poll runs —
        /// <c>SendMessage</c> then clearing <c>CurrentChatInput</c> — including its whitespace
        /// skip. Both members are real public API, only resolved reflectively, so no MUTATION-C
        /// marker applies. No "message sent" confirmation is spoken: the mod gives none, and the
        /// arriving echo is the real feedback.
        /// </summary>
        private void SendCurrentMessage()
        {
            string current = RimworldTogetherChatScopeCompat.GetCurrentChatInput();
            if (string.IsNullOrWhiteSpace(current))
            {
                return;
            }
            RimworldTogetherChatScopeCompat.SendMessage(current);
            RimworldTogetherChatScopeCompat.SetCurrentChatInput("");
        }

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                actions.Add(new ScreenAction(
                    "RimWorldAccess.Compat.RimworldTogether.Chat.SendLabel".Translate(),
                    SendCurrentMessage));
                actions.Add(new ScreenAction(
                    "RimWorldAccess.Compat.RimworldTogether.Chat.PinLabel".Translate(),
                    TogglePin,
                    check: RimworldTogetherChatScopeCompat.GetShouldScrollChat() ? CheckState.Checked : CheckState.Unchecked));
                actions.Add(new ScreenAction(
                    "RimWorldAccess.Compat.RimworldTogether.Chat.SoundsLabel".Translate(),
                    ToggleSounds,
                    check: RimworldTogetherChatScopeCompat.GetShouldPlaySounds() ? CheckState.Checked : CheckState.Unchecked));
                return actions;
            }
        }

        /// <summary>MUTATION-C: mirrors DLG_Chat.DrawPinCheckbox's own inline toggle delegate (flips the bare public-setter ShouldScrollChat property and plays the click sound); no gated Toggle method exists for it.</summary>
        private void TogglePin()
        {
            bool value = !RimworldTogetherChatScopeCompat.GetShouldScrollChat();
            RimworldTogetherChatScopeCompat.SetShouldScrollChat(value);
            SoundStarter.PlayOneShotOnCamera(SoundDefOf.Click, null);
            RefreshModel();
            AnnounceCurrentItem();
        }

        /// <summary>MUTATION-C: mirrors DLG_Chat.DrawMuteCheckbox's own inline toggle delegate, the same shape as <see cref="TogglePin"/>.</summary>
        private void ToggleSounds()
        {
            bool value = !RimworldTogetherChatScopeCompat.GetShouldPlaySounds();
            RimworldTogetherChatScopeCompat.SetShouldPlaySounds(value);
            SoundStarter.PlayOneShotOnCamera(SoundDefOf.Click, null);
            RefreshModel();
            AnnounceCurrentItem();
        }

        protected override string ComposeOpenAnnouncement()
        {
            return "RimWorldAccess.Compat.RimworldTogether.Chat.Opened".Translate();
        }

        /// <summary>Driven by RimworldTogetherChatScopeCompat's draw-pass guard patch.</summary>
        internal void OnGuiPass()
        {
            ShellTextFocus.ReleaseNativeFocus();
            session.MirrorLive();
        }
    }
}
