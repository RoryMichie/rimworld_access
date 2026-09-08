using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Keyboard support for PromptEnhance's "Colony Status" main tab,
    /// <c>RimTalkHealthEnhance.MainTabWindow_Announcement</c>. Its three vanilla TabRecords, built
    /// as a per-frame-recreated <c>List&lt;TabRecord&gt;</c> with no stored field, map onto FOUR of
    /// this scope's five regions: Current Status splits into Overview + Tasks, because
    /// <see cref="ScreenScope.ContentColumnCount"/> is one fixed value per region and Overview is
    /// a single row while the task list needs real columns for its four per-row actions.
    /// <see cref="OnRegionChanged"/> mirrors the region cursor into the mod's visible tab through
    /// the generic tab-strip capture's <c>WidgetCapture.RequestActivate</c>, which invokes the
    /// matching <c>TabRecord.clickedAction</c> with vanilla's exact click semantics, so no
    /// reflection into a private <c>currentTab</c> field is needed.
    ///
    /// RIDES CAPTURED-EXTRAS FOR FREE, no code here: the Overview text area's three buttons, New
    /// Task, both TabRecord strips, the History day-navigation row (including the two glyph-only
    /// shift buttons, already covered by <see cref="GlyphButtonNames"/>), Reset All/Copy to
    /// Overview/Regenerate/Custom Prompt, and New Area. Each already announces its own result via
    /// <c>Messages.Message</c>, so no bespoke feedback is added.
    ///
    /// HAND-MODELED, each because it needs grouped composition a flat captured-extras read would
    /// scatter across stray Label/Button rows:
    /// <list type="bullet">
    /// <item>Overview region (0): the single free-text row, committed via
    /// <c>WidgetCapture.RequestTextOverride</c> against the captured TextArea, so no reflection
    /// into the private <c>editingOverview</c> buffer is needed.</item>
    /// <item>Tasks region (1): a 5-column task-card table filtered by whatever category the
    /// free-riding category strip has selected, re-read fresh each refresh. Info composes label,
    /// category, priority, status, progress/assignee and description into one line, since the
    /// priority color stripe and category chip a sighted player sees are otherwise
    /// speech-invisible. Discuss invokes the mod's own private <c>ShowPawnSelectorMenu</c>
    /// (vehicle A: the exact FloatMenu the button opens, which already self-gates and messages on
    /// <c>DiscussionService</c> unavailability). Status-cycle
    /// is MUTATION-C (see <see cref="PromptEnhanceCompat.CycleStatus"/>): the button's handler is
    /// inline in the mod with no standalone method, so this mirrors it verbatim. Edit/Delete call
    /// the mod's own TaskEditorDialog constructor and DeleteAnnouncement exactly as the buttons
    /// do, including the mod's own choice not to confirm a deletion.</item>
    /// <item>History region (2): row 0 is the AI Summary, same RequestTextOverride vehicle as
    /// Overview. Row 1 is a read-only Details composition where the date and diff report stay one
    /// flowing paragraph (the diff report is already mod-authored prose) while Player Actions and
    /// Events, being discrete logged facts, are period-chopped.
    /// <see cref="PromptEnhanceCompat.GetCurrentSnapshotIndex"/> is re-read every refresh, so the
    /// free-riding Prev/Next Day buttons simply change what this region shows next pass.</item>
    /// <item>Custom Areas region (3): a 5-column table whose Info column replaces the mod's own
    /// hardcoded, untranslated "格子数: {0} | 状态: {1}" line with a localized cell-count/enabled
    /// composition over the SAME underlying data, rather than transcribing raw Chinese.
    /// Draw/Remove Cells still just select the mod's own <c>AreaDrawingDesignator</c>; the
    /// keyboard cell PAINTING once that designator is active is DESCOPED (see
    /// <see cref="PromptEnhanceCompat"/>'s class remarks), so the paint gesture itself stays
    /// mouse-only.</item>
    /// </list>
    ///
    /// ACCEPT/CANCEL: <c>MainTabWindow_Announcement</c> overrides neither
    /// <c>OnAcceptKeyPressed</c> nor <c>OnCancelKeyPressed</c>, so <see cref="ScreenScope"/>'s own
    /// defaults are correct.
    /// </summary>
    public sealed class PromptEnhanceAnnouncementScope : ScreenScope
    {
        private const int OverviewRegion = 0;
        private const int TasksRegion = 1;
        private const int HistoryRegion = 2;
        private const int CustomAreasRegion = 3;

        private const int TaskColInfo = 0;
        private const int TaskColDiscuss = 1;
        private const int TaskColStatusCycle = 2;
        private const int TaskColEdit = 3;
        private const int TaskColDelete = 4;

        private const int AreaColInfo = 0;
        private const int AreaColDraw = 1;
        private const int AreaColRemoveCells = 2;
        private const int AreaColEdit = 3;
        private const int AreaColDelete = 4;

        private readonly Window dialog;

        private object manager;
        private object data;

        private readonly List<object> tasks = new List<object>(); // boxed ColonyAnnouncement
        private object currentSnapshot; // boxed DailySnapshot, or null
        private readonly List<object> areas = new List<object>(); // boxed CustomNamedArea

        private int overviewTextFieldIndex = -1;
        private int summaryTextFieldIndex = -1;

        private readonly TextFieldEditSession overviewSession = new TextFieldEditSession();
        private bool overviewOverrideActive;

        private readonly TextFieldEditSession summarySession = new TextFieldEditSession();
        private bool summaryOverrideActive;

        public PromptEnhanceAnnouncementScope(Window dialog)
        {
            this.dialog = dialog;
        }

        public override string Name => "prompt-enhance-announcement";

        protected override int ContentRegionCount => 4;

        protected override string ContentRegionName(int region)
        {
            switch (region)
            {
                case OverviewRegion: return "RimWorldAccess.Compat.PromptEnhance.OverviewRegionName".Translate();
                case TasksRegion: return "RimWorldAccess.Compat.PromptEnhance.TasksRegionName".Translate();
                case HistoryRegion: return Translator.Translate("RTE_Tab_HistorySnapshots").Resolve();
                default: return Translator.Translate("RTE_Tab_CustomAreas").Resolve();
            }
        }

        /// <summary>Mirrors the region cursor into the mod's own visible tab so the sighted view follows (Overview and Tasks both live under "Current Status").</summary>
        protected override void OnRegionChanged(MoveResult result)
        {
            string tabLabel = Model.RegionIndex == HistoryRegion
                ? Translator.Translate("RTE_Tab_HistorySnapshots").Resolve()
                : Model.RegionIndex == CustomAreasRegion
                    ? Translator.Translate("RTE_Tab_CustomAreas").Resolve()
                    : Translator.Translate("RTE_Tab_CurrentStatus").Resolve();
            WidgetCapture.RequestActivate(WidgetKind.Tab, tabLabel, 0);
        }

        protected override void RefreshContent()
        {
            manager = PromptEnhanceCompat.GetManagerInstance();
            data = PromptEnhanceCompat.GetData(manager);

            tasks.Clear();
            areas.Clear();
            currentSnapshot = null;
            overviewTextFieldIndex = -1;
            summaryTextFieldIndex = -1;

            if (data == null)
            {
                return;
            }

            object selectedCategory = PromptEnhanceCompat.GetSelectedCategory(dialog);
            IList announcements = PromptEnhanceCompat.GetAnnouncements(data);
            if (announcements != null)
            {
                foreach (object a in announcements)
                {
                    if (a == null)
                    {
                        continue;
                    }
                    if (selectedCategory != null && !Equals(PromptEnhanceCompat.AnnCategory(a), selectedCategory))
                    {
                        continue;
                    }
                    tasks.Add(a);
                }
            }

            IList snapshots = PromptEnhanceCompat.GetDailySnapshots(data);
            if (snapshots != null && snapshots.Count > 0)
            {
                List<object> ordered = new List<object>();
                foreach (object s in snapshots)
                {
                    if (s != null)
                    {
                        ordered.Add(s);
                    }
                }
                ordered.Sort((x, y) => PromptEnhanceCompat.SnapAbsTick(y).CompareTo(PromptEnhanceCompat.SnapAbsTick(x)));
                int index = PromptEnhanceCompat.GetCurrentSnapshotIndex(dialog);
                if (index >= 0 && index < ordered.Count)
                {
                    currentSnapshot = ordered[index];
                }
            }

            IList customAreas = PromptEnhanceCompat.GetCustomAreas(manager);
            if (customAreas != null)
            {
                foreach (object area in customAreas)
                {
                    if (area != null)
                    {
                        areas.Add(area);
                    }
                }
            }

            FindTextFieldIndices();
        }

        /// <summary>
        /// Locates this pass's captured Overview / AI Summary TextField so the edit sessions can
        /// read and override it, matched by first appearance among the frame's TextField captures.
        /// Safe because only one of Current Status/History draws its TextArea in a given frame —
        /// the other tab is not rendering.
        /// </summary>
        private void FindTextFieldIndices()
        {
            IReadOnlyList<CapturedWidget> items = WidgetCapture.Items;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].Kind != WidgetKind.TextField)
                {
                    continue;
                }
                if (overviewTextFieldIndex < 0)
                {
                    overviewTextFieldIndex = i;
                }
                if (summaryTextFieldIndex < 0 && i != overviewTextFieldIndex)
                {
                    summaryTextFieldIndex = i;
                }
            }
        }

        /// <summary>
        /// The two rows backed by a captured widget this scope already addresses by index — the
        /// Overview and AI Summary text areas. The task and custom-area cards are virtualized
        /// behind the mod's own per-frame height cache and drawn in its priority-descending
        /// order, neither of which this scope's list mirrors, so their rows report nothing.
        /// </summary>
        protected internal override Rect FocusedContentRect()
        {
            ListModel region = Model.CurrentRegion;
            if (region == null)
            {
                return default(Rect);
            }
            int capture = Model.RegionIndex == OverviewRegion && region.Index == 0 ? overviewTextFieldIndex
                : Model.RegionIndex == HistoryRegion && region.Index == 0 ? summaryTextFieldIndex
                : -1;
            IReadOnlyList<CapturedWidget> items = WidgetCapture.Items;
            return capture >= 0 && capture < items.Count ? items[capture].VisibleScreenRect : default(Rect);
        }

        protected override int ContentItemCount(int region)
        {
            switch (region)
            {
                case OverviewRegion: return 1;
                case TasksRegion: return tasks.Count;
                case HistoryRegion: return currentSnapshot == null ? 0 : 2;
                default: return areas.Count;
            }
        }

        protected override int ContentColumnCount(int region)
        {
            return region == TasksRegion || region == CustomAreasRegion ? 5 : 0;
        }

        protected override TableColumnInfo ContentColumnInfo(int region, int column)
        {
            if (region != TasksRegion && region != CustomAreasRegion)
            {
                return null;
            }
            switch (column)
            {
                case 0: return new TableColumnInfo("RimWorldAccess.Compat.PromptEnhance.ColumnInfo".Translate());
                case 1: return new TableColumnInfo(region == CustomAreasRegion
                    ? Translator.Translate("RTE_Area_Draw").Resolve()
                    : Translator.Translate("RTE_Announcement_Discuss").Resolve());
                case 2: return new TableColumnInfo(region == CustomAreasRegion
                    ? Translator.Translate("RTE_Area_RemoveCells").Resolve()
                    : "RimWorldAccess.Compat.PromptEnhance.ColumnStatusCycle".Translate().Resolve());
                case 3: return new TableColumnInfo(Translator.Translate("RTE_Announcement_Edit").Resolve());
                default: return new TableColumnInfo(Translator.Translate("RTE_Announcement_Delete").Resolve());
            }
        }

        protected override string ContentCellText(int region, int row, int column)
        {
            if (region == TasksRegion)
            {
                if (row < 0 || row >= tasks.Count)
                {
                    return "";
                }
                if (column == TaskColInfo)
                {
                    return TaskInfoLine(tasks[row]);
                }
                if (column == TaskColStatusCycle)
                {
                    return PromptEnhanceCompat.StatusCycleLabel(tasks[row]);
                }
                return ContentColumnInfo(region, column)?.Label ?? "";
            }
            if (region == CustomAreasRegion)
            {
                if (row < 0 || row >= areas.Count)
                {
                    return "";
                }
                if (column == AreaColInfo)
                {
                    return AreaInfoLine(areas[row]);
                }
                return ContentColumnInfo(region, column)?.Label ?? "";
            }
            return "";
        }

        private static string TaskInfoLine(object task)
        {
            string title = PromptEnhanceCompat.AnnTitle(task);
            string category = PromptEnhanceCompat.CategoryLabel(PromptEnhanceCompat.AnnCategory(task));
            string priority = PromptEnhanceCompat.PriorityLabel(PromptEnhanceCompat.AnnPriority(task));
            string status = PromptEnhanceCompat.StatusLabel(PromptEnhanceCompat.AnnStatus(task));
            float progress = PromptEnhanceCompat.AnnProgress(task);
            string assigned = PromptEnhanceCompat.AnnAssignedPawnName(task);
            string description = PromptEnhanceCompat.AnnDescription(task);

            var sb = new StringBuilder();
            sb.Append("RimWorldAccess.Compat.PromptEnhance.TaskHeader".Translate(title, category, priority, status));
            if (progress > 0f)
            {
                sb.Append(" ");
                sb.Append("RimWorldAccess.Compat.PromptEnhance.TaskProgress".Translate(GenText.ToStringPercent(progress)));
            }
            if (!string.IsNullOrEmpty(assigned))
            {
                sb.Append(" ");
                sb.Append("RimWorldAccess.Compat.PromptEnhance.TaskAssigned".Translate(assigned));
            }
            if (!string.IsNullOrEmpty(description))
            {
                sb.Append(" ");
                sb.Append(description);
            }
            return sb.ToString();
        }

        private static string AreaInfoLine(object area)
        {
            int cellCount = PromptEnhanceCompat.AreaCellCount(area);
            bool active = PromptEnhanceCompat.AreaIsActive(area);
            string status = active
                ? "RimWorldAccess.Compat.PromptEnhance.AreaStatusEnabled".Translate(cellCount)
                : "RimWorldAccess.Compat.PromptEnhance.AreaStatusDisabled".Translate(cellCount);
            return PromptEnhanceCompat.AreaLabel(area) + ". " + status;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            switch (region)
            {
                case OverviewRegion:
                    DescribeOverviewRow(d);
                    break;
                case TasksRegion:
                    if (index >= 0 && index < tasks.Count)
                    {
                        d.Label = TaskInfoLine(tasks[index]);
                    }
                    break;
                case HistoryRegion:
                    DescribeHistoryRow(d, index);
                    break;
                default:
                    if (index >= 0 && index < areas.Count)
                    {
                        d.Label = AreaInfoLine(areas[index]);
                    }
                    break;
            }
            return d;
        }

        private void DescribeOverviewRow(ElementDescription d)
        {
            d.Role = ElementRole.TextField;
            d.Label = "RimWorldAccess.Compat.PromptEnhance.OverviewLabel".Translate();
            string overview = PromptEnhanceCompat.GetColonyOverview(data);
            if (string.IsNullOrEmpty(overview))
            {
                d.ValueBlank = true;
            }
            else
            {
                d.Value = overview;
            }
        }

        private void DescribeHistoryRow(ElementDescription d, int index)
        {
            if (currentSnapshot == null)
            {
                return;
            }
            if (index == 0)
            {
                d.Role = ElementRole.TextField;
                d.Label = Translator.Translate("RTE_Snapshot_AISummary").Resolve();
                string summary = PromptEnhanceCompat.SnapAISummary(currentSnapshot);
                if (string.IsNullOrEmpty(summary))
                {
                    d.ValueBlank = true;
                }
                else
                {
                    d.Value = summary;
                }
                return;
            }

            d.Role = ElementRole.None;
            d.ReadOnly = true;
            d.Label = "RimWorldAccess.Compat.PromptEnhance.HistoryDetailsLabel".Translate();
            long offset = PromptEnhanceCompat.GetDisplayTickOffset(data);
            string date = PromptEnhanceCompat.SnapDateString(currentSnapshot, offset);
            string diffReport = PromptEnhanceCompat.SnapDiffReport(currentSnapshot);

            var sb = new StringBuilder();
            sb.Append("RimWorldAccess.Compat.PromptEnhance.HistoryDate".Translate(date));
            if (!string.IsNullOrEmpty(diffReport))
            {
                sb.Append(" ");
                sb.Append(diffReport);
            }
            List<string> playerActions = PromptEnhanceCompat.SnapPlayerActions(currentSnapshot);
            if (playerActions != null && playerActions.Count > 0)
            {
                sb.Append(" ");
                sb.Append(Translator.Translate("RTE_Snapshot_PlayerActions").Resolve());
                sb.Append(". ");
                for (int i = 0; i < playerActions.Count; i++)
                {
                    sb.Append(playerActions[i]);
                    sb.Append(". ");
                }
            }
            List<string> events = PromptEnhanceCompat.SnapEvents(currentSnapshot);
            if (events != null && events.Count > 0)
            {
                sb.Append(Translator.Translate("RTE_Snapshot_Events").Resolve());
                sb.Append(". ");
                for (int i = 0; i < events.Count; i++)
                {
                    sb.Append(events[i]);
                    sb.Append(". ");
                }
            }
            d.Value = sb.ToString();
        }

        protected override void ActivateContentItem(int region, int index)
        {
            switch (region)
            {
                case OverviewRegion:
                    BeginOverviewEdit();
                    break;
                case HistoryRegion:
                    if (index == 0)
                    {
                        BeginSummaryEdit();
                    }
                    else
                    {
                        AnnounceCurrentItem();
                    }
                    break;
                default:
                    AnnounceCurrentItem();
                    break;
            }
        }

        protected override bool ActivateContentCell(int region, int row, int column)
        {
            if (region == TasksRegion)
            {
                if (row < 0 || row >= tasks.Count)
                {
                    return false;
                }
                object task = tasks[row];
                switch (column)
                {
                    case TaskColInfo:
                        return false;
                    case TaskColDiscuss:
                        ScopeDelegateGuard.Run(() => PromptEnhanceCompat.ShowPawnSelectorMenu(dialog, task));
                        return true;
                    case TaskColStatusCycle:
                        PromptEnhanceCompat.CycleStatus(task, manager);
                        RefreshModel();
                        AnnounceCurrentItem();
                        return true;
                    case TaskColEdit:
                        PromptEnhanceCompat.OpenTaskEditor(task, manager);
                        return true;
                    default:
                        PromptEnhanceCompat.DeleteAnnouncement(manager, PromptEnhanceCompat.AnnId(task));
                        RefreshModel();
                        return true;
                }
            }
            if (region == CustomAreasRegion)
            {
                if (row < 0 || row >= areas.Count)
                {
                    return false;
                }
                object area = areas[row];
                switch (column)
                {
                    case AreaColInfo:
                        return false;
                    case AreaColDraw:
                        StartAreaDrawing(area, adding: true);
                        return true;
                    case AreaColRemoveCells:
                        StartAreaDrawing(area, adding: false);
                        return true;
                    case AreaColEdit:
                        OpenAreaEditor(area);
                        return true;
                    default:
                        PromptEnhanceCompat.DeleteCustomArea(manager, PromptEnhanceCompat.AreaId(area));
                        RefreshModel();
                        return true;
                }
            }
            return false;
        }

        private void BeginOverviewEdit()
        {
            if (overviewTextFieldIndex < 0)
            {
                AnnounceCurrentItem();
                return;
            }
            string current = WidgetCapture.Items[overviewTextFieldIndex].Text ?? "";
            TextFieldSpec spec = TextFieldSpec.MultiLineUnrestricted("RimWorldAccess.TextInput.LabelDefault");
            overviewSession.EnterEdit(current, spec,
                "RimWorldAccess.Compat.PromptEnhance.OverviewLabel".Translate(),
                ApplyOverviewValue, ReAnnounceOverviewRow, true);
        }

        private void ApplyOverviewValue(string value)
        {
            if (overviewTextFieldIndex >= 0)
            {
                WidgetCapture.RequestTextOverride(overviewTextFieldIndex, value ?? "");
                overviewOverrideActive = true;
            }
        }

        private void ReAnnounceOverviewRow()
        {
            if (overviewOverrideActive)
            {
                WidgetCapture.ClearTextOverride();
                overviewOverrideActive = false;
            }
            AnnounceCurrentItem();
        }

        private void BeginSummaryEdit()
        {
            if (summaryTextFieldIndex < 0)
            {
                AnnounceCurrentItem();
                return;
            }
            string current = WidgetCapture.Items[summaryTextFieldIndex].Text ?? "";
            TextFieldSpec spec = TextFieldSpec.MultiLineUnrestricted("RimWorldAccess.TextInput.LabelDefault");
            summarySession.EnterEdit(current, spec,
                Translator.Translate("RTE_Snapshot_AISummary").Resolve(),
                ApplySummaryValue, ReAnnounceHistoryRow, true);
        }

        private void ApplySummaryValue(string value)
        {
            if (summaryTextFieldIndex >= 0)
            {
                WidgetCapture.RequestTextOverride(summaryTextFieldIndex, value ?? "");
                summaryOverrideActive = true;
            }
        }

        private void ReAnnounceHistoryRow()
        {
            if (summaryOverrideActive)
            {
                WidgetCapture.ClearTextOverride();
                summaryOverrideActive = false;
            }
            AnnounceCurrentItem();
        }

        /// <summary>Vehicle A: constructs the mod's own AreaDrawingDesignator and calls its public StartDrawing exactly as the Draw/Remove Cells buttons do. The keyboard cell-paint step is descoped (see PromptEnhanceCompat's class remarks); the designator is selected, ready for the mod's existing mouse-drag flow.</summary>
        private static void StartAreaDrawing(object area, bool adding)
        {
            System.Type designatorType = HarmonyLib.AccessTools.TypeByName("RimTalkHealthEnhance.UI.AreaDrawingDesignator");
            if (designatorType == null)
            {
                return;
            }
            object designator = System.Activator.CreateInstance(designatorType);
            System.Reflection.MethodInfo startDrawing = HarmonyLib.AccessTools.Method(designatorType, "StartDrawing");
            startDrawing?.Invoke(designator, new object[] { area, adding });
        }

        private static void OpenAreaEditor(object area)
        {
            System.Type dialogType = HarmonyLib.AccessTools.TypeByName("RimTalkHealthEnhance.UI.AreaEditorDialog");
            if (dialogType == null)
            {
                return;
            }
            object instance = System.Activator.CreateInstance(dialogType, area, false);
            Find.WindowStack.Add((Window)instance);
        }

        protected override bool CaptureWindowButtons => false;

        protected override bool IncludeCapturedExtrasRegion => true;

        protected override string ComposeOpenAnnouncement()
        {
            return "RimWorldAccess.Compat.PromptEnhance.Opened".Translate();
        }
    }
}
