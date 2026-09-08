using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Keyboard focus scope for the real vanilla <see cref="Dialog_ManageAreas"/> window,
    /// registered through <see cref="ScopeForWindow"/>. One TABLE REGION — a row per mutable
    /// area, one column per control vanilla draws on that row (color swatch, Expand, Shrink,
    /// Invert, Rename, Copy, Delete) — plus the automatic Buttons region, which keeps vanilla's
    /// own captured Close button and declares New Area (modeled here instead of captured so the
    /// result is spoken; vanilla hides the button at the area cap, this row stays and reports
    /// disabled).
    ///
    /// Every activation is the same vehicle vanilla's own row click runs: Expand/Shrink reflect
    /// into the dialog's private <c>SelectDesignator&lt;T&gt;</c> (escapes the current main tab,
    /// selects the RESOLVED Zone-category designator with the area pre-set, closes this dialog —
    /// DesignatorManagerPatch then routes it into shape placement), Rename and the color swatch
    /// open the real vanilla dialogs, Invert/Delete call the Area's own methods, and Copy mirrors
    /// the row button's inline body. Enter stays this scope's cell activation
    /// (chassis <see cref="ScreenScope.OwnsAccept"/>); Escape stays vanilla's close.
    ///
    /// The focused row's area paints on the map through
    /// <see cref="RimWorldAccess.SelectionPreviewPatch"/>'s per-frame
    /// <see cref="FocusedAreaForDisplay"/> read — the keyboard twin of vanilla's on-hover
    /// MarkForDraw.
    /// </summary>
    public sealed class ManageAreasScope : ScreenScope
    {
        private enum Column
        {
            Color,
            Expand,
            Shrink,
            Invert,
            Rename,
            Copy,
            Delete,
        }

        private const int ColumnCount = 7;

        private static readonly AccessTools.FieldRef<Dialog_ManageAreas, Map> mapField =
            AccessTools.FieldRefAccess<Dialog_ManageAreas, Map>("map");
        private static readonly MethodInfo selectExpandMethod =
            AccessTools.Method(typeof(Dialog_ManageAreas), "SelectDesignator")
                .MakeGenericMethod(typeof(Designator_AreaAllowedExpand));
        private static readonly MethodInfo selectShrinkMethod =
            AccessTools.Method(typeof(Dialog_ManageAreas), "SelectDesignator")
                .MakeGenericMethod(typeof(Designator_AreaAllowedClear));

        private readonly Dialog_ManageAreas dialog;
        private readonly List<Area> areas = new List<Area>();
        private readonly List<ScreenAction> actions = new List<ScreenAction>();
        private bool announcedOpen;

        private const string NewAreaActionId = "manageAreas.newArea";

        public ManageAreasScope(Dialog_ManageAreas dialog)
        {
            this.dialog = dialog;
            Claim(NewAreaActionId, e => CreateNewArea());
        }

        public override string Name
        {
            get { return "manage-areas"; }
        }

        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        private Map Map
        {
            get { return mapField(dialog); }
        }

        // ScreenScope content contract.

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        protected override string ContentRegionName(int region)
        {
            return "RimWorldAccess.Building.AreaMgr.AreasRegionName".Translate().ToString();
        }

        protected override void RefreshContent()
        {
            // Fresh every pass: a rename, copy or delete must show up at once.
            areas.Clear();
            Map map = Map;
            if (map?.areaManager == null)
            {
                return;
            }
            List<Area> allAreas = map.areaManager.AllAreas;
            for (int i = 0; i < allAreas.Count; i++)
            {
                if (allAreas[i].Mutable)
                {
                    areas.Add(allAreas[i]);
                }
            }
        }

        protected override int ContentItemCount(int region)
        {
            return areas.Count;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            if (index >= 0 && index < areas.Count)
            {
                Area area = areas[index];
                d.Label = "RimWorldAccess.Building.AreaMgr.AreaCellCount".Translate(area.Label, area.TrueCount).ToString();
            }
            return d;
        }

        protected override int ContentColumnCount(int region)
        {
            return ColumnCount;
        }

        /// <summary>Column headers are vanilla's own row-button labels; the icon buttons reuse the game's generic verbs. Every cell is a per-row button and says so.</summary>
        protected override TableColumnInfo ContentColumnInfo(int region, int column)
        {
            switch ((Column)column)
            {
                case Column.Color:  return new TableColumnInfo("Color".Translate().ToString(), cellRole: ElementRole.Button);
                case Column.Expand: return new TableColumnInfo("ExpandArea".Translate().ToString(), cellRole: ElementRole.Button);
                case Column.Shrink: return new TableColumnInfo("ShrinkArea".Translate().ToString(), cellRole: ElementRole.Button);
                case Column.Invert: return new TableColumnInfo("InvertArea".Translate().ToString(), cellRole: ElementRole.Button);
                case Column.Rename: return new TableColumnInfo("Rename".Translate().ToString(), cellRole: ElementRole.Button);
                case Column.Copy:   return new TableColumnInfo("Copy".Translate().ToString(), cellRole: ElementRole.Button);
                case Column.Delete: return new TableColumnInfo("Delete".Translate().ToString(), cellRole: ElementRole.Button);
                default:            return null;
            }
        }

        protected override string ContentCellText(int region, int row, int column)
        {
            if (row < 0 || row >= areas.Count)
            {
                return "";
            }
            if ((Column)column == Column.Color)
            {
                return ColorNameHelper.NameForColor(areas[row].Color);
            }
            return "";
        }

        protected override bool ActivateContentCell(int region, int row, int column)
        {
            if (row < 0 || row >= areas.Count)
            {
                return false;
            }
            Area area = areas[row];
            switch ((Column)column)
            {
                case Column.Color:
                    // Vanilla's swatch is clickable only for Area_Allowed (Home's color is fixed).
                    if (area is Area_Allowed allowed)
                    {
                        Find.WindowStack.Add(new Dialog_AllowedAreaColorPicker(allowed));
                    }
                    else
                    {
                        TolkHelper.Speak("RimWorldAccess.Building.AreaMgr.ColorFixed".Loc(), SpeechPriority.High);
                    }
                    return true;

                case Column.Expand:
                    // Shape placement announces its own entry; nothing to speak here.
                    selectExpandMethod.Invoke(dialog, new object[] { area });
                    return true;

                case Column.Shrink:
                    selectShrinkMethod.Invoke(dialog, new object[] { area });
                    return true;

                case Column.Invert:
                    area.Invert();
                    TolkHelper.Speak("RimWorldAccess.Building.AreaMgr.Inverted".Loc(
                        area.Label, area.TrueCount, area.Map.Area));
                    return true;

                case Column.Rename:
                    Find.WindowStack.Add(new Dialog_RenameArea(area));
                    TolkHelper.Speak("RimWorldAccess.Building.AreaMgr.RenamePrompt".Loc(area.Label));
                    return true;

                case Column.Copy:
                    CopyArea(area);
                    return true;

                case Column.Delete:
                    string deletedName = area.Label;
                    area.Delete();
                    TolkHelper.Speak("RimWorldAccess.Building.AreaMgr.Deleted".Loc(deletedName));
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>Every column handles its own Enter; a row has no separate default action.</summary>
        protected override void ActivateContentItem(int region, int index)
        {
        }

        // MUTATION-C: mirrors the Copy row button's inline body (Dialog_ManageAreas.DoAreaRow),
        // vanilla cap message included; the copy logic exists nowhere else to invoke.
        private void CopyArea(Area area)
        {
            Map map = Map;
            if (map?.areaManager == null)
            {
                return;
            }
            if (map.areaManager.TryMakeNewAllowed(out Area_Allowed newArea))
            {
                foreach (IntVec3 activeCell in area.ActiveCells)
                {
                    newArea[activeCell] = true;
                }
                TolkHelper.Speak("RimWorldAccess.Building.AreaMgr.CopiedTo".Loc(newArea.Label));
            }
            else
            {
                Messages.Message("MaxAreasReached".Translate(10), MessageTypeDefOf.RejectInput, historical: false);
            }
        }

        /// <summary>
        /// New Area is declared, not captured: the injected click would run vanilla's
        /// TryMakeNewAllowed silently, and vanilla HIDES the button at the cap where this row
        /// stays navigable and reports disabled.
        /// </summary>
        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                bool canMake = Map?.areaManager != null && Map.areaManager.CanMakeNewAllowed();
                actions.Add(new ScreenAction(
                    "NewArea".Translate().ToString(),
                    CreateNewArea,
                    NewAreaActionId,
                    disabled: !canMake,
                    disabledReason: "RimWorldAccess.Building.AreaMgr.MaxAreasReached".Translate().ToString()));
                return actions;
            }
        }

        // MUTATION vehicle B: TryMakeNewAllowed is the gated method vanilla's own New Area button calls.
        private void CreateNewArea()
        {
            Map map = Map;
            if (map?.areaManager == null)
            {
                return;
            }
            if (map.areaManager.TryMakeNewAllowed(out Area_Allowed newArea))
            {
                TolkHelper.Speak("RimWorldAccess.Building.AreaMgr.CreatedNewArea".Loc(newArea.Label));
            }
            else
            {
                Messages.Message("MaxAreasReached".Translate(10), MessageTypeDefOf.RejectInput, historical: false);
            }
        }

        /// <summary>The per-row Expand/Shrink/Invert ButtonTexts and New Area are modeled above; only vanilla's Close survives capture.</summary>
        protected override bool KeepCapturedButton(string rawLabel)
        {
            if (string.IsNullOrEmpty(rawLabel))
            {
                return false;
            }
            return rawLabel != "ExpandArea".Translate()
                && rawLabel != "ShrinkArea".Translate()
                && rawLabel != "InvertArea".Translate()
                && rawLabel != "NewArea".Translate();
        }

        /// <summary>
        /// The area to keep painted on the map while this scope is focused — the keyboard twin of
        /// vanilla's on-hover MarkForDraw. Pure read for SelectionPreviewPatch's per-frame call:
        /// null on the header row and in the Buttons region.
        /// </summary>
        internal Area FocusedAreaForDisplay()
        {
            if (Model.RegionIndex != 0)
            {
                return null;
            }
            TableModel table = Model.CurrentTable;
            if (table == null)
            {
                return null;
            }
            int row = table.Rows.Index - 1;
            return row >= 0 && row < areas.Count ? areas[row] : null;
        }

        public override void OnPush()
        {
            base.OnPush();
            announcedOpen = false;
        }

        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen)
            {
                return;
            }
            announcedOpen = true;
            AnnounceCurrentItem();
        }
    }
}
