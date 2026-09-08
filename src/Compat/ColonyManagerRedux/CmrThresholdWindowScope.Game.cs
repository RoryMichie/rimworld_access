using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using static RimWorldAccess.Shell.CompatText;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Keyboard support for Colony Manager Redux's threshold details window
    /// (<c>ColonyManagerRedux.WindowTriggerThresholdDetails</c>), the window a job's threshold line
    /// opens. The whole window is one vanilla <c>ThingFilterUI</c> panel plus three controls around
    /// it, so this is a <see cref="FilterTreeScopeBase"/> subclass -- the first one attached to a real
    /// window rather than a windowless facade -- and the three controls are its leading rows, in the
    /// order the mod draws them read top to bottom: the operator, the exact count beside it, and the
    /// stockpile strip above them both.
    ///
    /// ROW ORDER DEVIATION, deliberate: the mod draws the filter tree FIRST and the three controls
    /// under it, while the base puts leading rows above the tree. The base's order stands — the three
    /// controls are one short run reachable with Home, and it matches every other filter screen.
    ///
    /// ESCAPE CLOSES THIS WINDOW; ENTER NEVER DOES. The window's own body closes it on ANY Return
    /// KeyDown it sees (Window_TriggerThresholdDetails.cs:168-172), which is not
    /// <c>Window.OnAcceptKeyPressed</c> and so out of <see cref="WindowKeyRouter"/>'s reach, while
    /// Enter here belongs to the focused row. <see cref="CmrThresholdWindowAcceptGuardPatch"/>
    /// therefore masks the keyCode around that body while this scope owns the window — the QA R6
    /// mask/restore, never an <c>Event.current.Use()</c> that would starve this scope's own claims on
    /// one of the two pass orderings. <see cref="OwnsCancel"/> is true, so vanilla's two independent
    /// cancel passes stay blocked and <see cref="OnFilterTreeClose"/> removes the window once,
    /// stamping the cancel frame first so the same Escape cannot walk on and close the manager window
    /// underneath.
    ///
    /// HIT POINTS AND QUALITY RANGES ARE WITHHELD — a known parity gap, not a judgement: the window
    /// draws both sliders, but the shared range sub-editor writes back through
    /// <see cref="RangeEditScope"/>'s fixed caller chain, whose fallback arm targets the policy
    /// window's own filter, null while that screen is closed. Opening it from here would throw on
    /// Enter or write this window's range into a policy the player was not editing.
    /// </summary>
    internal sealed class CmrThresholdWindowScope : FilterTreeScopeBase
    {
        private const int OperatorRow = 0;
        private const int CountRow = 1;
        private const int StockpileRow = 2;
        private const int LeadingRows = 3;

        private readonly Window window;
        private readonly object trigger;
        private readonly CmrRowEditor editor = new CmrRowEditor();

        private CmrThresholdWindowScope(Window window, object trigger)
        {
            this.window = window;
            this.trigger = trigger;

            RegisterPopTeardown(editor.CancelIfActive);
        }

        /// <summary>
        /// Declines (null) when the trigger cannot be read: with no trigger there is no filter,
        /// operator or count, and a modal scope over an empty screen would be worse than none.
        /// </summary>
        public static FocusScope TryCreate(Window window)
        {
            object trigger = CmrCompat.Threshold.TriggerOf(window);
            return trigger == null ? null : new CmrThresholdWindowScope(window, trigger);
        }

        public override string Name
        {
            get { return "cmr-threshold"; }
        }

        protected internal override Window OwnedWindow
        {
            get { return window; }
        }

        /// <summary>The mod's own bare noun for what this window configures; the window itself carries no title.</summary>
        protected override string TreeRegionLabel
        {
            get { return ModText("ColonyManagerRedux.Threshold"); }
        }

        protected override ThingFilterSessionCore.FilterContext BuildFilterContext()
        {
            ThingFilter current = CmrCompat.Threshold.ThresholdFilter(trigger);
            ThingFilter parent = CmrCompat.Threshold.ParentFilter(trigger);
            return new ThingFilterSessionCore.FilterContext
            {
                CurrentFilter = current,
                ParentFilter = parent,
                // The window passes no force-hidden filters of its own
                // (Window_TriggerThresholdDetails.cs:56-61).
                ForceHiddenFilters = null,
                DisplayRoot = RootNode(parent, current),
            };
        }

        /// <summary>
        /// Every text button this window draws is already a row of this screen's content — the
        /// operator button, and the filter panel's ClearAll/AllowAll prefix rows — so capturing them
        /// would build a Buttons region of duplicates, one reading as the operator's bare glyph.
        /// There is no other text button, so nothing is lost.
        /// </summary>
        protected override bool CaptureWindowButtons
        {
            get { return false; }
        }

        /// <summary>See the class remarks: withheld until the shared range sub-editor can route back here.</summary>
        protected override bool ShowHitPointsRange
        {
            get { return false; }
        }

        /// <summary>See the class remarks.</summary>
        protected override bool ShowQualityRange
        {
            get { return false; }
        }

        // The three controls the window draws around the filter tree.

        protected override int SubclassLeadingRowCount
        {
            get { return LeadingRows; }
        }

        protected override ElementDescription DescribeSubclassLeadingRow(int index)
        {
            switch (index)
            {
                case OperatorRow:
                    return DescribeOperatorRow();
                case CountRow:
                    return DescribeCountRow();
                case StockpileRow:
                    return DescribeStockpileRow();
                default:
                    return new ElementDescription();
            }
        }

        protected override void ActivateSubclassLeadingRow(int index)
        {
            switch (index)
            {
                case OperatorRow:
                    OpenPicker(index, OperatorChoices());
                    return;
                case CountRow:
                    BeginCountEdit(index);
                    return;
                case StockpileRow:
                    OpenPicker(index, StockpileChoices());
                    return;
            }
        }

        protected override bool CanAdjustSubclassLeadingRow(int index)
        {
            return CanStep(index, -1) || CanStep(index, 1);
        }

        protected override void AdjustSubclassLeadingRow(int index, int direction)
        {
            if (!CanStep(index, direction))
            {
                // Already at that bound: re-announce in place so the boundary word is heard.
                AnnounceCurrentItem();
                return;
            }
            switch (index)
            {
                case OperatorRow:
                    StepOperator(direction);
                    break;
                case CountRow:
                    StepCount(direction);
                    break;
                case StockpileRow:
                    StepStockpile(direction);
                    break;
                default:
                    return;
            }
            SoundDefOf.DragSlider.PlayOneShotOnCamera();
            RefreshModel();
            AnnounceLeadingRowChange(index);
        }

        // The focus ring for the three controls above: the window draws nothing the capture engines
        // can see for them, but its layout is closed-form (see WindowContentRect).

        private static readonly Func<Window, float> MarginOf =
            AccessTools.MethodDelegate<Func<Window, float>>(AccessTools.PropertyGetter(typeof(Window), "Margin"));

        private const float TitleHeight = 25f;

        protected internal override Rect FocusedContentRect()
        {
            if (!CursorInTreeRegion)
            {
                return default(Rect);
            }
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty)
            {
                return default(Rect);
            }
            if (region.Index >= SubclassLeadingRowCountFor(Model.RegionIndex))
            {
                // A tree row or a filter-action row: ThingFilterTreeSync rings those.
                return default(Rect);
            }

            float margin = MarginOf(window);
            Rect inRect = WindowContentRect(margin);
            int stockpileZoneCount = CmrCompat.Threshold.Stockpiles(trigger).Count;
            int num = Math.Min((int)Math.Ceiling((stockpileZoneCount + 1) / 2.0), 3);
            float num2 = num * 30f;

            // mirrors WindowTriggerThresholdDetails.DoWindowContents' layout (ilspycmd, CMR 1.6)
            Rect val = inRect.ContractedBy(6f);
            val.height -= 2f * margin + num2 + 30f;
            Rect stockpileStrip = new Rect(val.xMin, val.yMax + margin, val.width, num2);
            Rect operatorButton = new Rect(
                val.xMin, stockpileStrip.yMax + margin, (val.width - margin) / 2f, 30f);

            Rect chosen;
            switch (region.Index)
            {
                case OperatorRow:
                    chosen = operatorButton;
                    break;
                case CountRow:
                    chosen = operatorButton;
                    chosen.x = operatorButton.xMax + margin;
                    break;
                case StockpileRow:
                    // The whole strip: the mod draws per-stockpile buttons inside it via its own
                    // StockpileGUI, and this scope's stockpile row is one row.
                    chosen = stockpileStrip;
                    break;
                default:
                    return default(Rect);
            }
            return GuiSpace.ToScreen(chosen);
        }

        /// <summary>
        /// The rect Window.InnerWindowOnGUI hands DoWindowContents, in window-local space: the
        /// window contracted by its own Margin, pushed down further by Margin plus the title bar
        /// when the window carries an optionalTitle (this one does not).
        /// </summary>
        private Rect WindowContentRect(float margin)
        {
            float titleOffset = string.IsNullOrEmpty(window.optionalTitle) ? 0f : margin + TitleHeight;
            return new Rect(
                margin,
                margin + titleOffset,
                window.windowRect.width - margin * 2f,
                window.windowRect.height - margin * 2f - titleOffset);
        }

        /// <summary>
        /// Whether the row can still move that way. All three are single-select value rows, so the
        /// bound words come from the same place the step does: the operator's own supported list, the
        /// count's 0..maximum range, the stockpile strip's cell order.
        /// </summary>
        private bool CanStep(int index, int direction)
        {
            switch (index)
            {
                case OperatorRow:
                    return CanStepAlong(SupportedOpCells(), CmrCompat.Threshold.Op(trigger),
                        direction);
                case CountRow:
                    int count = CmrCompat.Threshold.TargetCount(trigger);
                    return direction < 0
                        ? count > 0
                        : count < CmrCompat.Threshold.MaxUpperThreshold(trigger);
                case StockpileRow:
                    return CanStepAlong(StockpileCells(), CmrCompat.Threshold.Stockpile(trigger),
                        direction);
                default:
                    return false;
            }
        }

        private static bool CanStepAlong(IList<object> cells, object current, int direction)
        {
            int index = IndexOf(cells, current);
            if (index < 0)
            {
                return cells.Count > 0;
            }
            return direction < 0 ? index > 0 : index < cells.Count - 1;
        }

        private static int IndexOf(IList<object> cells, object value)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                if (cells[i] == null ? value == null : cells[i].Equals(value))
                {
                    return i;
                }
            }
            return -1;
        }

        private static object Stepped(IList<object> cells, object current, int direction)
        {
            if (cells.Count == 0)
            {
                return null;
            }
            int index = IndexOf(cells, current);
            return index < 0 ? cells[0] : cells[Mathf.Clamp(index + direction, 0, cells.Count - 1)];
        }

        // Operator: the window's own button, whose click opens a float menu of every operator the
        // job accepts (Window_TriggerThresholdDetails.cs:79-119).

        /// <summary>
        /// The operator's own NAME, the same key its float-menu option carries, rather than the
        /// button's glyph: the glyphs render the same fact, and the mod localizes the names in every
        /// language it ships. The tail is the operator's own tip, with the current count folded in.
        /// </summary>
        private ElementDescription DescribeOperatorRow()
        {
            object op = CmrCompat.Threshold.Op(trigger);
            return new ElementDescription
            {
                Label = "RimWorldAccess.Cmr.Threshold.OperatorRow".Translate(),
                Role = ElementRole.ComboBox,
                Value = OperatorLabel(op),
                Extras = OperatorTip(op),
                AtMinimum = !CanStep(OperatorRow, -1),
                AtMaximum = !CanStep(OperatorRow, 1),
            };
        }

        private static string OperatorLabel(object op)
        {
            string key = CmrCompat.Threshold.OpLabelKey(op);
            return key.Length == 0 ? "" : ModText(key);
        }

        /// <summary>The window puts this same tip on the operator button and on the count field beside it (Window_TriggerThresholdDetails.cs:133/164).</summary>
        private string OperatorTip(object op)
        {
            string key = CmrCompat.Threshold.OpLabelKey(op);
            return key.Length == 0
                ? ""
                : Flatten(ModArgs(key + ".Tip", CmrCompat.Threshold.TargetCount(trigger)));
        }

        private List<object> SupportedOpCells()
        {
            return CmrCompat.Threshold.SupportedOps(trigger);
        }

        private List<CmrPickerChoice> OperatorChoices()
        {
            List<object> supported = SupportedOpCells();
            var choices = new List<CmrPickerChoice>(supported.Count);
            for (int i = 0; i < supported.Count; i++)
            {
                object op = supported[i];
                choices.Add(new CmrPickerChoice(OperatorLabel(op),
                    () => CmrCompat.Threshold.SetOp(trigger, op)));
            }
            return choices;
        }

        private void StepOperator(int direction)
        {
            CmrCompat.Threshold.SetOp(trigger,
                Stepped(SupportedOpCells(), CmrCompat.Threshold.Op(trigger), direction));
        }

        // Exact count: the window's own text field (Window_TriggerThresholdDetails.cs:141-165).

        /// <summary>
        /// The plain number, not the trigger's own "&lt; 500" label: the operator is a row of its own
        /// here, so folding it into the count as well would say it twice.
        /// </summary>
        private ElementDescription DescribeCountRow()
        {
            return new ElementDescription
            {
                Label = "RimWorldAccess.Cmr.Threshold.TargetCountRow".Translate(),
                Role = ElementRole.Stepper,
                Value = CmrCompat.Threshold.TargetCount(trigger).ToString(),
                Extras = OperatorTip(CmrCompat.Threshold.Op(trigger)),
                AtMinimum = !CanStep(CountRow, -1),
                AtMaximum = !CanStep(CountRow, 1),
                // Enter opens numeric entry, so the row is not inert for the shared
                // Enter-again-to-proceed seam.
                EntersEditOnAccept = true,
            };
        }

        /// <summary>One keyboard step along the same 0..MaxUpperThreshold range the job's own slider spans.</summary>
        private void StepCount(int direction)
        {
            int max = CmrCompat.Threshold.MaxUpperThreshold(trigger);
            float stepped = SliderStep.Stepped(CmrCompat.Threshold.TargetCount(trigger), direction,
                0f, max, -1f);
            CmrCompat.Threshold.SetTargetCount(trigger, Mathf.RoundToInt(stepped));
        }

        /// <summary>
        /// Typing is the one thing the field offers that the job's slider cannot: a value above the
        /// current maximum, which the facade's write raises to match. The bound is therefore the
        /// field's own maximum, read fresh at apply time — the field re-syncs, so the number can
        /// move while the session is open.
        /// </summary>
        private void BeginCountEdit(int index)
        {
            editor.BeginCountEdit("RimWorldAccess.Cmr.Threshold.TargetCountRow".Translate(),
                () => new CmrNumericSpec
                {
                    Min = 0,
                    Max = int.MaxValue,
                    Current = () => CmrCompat.Threshold.TargetCount(trigger),
                    Apply = value => CmrCompat.Threshold.SetTargetCount(trigger, value),
                },
                RefreshModel,
                delegate
                {
                    RefreshModel();
                    AnnounceLeadingRowChange(index);
                });
        }

        // Stockpile: the window's own strip (StockpileGUI.DoStockpileSelectors), whose first cell is
        // "any stockpile" and whose remaining cells are the map's stockpile zones.

        private ElementDescription DescribeStockpileRow()
        {
            return new ElementDescription
            {
                Label = "RimWorldAccess.Cmr.Threshold.StockpileRow".Translate(),
                Role = ElementRole.ComboBox,
                Value = StockpileLabel(CmrCompat.Threshold.Stockpile(trigger)),
                AtMinimum = !CanStep(StockpileRow, -1),
                AtMaximum = !CanStep(StockpileRow, 1),
            };
        }

        /// <summary>The strip's cells in its own order: the null cell first, then every stockpile (StockpileGUI.cs:76-103).</summary>
        private List<object> StockpileCells()
        {
            var cells = new List<object> { null };
            List<Zone_Stockpile> zones = CmrCompat.Threshold.Stockpiles(trigger);
            for (int i = 0; i < zones.Count; i++)
            {
                cells.Add(zones[i]);
            }
            return cells;
        }

        /// <summary>The cell's own label: the zone's, or the mod's own word for the null cell (StockpileGUI.cs:168).</summary>
        private static string StockpileLabel(object cell)
        {
            var zone = cell as Zone_Stockpile;
            return zone != null ? zone.label : ModText("ColonyManagerRedux.AnyStockpile");
        }

        private List<CmrPickerChoice> StockpileChoices()
        {
            List<object> cells = StockpileCells();
            var choices = new List<CmrPickerChoice>(cells.Count);
            for (int i = 0; i < cells.Count; i++)
            {
                var zone = cells[i] as Zone_Stockpile;
                choices.Add(new CmrPickerChoice(StockpileLabel(zone),
                    () => CmrCompat.Threshold.SetStockpile(trigger, zone)));
            }
            return choices;
        }

        private void StepStockpile(int direction)
        {
            CmrCompat.Threshold.SetStockpile(trigger,
                Stepped(StockpileCells(), CmrCompat.Threshold.Stockpile(trigger), direction)
                    as Zone_Stockpile);
        }

        // Shared row behavior.

        private void OpenPicker(int index, List<CmrPickerChoice> choices)
        {
            ElementDescription current = DescribeSubclassLeadingRow(index);
            bool opened = CmrRowEditor.OpenPicker(choices, current.Value, current.Label, delegate
            {
                RefreshModel();
                AnnounceLeadingRowChange(index);
            });
            if (!opened)
            {
                AnnounceCurrentItem();
            }
        }

        /// <summary>The state-change echo: the row's identity was spoken on focus, so only its new value re-announces.</summary>
        private void AnnounceLeadingRowChange(int index)
        {
            TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(
                DescribeSubclassLeadingRow(index), TranslatedShellVocabulary.Instance));
        }

        // Escape: this window's close key (see the class remarks).

        /// <summary>
        /// Unconditional, a superset of the base's typeahead-only ownership: both of vanilla's
        /// independent cancel passes must stay blocked so the close below is the only one, whichever
        /// order the window pass and the dispatcher run in.
        /// </summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        protected override void OnFilterTreeClose()
        {
            // Blocks vanilla's cancel handling for the REST of this frame: once the window below is
            // topmost, Notify_PressedCancel would close the manager window with the same press.
            ShellFrameStamps.MarkCancelConsumed();
            // The window's own close statement (Window_TriggerThresholdDetails.cs:171).
            Find.WindowStack.TryRemove(window);
        }

        // Lifecycle.

        /// <summary>
        /// A fresh scope per window, so the tree is built once. The filter pair cannot change shape
        /// while this window has the keyboard: only the job's "allow any item" toggle replaces the
        /// parent filter, and that lives on the manager screen underneath.
        /// </summary>
        public override void OnPush()
        {
            base.OnPush();
            SetTreeRoot(BuildCategoryTreeRoot());
            RefreshModel();
            Model.CurrentRegion?.MoveFirst();
        }

        public override void OnFocus()
        {
            base.OnFocus();
            AnnounceCurrentItem();
        }

        /// <summary>The category/thing-def tree under a synthetic, non-navigable root -- the leading rows are rows, not tree nodes (see <see cref="FilterTreeScopeBase"/>'s header).</summary>
        private InspectionTreeItem BuildCategoryTreeRoot()
        {
            var root = new InspectionTreeItem
            {
                Label = "Root",
                IndentLevel = -1,
                IsExpanded = true,
                IsExpandable = false,
            };
            ThingFilterSessionCore.FilterContext ctx = ContextForBuild(TreeRegionIndex);
            if (ctx.DisplayRoot != null)
            {
                ThingFilterSessionCore.AddCategoryChildren(ctx, ctx.DisplayRoot, root, 0, isRoot: true);
            }
            return root;
        }

        /// <summary>Vanilla's own root resolution for the panel this window draws (decompiled Verse/ThingFilterUI.cs:48-53).</summary>
        private static TreeNode_ThingCategory RootNode(ThingFilter parentFilter, ThingFilter currentFilter)
        {
            if (parentFilter != null)
            {
                return parentFilter.DisplayRootCategory;
            }
            return currentFilter != null ? currentFilter.RootNode : null;
        }

        // Text shaping through the mod's own Keyed data, so every language rides the mod's
        // translation rather than a copy of ours.

    }

    /// <summary>
    /// Keeps the threshold details window's own Return handler away from an Enter this scope owns.
    /// The window closes itself on any Return KeyDown its <c>DoWindowContents</c> body sees
    /// (Window_TriggerThresholdDetails.cs:168-172), bypassing <c>Window.OnAcceptKeyPressed</c> and
    /// so out of <see cref="WindowKeyRouter"/>'s reach; that body can run BEFORE the dispatcher pass
    /// and shares <see cref="Event.current"/> with it.
    ///
    /// The mask fires in every state, because Escape closes this window and so the window's own
    /// Return poll is always wrong while the scope is attached. Masking rather than <c>Use()</c>ing
    /// leaves a pristine KeyDown for the dispatcher to claim exactly once.
    ///
    /// The gate is ATTACHMENT, deliberately not <see cref="FocusStack.Top"/>: the value rows open
    /// their pickers as windowless float menus, which put an overlay scope above this one, and a
    /// top-only gate would lift the mask for exactly the Enter that picks an operator. Attachment is
    /// also immune to the two pass orderings, and this window type has one registered factory, so
    /// "has an attached scope" is precisely "this scope is driving it".
    ///
    /// MANUAL PATCH: the window type is mod-internal and reflection-resolved, so there is no
    /// <c>Type</c> for an attribute-based <c>[HarmonyPatch]</c>;
    /// <see cref="RimWorldAccess.CmrModule"/> calls <see cref="Install"/> once, behind the same
    /// Ready gate as the scope registration.
    /// </summary>
    internal static class CmrThresholdWindowAcceptGuardPatch
    {
        /// <summary>No-op with a logged reason when <c>DoWindowContents</c> cannot be resolved: silently reopening the Return leak would be worse than saying why.</summary>
        public static void Install(Harmony harmony, Type windowType)
        {
            if (harmony == null || windowType == null)
            {
                return;
            }
            try
            {
                MethodInfo target = AccessTools.DeclaredMethod(windowType, "DoWindowContents");
                if (target == null)
                {
                    ModLogger.Error("Colony Manager Redux compat: could not resolve "
                        + windowType.FullName
                        + ".DoWindowContents; declining the threshold window's Return guard.");
                    return;
                }
                harmony.Patch(target,
                    prefix: new HarmonyMethod(typeof(CmrThresholdWindowAcceptGuardPatch), nameof(Prefix)),
                    postfix: new HarmonyMethod(typeof(CmrThresholdWindowAcceptGuardPatch), nameof(Postfix)));
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("CMR threshold window Return guard", ex);
            }
        }

        public static void Prefix(object __instance)
        {
            try
            {
                var window = __instance as Window;
                TextFieldRawPollGuard.MaskAcceptPoll(
                    window != null && ScopeForWindow.HasAttachedScope(window));
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("CMR threshold window Return guard", ex);
            }
        }

        public static void Postfix()
        {
            try
            {
                TextFieldRawPollGuard.RestoreAcceptPoll();
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("CMR threshold window Return guard", ex);
            }
        }
    }
}
