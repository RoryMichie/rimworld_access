using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The shared reader for a screen that is ONE BIG BLOCK OF TEXT: the credits,
    /// the What's New announcements, and every future screen whose whole content
    /// is prose rather than controls. Such a screen presents that block as a giant
    /// read-only field the player arrows through line by line — Up/Down for lines,
    /// Page Up/Page Down for sections — and it MUST come from here rather than
    /// from a hand-rolled row list of its own, so that every improvement to the
    /// mechanism (line navigation today, selection and copy later) reaches all of
    /// them at once.
    ///
    /// The contract is one content region (region 0) of read-only lines, built by
    /// <see cref="BuildTextBuffer"/> through <see cref="AddLine"/> and
    /// <see cref="AddSectionLine"/>; the buffer is rebuilt on every refresh, so a
    /// subclass whose text changes under it (What's New switching announcements)
    /// needs no invalidation of its own. Everything else a screen wants —
    /// declared actions and their Buttons region, the opening announcement, an
    /// owning window, cursor-settled side effects — stays on the subclass, which
    /// is a plain <see cref="ScreenScope"/> in every other respect.
    ///
    /// A section is a line the subclass marks as a heading with
    /// <see cref="AddSectionLine"/>; the lines after it belong to it until the
    /// next heading. Page Up/Page Down follow the shared section-jump grammar
    /// (GenericWindowScope's, the precedent): Page Down lands on the next
    /// heading, Page Up on the heading BEFORE the current section's own, and
    /// either clamps with the reject sound rather than wrapping. The claims exist
    /// only while the buffer really has two or more headings, so a sectionless
    /// buffer leaves both keys to whatever sits beneath it.
    /// </summary>
    public abstract class TextBufferScope : ScreenScope
    {
        /// <summary>Shared across every subclass — registered once under the "textBuffer" pseudo-screen (ShellActionInventory.Part8).</summary>
        public const string JumpToPreviousSectionActionId = "textBuffer.jumpToPreviousSection";
        public const string JumpToNextSectionActionId = "textBuffer.jumpToNextSection";

        private readonly List<string> lines = new List<string>();

        /// <summary>Indices into <see cref="lines"/> that start a section, ascending.</summary>
        private readonly List<int> sectionStarts = new List<int>();

        protected TextBufferScope()
        {
            Claim(JumpToPreviousSectionActionId, e => JumpToSection(false), when: HasSections);
            Claim(JumpToNextSectionActionId, e => JumpToSection(true), when: HasSections);
        }

        /// <summary>
        /// Fill the buffer for the current state of the underlying text: call
        /// <see cref="AddLine"/> / <see cref="AddSectionLine"/> in reading order.
        /// Runs on every refresh against an already-cleared buffer.
        /// </summary>
        protected abstract void BuildTextBuffer();

        /// <summary>Localized name of the one content region ("Credits", "Announcement").</summary>
        protected abstract string TextRegionName { get; }

        protected void AddLine(string label)
        {
            lines.Add(label ?? "");
        }

        /// <summary>Adds a line that also starts a new section for the Page Up/Page Down jumps.</summary>
        protected void AddSectionLine(string label)
        {
            sectionStarts.Add(lines.Count);
            lines.Add(label ?? "");
        }

        protected int TextLineCount
        {
            get { return lines.Count; }
        }

        protected string TextLineAt(int index)
        {
            return index >= 0 && index < lines.Count ? lines[index] : "";
        }

        /// <summary>
        /// Claim predicate, so deliberately side-effect free: it reads the buffer
        /// the last refresh built rather than rebuilding one during dispatch.
        /// </summary>
        private bool HasSections()
        {
            return sectionStarts.Count >= 2;
        }

        protected sealed override int ContentRegionCount
        {
            get { return 1; }
        }

        protected sealed override string ContentRegionName(int region)
        {
            return TextRegionName;
        }

        protected sealed override int ContentItemCount(int region)
        {
            return lines.Count;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            return new ElementDescription { Label = TextLineAt(index), ReadOnly = true };
        }

        /// <summary>Text lines are read-only, so Enter on one falls to the screen's own proceed offer rather than here.</summary>
        protected override void ActivateContentItem(int region, int index)
        {
        }

        protected sealed override void RefreshContent()
        {
            lines.Clear();
            sectionStarts.Clear();
            BuildTextBuffer();
        }

        private void JumpToSection(bool forward)
        {
            TypeaheadReset();
            RefreshModel();
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty || Model.RegionIndex != 0)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }

            int target = forward ? NextSectionStart(region.Index) : PreviousSectionStart(region.Index);
            if (target < 0)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }

            region.MoveTo(target);
            NotifyCursorSettled();
            AnnounceCurrentItem();
        }

        private int NextSectionStart(int index)
        {
            foreach (int start in sectionStarts)
            {
                if (start > index)
                {
                    return start;
                }
            }
            return -1;
        }

        /// <summary>
        /// The heading before the one the cursor's own section starts at, so a jump
        /// from mid-section reaches the previous section rather than re-landing on
        /// the current heading.
        /// </summary>
        private int PreviousSectionStart(int index)
        {
            int currentStart = -1;
            int beforeCurrent = -1;
            foreach (int start in sectionStarts)
            {
                if (start > index)
                {
                    break;
                }
                beforeCurrent = currentStart;
                currentStart = start;
            }
            return currentStart < 0 ? -1 : beforeCurrent;
        }
    }
}
