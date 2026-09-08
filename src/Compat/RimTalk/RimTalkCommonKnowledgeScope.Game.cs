using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Keyboard support for ExpandMemory's colony-wide lore/instruction library,
    /// <c>RimTalk.Memory.UI.Dialog_CommonKnowledge</c>. A small hand-modeled content surface for
    /// the one thing captured-extras cannot see (the entry list's row selection); everything else
    /// -- toolbar, New/Import/Export/Clear-All, the auto-generate settings, the whole edit-mode
    /// form, and every multi-selection or detail-panel button -- rides
    /// <see cref="ScreenScope.IncludeCapturedExtrasRegion"/> for free, since each is a real
    /// Widgets call whose own label already carries the live count or state a sighted player reads
    /// (for instance "Delete (3)").
    ///
    /// WHY THE ENTRY LIST IS BESPOKE: <c>DrawCenterList</c>'s per-row selection is raw
    /// <c>Event.current.mousePosition</c> arithmetic against a fixed 70px row height, computed in
    /// <c>HandleEntryClick</c> at the END of <c>DoWindowContents</c>, never a
    /// <c>Widgets.ButtonInvisible</c> -- there is no clickable widget for the generic engine to
    /// find. <c>DrawEntryRow</c> IS built from real widgets, but none of them constitutes "select
    /// this row"; that happens only through the coordinate-math handler below them.
    ///
    /// SELECTION MODEL: the Entries table's row default mirrors a plain click -- replace the whole
    /// selection with this entry AND clear edit mode, which the plain-click branch alone does --
    /// while the dedicated Selected column mirrors a Ctrl-click toggle. Drag-to-reorder has a real
    /// keyboard equivalent: <see cref="ScreenScope.CanReorderContentItem"/>/<see cref="ScreenScope.ReorderContentItem"/>
    /// swap the entry one position at a time in <c>CommonKnowledgeLibrary.Entries</c>, the same
    /// list <c>ExecuteDragReorder</c> mutates, which is simpler than reproducing its multi-select
    /// block-move index math. CAVEAT, noted rather than hidden: while a category filter or search
    /// narrows the visible list, an entry's real backing-list neighbor may not be its next VISIBLE
    /// row, so a reorder can leave the filtered view looking unchanged even though the real order
    /// moved. That is the same true-list-versus-filtered-view tension the mouse gesture's own
    /// index translation exists to paper over, simplified here rather than reproduced.
    ///
    /// EDIT MODE: while <c>editMode</c> is true every hand-modeled region reports zero items and
    /// opts out of navigability; the real edit-form widgets take over through captured-extras.
    ///
    /// ACCEPT/CANCEL: the dialog overrides neither <c>OnAcceptKeyPressed</c> nor
    /// <c>OnCancelKeyPressed</c>, so <see cref="ScreenScope"/>'s own defaults are correct.
    /// </summary>
    public sealed class RimTalkCommonKnowledgeScope : ScreenScope
    {
        private const int SearchRegion = 0;
        private const int CategoriesRegion = 1;
        private const int EntriesRegion = 2;
        private const int DetailRegion = 3;

        private const int ColTag = 0;
        private const int ColContent = 1;
        private const int ColImportance = 2;
        private const int ColEnabled = 3;
        private const int ColSelected = 4;
        private const int ColExtract = 5;
        private const int ColMatch = 6;

        private readonly Window dialog;
        private readonly TextFieldEditSession session = new TextFieldEditSession();

        private readonly List<object> entries = new List<object>(); // boxed CommonKnowledgeEntry, filtered view

        public RimTalkCommonKnowledgeScope(Window dialog)
        {
            this.dialog = dialog;

            RegisterPopTeardown(session.CancelIfActive);
        }

        public override string Name
        {
            get { return "rimtalk-common-knowledge"; }
        }

        protected override int ContentRegionCount
        {
            get { return 4; }
        }

        protected override string ContentRegionName(int region)
        {
            switch (region)
            {
                case SearchRegion: return "RimWorldAccess.Compat.RimTalk.Knowledge.SearchRegionName".Translate();
                case CategoriesRegion: return "RimWorldAccess.Compat.RimTalk.Knowledge.CategoriesRegionName".Translate();
                case EntriesRegion: return "RimWorldAccess.Compat.RimTalk.Knowledge.EntriesRegionName".Translate();
                default: return "RimWorldAccess.Compat.RimTalk.Knowledge.DetailRegionName".Translate();
            }
        }

        protected override void RefreshContent()
        {
            entries.Clear();
            if (RimTalkMemoryCompat.IsEditMode(dialog))
            {
                // Edit-mode form takes over entirely through captured-extras.
                return;
            }
            object library = RimTalkMemoryCompat.GetLibrary(dialog);
            IList all = RimTalkMemoryCompat.GetEntries(library);
            if (all == null)
            {
                return;
            }
            string category = CurrentCategoryName();
            string search = (RimTalkMemoryCompat.GetSearchFilter(dialog) ?? "").Trim().ToLowerInvariant();
            foreach (object entry in all)
            {
                if (entry == null)
                {
                    continue;
                }
                if (category != "All" && RimTalkMemoryCompat.EntryCategoryLabel(entry) != RimTalkMemoryCompat.CategoryLabel(category))
                {
                    continue;
                }
                if (!string.IsNullOrEmpty(search))
                {
                    string tag = (RimTalkMemoryCompat.EntryTag(entry) ?? "").ToLowerInvariant();
                    string content = (RimTalkMemoryCompat.EntryContent(entry) ?? "").ToLowerInvariant();
                    if (!tag.Contains(search) && !content.Contains(search))
                    {
                        continue;
                    }
                }
                entries.Add(entry);
            }
        }

        private string CurrentCategoryName()
        {
            object current = RimTalkMemoryCompat.GetCurrentCategory(dialog);
            return current?.ToString() ?? "All";
        }

        /// <summary>In edit mode the dialog draws only the captured-extras form — the browse regions are not on screen, so Tab must skip them.</summary>
        protected override bool ContentRegionAlwaysNavigable(int region)
        {
            return !RimTalkMemoryCompat.IsEditMode(dialog);
        }

        protected override int ContentItemCount(int region)
        {
            if (RimTalkMemoryCompat.IsEditMode(dialog))
            {
                return 0;
            }
            switch (region)
            {
                case SearchRegion: return 1;
                case CategoriesRegion: return 6;
                case EntriesRegion: return entries.Count;
                default: return DetailRowCount();
            }
        }

        private int DetailRowCount()
        {
            int selected = RimTalkMemoryCompat.SelectedEntryCount(dialog);
            if (selected == 0) return 1; // placeholder
            if (selected == 1) return 6;
            return 4;
        }

        protected override int ContentColumnCount(int region)
        {
            return region == EntriesRegion ? 7 : 0;
        }

        protected override TableColumnInfo ContentColumnInfo(int region, int column)
        {
            if (region != EntriesRegion)
            {
                return null;
            }
            switch (column)
            {
                case ColTag: return new TableColumnInfo("RimWorldAccess.Compat.RimTalk.Knowledge.ColumnTag".Translate());
                case ColContent: return new TableColumnInfo("RimWorldAccess.Compat.RimTalk.Knowledge.ColumnContent".Translate());
                case ColImportance: return new TableColumnInfo("RimWorldAccess.Compat.RimTalk.Knowledge.ColumnImportance".Translate());
                case ColEnabled: return new TableColumnInfo("RimWorldAccess.Compat.RimTalk.Knowledge.ColumnEnabled".Translate());
                case ColSelected: return new TableColumnInfo("RimWorldAccess.Compat.RimTalk.Knowledge.ColumnSelected".Translate());
                case ColExtract: return new TableColumnInfo("RimWorldAccess.Compat.RimTalk.Knowledge.ColumnExtract".Translate());
                default: return new TableColumnInfo("RimWorldAccess.Compat.RimTalk.Knowledge.ColumnMatch".Translate());
            }
        }

        protected override string ContentCellText(int region, int row, int column)
        {
            if (region != EntriesRegion || row < 0 || row >= entries.Count)
            {
                return "";
            }
            object entry = entries[row];
            switch (column)
            {
                case ColTag: return RimTalkMemoryCompat.EntryTag(entry) ?? "";
                case ColContent: return RimTalkMemoryCompat.EntryContent(entry) ?? "";
                case ColImportance: return RimTalkMemoryCompat.EntryImportance(entry).ToString("F1");
                case ColEnabled: return YesNo(RimTalkMemoryCompat.EntryIsEnabled(entry));
                case ColSelected: return YesNo(RimTalkMemoryCompat.IsEntrySelected(dialog, entry));
                case ColExtract: return YesNo(RimTalkMemoryCompat.EntryCanBeExtracted(entry));
                default: return YesNo(RimTalkMemoryCompat.EntryCanBeMatched(entry));
            }
        }

        private static string YesNo(bool value)
        {
            return (value ? "RimWorldAccess.Compat.RimTalk.Knowledge.Yes" : "RimWorldAccess.Compat.RimTalk.Knowledge.No").Translate();
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            switch (region)
            {
                case SearchRegion:
                    d.Role = ElementRole.TextField;
                    d.Label = "RimWorldAccess.Compat.RimTalk.Knowledge.SearchLabel".Translate();
                    string value = RimTalkMemoryCompat.GetSearchFilter(dialog);
                    if (string.IsNullOrEmpty(value)) d.ValueBlank = true; else d.Value = value;
                    break;
                case CategoriesRegion:
                    DescribeCategoryRow(d, index);
                    break;
                case EntriesRegion:
                    d.Label = ContentCellText(EntriesRegion, index, ColTag);
                    break;
                default:
                    DescribeDetailRow(d, index);
                    break;
            }
            return d;
        }

        private void DescribeCategoryRow(ElementDescription d, int index)
        {
            string categoryName = index >= 0 && index < 6 ? System.Linq.Enumerable.ElementAt(RimTalkMemoryCompat.KnowledgeCategories(), index) : "All";
            d.Role = ElementRole.RadioButton;
            d.Selected = CurrentCategoryName() == categoryName;
            int count = RimTalkMemoryCompat.GetCategoryCount(dialog, categoryName);
            d.Label = "RimWorldAccess.Compat.RimTalk.Knowledge.CategoryRow".Translate(RimTalkMemoryCompat.CategoryLabel(categoryName), count);
        }

        private void DescribeDetailRow(ElementDescription d, int index)
        {
            d.Role = ElementRole.None;
            d.ReadOnly = true;
            int selected = RimTalkMemoryCompat.SelectedEntryCount(dialog);
            if (selected == 0)
            {
                d.Label = "RimWorldAccess.Compat.RimTalk.Knowledge.DetailEmpty".Translate();
                return;
            }
            if (selected == 1)
            {
                DescribeSingleDetailRow(d, index);
                return;
            }
            DescribeMultiDetailRow(d, index);
        }

        private void DescribeSingleDetailRow(ElementDescription d, int index)
        {
            object entry = System.Linq.Enumerable.First(RimTalkMemoryCompat.SelectedEntries(dialog));
            switch (index)
            {
                case 0:
                    d.Label = "RimWorldAccess.Compat.RimTalk.Knowledge.DetailTag".Translate();
                    d.Value = RimTalkMemoryCompat.EntryTag(entry);
                    d.Extras = "RimWorldAccess.Compat.RimTalk.Knowledge.DetailCategoryExtra".Translate(RimTalkMemoryCompat.EntryCategoryLabel(entry));
                    break;
                case 1:
                    d.Label = "RimWorldAccess.Compat.RimTalk.Knowledge.DetailImportance".Translate();
                    d.Value = RimTalkMemoryCompat.EntryImportance(entry).ToString("F1");
                    break;
                case 2:
                    d.Label = "RimWorldAccess.Compat.RimTalk.Knowledge.DetailStatus".Translate();
                    d.Value = RimTalkMemoryCompat.EntryIsEnabled(entry)
                        ? "RimWorldAccess.Compat.RimTalk.Knowledge.StatusEnabled".Translate()
                        : "RimWorldAccess.Compat.RimTalk.Knowledge.StatusDisabled".Translate();
                    break;
                case 3:
                    d.Label = "RimWorldAccess.Compat.RimTalk.Knowledge.DetailVisibility".Translate();
                    d.Value = RimTalkMemoryCompat.EntryVisibilityText(entry);
                    break;
                case 4:
                    d.Label = "RimWorldAccess.Compat.RimTalk.Knowledge.DetailContent".Translate();
                    string content = RimTalkMemoryCompat.EntryContent(entry);
                    if (string.IsNullOrEmpty(content)) d.ValueBlank = true; else d.Value = content;
                    break;
                default:
                    d.Label = "RimWorldAccess.Compat.RimTalk.Knowledge.DetailExtendedProperties".Translate();
                    d.Value = "RimWorldAccess.Compat.RimTalk.Knowledge.ExtendedPropertiesValue".Translate(
                        YesNo(RimTalkMemoryCompat.EntryCanBeExtracted(entry)), YesNo(RimTalkMemoryCompat.EntryCanBeMatched(entry)));
                    break;
            }
        }

        private void DescribeMultiDetailRow(ElementDescription d, int index)
        {
            int selected = 0, enabled = 0;
            float importanceSum = 0f;
            foreach (object entry in RimTalkMemoryCompat.SelectedEntries(dialog))
            {
                selected++;
                if (RimTalkMemoryCompat.EntryIsEnabled(entry)) enabled++;
                importanceSum += RimTalkMemoryCompat.EntryImportance(entry);
            }
            switch (index)
            {
                case 0:
                    d.Label = "RimWorldAccess.Compat.RimTalk.Knowledge.DetailMultiCount".Translate(selected);
                    break;
                case 1:
                    d.Label = "RimWorldAccess.Compat.RimTalk.Knowledge.DetailEnabledCount".Translate(enabled);
                    break;
                case 2:
                    d.Label = "RimWorldAccess.Compat.RimTalk.Knowledge.DetailDisabledCount".Translate(selected - enabled);
                    break;
                default:
                    d.Label = "RimWorldAccess.Compat.RimTalk.Knowledge.DetailAvgImportance".Translate((selected > 0 ? importanceSum / selected : 0f).ToString("F2"));
                    break;
            }
        }

        protected override void ActivateContentItem(int region, int index)
        {
            switch (region)
            {
                case SearchRegion:
                    BeginEditSearch();
                    return;
                case CategoriesRegion:
                    ActivateCategoryRow(index);
                    return;
                case EntriesRegion:
                    if (index < 0 || index >= entries.Count) return;
                    RimTalkMemoryCompat.ReplaceEntrySelectionWith(dialog, entries[index]);
                    RefreshModel();
                    AnnounceCurrentItem();
                    return;
                default:
                    AnnounceCurrentItem();
                    return;
            }
        }

        private void ActivateCategoryRow(int index)
        {
            string categoryName = index >= 0 && index < 6 ? System.Linq.Enumerable.ElementAt(RimTalkMemoryCompat.KnowledgeCategories(), index) : "All";
            RimTalkMemoryCompat.SetCurrentCategory(dialog, categoryName);
            RefreshModel();
            AnnounceCurrentItem();
        }

        protected override bool ActivateContentCell(int region, int row, int column)
        {
            if (region != EntriesRegion || row < 0 || row >= entries.Count)
            {
                return false;
            }
            object entry = entries[row];
            switch (column)
            {
                case ColEnabled:
                    // MUTATION-C: mirrors DrawEntryRow's own return-assign (Widgets.Checkbox(..., ref entry.isEnabled, ...)).
                    RimTalkMemoryCompat.SetEntryIsEnabled(entry, !RimTalkMemoryCompat.EntryIsEnabled(entry));
                    AnnounceCurrentItem();
                    return true;
                case ColSelected:
                    RimTalkMemoryCompat.ToggleEntrySelection(dialog, entry);
                    RefreshModel();
                    AnnounceCurrentItem();
                    return true;
                case ColExtract:
                    RimTalkMemoryCompat.SetEntryCanBeExtracted(entry, !RimTalkMemoryCompat.EntryCanBeExtracted(entry));
                    AnnounceCurrentItem();
                    return true;
                case ColMatch:
                    RimTalkMemoryCompat.SetEntryCanBeMatched(entry, !RimTalkMemoryCompat.EntryCanBeMatched(entry));
                    AnnounceCurrentItem();
                    return true;
                default:
                    return false;
            }
        }

        // Focus ring: DrawCenterList lays the entry rows out on a fixed 70px pitch from the
        // scroll view's own origin, and DrawEntryRow draws each row's enabled checkbox at the
        // row's (5, +10) offset. That checkbox is the only thing about the row the capture pass
        // can see, and it is enough: it pins the row index AND carries the scrolled group's
        // translation and visible band, which the pass this runs after has already closed.

        private const float EntryRowHeight = 70f;
        private const float EntryCheckboxX = 5f;
        private const float EntryCheckboxY = 10f;

        protected internal override Rect FocusedContentRect()
        {
            ListModel region = Model.CurrentRegion;
            if (Model.RegionIndex != EntriesRegion || region == null
                || region.Index < 0 || region.Index >= entries.Count)
            {
                return default(Rect);
            }
            float rowY = region.Index * EntryRowHeight;
            IReadOnlyList<CapturedWidget> pass = WidgetCapture.Items;
            for (int i = 0; i < pass.Count; i++)
            {
                CapturedWidget widget = pass[i];
                if (widget.Kind != WidgetKind.Checkbox
                    || Mathf.Abs(widget.Rect.x - EntryCheckboxX) > 0.5f
                    || Mathf.Abs(widget.Rect.y - (rowY + EntryCheckboxY)) > 0.5f)
                {
                    continue;
                }
                return GuiSpace.VisibleScreenRectFrom(widget.Rect, widget.ScreenRect, widget.Clip,
                    new Rect(0f, rowY, widget.Clip.VisibleRect.width, EntryRowHeight));
            }
            return default(Rect);
        }

        // Reorder: the keyboard equivalent of drag-to-reorder.

        protected override bool CanReorderContentItem(int region, int index, int direction)
        {
            if (region != EntriesRegion || index < 0 || index >= entries.Count)
            {
                return false;
            }
            object library = RimTalkMemoryCompat.GetLibrary(dialog);
            IList real = RimTalkMemoryCompat.GetEntries(library);
            int realIndex = real?.IndexOf(entries[index]) ?? -1;
            int target = realIndex + direction;
            return realIndex >= 0 && target >= 0 && target < real.Count;
        }

        protected override int ReorderContentItem(int region, int index, int direction)
        {
            if (region != EntriesRegion || index < 0 || index >= entries.Count)
            {
                return -1;
            }
            object entry = entries[index];
            object library = RimTalkMemoryCompat.GetLibrary(dialog);
            if (!RimTalkMemoryCompat.ReorderEntry(library, entry, direction))
            {
                return -1;
            }
            RefreshModel();
            int newIndex = entries.IndexOf(entry);
            return newIndex >= 0 ? newIndex : index;
        }

        // Search field editing (the one hand-built TextField).

        private void BeginEditSearch()
        {
            string current = RimTalkMemoryCompat.GetSearchFilter(dialog) ?? "";
            var spec = new TextFieldSpec(labelKey: "RimWorldAccess.TextInput.LabelDefault", maxLength: null, minLength: 0);
            session.EnterEdit(
                current,
                spec,
                "RimWorldAccess.Compat.RimTalk.Knowledge.SearchLabel".Translate(),
                ApplySearchValue,
                ReAnnounceSearchRow,
                announcePrompt: true);
        }

        /// <summary>MUTATION-C: mirrors DrawToolbar's own return-assign verbatim.</summary>
        private void ApplySearchValue(string value)
        {
            RimTalkMemoryCompat.SetSearchFilter(dialog, value ?? "");
            RefreshModel();
        }

        private void ReAnnounceSearchRow()
        {
            AnnounceCurrentItem();
        }

        // Captured-extras region: the toolbar, auto-generate settings, Tag Test/Help, and every
        // detail, multi-selection and edit-form control -- all real widgets.

        protected override bool CaptureWindowButtons
        {
            get { return false; }
        }

        protected override bool IncludeCapturedExtrasRegion
        {
            get { return true; }
        }

        /// <summary>Drops the TextField capture, since the hand-built Search row owns searchFilter -- except in editMode, where the real Tag field and Content TextArea must ride captured-extras normally (the hand-modeled regions are empty in that state, so nothing doubles).</summary>
        protected override bool ExcludeFromCapturedExtras(CapturedWidget widget)
        {
            return !RimTalkMemoryCompat.IsEditMode(dialog) && widget.Kind == WidgetKind.TextField;
        }

        protected override string ComposeOpenAnnouncement()
        {
            return "RimWorldAccess.Compat.RimTalk.Knowledge.Opened".Translate();
        }
    }
}
