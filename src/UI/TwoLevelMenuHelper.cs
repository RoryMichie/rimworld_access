using System;
using System.Collections.Generic;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>One button in a detail view.</summary>
    public class ButtonInfo
    {
        public string Label { get; set; }

        public Action Action { get; set; }

        public bool IsDisabled { get; set; }

        public string DisabledReason { get; set; }

        /// <summary>
        /// Whether this button's action jumps to a map/world target and closes the containing menu
        /// on success. Set at construction by the caller that builds the action, so post-activation
        /// handling never pattern-matches the translated <see cref="Label"/>.
        /// </summary>
        public bool IsJumpAction { get; set; } = false;
    }

    /// <summary>
    /// Shared two-level menu navigation: level 1 is a list (Up/Down over items, Enter opens),
    /// level 2 a detail view (Up/Down over header, content lines, then buttons; Left/Right between
    /// buttons; Enter activates). Each consumer owns its own list-view announcements; this class
    /// owns only the detail-view header/content-line/button shape.
    ///
    /// Detail-view announcements compose through <see cref="AnnouncementComposer.ComposeFocus"/>,
    /// with each consumer delegate returning one fully-composed, whole-phrase-translated string.
    /// The header is Role=None (a screen-entry description, not a focusable row); a content line is
    /// Role=MenuItem with deliberately NO Position, since content lines read as paragraphs the user
    /// pages through rather than a picklist; a button is Role=Button with Disabled+Extras carrying
    /// the reason and Position from the composer's own fields.
    /// </summary>
    public class TwoLevelMenuHelper
    {
        private bool isInDetailView = false;
        private int detailPosition = 0; // 0=header, 1-N=content lines, N+1+=buttons
        private int currentButtonIndex = 0;
        private readonly List<ButtonInfo> currentButtons = new List<ButtonInfo>();

        private readonly Func<int> getContentLineCount;
        private readonly Action<List<ButtonInfo>> populateButtons;
        private readonly Func<string> getHeaderAnnouncement;
        private readonly Func<int, string> getContentLineAnnouncement;

        private readonly string endOfItemMessage;
        private readonly string startOfItemMessage;
        private readonly string openFirstMessage;
        private readonly string navigateDownMessage;
        private readonly string noButtonsMessage;

        /// <summary>Whether the helper is currently in detail view.</summary>
        public bool IsInDetailView => isInDetailView;

        /// <summary>Whether the current position is in the buttons section.</summary>
        public bool IsInButtonsSection => isInDetailView && IsPositionInButtonsSection();

        /// <summary>The current detail position: 0 is the header, 1-N content lines, N+1 and up buttons.</summary>
        public int DetailPosition => detailPosition;

        public int CurrentButtonIndex => currentButtonIndex;

        public int ButtonCount => currentButtons.Count;

        public IReadOnlyList<ButtonInfo> CurrentButtons => currentButtons;

        /// <summary>The message parameters all default to their localized TwoLevel.* keys when null.</summary>
        public TwoLevelMenuHelper(
            Func<int> getContentLineCount,
            Action<List<ButtonInfo>> populateButtons,
            Func<string> getHeaderAnnouncement,
            Func<int, string> getContentLineAnnouncement,
            string endOfItemMessage = "End of letter",
            string startOfItemMessage = "Start of letter",
            string openFirstMessage = null,
            string navigateDownMessage = null,
            string noButtonsMessage = null)
        {
            this.getContentLineCount = getContentLineCount ?? throw new ArgumentNullException(nameof(getContentLineCount));
            this.populateButtons = populateButtons ?? throw new ArgumentNullException(nameof(populateButtons));
            this.getHeaderAnnouncement = getHeaderAnnouncement ?? throw new ArgumentNullException(nameof(getHeaderAnnouncement));
            this.getContentLineAnnouncement = getContentLineAnnouncement ?? throw new ArgumentNullException(nameof(getContentLineAnnouncement));
            this.endOfItemMessage = endOfItemMessage;
            this.startOfItemMessage = startOfItemMessage;
            this.openFirstMessage = openFirstMessage ?? (string)"RimWorldAccess.TwoLevel.OpenFirstDefault".Translate();
            this.navigateDownMessage = navigateDownMessage ?? (string)"RimWorldAccess.TwoLevel.NavigateDownDefault".Translate();
            this.noButtonsMessage = noButtonsMessage ?? (string)"RimWorldAccess.TwoLevel.NoButtonsDefault".Translate();
        }

        /// <summary>Enters detail view, resetting the position to the header.</summary>
        public void EnterDetailView()
        {
            isInDetailView = true;
            detailPosition = 0;
            currentButtonIndex = 0;
        }

        /// <summary>Goes back to list view; false when already there.</summary>
        public bool GoBackToList()
        {
            if (!isInDetailView)
            {
                return false;
            }

            isInDetailView = false;
            detailPosition = 0;
            return true;
        }

        public void RefreshButtons()
        {
            currentButtons.Clear();
            currentButtonIndex = 0;
            populateButtons(currentButtons);
        }

        /// <summary>
        /// Moves to the next detail position. Inside the buttons section it answers with the edge
        /// tone instead of advancing, since Left/Right move between buttons.
        /// </summary>
        public void SelectNextDetailPosition()
        {
            if (IsPositionInButtonsSection())
            {
                MenuHelper.PlayEdgeTone();
                return;
            }

            int firstButtonPos = GetFirstButtonPosition();

            if (detailPosition < firstButtonPos)
            {
                detailPosition++;

                if (IsPositionInButtonsSection())
                {
                    currentButtonIndex = 0;
                }

                AnnounceDetailPosition();
            }
            else
            {
                MenuHelper.PlayEdgeTone();
            }
        }

        /// <summary>Moves to the previous detail position; from the buttons section back to the last content line.</summary>
        public void SelectPreviousDetailPosition()
        {
            if (IsPositionInButtonsSection())
            {
                int lineCount = getContentLineCount();
                detailPosition = lineCount; // Last content line (0=header, 1 to lineCount = content)
                currentButtonIndex = 0;

                if (lineCount == 0)
                {
                    detailPosition = 0;
                }

                AnnounceDetailPosition();
                return;
            }

            if (detailPosition > 0)
            {
                detailPosition--;
                AnnounceDetailPosition();
            }
            else
            {
                MenuHelper.PlayEdgeTone();
            }
        }

        /// <summary>Next button, wrapping when the WrapNavigation setting is on. Buttons section only.</summary>
        public void SelectNextButton()
        {
            if (!ValidateButtonNavigationState())
                return;

            if (currentButtonIndex < currentButtons.Count - 1)
            {
                currentButtonIndex++;
            }
            else if (RimWorldAccessMod_Settings.Settings?.WrapNavigation == true)
            {
                currentButtonIndex = 0;
                MenuHelper.PlayWrapTone();
            }
            else
            {
                MenuHelper.PlayEdgeTone();
                return;
            }

            detailPosition = GetFirstButtonPosition() + currentButtonIndex;
            AnnounceCurrentButton();
        }

        /// <summary>Previous button, wrapping when the WrapNavigation setting is on. Buttons section only.</summary>
        public void SelectPreviousButton()
        {
            if (!ValidateButtonNavigationState())
                return;

            if (currentButtonIndex > 0)
            {
                currentButtonIndex--;
            }
            else if (RimWorldAccessMod_Settings.Settings?.WrapNavigation == true)
            {
                currentButtonIndex = currentButtons.Count - 1;
                MenuHelper.PlayWrapTone();
            }
            else
            {
                MenuHelper.PlayEdgeTone();
                return;
            }

            detailPosition = GetFirstButtonPosition() + currentButtonIndex;
            AnnounceCurrentButton();
        }

        /// <summary>Activates the selected button; false when it is disabled or the state is invalid.</summary>
        public bool ActivateCurrentButton()
        {
            if (!ValidateButtonNavigationState())
                return false;

            ButtonInfo button = currentButtons[currentButtonIndex];

            if (button.IsDisabled)
            {
                // The standard disabled refusal, shared with ScreenScope's gated captured controls.
                string refusal = "RimWorldAccess.Shell.GenericWindow.Disabled".Translate(button.Label).ToString();
                if (!string.IsNullOrEmpty(button.DisabledReason))
                {
                    refusal = refusal + " " + button.DisabledReason;
                }
                TolkHelper.SpeakData(refusal);
                return false;
            }

            return true;
        }

        /// <summary>The selected button, or null outside the buttons section.</summary>
        public ButtonInfo GetCurrentButton()
        {
            if (!isInDetailView || !IsPositionInButtonsSection())
                return null;

            if (currentButtonIndex < 0 || currentButtonIndex >= currentButtons.Count)
                return null;

            return currentButtons[currentButtonIndex];
        }

        /// <summary>Jumps to the header.</summary>
        public void JumpToDetailStart()
        {
            if (!isInDetailView)
            {
                TolkHelper.SpeakData(openFirstMessage);
                return;
            }

            detailPosition = 0;
            currentButtonIndex = 0;
            AnnounceDetailPosition();
        }

        /// <summary>Jumps to the buttons section, or the last content line when there are no buttons.</summary>
        public void JumpToDetailEnd()
        {
            if (!isInDetailView)
            {
                TolkHelper.SpeakData(openFirstMessage);
                return;
            }

            if (currentButtons != null && currentButtons.Count > 0)
            {
                detailPosition = GetFirstButtonPosition();
                currentButtonIndex = 0;
            }
            else
            {
                int lineCount = getContentLineCount();
                detailPosition = lineCount; // 0=header, so lineCount is last line position
                if (detailPosition < 0) detailPosition = 0;
            }

            AnnounceDetailPosition();
        }

        /// <summary>Announces the canonical "Back to list" message, with an optional context suffix.</summary>
        public static void SpeakReturnToList(string suffix = null)
        {
            string phrase = string.IsNullOrEmpty(suffix)
                ? "RimWorldAccess.TwoLevel.BackToList".Translate().ToString()
                : "RimWorldAccess.TwoLevel.BackToListWithSuffix".Translate(suffix).ToString();
            TolkHelper.SpeakData(phrase);
        }

        /// <summary>Resets all state, for menu close.</summary>
        public void Reset()
        {
            isInDetailView = false;
            detailPosition = 0;
            currentButtonIndex = 0;
            currentButtons.Clear();
        }

        /// <summary>Resets the detail position only, keeping button state, for changing items in list view.</summary>
        public void ResetDetailPosition()
        {
            detailPosition = 0;
            currentButtonIndex = 0;
        }

        /// <summary>Announces the current detail position.</summary>
        public void AnnounceDetailPosition()
        {
            int lineCount = getContentLineCount();

            if (detailPosition == 0)
            {
                ElementDescription headerDescription = new ElementDescription();
                headerDescription.Label = getHeaderAnnouncement();
                headerDescription.Role = ElementRole.None;
                TolkHelper.SpeakData(AnnouncementComposer.ComposeFocus(headerDescription, TranslatedShellVocabulary.Instance, TextDialogShared.StandardComposeOptions()));
            }
            else if (detailPosition <= lineCount)
            {
                int lineIndex = detailPosition - 1;
                string line = getContentLineAnnouncement(lineIndex);
                if (!string.IsNullOrEmpty(line))
                {
                    ElementDescription lineDescription = new ElementDescription();
                    lineDescription.Label = line;
                    lineDescription.Role = ElementRole.MenuItem;
                    TolkHelper.SpeakData(AnnouncementComposer.ComposeFocus(lineDescription, TranslatedShellVocabulary.Instance, TextDialogShared.StandardComposeOptions()));
                }
            }
            else if (IsPositionInButtonsSection())
            {
                AnnounceCurrentButton();
            }
        }

        /// <summary>The detail position the buttons start at, after the header and content lines.</summary>
        private int GetFirstButtonPosition()
        {
            return 1 + getContentLineCount(); // 1 for header + line count
        }

        /// <summary>Total detail positions: header plus content lines plus buttons.</summary>
        private int GetTotalDetailPositions()
        {
            int buttonCount = currentButtons?.Count ?? 0;
            return 1 + getContentLineCount() + buttonCount; // header + lines + buttons
        }

        private bool IsPositionInButtonsSection()
        {
            return detailPosition >= GetFirstButtonPosition() && currentButtons != null && currentButtons.Count > 0;
        }

        /// <summary>Whether button navigation is allowed right now; announces the reason when it is not.</summary>
        private bool ValidateButtonNavigationState()
        {
            if (!isInDetailView)
            {
                TolkHelper.SpeakData(openFirstMessage);
                return false;
            }

            if (!IsPositionInButtonsSection())
            {
                TolkHelper.SpeakData(navigateDownMessage);
                return false;
            }

            if (currentButtons == null || currentButtons.Count == 0)
            {
                TolkHelper.SpeakData(noButtonsMessage);
                return false;
            }

            return true;
        }

        /// <summary>Announces the selected button through the composer: Role=Button, Disabled plus Extras carrying the reason.</summary>
        private void AnnounceCurrentButton()
        {
            if (currentButtons == null || currentButtons.Count == 0)
                return;

            if (currentButtonIndex < 0 || currentButtonIndex >= currentButtons.Count)
                return;

            ButtonInfo button = currentButtons[currentButtonIndex];
            ElementDescription d = new ElementDescription();
            d.Label = button.Label;
            d.Role = ElementRole.Button;
            d.Disabled = button.IsDisabled;
            if (button.IsDisabled && !string.IsNullOrEmpty(button.DisabledReason))
            {
                d.Extras = button.DisabledReason;
            }
            d.PositionIndex = currentButtonIndex + 1;
            d.PositionCount = currentButtons.Count;

            TolkHelper.SpeakData(AnnouncementComposer.ComposeFocus(d, TranslatedShellVocabulary.Instance, TextDialogShared.StandardComposeOptions()));
        }
    }
}
