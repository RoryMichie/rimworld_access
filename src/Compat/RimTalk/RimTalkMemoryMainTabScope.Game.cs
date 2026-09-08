using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Keyboard support for ExpandMemory's "Mind Stream" main tab,
    /// <c>RimTalk.Memory.UI.MainTabWindow_Memory</c>. A small hand-modeled content surface plus a
    /// large free-riding captured-extras surface: the window draws nothing but real
    /// <c>Widgets</c> calls EXCEPT the memory-card timeline, which is raw
    /// <c>Event.current.mousePosition</c> hit-testing against a cached Y-position table in
    /// DoWindowContents' end-of-frame click handler, never a <c>Widgets.ButtonInvisible</c>. That
    /// timeline is the one surface the captured-extras engine cannot see at all.
    ///
    /// RIDES CAPTURED-EXTRAS FOR FREE, no code here: the pawn-selector button and its FloatMenu,
    /// the "Show All Humanlikes" checkbox, the four colored layer checkboxes, the batch-action
    /// buttons (their labels already carry the live selected-vs-all counts, and their GUI.enabled
    /// state presents generically as a disabled row), the global actions, and the top-bar buttons.
    ///
    /// HAND-MODELED, and why:
    /// <list type="bullet">
    /// <item>Filters region (0): the Type filter is a 3-state exclusive picker a sighted player
    /// reads via <c>GUI.color</c> tint on the active button. Tint carries no state the generic
    /// plain-Button capture would surface, so it is rebuilt as three RadioButton rows with real
    /// <c>Selected</c> state. Plus a read-only Stats row, since those counts are plain top-bar
    /// text with no adjacent widget and captured-extras only sees INTERACTIVE widgets.</item>
    /// <item>Timeline region (1): the virtualized memory-card list. Each card's full
    /// content/importance/activity, hover-tooltip-only for a sighted player, is ALWAYS present as
    /// row content rather than gated behind a tooltip lookup.</item>
    /// <item>DeclaredActions: the four "add memory to this layer" actions, each invoking the mod's
    /// own private <c>ShowCreateMemoryMenu(MemoryLayer)</c> (vehicle A). That FloatMenu opens only
    /// on a mouse RIGHT-click, which <c>DrawColoredCheckbox</c> hides behind
    /// <c>Event.current.button == 1</c> with only a hover tooltip advertising it, and a right-click
    /// has no keyboard analogue -- so these are its keyboard door, reachable from the automatic
    /// Buttons region rather than a new hotkey.</item>
    /// </list>
    ///
    /// SELECTION MODEL. The mod's own <c>selectedMemories</c> backs BOTH single-target selection
    /// AND the batch buttons' "operate on selection, or on everything filtered if none selected"
    /// fallback. Two keyboard actions reproduce the mouse's two gestures without collapsing them:
    /// the Timeline row's default Activate mirrors a PLAIN click (replace the whole selection with
    /// this card), while the dedicated "Selected" column mirrors a Ctrl-click (toggle membership,
    /// preserving the rest). The mouse's rubber-band drag-select and Shift-click range gestures
    /// have no keyboard equivalent: toggling each card through the Selected column reaches the
    /// identical end state, so this is a deliberate simplification, not a silent drop.
    ///
    /// ACCEPT/CANCEL: MainTabWindow overrides neither <c>OnAcceptKeyPressed</c> nor
    /// <c>OnCancelKeyPressed</c>, so <see cref="ScreenScope"/>'s own defaults are correct.
    /// </summary>
    public sealed class RimTalkMemoryMainTabScope : ScreenScope
    {
        private const int FiltersRegion = 0;
        private const int TimelineRegion = 1;

        private const int FilterItemAll = 0;
        private const int FilterItemConversation = 1;
        private const int FilterItemAction = 2;
        private const int FilterItemStats = 3;

        private const int ColInfo = 0;
        private const int ColContent = 1;
        private const int ColImportance = 2;
        private const int ColActivity = 3;
        private const int ColSelected = 4;
        private const int ColPinned = 5;
        private const int ColEdit = 6;

        private readonly Window dialog;

        private readonly List<object> memories = new List<object>(); // boxed MemoryEntry

        public RimTalkMemoryMainTabScope(Window dialog)
        {
            this.dialog = dialog;
        }

        public override string Name
        {
            get { return "rimtalk-memory-mainttab"; }
        }

        protected override int ContentRegionCount
        {
            get { return 2; }
        }

        protected override string ContentRegionName(int region)
        {
            return region == FiltersRegion
                ? "RimWorldAccess.Compat.RimTalk.Memory.FiltersRegionName".Translate()
                : "RimWorldAccess.Compat.RimTalk.Memory.TimelineRegionName".Translate();
        }

        protected override void RefreshContent()
        {
            memories.Clear();
            Pawn pawn = RimTalkMemoryCompat.GetSelectedPawn(dialog);
            object comp = RimTalkMemoryCompat.GetCurrentMemoryComp(dialog);
            if (pawn == null || comp == null)
            {
                return;
            }
            IList filtered = RimTalkMemoryCompat.GetFilteredMemories(dialog);
            if (filtered == null)
            {
                return;
            }
            foreach (object entry in filtered)
            {
                if (entry != null)
                {
                    memories.Add(entry);
                }
            }
        }

        protected override int ContentItemCount(int region)
        {
            return region == FiltersRegion ? 4 : memories.Count;
        }

        protected override int ContentColumnCount(int region)
        {
            return region == TimelineRegion ? 7 : 0;
        }

        protected override TableColumnInfo ContentColumnInfo(int region, int column)
        {
            if (region != TimelineRegion)
            {
                return null;
            }
            switch (column)
            {
                case ColInfo: return new TableColumnInfo("RimWorldAccess.Compat.RimTalk.Memory.ColumnInfo".Translate());
                case ColContent: return new TableColumnInfo("RimWorldAccess.Compat.RimTalk.Memory.ColumnContent".Translate());
                case ColImportance: return new TableColumnInfo("RimWorldAccess.Compat.RimTalk.Memory.ColumnImportance".Translate());
                case ColActivity: return new TableColumnInfo("RimWorldAccess.Compat.RimTalk.Memory.ColumnActivity".Translate());
                case ColSelected: return new TableColumnInfo("RimWorldAccess.Compat.RimTalk.Memory.ColumnSelected".Translate());
                case ColPinned: return new TableColumnInfo("RimWorldAccess.Compat.RimTalk.Memory.ColumnPinned".Translate());
                default: return new TableColumnInfo("RimWorldAccess.Compat.RimTalk.Memory.ColumnEdit".Translate());
            }
        }

        protected override string ContentCellText(int region, int row, int column)
        {
            if (region != TimelineRegion || row < 0 || row >= memories.Count)
            {
                return "";
            }
            object entry = memories[row];
            switch (column)
            {
                case ColInfo: return InfoLine(entry);
                case ColContent: return RimTalkMemoryCompat.MemContent(entry) ?? "";
                case ColImportance: return RimTalkMemoryCompat.MemImportance(entry).ToString("F2");
                case ColActivity: return RimTalkMemoryCompat.MemActivity(entry).ToString("F2");
                case ColSelected: return RimTalkMemoryCompat.IsSelected(dialog, entry)
                    ? "RimWorldAccess.Compat.RimTalk.Memory.Selected".Translate()
                    : "RimWorldAccess.Compat.RimTalk.Memory.NotSelected".Translate();
                case ColPinned: return RimTalkMemoryCompat.MemIsPinned(entry)
                    ? "RimWorldAccess.Compat.RimTalk.Memory.Pinned".Translate()
                    : "RimWorldAccess.Compat.RimTalk.Memory.NotPinned".Translate();
                default: return "RimWorldAccess.Compat.RimTalk.Memory.EditAction".Translate();
            }
        }

        /// <summary>Mirrors DrawMemoryCard's own header line format ("[Layer] Type · Age" plus "with X" when related).</summary>
        private static string InfoLine(object entry)
        {
            string relatedPawn = RimTalkMemoryCompat.MemRelatedPawnName(entry);
            return string.IsNullOrEmpty(relatedPawn)
                ? "RimWorldAccess.Compat.RimTalk.Memory.InfoLine".Translate(
                    RimTalkMemoryCompat.MemLayerName(entry), RimTalkMemoryCompat.MemTypeName(entry), RimTalkMemoryCompat.MemAgeString(entry))
                : "RimWorldAccess.Compat.RimTalk.Memory.InfoLineWithPawn".Translate(
                    RimTalkMemoryCompat.MemLayerName(entry), RimTalkMemoryCompat.MemTypeName(entry), RimTalkMemoryCompat.MemAgeString(entry), relatedPawn);
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            if (region == FiltersRegion)
            {
                DescribeFilterRow(d, index);
            }
            else
            {
                d.Label = ContentCellText(TimelineRegion, index, ColInfo);
            }
            return d;
        }

        private void DescribeFilterRow(ElementDescription d, int index)
        {
            if (index == FilterItemStats)
            {
                d.Role = ElementRole.None;
                d.ReadOnly = true;
                d.Label = "RimWorldAccess.Compat.RimTalk.Memory.StatsLabel".Translate();
                Pawn pawn = RimTalkMemoryCompat.GetSelectedPawn(dialog);
                object comp = RimTalkMemoryCompat.GetCurrentMemoryComp(dialog);
                if (pawn == null)
                {
                    d.Value = "RimWorldAccess.Compat.RimTalk.Memory.NoPawnSelected".Translate();
                }
                else if (comp == null)
                {
                    d.Value = "RimWorldAccess.Compat.RimTalk.Memory.NoMemoryComponent".Translate();
                }
                else
                {
                    d.Value = "RimWorldAccess.Compat.RimTalk.Memory.StatsValue".Translate(
                        RimTalkMemoryCompat.GetActiveMemories(comp)?.Count ?? 0,
                        RimTalkMemoryCompat.GetSituationalMemories(comp)?.Count ?? 0,
                        RimTalkMemoryCompat.GetEventLogMemories(comp)?.Count ?? 0,
                        RimTalkMemoryCompat.GetArchiveMemories(comp)?.Count ?? 0);
                }
                return;
            }

            d.Role = ElementRole.RadioButton;
            object currentFilter = RimTalkMemoryCompat.GetFilterType(dialog);
            switch (index)
            {
                case FilterItemAll:
                    d.Label = "RimWorldAccess.Compat.RimTalk.Memory.FilterAll".Translate();
                    d.Selected = currentFilter == null;
                    break;
                case FilterItemConversation:
                    d.Label = "RimWorldAccess.Compat.RimTalk.Memory.FilterConversation".Translate();
                    d.Selected = Equals(currentFilter, RimTalkMemoryCompat.GetMemoryTypeConversation());
                    break;
                default:
                    d.Label = "RimWorldAccess.Compat.RimTalk.Memory.FilterAction".Translate();
                    d.Selected = Equals(currentFilter, RimTalkMemoryCompat.GetMemoryTypeAction());
                    break;
            }
        }

        protected override void ActivateContentItem(int region, int index)
        {
            if (region == FiltersRegion)
            {
                ActivateFilterRow(index);
                return;
            }
            if (index < 0 || index >= memories.Count)
            {
                return;
            }
            RimTalkMemoryCompat.ReplaceSelectionWith(dialog, memories[index]);
            AnnounceCurrentItem();
        }

        private void ActivateFilterRow(int index)
        {
            if (index == FilterItemStats)
            {
                AnnounceCurrentItem();
                return;
            }
            object value = index == FilterItemAll ? null
                : index == FilterItemConversation ? RimTalkMemoryCompat.GetMemoryTypeConversation()
                : RimTalkMemoryCompat.GetMemoryTypeAction();
            RimTalkMemoryCompat.SetFilterType(dialog, value);
            RefreshModel();
            AnnounceCurrentItem();
        }

        protected override bool ActivateContentCell(int region, int row, int column)
        {
            if (region != TimelineRegion || row < 0 || row >= memories.Count)
            {
                return false;
            }
            object entry = memories[row];
            switch (column)
            {
                case ColSelected:
                    RimTalkMemoryCompat.ToggleSelection(dialog, entry);
                    RefreshModel();
                    AnnounceCurrentItem();
                    return true;
                case ColPinned:
                {
                    object comp = RimTalkMemoryCompat.GetCurrentMemoryComp(dialog);
                    if (comp != null)
                    {
                        RimTalkMemoryCompat.PinMemory(comp, RimTalkMemoryCompat.MemId(entry), !RimTalkMemoryCompat.MemIsPinned(entry));
                        RefreshModel();
                        AnnounceCurrentItem();
                    }
                    return true;
                }
                case ColEdit:
                {
                    object comp = RimTalkMemoryCompat.GetCurrentMemoryComp(dialog);
                    if (comp != null)
                    {
                        RimTalkMemoryCompat.OpenEditMemory(entry, comp);
                    }
                    return true;
                }
                default:
                    return false;
            }
        }

        // Focus ring. The timeline is virtualized, so a card's place on screen lives only in
        // DrawTimeline's own y/height caches; the card's pin button (the one 24px square it
        // draws, at the card's own +8 offset) both confirms the card was drawn this pass and
        // carries the scrolled group's translation and visible band, which the pass this runs
        // after has already closed. A card scrolled out draws no button, and no ring is right.

        private const float CardButtonSize = 24f;
        private const float CardButtonInset = 8f;

        protected internal override Rect FocusedContentRect()
        {
            ListModel region = Model.CurrentRegion;
            if (region == null || region.Index < 0)
            {
                return default(Rect);
            }
            return Model.RegionIndex == FiltersRegion
                ? FilterRowRect(region.Index)
                : Model.RegionIndex == TimelineRegion ? CardRect(region.Index) : default(Rect);
        }

        private Rect FilterRowRect(int index)
        {
            if (index == FilterItemStats)
            {
                return default(Rect);
            }
            string label = RimTalkMemoryCompat.FilterButtonRawLabel(index);
            IReadOnlyList<CapturedWidget> pass = WidgetCapture.Items;
            for (int i = 0; i < pass.Count; i++)
            {
                if (pass[i].Kind == WidgetKind.Button && pass[i].Label == label)
                {
                    return pass[i].VisibleScreenRect;
                }
            }
            return default(Rect);
        }

        private Rect CardRect(int index)
        {
            if (index >= memories.Count)
            {
                return default(Rect);
            }
            IList cached = RimTalkMemoryCompat.GetCachedMemories(dialog);
            IList<float> yPositions = RimTalkMemoryCompat.GetCachedCardYPositions(dialog);
            IList<float> heights = RimTalkMemoryCompat.GetCachedCardHeights(dialog);
            int cardIndex = cached?.IndexOf(memories[index]) ?? -1;
            if (cardIndex < 0 || yPositions == null || heights == null
                || cardIndex >= yPositions.Count || cardIndex >= heights.Count)
            {
                return default(Rect);
            }
            float cardY = yPositions[cardIndex];
            IReadOnlyList<CapturedWidget> pass = WidgetCapture.Items;
            for (int i = 0; i < pass.Count; i++)
            {
                CapturedWidget widget = pass[i];
                if (widget.Kind != WidgetKind.Button
                    || Mathf.Abs(widget.Rect.height - CardButtonSize) > 0.5f
                    || Mathf.Abs(widget.Rect.y - (cardY + CardButtonInset)) > 0.5f)
                {
                    continue;
                }
                return GuiSpace.VisibleScreenRectFrom(widget.Rect, widget.ScreenRect, widget.Clip,
                    new Rect(0f, cardY, widget.Clip.VisibleRect.width, heights[cardIndex]));
            }
            return default(Rect);
        }

        // Declared actions: the four right-click-only "create memory here" gestures.

        private readonly List<ScreenAction> actions = new List<ScreenAction>();

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                actions.Add(new ScreenAction(
                    PawnSelectorLabel(), ActivatePawnSelector, "rimTalkMemory.pawnSelector"));
                actions.Add(new ScreenAction(
                    "RimWorldAccess.Compat.RimTalk.Memory.AddToActive".Translate(),
                    delegate { RimTalkMemoryCompat.ShowCreateMemoryMenu(dialog, "Active"); },
                    "rimTalkMemory.addToActive"));
                actions.Add(new ScreenAction(
                    "RimWorldAccess.Compat.RimTalk.Memory.AddToSituational".Translate(),
                    delegate { RimTalkMemoryCompat.ShowCreateMemoryMenu(dialog, "Situational"); },
                    "rimTalkMemory.addToSituational"));
                actions.Add(new ScreenAction(
                    "RimWorldAccess.Compat.RimTalk.Memory.AddToEventLog".Translate(),
                    delegate { RimTalkMemoryCompat.ShowCreateMemoryMenu(dialog, "EventLog"); },
                    "rimTalkMemory.addToEventLog"));
                actions.Add(new ScreenAction(
                    "RimWorldAccess.Compat.RimTalk.Memory.AddToArchive".Translate(),
                    delegate { RimTalkMemoryCompat.ShowCreateMemoryMenu(dialog, "Archive"); },
                    "rimTalkMemory.addToArchive"));
                return actions;
            }
        }

        protected override bool CaptureWindowButtons
        {
            get { return false; }
        }

        protected override bool IncludeCapturedExtrasRegion
        {
            get { return true; }
        }

        // Pawn selector: DrawTopBar's pawn-picker draws Widgets.ButtonText with the pawn's bare
        // LabelShort, so riding captured-extras would announce "Vlad. button. 1 of 32" with no
        // indication of what it is. Excluded here and re-declared with a "Pawn: {0}" label, still
        // activated through the same captured widget so its own FloatMenu keeps working.

        /// <summary>The exact raw text the mod's own button shows this frame -- mirrors DrawTopBar's own string choice so both the exclusion match and the reactivation lookup target the real widget.</summary>
        private string PawnSelectorRawLabel()
        {
            Pawn pawn = RimTalkMemoryCompat.GetSelectedPawn(dialog);
            return pawn != null ? pawn.LabelShort : Translator.Translate("RimTalk_SelectColonist").Resolve();
        }

        private string PawnSelectorLabel()
        {
            Pawn pawn = RimTalkMemoryCompat.GetSelectedPawn(dialog);
            string name = pawn != null ? pawn.LabelShortCap : PawnSelectorRawLabel();
            return "RimWorldAccess.Compat.RimTalk.Memory.PawnSelector".Translate(name);
        }

        /// <summary>Vehicle A: re-invokes the SAME captured button by raw label + ordinal 0, unique because the label is either the current pawn's name or the mod's "select colonist" fallback, never shared with another button on this window.</summary>
        private void ActivatePawnSelector()
        {
            WidgetCapture.RequestActivate(WidgetKind.Button, PawnSelectorRawLabel(), 0);
        }

        protected override bool ExcludeFromCapturedExtras(CapturedWidget widget)
        {
            return widget.Kind == WidgetKind.Button && widget.Label == PawnSelectorRawLabel();
        }

        protected override string ComposeOpenAnnouncement()
        {
            return "RimWorldAccess.Compat.RimTalk.Memory.Opened".Translate();
        }
    }
}
