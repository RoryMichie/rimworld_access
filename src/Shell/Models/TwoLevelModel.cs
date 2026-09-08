using System;

namespace RimWorldAccess.Shell
{
    /// <summary>Where the two-level cursor currently sits.</summary>
    public enum TwoLevelArea
    {
        /// <summary>Browsing the item list.</summary>
        List,
        /// <summary>Detail position 0: the item header.</summary>
        Header,
        /// <summary>Detail positions 1..LineCount: content lines.</summary>
        ContentLine,
        /// <summary>Past the content: the item's action buttons (Left/Right navigate).</summary>
        Buttons,
    }

    /// <summary>
    /// The list ↔ detail ↔ buttons shape from TwoLevelMenuHelper (letters,
    /// quests, messages): a list cursor, and inside an item a vertical detail
    /// cursor over header (position 0), content lines (1..LineCount), then a
    /// buttons section where Up exits back to the last content line and
    /// Left/Right walk the buttons (wrapping per the WrapNavigation setting).
    /// Announcement text, button labels, and actions stay with the scope.
    ///
    /// PURE: links into the test project.
    /// </summary>
    public sealed class TwoLevelModel
    {
        private bool inDetail;
        private int lineCount;
        private int buttonCount;
        private int detailPosition;
        private int buttonIndex;

        public TwoLevelModel(int itemCount = 0, bool wrapButtons = false)
        {
            List = new ListModel(itemCount);
            WrapButtons = wrapButtons;
        }

        /// <summary>The outer item list.</summary>
        public ListModel List { get; }

        /// <summary>Buttons wrap at the ends (the WrapNavigation setting).</summary>
        public bool WrapButtons;

        public bool InDetail
        {
            get { return inDetail; }
        }

        public int LineCount
        {
            get { return lineCount; }
        }

        public int ButtonCount
        {
            get { return buttonCount; }
        }

        /// <summary>0 = header, 1..LineCount = content lines, above that = buttons.</summary>
        public int DetailPosition
        {
            get { return detailPosition; }
        }

        /// <summary>0-based index within the buttons section.</summary>
        public int ButtonIndex
        {
            get { return buttonIndex; }
        }

        public TwoLevelArea Area
        {
            get
            {
                if (!inDetail)
                    return TwoLevelArea.List;
                if (detailPosition == 0)
                    return TwoLevelArea.Header;
                if (detailPosition <= lineCount)
                    return TwoLevelArea.ContentLine;
                return TwoLevelArea.Buttons;
            }
        }

        /// <summary>Current content line as a 0-based index; -1 outside the content area.</summary>
        public int ContentLineIndex
        {
            get { return Area == TwoLevelArea.ContentLine ? detailPosition - 1 : -1; }
        }

        /// <summary>
        /// Opens the current item's detail view at the header. The scope
        /// supplies the item's shape (content line count, button count).
        /// </summary>
        public void EnterDetail(int contentLineCount, int itemButtonCount)
        {
            if (contentLineCount < 0)
                throw new ArgumentOutOfRangeException(nameof(contentLineCount));
            if (itemButtonCount < 0)
                throw new ArgumentOutOfRangeException(nameof(itemButtonCount));
            inDetail = true;
            lineCount = contentLineCount;
            buttonCount = itemButtonCount;
            detailPosition = 0;
            buttonIndex = 0;
        }

        /// <summary>Item content changed while open (refresh); cursor clamps into the new shape.</summary>
        public void SetShape(int contentLineCount, int itemButtonCount)
        {
            if (contentLineCount < 0)
                throw new ArgumentOutOfRangeException(nameof(contentLineCount));
            if (itemButtonCount < 0)
                throw new ArgumentOutOfRangeException(nameof(itemButtonCount));
            lineCount = contentLineCount;
            buttonCount = itemButtonCount;
            int maxPosition = lineCount + (buttonCount > 0 ? 1 : 0);
            if (detailPosition > maxPosition)
                detailPosition = maxPosition;
            if (buttonIndex >= buttonCount)
                buttonIndex = buttonCount > 0 ? buttonCount - 1 : 0;
        }

        /// <summary>Back from detail to the list. False when already in the list.</summary>
        public bool ExitDetail()
        {
            if (!inDetail)
                return false;
            inDetail = false;
            detailPosition = 0;
            buttonIndex = 0;
            return true;
        }

        /// <summary>
        /// Down within the detail view: header → lines → into the buttons
        /// section (landing on the first button). Once in buttons, Down stays
        /// put (AtEdge; Left/Right navigate buttons). AtEdge at the end of
        /// content when the item has no buttons.
        /// </summary>
        public MoveResult NextDetailPosition()
        {
            if (!inDetail)
                return new MoveResult(MoveKind.Empty, -1);
            if (Area == TwoLevelArea.Buttons)
                return new MoveResult(MoveKind.AtEdge, detailPosition);

            int firstButtonPosition = lineCount + 1;
            if (buttonCount > 0 && detailPosition < firstButtonPosition)
            {
                detailPosition++;
                if (detailPosition == firstButtonPosition)
                    buttonIndex = 0;
                return new MoveResult(MoveKind.Moved, detailPosition);
            }
            if (detailPosition < lineCount)
            {
                detailPosition++;
                return new MoveResult(MoveKind.Moved, detailPosition);
            }
            return new MoveResult(MoveKind.AtEdge, detailPosition);
        }

        /// <summary>
        /// Up within the detail view: from buttons back to the last content
        /// line (or header when there are none), then up through lines to the
        /// header. AtEdge on the header.
        /// </summary>
        public MoveResult PreviousDetailPosition()
        {
            if (!inDetail)
                return new MoveResult(MoveKind.Empty, -1);
            if (Area == TwoLevelArea.Buttons)
            {
                detailPosition = lineCount;
                buttonIndex = 0;
                return new MoveResult(MoveKind.Moved, detailPosition);
            }
            if (detailPosition == 0)
                return new MoveResult(MoveKind.AtEdge, detailPosition);
            detailPosition--;
            return new MoveResult(MoveKind.Moved, detailPosition);
        }

        public MoveResult FirstDetailPosition()
        {
            if (!inDetail)
                return new MoveResult(MoveKind.Empty, -1);
            if (detailPosition == 0)
                return new MoveResult(MoveKind.AtEdge, 0);
            detailPosition = 0;
            buttonIndex = 0;
            return new MoveResult(MoveKind.Moved, 0);
        }

        /// <summary>End jump: the last content line (never into buttons, matching the helper).</summary>
        public MoveResult LastDetailPosition()
        {
            if (!inDetail)
                return new MoveResult(MoveKind.Empty, -1);
            if (Area == TwoLevelArea.Buttons)
            {
                detailPosition = lineCount;
                buttonIndex = 0;
                return new MoveResult(MoveKind.Moved, detailPosition);
            }
            if (detailPosition == lineCount)
                return new MoveResult(MoveKind.AtEdge, detailPosition);
            detailPosition = lineCount;
            return new MoveResult(MoveKind.Moved, detailPosition);
        }

        /// <summary>Right within the buttons section; wraps per WrapButtons, else AtEdge.</summary>
        public MoveResult NextButton()
        {
            if (Area != TwoLevelArea.Buttons || buttonCount == 0)
                return new MoveResult(MoveKind.Empty, -1);
            if (buttonIndex < buttonCount - 1)
            {
                buttonIndex++;
                return new MoveResult(MoveKind.Moved, buttonIndex);
            }
            if (WrapButtons && buttonCount > 1)
            {
                buttonIndex = 0;
                return new MoveResult(MoveKind.Wrapped, buttonIndex);
            }
            return new MoveResult(MoveKind.AtEdge, buttonIndex);
        }

        /// <summary>Left within the buttons section; wraps per WrapButtons, else AtEdge.</summary>
        public MoveResult PreviousButton()
        {
            if (Area != TwoLevelArea.Buttons || buttonCount == 0)
                return new MoveResult(MoveKind.Empty, -1);
            if (buttonIndex > 0)
            {
                buttonIndex--;
                return new MoveResult(MoveKind.Moved, buttonIndex);
            }
            if (WrapButtons && buttonCount > 1)
            {
                buttonIndex = buttonCount - 1;
                return new MoveResult(MoveKind.Wrapped, buttonIndex);
            }
            return new MoveResult(MoveKind.AtEdge, buttonIndex);
        }
    }
}
