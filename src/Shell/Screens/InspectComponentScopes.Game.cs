using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Modal mirror scopes for the five windowless building-component/zone drill-in menus:
    /// Door, Forbid, Temp, Refuelable, PlantSelection.
    /// The mirror gate is <c>State.IsActive &amp;&amp; !InfoCardState.IsActive</c>, never bare
    /// (<see cref="InspectComponentScopeMirror"/>): a bare gate re-floats one of these
    /// windowless scopes above the real, window-attached InfoCardScope.
    /// The five are mutually exclusive — each opens from one row-activate (or, for
    /// PlantSelection, the equivalent gizmo path) and closes fully on Escape before another
    /// can open — so push order among them is ARBITRARY.
    /// </summary>
    public sealed class DoorControlScope : ScreenScope
    {
        private const int HoldOpenRow = 0;
        private const int DetailedStatusRow = 1;

        private bool announcedOpen;

        public DoorControlScope()
        {
            // A discoverable shortcut to what Enter-on-row-1 reaches; the row teaches the
            // chord itself through ChordDisplay below.
            Claim("doorControl.detailedStatus", delegate { DoorControlState.AnnounceDetailedStatus(); });
            Claim(SharedMenuGrammar.Cancel, OnCancel);
        }

        public override string Name
        {
            get { return "door-control"; }
        }

        /// <summary>Windowless overlay: this scope owns Escape itself unconditionally — there is no typeahead here to gate on.</summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        protected override bool IncludeActionsRegion
        {
            get { return false; }
        }

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        protected override string ContentRegionName(int region)
        {
            return InspectionCategoryLocalizer.Localize("Door Controls");
        }

        protected override int ContentItemCount(int region)
        {
            return 2;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            if (index == HoldOpenRow)
            {
                d.Label = "CommandToggleDoorHoldOpen".Translate().ToString();
                d.Role = ElementRole.Checkbox;
                d.Check = DoorControlState.HoldOpen ? CheckState.Checked : CheckState.Unchecked;
            }
            else
            {
                d.Label = "RimWorldAccess.Building.Door.DetailedStatusRowLabel".Translate().ToString();
                d.Role = ElementRole.Button;
                d.Hotkey = ChordDisplay("doorControl.detailedStatus");
            }
            return d;
        }

        protected override void ActivateContentItem(int region, int index)
        {
            if (index == HoldOpenRow)
            {
                if (!DoorControlState.ToggleHoldOpen())
                {
                    return; // reflection failure already spoke its own error
                }
                bool nowHeld = DoorControlState.HoldOpen;
                (nowHeld ? SoundDefOf.Checkbox_TurnedOn : SoundDefOf.Checkbox_TurnedOff).PlayOneShotOnCamera();
                var d = new ElementDescription { Check = nowHeld ? CheckState.Checked : CheckState.Unchecked };
                TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(d, TranslatedShellVocabulary.Instance));
            }
            else
            {
                DoorControlState.AnnounceDetailedStatus();
            }
        }

        public override void OnPush()
        {
            base.OnPush();
            announcedOpen = false;
            GizmoRingRequest.Provider = FocusedGizmoForRing;
        }

        public override void OnPop()
        {
            base.OnPop();
            GizmoRingRequest.Provider = null;
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

        /// <summary>
        /// The hold-open toggle Building_Door.GetGizmos draws for a player-owned, non-AlwaysOpen
        /// door (decompiled :572-583). Matched by GizmoRectRegistry's GroupsWith fallback, never
        /// by reference — a fresh Command_Toggle draws every frame. DetailedStatus has no gizmo.
        /// </summary>
        private Gizmo FocusedGizmoForRing()
        {
            RefreshModel();
            ListModel region = Model.CurrentRegion;
            if (region == null || region.Index != HoldOpenRow)
            {
                return null;
            }
            return new Command_Toggle
            {
                defaultLabel = "CommandToggleDoorHoldOpen".Translate(),
                hotKey = KeyBindingDefOf.Misc3,
                icon = TexCommand.HoldOpen,
            };
        }

        private static void OnCancel(KeyEventSnapshot e)
        {
            ShellFrameStamps.MarkCancelConsumed();
            DoorControlState.Close();
            InspectionReturnHelper.AnnounceParentOrFallback("RimWorldAccess.Inspection.Patch.ClosedDoorControls".Translate());
        }
    }

    /// <summary>The forbid-control drill-in — identical shape to <see cref="DoorControlScope"/>.</summary>
    public sealed class ForbidControlScope : ScreenScope
    {
        private const int ForbiddenRow = 0;
        private const int DetailedStatusRow = 1;

        private bool announcedOpen;

        public ForbidControlScope()
        {
            Claim("forbidControl.detailedStatus", delegate { ForbidControlState.AnnounceDetailedStatus(); });
            Claim(SharedMenuGrammar.Cancel, OnCancel);
        }

        public override string Name
        {
            get { return "forbid-control"; }
        }

        public override bool OwnsCancel
        {
            get { return true; }
        }

        protected override bool IncludeActionsRegion
        {
            get { return false; }
        }

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        protected override string ContentRegionName(int region)
        {
            return InspectionCategoryLocalizer.Localize("Forbid Controls");
        }

        protected override int ContentItemCount(int region)
        {
            return 2;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            if (index == ForbiddenRow)
            {
                d.Label = "RimWorldAccess.Building.Forbid.CheckboxLabel".Translate().ToString();
                d.Role = ElementRole.Checkbox;
                d.Check = ForbidControlState.Forbidden ? CheckState.Checked : CheckState.Unchecked;
            }
            else
            {
                d.Label = "RimWorldAccess.Building.Forbid.DetailedStatusRowLabel".Translate().ToString();
                d.Role = ElementRole.Button;
                d.Hotkey = ChordDisplay("forbidControl.detailedStatus");
            }
            return d;
        }

        protected override void ActivateContentItem(int region, int index)
        {
            if (index == ForbiddenRow)
            {
                ForbidControlState.ToggleForbidden();
                bool nowForbidden = ForbidControlState.Forbidden;
                (nowForbidden ? SoundDefOf.Checkbox_TurnedOn : SoundDefOf.Checkbox_TurnedOff).PlayOneShotOnCamera();
                var d = new ElementDescription { Check = nowForbidden ? CheckState.Checked : CheckState.Unchecked };
                TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(d, TranslatedShellVocabulary.Instance));
            }
            else
            {
                ForbidControlState.AnnounceDetailedStatus();
            }
        }

        public override void OnPush()
        {
            base.OnPush();
            announcedOpen = false;
            GizmoRingRequest.Provider = FocusedGizmoForRing;
        }

        public override void OnPop()
        {
            base.OnPop();
            GizmoRingRequest.Provider = null;
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

        /// <summary>
        /// CompForbiddable.CompGetGizmosExtra's toggle (decompiled :105-142); icon and label are
        /// constant across forbidden state, so GizmoRectRegistry's GroupsWith fallback matches
        /// it. DetailedStatus has no gizmo.
        /// </summary>
        private Gizmo FocusedGizmoForRing()
        {
            RefreshModel();
            ListModel region = Model.CurrentRegion;
            if (region == null || region.Index != ForbiddenRow)
            {
                return null;
            }
            return new Command_Toggle
            {
                hotKey = KeyBindingDefOf.Command_ItemForbid,
                icon = TexCommand.ForbidOff,
                defaultLabel = "CommandAllow".TranslateWithBackup("DesignatorUnforbid"),
            };
        }

        private static void OnCancel(KeyEventSnapshot e)
        {
            ShellFrameStamps.MarkCancelConsumed();
            ForbidControlState.Close();
            InspectionReturnHelper.AnnounceParentOrFallback("RimWorldAccess.Inspection.Patch.ClosedForbidControls".Translate());
        }
    }

    /// <summary>
    /// The temperature-control drill-in. Up/Down move between the two rows; Left/Right (small
    /// step) and Shift+Left/Right (large step) drive <see cref="TempControlMenuState"/>'s
    /// vehicle-A gizmo invocations, which carry vanilla's own DragSlider/Tick_Tiny cues.
    /// </summary>
    public sealed class TempControlScope : ScreenScope
    {
        private const int TargetTempRow = 0;
        private const int ResetRow = 1;

        private bool announcedOpen;

        public TempControlScope()
        {
            Claim("tempControl.increaseLarge", delegate { TempControlMenuState.IncreaseTemperatureLarge(); });
            Claim("tempControl.decreaseLarge", delegate { TempControlMenuState.DecreaseTemperatureLarge(); });
            // A discoverable shortcut to what Enter-on-row-1 reaches.
            Claim("tempControl.reset", delegate { TempControlMenuState.ResetTemperature(); });
            Claim(SharedMenuGrammar.Cancel, OnCancel);
        }

        public override string Name
        {
            get { return "temp-control"; }
        }

        public override bool OwnsCancel
        {
            get { return true; }
        }

        protected override bool IncludeActionsRegion
        {
            get { return false; }
        }

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        protected override string ContentRegionName(int region)
        {
            return InspectionCategoryLocalizer.Localize("Temperature");
        }

        protected override int ContentItemCount(int region)
        {
            return 2;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            if (index == TargetTempRow)
            {
                float temp = TempControlMenuState.TargetTemperature;
                d.Label = "TargetTemperature".Translate().ToString();
                d.Role = ElementRole.Stepper;
                d.Value = MenuHelper.FormatTemperature(temp, "F0");
                d.AtMinimum = temp <= -273.15f;
                d.AtMaximum = temp >= 1000f;
            }
            else
            {
                d.Label = "CommandResetTemp".Translate().ToString();
                d.Role = ElementRole.Button;
                d.Hotkey = ChordDisplay("tempControl.reset");
            }
            return d;
        }

        /// <summary>Small-step Left/Right on the stepper row. TempControlMenuState's own post-adjust speech announces; the composer must not echo it a second time.</summary>
        protected override bool CanAdjustContentItem(int region, int index)
        {
            return index == TargetTempRow;
        }

        protected override void AdjustContentItem(int region, int index, int direction)
        {
            if (index != TargetTempRow)
            {
                return;
            }
            if (direction > 0)
            {
                TempControlMenuState.IncreaseTemperatureSmall();
            }
            else
            {
                TempControlMenuState.DecreaseTemperatureSmall();
            }
        }

        protected override void ActivateContentItem(int region, int index)
        {
            if (index == TargetTempRow)
            {
                AnnounceCurrentItem();
            }
            else
            {
                TempControlMenuState.ResetTemperature();
            }
        }

        public override void OnPush()
        {
            base.OnPush();
            announcedOpen = false;
            GizmoRingRequest.Provider = FocusedGizmoForRing;
        }

        public override void OnPop()
        {
            base.OnPop();
            GizmoRingRequest.Provider = null;
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

        /// <summary>
        /// Only Reset has one fixed gizmo behind it: CompTempControl's reset Command_Action
        /// (decompiled CompGetGizmosExtra :83-94), matched by GroupsWith. TargetTemp's
        /// Left/Right span four offset gizmos, so no single gizmo backs it and it rings
        /// nothing — an accepted edge.
        /// </summary>
        private Gizmo FocusedGizmoForRing()
        {
            RefreshModel();
            ListModel region = Model.CurrentRegion;
            if (region == null || region.Index != ResetRow)
            {
                return null;
            }
            return new Command_Action
            {
                hotKey = KeyBindingDefOf.Misc1,
                icon = ContentFinder<Texture2D>.Get("UI/Commands/TempReset"),
                defaultLabel = "CommandResetTemp".Translate(),
            };
        }

        private static void OnCancel(KeyEventSnapshot e)
        {
            ShellFrameStamps.MarkCancelConsumed();
            TempControlMenuState.Close();
            InspectionReturnHelper.AnnounceParentOrFallback("RimWorldAccess.Inspection.Patch.ClosedTemperatureControl".Translate());
        }
    }

    /// <summary>
    /// The refuelable-component drill-in. The row SET is fixed at Open() from the building's
    /// CompProperties_Refuelable flags, which never change at runtime; each row's label and
    /// check state are read live at announce time, never cached.
    /// </summary>
    public sealed class RefuelableScope : ScreenScope
    {
        private bool announcedOpen;

        public RefuelableScope()
        {
            Claim(SharedMenuGrammar.Cancel, OnCancel);
        }

        public override string Name
        {
            get { return "refuelable"; }
        }

        public override bool OwnsCancel
        {
            get { return true; }
        }

        protected override bool IncludeActionsRegion
        {
            get { return false; }
        }

        /// <summary>Few rows, but the search affordance stays available.</summary>
        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        /// <summary>This dynamic category has no InspectionCategoryLocalizer entry; the name is derived live, as RefuelableAdapter.CategoryDisplayName does.</summary>
        protected override string ContentRegionName(int region)
        {
            return RefuelableComponentState.RegionName;
        }

        protected override int ContentItemCount(int region)
        {
            return RefuelableComponentState.OptionCount;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            switch (RefuelableComponentState.KindOf(index))
            {
                case RefuelableComponentState.OptionKind.ToggleAutoRefuel:
                    d.Label = "RimWorldAccess.Building.Refuel.AutoRefuelCheckboxLabel".Translate().ToString();
                    d.Role = ElementRole.Checkbox;
                    d.Check = RefuelableComponentState.AllowAutoRefuel ? CheckState.Checked : CheckState.Unchecked;
                    break;
                default:
                    d.Label = RefuelableComponentState.LabelOf(index);
                    d.Role = ElementRole.Button;
                    break;
            }
            return d;
        }

        protected override bool CanAdjustContentItem(int region, int index)
        {
            return RefuelableComponentState.IsAdjustTargetFuelOption(index);
        }

        /// <summary>Left/Right on the target-fuel row open the same picker as Enter; direction is irrelevant.</summary>
        protected override void AdjustContentItem(int region, int index, int direction)
        {
            if (RefuelableComponentState.IsAdjustTargetFuelOption(index))
            {
                RefuelableComponentState.OpenTargetFuelDialog();
            }
        }

        protected override void ActivateContentItem(int region, int index)
        {
            if (RefuelableComponentState.KindOf(index) == RefuelableComponentState.OptionKind.ToggleAutoRefuel)
            {
                // Vehicle A: the Command_Toggle's own ProcessInput plays vanilla's
                // turnOnSound/turnOffSound, so no sound belongs here.
                RefuelableComponentState.ToggleAutoRefuel();
                bool nowOn = RefuelableComponentState.AllowAutoRefuel;
                var d = new ElementDescription { Check = nowOn ? CheckState.Checked : CheckState.Unchecked };
                TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(d, TranslatedShellVocabulary.Instance));
                return;
            }
            RefuelableComponentState.ExecuteSelected(index);
        }

        public override void OnPush()
        {
            base.OnPush();
            announcedOpen = false;
            // No row rings: the single-building case draws Gizmo_SetFuelLevel, a plain Gizmo
            // whose reference-equality GroupsWith no freshly-built proxy can match. Accepted edge.
            GizmoRingRequest.Provider = null;
        }

        public override void OnPop()
        {
            base.OnPop();
            GizmoRingRequest.Provider = null;
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

        private static void OnCancel(KeyEventSnapshot e)
        {
            ShellFrameStamps.MarkCancelConsumed();
            RefuelableComponentState.Close();
            InspectionReturnHelper.AnnounceParentOrFallback("RimWorldAccess.Inspection.Patch.ClosedFuelSettings".Translate());
        }
    }

    /// <summary>
    /// The plant-selection drill-in. Enter settles an active search (clears it and re-reads
    /// the row) and a second Enter activates; Escape clears an active search before closing.
    /// </summary>
    public sealed class PlantSelectionScope : ScreenScope
    {
        private bool announcedOpen;

        public PlantSelectionScope()
        {
            Claim("plantSelection.infoCard", delegate
            {
                int index = CurrentIndex();
                if (index >= 0)
                {
                    PlantSelectionMenuState.OpenInfoCard(index);
                }
            });
            Claim(SharedMenuGrammar.Cancel, OnCancel);
        }

        public override string Name
        {
            get { return "plant-selection"; }
        }

        public override bool OwnsCancel
        {
            get { return true; }
        }

        protected override bool IncludeActionsRegion
        {
            get { return false; }
        }

        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        protected override string ContentRegionName(int region)
        {
            return InspectionCategoryLocalizer.Localize("Plant Selection");
        }

        protected override int ContentItemCount(int region)
        {
            return PlantSelectionMenuState.Count;
        }

        /// <summary>
        /// Name and detail must stay split across Label/Extras, never baked into one string:
        /// typeahead searches Label only, and it re-describes every row on every keystroke, so
        /// detail text in Label would both pollute matches and run DetailedInfoOf's live
        /// roof-check for every plant per character. The composer's join speaks the same line.
        /// </summary>
        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            d.Label = PlantSelectionMenuState.DisplayTextOf(index);
            d.Extras = PlantSelectionMenuState.DetailedInfoOf(index);
            return d;
        }

        protected override void ActivateContentItem(int region, int index)
        {
            PlantSelectionMenuState.ConfirmSelection(index);
        }

        private int CurrentIndex()
        {
            RefreshModel();
            ListModel region = Model.CurrentRegion;
            return region == null ? -1 : region.Index;
        }

        public override void OnPush()
        {
            base.OnPush();
            announcedOpen = false;
            RefreshModel();
            ListModel region = Model.CurrentRegion;
            if (region != null && !region.IsEmpty)
            {
                int initial = PlantSelectionMenuState.InitialIndex;
                if (initial >= 0 && initial < region.Count)
                {
                    region.MoveTo(initial);
                }
            }
            // No row rings: PlantSelectionMenuState keeps no reference to the
            // Command_SetPlantToGrow that opened it, and no per-plant row has a gizmo.
            GizmoRingRequest.Provider = null;
        }

        public override void OnPop()
        {
            base.OnPop();
            GizmoRingRequest.Provider = null;
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

        private static void OnCancel(KeyEventSnapshot e)
        {
            ShellFrameStamps.MarkCancelConsumed();
            PlantSelectionMenuState.Close();
            InspectionReturnHelper.AnnounceParentOrFallback("RimWorldAccess.Inspection.Storage.PlantSelectionClosed".Translate());
        }
    }

    /// <summary>
    /// Reconciles the five drill-in scopes. Each gate is
    /// <c>State.IsActive &amp;&amp; !InfoCardState.IsActive</c>, never bare: a bare per-pass
    /// Push re-floats one of these windowless scopes above the window-attached InfoCardScope
    /// whenever the card is open over a drill-in. The unmatched-key swallow is unaffected —
    /// all five states are IAAMA members, so InfoCardScope's modal masking covers it.
    /// Push order among the five is ARBITRARY; they are mutually exclusive
    /// (<see cref="DoorControlScope"/>).
    /// </summary>
    internal static class InspectComponentScopeMirror
    {
        private static readonly TempControlScope tempControl = new TempControlScope();
        private static readonly RefuelableScope refuelable = new RefuelableScope();
        private static readonly DoorControlScope doorControl = new DoorControlScope();
        private static readonly ForbidControlScope forbidControl = new ForbidControlScope();
        private static readonly PlantSelectionScope plantSelection = new PlantSelectionScope();

        public static void Reconcile()
        {
            bool infoCard = InfoCardState.IsActive;
            ReconcileOne(tempControl, TempControlMenuState.IsActive && !infoCard);
            ReconcileOne(refuelable, RefuelableComponentState.IsActive && !infoCard);
            ReconcileOne(doorControl, DoorControlState.IsActive && !infoCard);
            ReconcileOne(forbidControl, ForbidControlState.IsActive && !infoCard);
            ReconcileOne(plantSelection, PlantSelectionMenuState.IsActive && !infoCard);
        }

        private static void ReconcileOne(FocusScope scope, bool live)
        {
            if (live)
            {
                FocusStack.Push(scope);
            }
            else
            {
                FocusStack.Pop(scope);
            }
        }
    }
}
