using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Keyboard navigation for CompRefuelable. Options are built dynamically from
    /// Props flags so buildings only expose what actually applies (e.g. a mortar's
    /// reinforced barrel shows only the view option; a fueled smelter shows all three).
    ///
    /// Map-controls migration: cursor/typeahead arithmetic (selectedIndex, the private
    /// TypeaheadSearchHelper, SelectNext/Previous/JumpToFirst/Last,
    /// ProcessTypeaheadCharacter/Backspace, AnnounceCurrentOption/AnnounceWithSearch) moved to
    /// RefuelableScope (ScreenScope + the shared typeahead engine); this state shrinks to the option
    /// DATA model, the mutation methods, and the vanilla- mirroring status text builders.
    /// </summary>
    public static class RefuelableComponentState
    {
        public enum OptionKind
        {
            ViewStatus,
            ToggleAutoRefuel,
            AdjustTargetFuel,
        }

        private class Option
        {
            public OptionKind Kind;
        }

        private static CompRefuelable refuelable = null;
        private static Building building = null;
        private static bool isActive = false;
        private static List<Option> options = new List<Option>();

        /// <summary>
        /// False while our own <see cref="OpenTargetFuelDialog"/> has a real
        /// Dialog_Slider open (<see cref="SliderDialogState.IsActive"/>) so
        /// <see cref="RimWorldAccess.Shell.InspectComponentScopeMirror"/> pops this
        /// menu's scope off the focus stack for the dialog's duration — the same stand-down
        /// <see cref="RimWorldAccess.Shell.GizmoScopeMirror"/> already applies for gizmo-opened sliders
        /// (VF's refuel-from-inventory, turret quota). Without this, the mirror would keep re-floating
        /// this menu's scope above SliderDialogScope every reconcile pass, starving the real dialog of
        /// keyboard input. LOAD-BEARING: do not simplify.
        /// </summary>
        public static bool IsActive => isActive && !SliderDialogState.IsActive;

        /// <summary>Live region name for RefuelableScope, matching RefuelableAdapter.CategoryDisplayName's own derivation (no InspectionCategoryLocalizer entry exists for this dynamic category).</summary>
        public static string RegionName => (refuelable?.Props?.FuelGizmoLabel ?? "Fuel".TranslateSimple()).CapitalizeFirst();

        public static void Open(Building targetBuilding)
        {
            if (!GuardHelper.RequireBuilding(targetBuilding)) return;

            CompRefuelable comp = targetBuilding.TryGetComp<CompRefuelable>();
            if (comp == null)
            {
                TolkHelper.Speak("RimWorldAccess.Building.Refuel.NoFuelComponent".Loc());
                return;
            }

            MapNavigationState.SuppressMapNavigation = true;
            building = targetBuilding;
            refuelable = comp;
            isActive = true;

            BuildOptions();
            TolkHelper.SpeakData(BuildVanillaFuelStatus());
        }

        public static void Close()
        {
            MapNavigationState.SuppressMapNavigation = false;
            refuelable = null;
            building = null;
            isActive = false;
            options.Clear();
        }

        private static void BuildOptions()
        {
            options.Clear();
            options.Add(new Option { Kind = OptionKind.ViewStatus });

            if (refuelable.Props.showAllowAutoRefuelToggle)
                options.Add(new Option { Kind = OptionKind.ToggleAutoRefuel });

            if (refuelable.Props.targetFuelLevelConfigurable)
                options.Add(new Option { Kind = OptionKind.AdjustTargetFuel });
        }

        // ------------------------------------------------------------------
        // Row data for RefuelableScope (rebuilt fresh via RefreshContent -> BuildOptions).
        // ------------------------------------------------------------------

        public static int OptionCount => options.Count;

        public static OptionKind KindOf(int index) => options[index].Kind;

        /// <summary>Live label for the Button-kind rows (ViewStatus/AdjustTargetFuel); read fresh, never cached.</summary>
        public static string LabelOf(int index)
        {
            switch (options[index].Kind)
            {
                case OptionKind.ViewStatus:
                    return "RimWorldAccess.Building.Refuel.OptionViewStatus".Translate();
                case OptionKind.AdjustTargetFuel:
                    return TargetFuelLabel();
                default:
                    return "";
            }
        }

        /// <summary>Live checkbox state for the ToggleAutoRefuel row.</summary>
        public static bool AllowAutoRefuel => refuelable != null && refuelable.allowAutoRefuel;

        private static string TargetFuelLabel()
            => "RimWorldAccess.Building.Refuel.TargetFuelLevelLabel".Translate(
                refuelable.TargetFuelLevel.ToStringDecimalIfSmall(),
                refuelable.Props.fuelCapacity.ToStringDecimalIfSmall());

        public static bool IsAdjustTargetFuelOption(int index)
            => index >= 0 && index < options.Count && options[index].Kind == OptionKind.AdjustTargetFuel;

        public static void ExecuteSelected(int index)
        {
            if (refuelable == null || building == null || index < 0 || index >= options.Count) return;

            switch (options[index].Kind)
            {
                case OptionKind.ViewStatus:
                    AnnounceDetailedStatus();
                    break;
                case OptionKind.ToggleAutoRefuel:
                    ToggleAutoRefuel();
                    break;
                case OptionKind.AdjustTargetFuel:
                    OpenTargetFuelDialog();
                    break;
            }
        }

        /// <summary>
        /// MUTATION vehicle A, the same template <see cref="OpenTargetFuelDialog"/>
        /// establishes: CompRefuelable.CompGetGizmosExtra only yields this
        /// Command_Toggle when Find.Selector.SelectedObjects.Count != 1 (the
        /// single-selection case this menu actually runs in instead draws
        /// Gizmo_SetFuelLevel, which carries no toggle at all) — so this state
        /// can never reach vanilla's own toggle instance through enumeration.
        /// Command_Toggle.ProcessInput has no selection-count gate of its own,
        /// so constructing the identical isActive/toggleAction wiring vanilla's
        /// comp would build and driving its real ProcessInput rides the same
        /// vanilla vehicle A pattern — including its own turnOnSound/turnOffSound
        /// pick (Command_Toggle.CurActivateSound, decompiled Verse/Command_Toggle.cs:21-31,
        /// reads isActive() BEFORE toggleAction flips it), which now plays
        /// correctly instead of the old unconditional Checkbox_TurnedOn.
        /// </summary>
        public static void ToggleAutoRefuel()
        {
            if (refuelable == null) return;
            var gizmo = new Command_Toggle
            {
                isActive = () => refuelable.allowAutoRefuel,
                toggleAction = delegate { refuelable.allowAutoRefuel = !refuelable.allowAutoRefuel; },
            };
            gizmo.ProcessInput(null);
        }

        /// <summary>
        /// MUTATION vehicle A: constructs and drives the real vanilla
        /// Command_SetTargetFuelLevel gizmo directly, rather than stepping
        /// TargetFuelLevel by a hand-picked +/-10%-of-capacity amount.
        /// CompRefuelable.CompGetGizmosExtra only yields this gizmo type when
        /// multiple refuelables are selected together
        /// (Find.Selector.SelectedObjects.Count != 1) — for the single-building
        /// case this state actually runs in, vanilla instead draws
        /// Gizmo_SetFuelLevel, a mouse-drag-only slider gizmo with no keyboard
        /// equivalent and no Dialog_Slider at all (verified:
        /// RimWorld/Gizmo_SetFuelLevel.cs's Target setter writes
        /// refuelable.TargetFuelLevel directly from a drag position). But
        /// Command_SetTargetFuelLevel.ProcessInput itself has no selection-count
        /// gate of its own — only CompGetGizmosExtra's dispatch branch does — so
        /// constructing the command directly and calling its own ProcessInput
        /// rides the exact same vanilla method regardless of what else is
        /// selected, opening the real Dialog_Slider (with the pod-launcher
        /// max-launch-distance readout for free, via the command's own
        /// textGetter) instead of our own hand-rolled stepper.
        /// SliderDialogPatch/SliderDialogState/SliderDialogScope already make
        /// every Dialog_Slider fully keyboard-accessible the moment it's added
        /// to the window stack, so nothing further is needed here; IsActive's
        /// SliderDialogState.IsActive term yields this menu's own scope for the
        /// dialog's duration.
        /// </summary>
        public static void OpenTargetFuelDialog()
        {
            if (refuelable == null || building == null) return;
            var gizmo = new Command_SetTargetFuelLevel { refuelable = refuelable };
            gizmo.ProcessInput(null);
        }

        /// <summary>
        /// Top-line fuel status mirroring CompRefuelable.CompInspectStringExtra — what a
        /// sighted player sees in the inspect panel. Composed via AnnouncementBuilder so
        /// each fragment stays free of trailing punctuation; the builder owns separators.
        /// </summary>
        private static string BuildVanillaFuelStatus()
        {
            if (refuelable.Props.fuelIsMortarBarrel && Find.Storyteller.difficulty.classicMortars)
            {
                var classic = new AnnouncementBuilder();
                classic.Add(building.LabelCap);
                classic.Add("RimWorldAccess.Building.Refuel.ClassicMortarStatus".Translate());
                return classic.Build();
            }

            var b = new AnnouncementBuilder();

            string fuelLabel = refuelable.Props.FuelLabel.CapitalizeFirst();
            string fuelLine = "RimWorldAccess.Building.Refuel.FuelStatusLine".Translate(
                fuelLabel,
                refuelable.Fuel.ToStringDecimalIfSmall(),
                refuelable.Props.fuelCapacity.ToStringDecimalIfSmall());

            if (!refuelable.Props.consumeFuelOnlyWhenUsed && refuelable.HasFuel)
            {
                int numTicks = (int)(refuelable.Fuel / refuelable.Props.fuelConsumptionRate * 60000f);
                fuelLine += "RimWorldAccess.Building.Refuel.RemainingTimeSuffix".Translate(numTicks.ToStringTicksToPeriod());
            }

            b.Add(fuelLine);

            if (!refuelable.HasFuel && !refuelable.Props.outOfFuelMessage.NullOrEmpty())
            {
                b.Add(refuelable.Props.outOfFuelMessage);
            }

            if (refuelable.Props.targetFuelLevelConfigurable)
            {
                b.Add("ConfiguredTargetFuelLevel".Translate(refuelable.TargetFuelLevel.ToStringDecimalIfSmall()));
            }

            return b.Build();
        }

        public static void AnnounceDetailedStatus()
        {
            if (refuelable == null || building == null) return;

            var b = new AnnouncementBuilder();
            b.Add(BuildVanillaFuelStatus());

            if (refuelable.Props.fuelFilter != null && refuelable.Props.fuelFilter.AllowedDefCount == 1)
            {
                var fuelDef = refuelable.Props.fuelFilter.AllowedThingDefs.First();
                b.Add("RimWorldAccess.Building.Refuel.FuelTypeLabel".Translate(fuelDef.label));
            }

            if (refuelable.Props.showAllowAutoRefuelToggle)
            {
                b.Add(refuelable.allowAutoRefuel
                    ? "RimWorldAccess.Building.Refuel.AutoRefuelOn".Translate()
                    : "RimWorldAccess.Building.Refuel.AutoRefuelOff".Translate());
            }

            TolkHelper.SpeakData(b.Build());
        }
    }
}
