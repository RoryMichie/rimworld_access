using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using LudeonTK;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Focus scope for dev mode's debug log window (<see cref="LudeonTK.EditWindow_Log"/>):
    /// browse the logged messages, read a message's full text and stack trace, and drive the
    /// window's own top-row controls.
    /// Attaches on EVERY open, the auto-open on errors included — an auto-opened log a player
    /// cannot hear, reach, or dismiss is worse than the masking risk. What makes that survivable
    /// is that <see cref="OnFocus"/> announces the window on arrival and Escape dismisses it. The
    /// sibling dev windows keep their arming flags; they never appear uninvited.
    /// Escape honors vanilla's own <c>Prefs.CloseLogWindowOnEscape</c>, claimed in BOTH states —
    /// unclaimed Escape is modal-swallowed and never reaches vanilla's closeOnCancel. Pref true:
    /// close the window ourselves; false: detach WITHOUT closing, exactly as Escape leaves the
    /// window on screen for a sighted player.
    /// Regions: Messages (the filtered log, honoring the window's own show-info/warning/error
    /// toggles), Details, and the automatic Buttons region of mirrored top-row controls. The log
    /// grows live, so <see cref="RefreshContent"/> re-snapshots each cycle without announcing.
    /// Details is a real TEXT FIELD, not a list of lines — one
    /// <see cref="TextFieldSpec.ReadOnlyText"/> field on the shared
    /// <see cref="TextFieldEditSession"/>, matching what vanilla itself draws (one
    /// <c>TextAreaScrollable(readOnly: true)</c>) and giving a stack trace the character, word,
    /// and line keys every other field answers to. Arriving on the region opens the field at
    /// once, since the region holds nothing else; Tab leaves for the neighbouring regions and
    /// Escape for the row. The buffer is a snapshot, so a message whose repeat count ticks up
    /// while it is read re-snapshots on the next entry, never under the caret.
    /// The Messages cursor IS the selection: arrowing selects that row's message through
    /// vanilla's own SelectedMessage setter, so Details always holds the row the player stands on
    /// and a sighted viewer sees the same row highlighted.
    /// </summary>
    internal sealed class DevLogScope : ScreenScope
    {
        private const int MessagesRegion = 0;
        private const int DetailsRegion = 1;

        private static readonly FieldInfo SelectedMessageField =
            AccessTools.Field(typeof(EditWindow_Log), "selectedMessage");
        private static readonly MethodInfo SelectedMessageSetter =
            AccessTools.PropertySetter(typeof(EditWindow_Log), "SelectedMessage");
        private static readonly FieldInfo ShowMessagesField =
            AccessTools.Field(typeof(EditWindow_Log), "showMessages");
        private static readonly FieldInfo ShowWarningsField =
            AccessTools.Field(typeof(EditWindow_Log), "showWarnings");
        private static readonly FieldInfo ShowErrorsField =
            AccessTools.Field(typeof(EditWindow_Log), "showErrors");
        private static readonly FieldInfo CanAutoOpenField =
            AccessTools.Field(typeof(EditWindow_Log), "canAutoOpen");
        private static readonly FieldInfo DetailsPaneHeightField =
            AccessTools.Field(typeof(EditWindow_Log), "detailsPaneHeight");
        private static readonly MethodInfo CopyAllMethod =
            AccessTools.Method(typeof(EditWindow_Log), "CopyAllMessagesToClipboard");

        private readonly EditWindow_Log window;
        private readonly List<LogMessage> messages = new List<LogMessage>();
        private readonly TextFieldEditSession detailsSession = new TextFieldEditSession();
        private LogMessage selected;
        private string detailText = "";
        private bool announcedOpen;
        private bool leftDetailsField;

        public DevLogScope(EditWindow_Log window)
        {
            this.window = window;
            RegisterPopTeardown(detailsSession.CancelIfActive);

            // Registered after the base's typeahead-first cancel claim, which fires only during
            // a search: the two never overlap.
            Claim(SharedMenuGrammar.Cancel, OnCancel, when: () => !TypeaheadHasActiveSearch);
        }

        public override string Name => "dev-log";

        /// <summary>The logged messages are named data worth searching.</summary>
        protected override bool EnableTypeahead => true;

        /// <summary>EditWindow_Log draws its controls via DevGUI, not Widgets.ButtonText, so there is nothing to scrape.</summary>
        protected override bool CaptureWindowButtons => false;

        public override void OnFocus()
        {
            bool handedBack = leftDetailsField;
            leftDetailsField = false;
            if (handedBack)
            {
                SuppressNextEntryAnnouncement();
            }
            base.OnFocus();
            // The landing row is the selection too, so Details has content before the first arrow.
            NotifyCursorSettled();
            if (handedBack)
            {
                return;
            }
            if (!announcedOpen)
            {
                announcedOpen = true;
                AnnounceRegion();
                return;
            }
            AnnounceCurrentItem();
        }

        protected override int ContentRegionCount => 2;

        /// <summary>
        /// Down from the Details field row continues into Buttons and Up returns; the Messages
        /// list keeps its hard edge. Only reachable while the field is closed — an open field
        /// owns Up/Down for its own lines.
        /// </summary>
        protected override bool ContentFlowsToActions(int region)
        {
            return region == DetailsRegion;
        }

        protected override string ContentRegionName(int region)
        {
            return (region == DetailsRegion
                ? "RimWorldAccess.Dev.LogDetails"
                : "RimWorldAccess.Dev.LogMessages").Translate().ToString();
        }

        /// <summary>Details is always exactly the one field row, so the region stays reachable by Tab even with nothing selected.</summary>
        protected override int ContentItemCount(int region)
        {
            return region == DetailsRegion ? 1 : messages.Count;
        }

        /// <summary>
        /// Re-snapshots the filtered list and the selected message's detail text each cycle,
        /// honoring the window's own show-info/warning/error filter so Messages matches what
        /// vanilla draws.
        /// </summary>
        protected override void RefreshContent()
        {
            messages.Clear();
            bool showMessages = (bool)ShowMessagesField.GetValue(null);
            bool showWarnings = (bool)ShowWarningsField.GetValue(null);
            bool showErrors = (bool)ShowErrorsField.GetValue(null);
            foreach (LogMessage message in Log.Messages)
            {
                if (message == null)
                {
                    continue;
                }
                if ((message.type == LogMessageType.Message && !showMessages) ||
                    (message.type == LogMessageType.Warning && !showWarnings) ||
                    (message.type == LogMessageType.Error && !showErrors))
                {
                    continue;
                }
                messages.Add(message);
            }

            selected = SelectedMessageField.GetValue(null) as LogMessage;
            RebuildDetailText();
        }

        /// <summary>
        /// What vanilla's details pane draws — message text then stack trace — as one buffer,
        /// with trailing blank lines trimmed so Ctrl+End lands on real text.
        /// </summary>
        private void RebuildDetailText()
        {
            detailText = selected == null
                ? ""
                : (selected.text + "\n" + selected.StackTrace).Replace("\r", "").TrimEnd('\n');
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            if (region == DetailsRegion)
            {
                var detail = new ElementDescription { ReadOnly = true };
                if (detailText.Length == 0)
                {
                    detail.Label = "RimWorldAccess.Dev.LogNothingSelected".Translate().ToString();
                    return detail;
                }
                detail.Role = ElementRole.TextField;
                detail.Label = DetailsCaption();
                // The row is the field's cover: it speaks the pane's opening line, the field
                // carries the rest.
                string firstLine = TextCursor.LineAt(detailText, 0);
                if (firstLine.Length == 0)
                {
                    detail.ValueBlank = true;
                }
                else
                {
                    detail.Value = firstLine;
                }
                return detail;
            }
            if (index < 0 || index >= messages.Count)
            {
                return new ElementDescription();
            }
            LogMessage message = messages[index];
            var d = new ElementDescription();
            // Only the selected row is marked, so a long log does not append "not selected" to
            // every line.
            if (ReferenceEquals(message, selected))
            {
                d.Selected = true;
            }
            string body = TypeWord(message.type) + ". " + FirstLine(message.text);
            d.Label = message.repeats > 1
                ? "RimWorldAccess.Dev.LogRepeats".Translate(body, message.repeats).ToString()
                : body;
            return d;
        }

        protected override void ActivateContentItem(int region, int index)
        {
            if (region == DetailsRegion)
            {
                // Enter re-opens the field after an Escape left it; arrival opens it itself.
                if (!BeginBrowsingDetails())
                {
                    AnnounceCurrentItem();
                }
                return;
            }
            // The cursor already selected this row, so Enter only re-reads.
            AnnounceCurrentItem();
        }

        /// <summary>
        /// Keeps vanilla's selection on the row the cursor rests on. Rides the private
        /// SelectedMessage setter, which carries the details-box unfocus a bare field write skips.
        /// </summary>
        protected override void OnCursorSettled(int region, int index)
        {
            base.OnCursorSettled(region, index);
            if (region != MessagesRegion || index < 0 || index >= messages.Count)
            {
                return;
            }
            LogMessage message = messages[index];
            if (ReferenceEquals(message, selected))
            {
                return;
            }
            SelectedMessageSetter.Invoke(null, new object[] { message });
            selected = message;
            RebuildDetailText();
        }

        /// <summary>Opening the field is the arrival announcement, so the ordinary landing one stands down.</summary>
        protected override void OnRegionChanged(MoveResult result)
        {
            base.OnRegionChanged(result);
            if (Model.RegionIndex == DetailsRegion)
            {
                BeginBrowsingDetails();
            }
        }

        protected override void AnnounceRegion()
        {
            if (Model.RegionIndex == DetailsRegion && detailsSession.Editing)
            {
                return;
            }
            base.AnnounceRegion();
        }

        /// <summary>False when there is nothing selected to read, so the caller can speak the empty row instead.</summary>
        private bool BeginBrowsingDetails()
        {
            if (detailText.Length == 0 || detailsSession.Editing)
            {
                return false;
            }
            detailsSession.EnterEdit(
                detailText,
                TextFieldSpec.ReadOnlyText("RimWorldAccess.TextInput.LabelDefault"),
                DetailsCaption(),
                // Read-only: the log's text belongs to the game.
                apply: null,
                onExit: () => LeaveDetails(AnnounceCurrentItem),
                onTabExit: shiftHeld => LeaveDetails(() => MoveRegion(!shiftHeld)));
            return true;
        }

        /// <summary>
        /// Every way out of the field speaks for itself, and the text session's pop refocuses
        /// this scope right after — so the next <see cref="OnFocus"/> must stay silent, or the
        /// same sentence is spoken twice.
        /// </summary>
        private void LeaveDetails(Action speak)
        {
            leftDetailsField = true;
            speak();
        }

        private static string DetailsCaption()
        {
            return "RimWorldAccess.Dev.LogDetails".Translate().ToString();
        }

        // The mirrored top-row controls, rebuilt on each read so toggle labels stay live.
        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                var actions = new List<ScreenAction>
                {
                    new ScreenAction("Clear", ClearLog),
                    new ScreenAction("Trace big", () => SetDetailsPaneHeight(700f)),
                    new ScreenAction("Trace medium", () => SetDetailsPaneHeight(300f)),
                    new ScreenAction("Trace small", () => SetDetailsPaneHeight(100f)),
                    new ScreenAction(
                        (bool)CanAutoOpenField.GetValue(null) ? "Auto-open is ON" : "Auto-open is OFF",
                        ToggleAutoOpen),
                    new ScreenAction("Copy to clipboard", CopyToClipboard),
                    new ScreenAction(
                        DebugSettings.pauseOnError ? "Pause on error is ON" : "Pause on error is OFF",
                        TogglePauseOnError),
                    new ScreenAction(ShowToggleLabel("Show info messages", ShowMessagesField),
                        () => ToggleShow(ShowMessagesField, "Show info messages")),
                    new ScreenAction(ShowToggleLabel("Show warning messages", ShowWarningsField),
                        () => ToggleShow(ShowWarningsField, "Show warning messages")),
                    new ScreenAction(ShowToggleLabel("Show error messages", ShowErrorsField),
                        () => ToggleShow(ShowErrorsField, "Show error messages")),
                };
                return actions;
            }
        }

        private void ClearLog()
        {
            // Vehicle A: the calls vanilla's Clear button handler makes.
            Log.Clear();
            EditWindow_Log.ClearAll();
            RefreshModel();
            TolkHelper.SpeakData("RimWorldAccess.Dev.LogCleared".Translate().ToString());
        }

        private void CopyToClipboard()
        {
            // Vehicle A: the window's own private CopyAllMessagesToClipboard(). A clipboard
            // write is inaudible, so confirm it.
            CopyAllMethod.Invoke(window, null);
            TolkHelper.SpeakData("RimWorldAccess.Dev.LogCopied".Translate().ToString());
        }

        private static void SetDetailsPaneHeight(float height)
        {
            // Cosmetic here — Details reads the full trace at any pane height — but mirrored
            // for sighted parity.
            // MUTATION-C: mirrors EditWindow_Log.DoWindowContents' inline Trace
            // big/medium/small handlers (set the private static detailsPaneHeight).
            // Inline delegates in the draw pass; no invocable vanilla method.
            DetailsPaneHeightField.SetValue(null, height);
        }

        private void ToggleAutoOpen()
        {
            bool on = !(bool)CanAutoOpenField.GetValue(null);
            // MUTATION-C: mirrors EditWindow_Log.DoWindowContents' inline
            // auto-open button handler (flips the private static canAutoOpen).
            // Inline delegate in the draw pass; no invocable vanilla method.
            CanAutoOpenField.SetValue(null, on);
            RefreshModel();
            TolkHelper.SpeakData(on ? "Auto-open is ON" : "Auto-open is OFF");
        }

        private void TogglePauseOnError()
        {
            // MUTATION-C: mirrors EditWindow_Log.DoWindowContents' inline
            // pause-on-error button handler (flips DebugSettings.pauseOnError).
            // Inline delegate in the draw pass; no invocable vanilla method.
            DebugSettings.pauseOnError = !DebugSettings.pauseOnError;
            RefreshModel();
            TolkHelper.SpeakData(DebugSettings.pauseOnError ? "Pause on error is ON" : "Pause on error is OFF");
        }

        private void ToggleShow(FieldInfo field, string caption)
        {
            bool on = !(bool)field.GetValue(null);
            // MUTATION-C: mirrors EditWindow_Log.DoWindowContents' inline
            // DoImageToggle handlers (flip the private static showMessages/
            // showWarnings/showErrors filter flags). Inline in the draw pass;
            // no invocable vanilla method.
            field.SetValue(null, on);
            RefreshModel();
            TolkHelper.SpeakData(ShowToggleLabel(caption, field));
        }

        private static string ShowToggleLabel(string caption, FieldInfo field)
        {
            string state = ((bool)field.GetValue(null)
                ? "RimWorldAccess.Shell.State.Checked"
                : "RimWorldAccess.Shell.State.Unchecked").Translate().ToString();
            return caption + ", " + state;
        }

        private void OnCancel(KeyEventSnapshot e)
        {
            ShellFrameStamps.MarkCancelConsumed();
            if (Prefs.CloseLogWindowOnEscape)
            {
                // The dispatcher swallow starves vanilla's closeOnCancel; close here instead.
                window.Close();
                return;
            }
            // Pref off: vanilla keeps the window open, so detach and leave it on screen.
            Window owned = OwnedWindow;
            if (owned != null)
            {
                ScopeForWindow.Detach(owned);
            }
            TolkHelper.SpeakData("RimWorldAccess.Dev.LogLeft".Translate().ToString());
        }

        private static string TypeWord(LogMessageType type)
        {
            switch (type)
            {
                case LogMessageType.Warning:
                    return "RimWorldAccess.Dev.LogTypeWarning".Translate().ToString();
                case LogMessageType.Error:
                    return "RimWorldAccess.Dev.LogTypeError".Translate().ToString();
                default:
                    return "RimWorldAccess.Dev.LogTypeMessage".Translate().ToString();
            }
        }

        private static string FirstLine(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }
            int nl = text.IndexOf('\n');
            string line = nl >= 0 ? text.Substring(0, nl) : text;
            return line.TrimEnd('\r');
        }
    }
}
