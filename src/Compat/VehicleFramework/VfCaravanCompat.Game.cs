using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// <see cref="ICaravanVehicleTabProvider"/> implementation for Vehicle Framework's Vehicles
    /// tab in <c>Dialog_FormCaravan</c> (and, for the tab-sync/seat-lock surface only, its
    /// <c>Dialog_SplitCaravan</c> counterpart). <see cref="CaravanFormationScope"/> and
    /// <see cref="SplitCaravanScope"/> never touch reflection directly -- every VF-typed value
    /// stays boxed as <c>object</c> here and is read back only through this facade's own methods.
    ///
    /// DEVIATION: <c>DrawCargoCapacity</c>/<c>DrawMoveSpeed</c> in VF's own
    /// <c>TransferableVehicleWidget</c> format cargo capacity with SmashTools' third-party
    /// <c>ToStringMassOffset()</c> extension, which this mod does not reference. Plain vanilla
    /// <c>float.ToStringMass()</c> is the accessible equivalent used instead.
    ///
    /// <see cref="SyncTab"/>'s write into VF's <c>selectedTab</c> static is a no-op with the
    /// "Caravan Item Selection Enhanced" mod active: that mod skips VF's own draw transpilers
    /// entirely, so the field is simply never read in that configuration.
    /// </summary>
    internal sealed class VfCaravanCompat : ICaravanVehicleTabProvider
    {
        // Vehicles.Patch_FormCaravanDialog's own private const TabVehicles = 10, offset high to
        // avoid clashing with the underlying enum. A private const has no runtime field to read.
        private const int VehiclesTabValue = 10;

        private static readonly Type patchType;
        private static readonly Type widgetType;
        private static readonly Type sectionType;
        private static readonly Type caravanFormationType;
        private static readonly Type iCaravanInfoType;
        private static readonly Type dialogAssignSeatsType;
        private static readonly Type caravanHelperType;
        private static readonly Type vehicleAssignmentType;
        private static readonly Type assignedSeatType;
        private static readonly Type vehicleRoleHandlerType;
        private static readonly Type vehiclePawnType;
        private static readonly Type vehicleStatHandlerType;
        private static readonly Type vehicleStatDefOfType;
        private static readonly Type vehicleStatDefType;
        private static readonly Type vehicleCaravanTicksType;
        private static readonly Type vehicleDefType;
        private static readonly Type vehiclePropertiesType;
        private static readonly Type extVehiclesType;

        private static readonly FieldInfo selectedTabField;
        private static readonly FieldInfo vehiclesTransferField;
        private static readonly FieldInfo widgetVehicleSectionField;
        private static readonly FieldInfo widgetPawnsField;
        private static readonly MethodInfo widgetCanCaravanMethod;
        private static readonly FieldInfo sectionTransferablesField;
        private static readonly FieldInfo formationField;
        private static readonly FieldInfo splitterField;
        private static readonly PropertyInfo currentProperty;
        private static readonly MethodInfo notifyTransferablesChangedMethod;
        private static readonly ConstructorInfo dialogAssignSeatsCtor;
        private static readonly FieldInfo assignedSeatsField;
        private static readonly MethodInfo getAssignmentsMethod;
        private static readonly MethodInfo removeAssignmentsMethod;
        private static readonly MethodInfo isAssignedMethod;
        private static readonly MethodInfo getAssignmentMethod;
        private static readonly FieldInfo assignedSeatPawnField;
        private static readonly FieldInfo assignedSeatHandlerField;
        private static readonly FieldInfo handlerVehicleField;
        private static readonly PropertyInfo worldSpeedMultiplierProperty;
        private static readonly MethodInfo getStatValueMethod;
        private static readonly FieldInfo moveSpeedStatField;
        private static readonly FieldInfo cargoCapacityStatField;
        private static readonly MethodInfo moveSpeedToTileSpeedMethod;
        // True when the resolved conversion is the older TicksFromMoveSpeed: that build's own
        // DrawMoveSpeed normalizes with "moveSpeed /= 60" before converting, while the newer
        // MoveSpeedToTileSpeed takes the raw cells-per-second value directly.
        private static readonly bool moveSpeedConversionIsLegacy;
        private static readonly MethodInfo inVehiclePawnMethod;

        // OPTIONAL: only def-only (unspawned) vehicle rows use these, and form-caravan rows are
        // spawned vehicles in practice. Missing members degrade to blank move-speed/cargo cells
        // rather than declining the whole provider.
        private static readonly FieldInfo vehicleDefPropertiesField;
        private static readonly FieldInfo worldSpeedMultiplierPropField;
        private static readonly MethodInfo getStatValueAbstractMethod;
        private static readonly bool abstractStatReady;

        private static readonly bool ready;

        public static bool Ready => ready;

        static VfCaravanCompat()
        {
            var surface = new ReflectionSurface("VfCaravanCompat");

            patchType = surface.Type("Vehicles.Patch_FormCaravanDialog");
            widgetType = surface.Type("Vehicles.World.TransferableVehicleWidget");
            sectionType = widgetType != null ? AccessTools.Inner(widgetType, "Section") : null;
            caravanFormationType = surface.Type("Vehicles.World.CaravanFormation");
            iCaravanInfoType = surface.Type("Vehicles.World.ICaravanInfo");
            dialogAssignSeatsType = surface.Type("Vehicles.World.Dialog_AssignSeats");
            vehicleAssignmentType = surface.Type("Vehicles.VehicleAssignment");
            assignedSeatType = surface.Type("Vehicles.AssignedSeat");
            vehicleRoleHandlerType = surface.Type("Vehicles.VehicleRoleHandler");
            vehiclePawnType = surface.Supplied("Vehicles.VehiclePawn", VfVehiclePawn.VehiclePawnType);
            vehicleStatHandlerType = surface.Type("Vehicles.VehicleStatHandler");
            vehicleStatDefOfType = surface.Type("Vehicles.VehicleStatDefOf");
            vehicleStatDefType = surface.Type("Vehicles.VehicleStatDef");
            // Only the optional def-only-row surface needs these two, so neither gates Ready.
            vehicleDefType = AccessTools.TypeByName("Vehicles.VehicleDef");
            vehiclePropertiesType = AccessTools.TypeByName("Vehicles.VehicleProperties");
            extVehiclesType = surface.Type("Vehicles.Ext_Vehicles");

            // CaravanHelper and VehicleCaravanTicksPerMoveUtility have each moved between the
            // Vehicles and Vehicles.World namespaces across VF versions, in opposite directions;
            // both names are tried so either build resolves.
            caravanHelperType = AccessTools.TypeByName("Vehicles.CaravanHelper")
                ?? AccessTools.TypeByName("Vehicles.World.CaravanHelper");
            vehicleCaravanTicksType = AccessTools.TypeByName("Vehicles.VehicleCaravanTicksPerMoveUtility")
                ?? AccessTools.TypeByName("Vehicles.World.VehicleCaravanTicksPerMoveUtility");

            selectedTabField = surface.Field(patchType, "selectedTab");
            vehiclesTransferField = surface.Field(patchType, "vehiclesTransfer");

            widgetVehicleSectionField = surface.Field(widgetType, "vehicleSection");
            widgetPawnsField = surface.Field(widgetType, "pawns");
            widgetCanCaravanMethod = surface.Method(widgetType, "CanCaravan",
                new[] { typeof(TransferableOneWay), typeof(string).MakeByRefType() });
            sectionTransferablesField = surface.Field(sectionType, "transferables");

            formationField = surface.Field(caravanFormationType, "formation");
            splitterField = surface.Field(caravanFormationType, "splitter");
            currentProperty = surface.Property(caravanFormationType, "Current");
            notifyTransferablesChangedMethod = surface.Method(iCaravanInfoType, "NotifyTransferablesChanged");
            dialogAssignSeatsCtor = dialogAssignSeatsType != null && iCaravanInfoType != null
                ? AccessTools.Constructor(dialogAssignSeatsType,
                    new[] { iCaravanInfoType, typeof(List<TransferableOneWay>), typeof(TransferableOneWay) })
                : null;

            assignedSeatsField = surface.Field(caravanHelperType, "assignedSeats");
            if (vehiclePawnType != null)
            {
                getAssignmentsMethod = surface.Method(vehicleAssignmentType, "GetAssignments", new[] { vehiclePawnType });
                removeAssignmentsMethod = surface.Method(vehicleAssignmentType, "RemoveAssignments", new[] { vehiclePawnType });
            }
            isAssignedMethod = surface.Method(vehicleAssignmentType, "IsAssigned", new[] { typeof(Pawn) });
            getAssignmentMethod = surface.Method(vehicleAssignmentType, "GetAssignment", new[] { typeof(Pawn) });
            assignedSeatPawnField = surface.Field(assignedSeatType, "pawn");
            assignedSeatHandlerField = surface.Field(assignedSeatType, "handler");
            handlerVehicleField = surface.Field(vehicleRoleHandlerType, "vehicle");

            worldSpeedMultiplierProperty = surface.Property(vehiclePawnType, "WorldSpeedMultiplier");
            getStatValueMethod = vehicleStatDefType != null
                ? surface.Method(vehicleStatHandlerType, "GetStatValue", new[] { vehicleStatDefType })
                : null;
            moveSpeedStatField = surface.Field(vehicleStatDefOfType, "MoveSpeed");
            cargoCapacityStatField = surface.Field(vehicleStatDefOfType, "CargoCapacity");
            inVehiclePawnMethod = surface.Method(extVehiclesType, "InVehicle", new[] { typeof(Pawn) });

            if (vehicleCaravanTicksType != null)
            {
                // Newer VF names the moveSpeed-to-ticks-per-tile conversion MoveSpeedToTileSpeed
                // (returns float); older builds name it TicksFromMoveSpeed (returns int). Same
                // semantics -- the caller converts the boxed result with Convert.ToSingle.
                moveSpeedToTileSpeedMethod = AccessTools.Method(vehicleCaravanTicksType, "MoveSpeedToTileSpeed", new[] { typeof(float) });
                if (moveSpeedToTileSpeedMethod == null)
                {
                    moveSpeedToTileSpeedMethod = AccessTools.Method(vehicleCaravanTicksType, "TicksFromMoveSpeed", new[] { typeof(float) });
                    moveSpeedConversionIsLegacy = moveSpeedToTileSpeedMethod != null;
                }
            }

            // Optional def-only-row surface (see the field declarations).
            vehicleDefPropertiesField = vehicleDefType != null ? AccessTools.Field(vehicleDefType, "properties") : null;
            worldSpeedMultiplierPropField = vehiclePropertiesType != null
                ? AccessTools.Field(vehiclePropertiesType, "worldSpeedMultiplier")
                : null;
            getStatValueAbstractMethod = extVehiclesType != null && vehicleDefType != null && vehicleStatDefType != null
                ? AccessTools.Method(extVehiclesType, "GetStatValueAbstract", new[] { vehicleDefType, vehicleStatDefType })
                : null;
            abstractStatReady = vehicleDefPropertiesField != null && worldSpeedMultiplierPropField != null && getStatValueAbstractMethod != null;

            ready = surface.Ready && VfVehiclePawn.Ready && caravanHelperType != null && vehicleCaravanTicksType != null
                && sectionType != null && dialogAssignSeatsCtor != null && moveSpeedToTileSpeedMethod != null;

            if (ready && !abstractStatReady)
            {
                ModLogger.Error("VfCaravanCompat: could not resolve the def-only vehicle stat surface (Ext_Vehicles.GetStatValueAbstract/VehicleDef.properties.worldSpeedMultiplier); unspawned vehicle rows will show blank move-speed/cargo cells.");
            }
        }

        /// <summary>Sets <see cref="CaravanVehicleTab.Provider"/> when the reflection surface resolved. No-op unless Ready.</summary>
        public static void Register()
        {
            if (!ready)
                return;
            CaravanVehicleTab.Provider = new VfCaravanCompat();
        }

        // ------------------------------------------------------------------
        // ICaravanVehicleTabProvider.
        // ------------------------------------------------------------------

        public bool Active
        {
            get
            {
                if (!ready)
                    return false;
                try
                {
                    return formationField.GetValue(null) != null || splitterField.GetValue(null) != null;
                }
                catch (Exception ex)
                {
                    ModLogger.Error($"VfCaravanCompat.Active failed: {ex.Message}");
                    return false;
                }
            }
        }

        public string RegionName => "RimWorldAccess.Compat.Vf.RegionName".Translate().ToString();

        public bool IsVehicle(TransferableOneWay t)
        {
            if (t == null)
                return false;
            return IsVehiclePawn(t.AnyThing) || (vehicleDefType != null && vehicleDefType.IsInstanceOfType(t.ThingDef));
        }

        public List<TransferableOneWay> VehicleRows()
        {
            if (!ready)
                return new List<TransferableOneWay>();
            try
            {
                object widget = vehiclesTransferField.GetValue(null);
                if (widget == null)
                    return new List<TransferableOneWay>();
                object section = widgetVehicleSectionField.GetValue(widget);
                if (section == null)
                    return new List<TransferableOneWay>();
                return sectionTransferablesField.GetValue(section) as List<TransferableOneWay> ?? new List<TransferableOneWay>();
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfCaravanCompat.VehicleRows failed: {ex.Message}");
                return new List<TransferableOneWay>();
            }
        }

        public int ColumnCount => 4;

        public TableColumnInfo ColumnInfo(int column)
        {
            switch (column)
            {
                case 0:
                    return new TableColumnInfo("RimWorldAccess.Compat.Vf.ColSelected".Translate().ToString(), null, false);
                case 1:
                    return new TableColumnInfo(DefLabel(MoveSpeedDef()), "RimWorldAccess.Compat.Vf.TipMoveSpeed".Translate().ToString(), false);
                case 2:
                    return new TableColumnInfo(DefLabel(CargoCapacityDef()), DefDescription(CargoCapacityDef()), false);
                case 3:
                    return new TableColumnInfo(StatDefOf.MarketValue.LabelCap.Resolve(), StatDefOf.MarketValue.description, false);
                default:
                    return new TableColumnInfo();
            }
        }

        public string CellText(TransferableOneWay t, int column)
        {
            if (t == null)
                return "";
            try
            {
                switch (column)
                {
                    case 0:
                        return t.CountToTransfer > 0
                            ? "RimWorldAccess.Compat.Vf.CellSelected".Translate().ToString()
                            : "RimWorldAccess.Compat.Vf.CellNotSelected".Translate().ToString();
                    case 1:
                        return FormatMoveSpeed(t);
                    case 2:
                        return FormatCargoCapacity(t);
                    case 3:
                        return FormatMarketValue(t);
                    default:
                        return "";
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfCaravanCompat.CellText failed: {ex.Message}");
                return "";
            }
        }

        public string CellTip(TransferableOneWay t, int column)
        {
            if (t == null)
                return null;
            try
            {
                // Columns 1-3 carry their explanation on the COLUMN already, so returning it per
                // cell would speak it twice on every cell visit. Only column 0 has genuinely
                // per-row tip content: the card checkbox's disable reason.
                if (column != 0)
                    return null;
                bool canCaravan = TryCanCaravan(t, out string reasonKey);
                return !canCaravan && !string.IsNullOrEmpty(reasonKey) ? reasonKey.Translate().ToString() : null;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfCaravanCompat.CellTip failed: {ex.Message}");
                return null;
            }
        }

        public string DescribeRow(TransferableOneWay t)
        {
            if (t == null)
                return "";
            try
            {
                string label = RowLabel(t);
                bool selected = t.CountToTransfer > 0;
                bool canCaravan = TryCanCaravan(t, out string reasonKey);

                string result = label;
                if (selected)
                    result += ", " + "RimWorldAccess.Compat.Vf.CellSelected".Translate();
                if (!canCaravan && !string.IsNullOrEmpty(reasonKey))
                    result += ", " + reasonKey.Translate();
                return result;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfCaravanCompat.DescribeRow failed: {ex.Message}");
                return "";
            }
        }

        /// <summary>
        /// MUTATION-C: mirrors TransferableVehicleWidget.DrawCard's checkbox branch; inline
        /// IMGUI, no invocable vehicle.
        /// </summary>
        public bool ToggleVehicle(TransferableOneWay t)
        {
            if (!ready || t == null)
                return false;
            try
            {
                bool canCaravan = TryCanCaravan(t, out string reasonKey);
                if (!canCaravan)
                {
                    if (!string.IsNullOrEmpty(reasonKey))
                        TolkHelper.Speak(reasonKey.Loc(), SpeechPriority.High);
                    return false;
                }

                bool currentlySelected = t.CountToTransfer > 0;
                Thing anyThing = t.AnyThing;
                bool isVehiclePawn = IsVehiclePawn(anyThing);

                SoundDefOf.Click.PlayOneShotOnCamera();

                if (!currentlySelected)
                {
                    if (isVehiclePawn)
                    {
                        object current = currentProperty.GetValue(null);
                        List<TransferableOneWay> widgetPawns = GetWidgetPawns();
                        object dialog = dialogAssignSeatsCtor.Invoke(new object[] { current, widgetPawns, t });
                        Find.WindowStack.Add((Window)dialog);
                        // VfAssignSeatsScope announces the seat dialog's own open.
                        return true;
                    }
                    else
                    {
                        t.ForceTo(t.GetMaximumToTransfer());
                        TolkHelper.Speak("RimWorldAccess.Compat.Vf.VehicleSelected".Loc(RowLabel(t)));
                    }
                }
                else
                {
                    t.ForceTo(0);
                    if (isVehiclePawn)
                    {
                        object assigner = assignedSeatsField.GetValue(null);
                        if (assigner != null)
                        {
                            List<TransferableOneWay> widgetPawns = GetWidgetPawns();
                            if (getAssignmentsMethod.Invoke(assigner, new object[] { anyThing }) is System.Collections.IEnumerable seats)
                            {
                                foreach (object seat in seats)
                                {
                                    Pawn seatPawn = assignedSeatPawnField.GetValue(seat) as Pawn;
                                    if (seatPawn == null)
                                        continue;
                                    TransferableOneWay pawnTransferable = widgetPawns.FirstOrDefault(pt => pt.AnyThing == seatPawn);
                                    bool inVehicle = (bool)inVehiclePawnMethod.Invoke(null, new object[] { seatPawn });
                                    if (pawnTransferable != null && !inVehicle)
                                        pawnTransferable.ForceTo(0);
                                }
                            }
                            List<Pawn> aboard = VfVehiclePawn.AllPawnsAboard(anyThing);
                            if (aboard != null)
                            {
                                foreach (Pawn aboardPawn in aboard)
                                {
                                    TransferableOneWay pawnTransferable = widgetPawns.FirstOrDefault(pt => pt.AnyThing == aboardPawn);
                                    pawnTransferable?.ForceTo(0);
                                }
                            }
                            removeAssignmentsMethod.Invoke(assigner, new object[] { anyThing });
                        }
                        object currentInfo = currentProperty.GetValue(null);
                        if (currentInfo != null)
                            notifyTransferablesChangedMethod.Invoke(currentInfo, null);
                    }
                    TolkHelper.Speak("RimWorldAccess.Compat.Vf.VehicleDeselected".Loc(RowLabel(t)));
                }
                return false;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfCaravanCompat.ToggleVehicle failed: {ex.Message}");
                return false;
            }
        }

        public bool IsPawnSeatLocked(Pawn pawn, out string note)
        {
            note = null;
            if (!ready || pawn == null)
                return false;
            try
            {
                object assigner = assignedSeatsField.GetValue(null);
                if (assigner == null)
                    return false;

                bool isAssigned = (bool)isAssignedMethod.Invoke(assigner, new object[] { pawn });
                if (isAssigned)
                {
                    object seat = getAssignmentMethod.Invoke(assigner, new object[] { pawn });
                    object handler = seat != null ? assignedSeatHandlerField.GetValue(seat) : null;
                    object vehicle = handler != null ? handlerVehicleField.GetValue(handler) : null;
                    string vehicleLabel = vehicle is Pawn vehiclePawn ? vehiclePawn.LabelCap.ToString() : "";
                    note = "RimWorldAccess.Compat.Vf.PawnSeatLocked".Translate(vehicleLabel);
                    return true;
                }

                bool inVehicle = (bool)inVehiclePawnMethod.Invoke(null, new object[] { pawn });
                if (inVehicle)
                {
                    note = "RimWorldAccess.Compat.Vf.PawnAboardLocked".Translate();
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfCaravanCompat.IsPawnSeatLocked failed: {ex.Message}");
                return false;
            }
        }

        public void SyncTab(bool vehiclesRegion, int vanillaTab)
        {
            if (!ready)
                return;
            try
            {
                // MUTATION-C: mirrors CaravanFormationScope.SyncGameTab's own vanilla tabField
                // write -- a display-only "which tab is shown" field, not game state; VF
                // exposes no method to set it, only this static field.
                selectedTabField.SetValue(null, vehiclesRegion ? VehiclesTabValue : vanillaTab);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfCaravanCompat.SyncTab failed: {ex.Message}");
            }
        }

        // ------------------------------------------------------------------
        // Helpers.
        // ------------------------------------------------------------------

        private static bool IsVehiclePawn(Thing thing)
        {
            return thing != null && vehiclePawnType.IsInstanceOfType(thing);
        }

        private static string RowLabel(TransferableOneWay t)
        {
            if (t.AnyThing != null)
                return t.AnyThing.LabelCap.ToString();
            return t.ThingDef != null ? t.ThingDef.LabelCap.ToString() : "";
        }

        private List<TransferableOneWay> GetWidgetPawns()
        {
            try
            {
                object widget = vehiclesTransferField.GetValue(null);
                return widget != null ? widgetPawnsField.GetValue(widget) as List<TransferableOneWay> ?? new List<TransferableOneWay>() : new List<TransferableOneWay>();
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfCaravanCompat.GetWidgetPawns failed: {ex.Message}");
                return new List<TransferableOneWay>();
            }
        }

        /// <summary>Vehicle A: the live widget's own CanCaravan(transferable, out reasonKey), exactly as the card calls it. A widget not yet built this session does not block the toggle.</summary>
        private bool TryCanCaravan(TransferableOneWay t, out string reasonKey)
        {
            reasonKey = null;
            try
            {
                object widget = vehiclesTransferField.GetValue(null);
                if (widget == null)
                    return true;
                object[] args = { t, null };
                bool result = (bool)widgetCanCaravanMethod.Invoke(widget, args);
                reasonKey = args[1] as string;
                return result;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfCaravanCompat.TryCanCaravan failed: {ex.Message}");
                return true;
            }
        }

        private static Def MoveSpeedDef()
        {
            return moveSpeedStatField.GetValue(null) as Def;
        }

        private static Def CargoCapacityDef()
        {
            return cargoCapacityStatField.GetValue(null) as Def;
        }

        private static string DefLabel(Def def)
        {
            return def != null ? def.LabelCap.Resolve() : "";
        }

        private static string DefDescription(Def def)
        {
            return def?.description;
        }

        /// <summary>Mirrors DrawMoveSpeed's own formula verbatim (spawned: statHandler.GetStatValue * WorldSpeedMultiplier; def-only: GetStatValueAbstract * properties.worldSpeedMultiplier), then the same ticks-per-tile-to-tiles-per-day conversion and "{0:0.#} TilesPerDay" formatting.</summary>
        private string FormatMoveSpeed(TransferableOneWay t)
        {
            float moveSpeed = ComputeStat(t, moveSpeedStatField, applyWorldSpeedMultiplier: true);
            float tilesPerDay = 0f;
            if (moveSpeed > 0f)
            {
                if (moveSpeedConversionIsLegacy)
                {
                    // The legacy DrawMoveSpeed's own "moveSpeed /= 60" normalize step ahead of
                    // TicksFromMoveSpeed (see the field's remarks).
                    moveSpeed /= 60f;
                }
                // The conversion method returns float in newer VF builds and int in older ones.
                float ticksPerTile = Convert.ToSingle(moveSpeedToTileSpeedMethod.Invoke(null, new object[] { moveSpeed }));
                if (ticksPerTile > 0f)
                    tilesPerDay = GenDate.TicksPerDay / ticksPerTile;
            }
            return $"{tilesPerDay:0.#} " + "TilesPerDay".Translate();
        }

        /// <summary>Mirrors DrawCargoCapacity's own value read (see class remarks for the ToStringMassOffset -> ToStringMass substitution).</summary>
        private string FormatCargoCapacity(TransferableOneWay t)
        {
            float cargoCapacity = ComputeStat(t, cargoCapacityStatField, applyWorldSpeedMultiplier: false);
            return cargoCapacity.ToStringMass();
        }

        /// <summary>
        /// Reads one vehicle stat (move speed or cargo capacity) the way VF's own
        /// DrawMoveSpeed/DrawCargoCapacity do: spawned vehicles read
        /// statHandler.GetStatValue(statDef); def-only rows read
        /// GetStatValueAbstract(vehicleDef, statDef). <paramref name="applyWorldSpeedMultiplier"/>
        /// is true only for the move-speed call -- DrawCargoCapacity applies no multiplier at all.
        /// </summary>
        private float ComputeStat(TransferableOneWay t, FieldInfo statDefOfField, bool applyWorldSpeedMultiplier)
        {
            Thing anyThing = t.AnyThing;
            object statDef = statDefOfField.GetValue(null);
            if (statDef == null)
                return 0f;

            if (IsVehiclePawn(anyThing))
            {
                object statHandler = VfVehiclePawn.StatHandler(anyThing);
                float baseValue = statHandler != null ? (float)getStatValueMethod.Invoke(statHandler, new object[] { statDef }) : 0f;
                if (!applyWorldSpeedMultiplier)
                    return baseValue;
                float multiplier = (float)worldSpeedMultiplierProperty.GetValue(anyThing);
                return baseValue * multiplier;
            }

            if (!abstractStatReady || vehicleDefType == null || !vehicleDefType.IsInstanceOfType(t.ThingDef))
                return 0f;

            float abstractValue = (float)getStatValueAbstractMethod.Invoke(null, new object[] { t.ThingDef, statDef });
            if (!applyWorldSpeedMultiplier)
                return abstractValue;

            object properties = vehicleDefPropertiesField.GetValue(t.ThingDef);
            float propMultiplier = properties != null ? (float)worldSpeedMultiplierPropField.GetValue(properties) : 0f;
            return abstractValue * propMultiplier;
        }

        private string FormatMarketValue(TransferableOneWay t)
        {
            if (t.ThingDef == null)
                return "";
            if (t.ThingDef.tradeability != Tradeability.Sellable && t.ThingDef.tradeability != Tradeability.All)
                return "";
            return t.AnyThing != null ? t.AnyThing.MarketValue.ToStringMoney() : "";
        }
    }
}
