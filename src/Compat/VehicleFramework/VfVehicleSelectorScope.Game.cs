using System.Collections;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The keyboard focus scope for Vehicle Framework's <c>Vehicles.World.Dialog_VehicleSelector</c>
    /// (the vehicle picker opened by the standalone route planner's world-map button). All
    /// reflection lives in <see cref="VfVehicleSelectorCompat"/>; this scope only reads its typed
    /// methods.
    ///
    /// Two content regions plus the automatic Buttons region:
    /// <list type="number">
    /// <item>Mode (1 item): the current picker mode -- Enter toggles it (MUTATION-C, see
    /// <see cref="VfVehicleSelectorCompat.ToggleMode"/>).</item>
    /// <item>Vehicles (N items): vehicle defs or on-map vehicle pawns, depending on mode. Each row
    /// is a checkbox; Enter toggles membership in the matching selection set (MUTATION-C). A row
    /// incompatible with the already-selected vehicle type (Dialog_VehicleSelector's own
    /// SelectedType != Universal gate) stays navigable, disabled, and says why, rather than taking
    /// vanilla's silent Widgets.Checkbox(disabled: true) treatment.</item>
    /// </list>
    /// The dialog's Start/Cancel buttons are the ONLY real <c>Widgets.ButtonText</c> calls it draws
    /// (every row uses <c>Widgets.Checkbox</c>), so <see cref="CaptureWindowButtons"/> stays at its
    /// ScreenScope default of true with no over-capture risk. Activating Start runs the dialog's
    /// real, self-gating (NoVehiclesSelected) body and closes; Cancel stops the planner and closes.
    /// Both are vehicle A -- no MUTATION-C fallback needed there. Closing after a successful Start
    /// hands the route planner scope its own screen at the next reconcile pass
    /// (<see cref="VfRoutePlannerScopeMirror"/>).
    /// </summary>
    internal sealed class VfVehicleSelectorScope : ScreenScope
    {
        private const int ModeRegion = 0;
        private const int VehiclesRegion = 1;

        private readonly Window dialog;
        private readonly List<object> items = new List<object>();
        private bool announcedOpen;

        public VfVehicleSelectorScope(Window dialog)
        {
            this.dialog = dialog;
        }

        public override string Name => "vf-vehicle-selector";

        /// <summary>Vehicle/def rows are named items worth searching.</summary>
        protected override bool EnableTypeahead => true;

        public override void OnPush()
        {
            base.OnPush();
            VfSelectorRowDrawPatch.Recording = true;
        }

        public override void OnPop()
        {
            VfSelectorRowDrawPatch.Recording = false;
            base.OnPop();
        }

        /// <summary>
        /// <see cref="items"/> holds the very instances the dialog's own availableVehicles/
        /// availableVehicleDefs lists hold (RefreshContent copies straight out of them through
        /// <see cref="VfVehicleSelectorCompat.AvailableVehicles"/>/<c>AvailableVehicleDefs</c>),
        /// which is the same list DrawVehicles indexes to call its row methods -- so the recorded
        /// identity matches by reference. The Mode row rings the dialog's own ClickableLabel rect
        /// (see <see cref="VfSelectorRowDrawPatch"/>).
        /// </summary>
        protected internal override Rect FocusedContentRect()
        {
            ListModel region = Model.CurrentRegion;
            if (region == null || region.Index < 0)
            {
                return default(Rect);
            }
            if (Model.RegionIndex == ModeRegion)
            {
                return VfSelectorRowDrawPatch.ModeRect;
            }
            if (Model.RegionIndex != VehiclesRegion || region.Index >= items.Count)
            {
                return default(Rect);
            }
            return VfSelectorRowDrawPatch.Rows.FindFirst(items[region.Index]);
        }

        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen)
                return;
            announcedOpen = true;
            TolkHelper.SpeakData((string)"RimWorldAccess.Compat.Vf.SelectorModeSwitched".Translate(ModeLabel(), items.Count));
        }

        // ------------------------------------------------------------------
        // ScreenScope content contract.
        // ------------------------------------------------------------------

        protected override int ContentRegionCount => 2;

        protected override string ContentRegionName(int region)
        {
            switch (region)
            {
                case ModeRegion: return "RimWorldAccess.Compat.Vf.SelectorRegionMode".Translate().ToString();
                case VehiclesRegion: return "RimWorldAccess.Compat.Vf.SelectorRegionVehicles".Translate().ToString();
                default: return "";
            }
        }

        protected override int ContentItemCount(int region)
        {
            switch (region)
            {
                case ModeRegion: return 1;
                case VehiclesRegion: return items.Count;
                default: return 0;
            }
        }

        /// <summary>Rebuilds the current mode's item list every cycle: selection sets change on nearly every action, and the mode itself can flip mid-session.</summary>
        protected override void RefreshContent()
        {
            items.Clear();
            if (!VfVehicleSelectorCompat.Ready)
                return;

            IList source = VfVehicleSelectorCompat.ShowVehicleDefs(dialog)
                ? VfVehicleSelectorCompat.AvailableVehicleDefs(dialog)
                : VfVehicleSelectorCompat.AvailableVehicles(dialog);
            if (source == null)
                return;

            foreach (object item in source)
            {
                items.Add(item);
            }
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            switch (region)
            {
                case ModeRegion: return DescribeModeRow();
                case VehiclesRegion: return DescribeVehicleRow(index);
                default: return new ElementDescription();
            }
        }

        protected override void ActivateContentItem(int region, int index)
        {
            switch (region)
            {
                case ModeRegion: ActivateModeRow(); break;
                case VehiclesRegion: ActivateVehicleRow(index); break;
            }
        }

        // ------------------------------------------------------------------
        // Region 0: Mode.
        // ------------------------------------------------------------------

        private ElementDescription DescribeModeRow()
        {
            var d = new ElementDescription();
            d.Role = ElementRole.Button;
            d.Label = ModeLabel();
            return d;
        }

        private void ActivateModeRow()
        {
            VfVehicleSelectorCompat.ToggleMode(dialog);
            SoundDefOf.Click.PlayOneShotOnCamera();
            RefreshModel();
            TolkHelper.SpeakData((string)"RimWorldAccess.Compat.Vf.SelectorModeSwitched".Translate(ModeLabel(), items.Count));
        }

        private string ModeLabel()
        {
            return (VfVehicleSelectorCompat.ShowVehicleDefs(dialog)
                ? "RimWorldAccess.Compat.Vf.SelectorModeDefs"
                : "RimWorldAccess.Compat.Vf.SelectorModeVehicles").Translate().ToString();
        }

        // ------------------------------------------------------------------
        // Region 1: Vehicles.
        // ------------------------------------------------------------------

        private ElementDescription DescribeVehicleRow(int index)
        {
            var d = new ElementDescription();
            object item = ItemAt(index);
            if (item == null)
                return d;

            d.Role = ElementRole.Checkbox;
            d.Label = ItemLabel(item);
            d.Check = IsSelected(item) ? CheckState.Checked : CheckState.Unchecked;

            if (VfVehicleSelectorCompat.IsIncompatibleType(VfVehicleSelectorCompat.CurrentSelectedType(dialog), VfVehicleSelectorCompat.TypeOf(item)))
            {
                d.Disabled = true;
                d.Extras = "RimWorldAccess.Compat.Vf.SelectorIncompatibleType".Translate().ToString();
            }

            return d;
        }

        private void ActivateVehicleRow(int index)
        {
            object item = ItemAt(index);
            if (item == null)
                return;

            if (VfVehicleSelectorCompat.IsIncompatibleType(VfVehicleSelectorCompat.CurrentSelectedType(dialog), VfVehicleSelectorCompat.TypeOf(item)))
            {
                TolkHelper.Speak("RimWorldAccess.Compat.Vf.SelectorIncompatibleType".Loc(), SpeechPriority.High);
                return;
            }

            string label = ItemLabel(item);
            bool nowSelected = item is Pawn vehicle
                ? VfVehicleSelectorCompat.ToggleVehicleSelection(dialog, vehicle)
                : VfVehicleSelectorCompat.ToggleDefSelection(dialog, item);

            SoundDefOf.Click.PlayOneShotOnCamera();
            RefreshModel();
            TolkHelper.SpeakData((string)(nowSelected
                ? "RimWorldAccess.Compat.Vf.SelectorSelected".Translate(label)
                : "RimWorldAccess.Compat.Vf.SelectorDeselected".Translate(label)));
        }

        private bool IsSelected(object item)
        {
            return item is Pawn vehicle
                ? VfVehicleSelectorCompat.IsVehicleSelected(dialog, vehicle)
                : VfVehicleSelectorCompat.IsDefSelected(dialog, item);
        }

        private static string ItemLabel(object item)
        {
            if (item is Pawn pawn)
                return pawn.LabelShortCap;
            if (item is Def def)
                return def.LabelCap.ToString();
            return "";
        }

        private object ItemAt(int index)
        {
            return index >= 0 && index < items.Count ? items[index] : null;
        }
    }
}
