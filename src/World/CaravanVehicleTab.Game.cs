using System.Collections.Generic;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Generic seam for a mod that rewires the caravan-formation dialog with its own vehicles
    /// tab -- mirrors the <see cref="PawnNeedsAdapter"/> <c>RegisterDetailExtender</c> precedent:
    /// this file has zero references to any specific mod; every mod-specific implementation
    /// (reflection, mutation, wording) lives behind <see cref="CaravanVehicleTab.Provider"/> in a
    /// compat class under src/Compat (currently <c>VfCaravanCompat</c>, for Vehicle Framework).
    ///
    /// <see cref="RimWorldAccess.Shell.CaravanFormationScope"/> reads
    /// <see cref="CaravanVehicleTab.Active"/> once at construction to decide whether it needs a
    /// conditional Vehicles region; <see cref="RimWorldAccess.Shell.SplitCaravanScope"/> only
    /// ever calls <see cref="ICaravanVehicleTabProvider.SyncTab"/> and
    /// <see cref="ICaravanVehicleTabProvider.IsPawnSeatLocked"/> (its own dialog has no vehicles
    /// region of its own -- see that scope's remarks).
    /// </summary>
    internal interface ICaravanVehicleTabProvider
    {
        /// <summary>Provider loaded AND the current form-caravan (or split-caravan) session is driven by the mod's own vehicles tab.</summary>
        bool Active { get; }

        /// <summary>The tab label the sighted player sees for the vehicles tab.</summary>
        string RegionName { get; }

        /// <summary>True when this transferable is a vehicle row (a spawned vehicle pawn, or a def-only vehicle row).</summary>
        bool IsVehicle(TransferableOneWay t);

        /// <summary>The vehicles tab widget's own live, sorted row order -- read fresh every pass, never cached by the caller.</summary>
        List<TransferableOneWay> VehicleRows();

        /// <summary>The vehicles table's DATA column count (the scope prepends its own identity column).</summary>
        int ColumnCount { get; }

        TableColumnInfo ColumnInfo(int column);
        string CellText(TransferableOneWay t, int column);
        string CellTip(TransferableOneWay t, int column);

        /// <summary>The row's identity label: name, selected state, disable reason.</summary>
        string DescribeRow(TransferableOneWay t);

        /// <summary>
        /// Mirrors the vehicle card's own checkbox; announces its own outcome. Returns true when
        /// the toggle opened the seat-assignment dialog (whose scope takes over announcing) --
        /// the caller must NOT re-announce its own row over the new dialog in that case.
        /// </summary>
        bool ToggleVehicle(TransferableOneWay t);

        /// <summary>True when the pawn is locked to a vehicle seat (read-only in the vehicles-aware widget); <paramref name="note"/> is the spoken reason.</summary>
        bool IsPawnSeatLocked(Pawn pawn, out string note);

        /// <summary>Drives the sighted tab: true syncs to the vehicles tab; false syncs to <paramref name="vanillaTab"/> (0/1/2 = Pawns/Items/Supplies).</summary>
        void SyncTab(bool vehiclesRegion, int vanillaTab);
    }

    /// <summary>Static registry: one provider, registered once at startup by whichever compat shim resolves it.</summary>
    internal static class CaravanVehicleTab
    {
        public static ICaravanVehicleTabProvider Provider;

        public static bool Active => Provider != null && Provider.Active;
    }
}
