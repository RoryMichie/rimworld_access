using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Vehicle Framework's <c>Vehicles.VehiclePawn</c> and the three members more than one compat
    /// surface reads off it: the stat handler every stat and component readout goes through
    /// (<c>statHandler</c>), the pending-cargo list (<c>cargoToLoad</c>), and the aboard-pawn list
    /// (<c>AllPawnsAboard</c>). Twelve files resolved the type independently and each of these three
    /// members was bound twice, so a VF rename was caught in N places with N failure modes; this is
    /// the one resolution.
    ///
    /// <see cref="VehiclePawnType"/> resolves OUTSIDE the member gate deliberately. The files that
    /// need nothing but the type -- the passengers, upgrades, info-card, actions, selector and
    /// targeter surfaces -- bind their own members on it and declare it through their own surface,
    /// so a rename of a member none of them touch must not take their feature down. Only consumers
    /// of the accessors below conjoin <see cref="Ready"/>.
    ///
    /// Reads only, and they throw rather than swallow: every call site already wraps its read in its
    /// own try/catch with its own failure presentation. The single write to <c>cargoToLoad</c> stays
    /// at its own call site with its MUTATION-C citation (VfCargoDialogAdapter.TriggerAccept); this
    /// class hands that file <see cref="CargoToLoadField"/> and nothing more.
    /// </summary>
    internal static class VfVehiclePawn
    {
        private static bool typeResolved;
        private static Type vehiclePawnType;

        private static FieldInfo statHandlerField;
        private static FieldInfo cargoToLoadField;
        private static PropertyInfo allPawnsAboardProperty;

        private static readonly LazyReflectionGate members = new LazyReflectionGate("VfVehiclePawn", ResolveMembers);

        /// <summary>
        /// The VehiclePawn type itself, null when VF is not loaded. Consumers declare it on their
        /// own surface with <c>surface.Supplied("Vehicles.VehiclePawn", ...)</c> so a rename still
        /// names itself in their log line.
        /// </summary>
        public static Type VehiclePawnType
        {
            get
            {
                if (!typeResolved)
                {
                    typeResolved = true;
                    vehiclePawnType = AccessTools.TypeByName("Vehicles.VehiclePawn");
                }
                return vehiclePawnType;
            }
        }

        /// <summary>True when every member this shared vehicle slice reads resolved.</summary>
        public static bool Ready
        {
            get { return members.Ensure(); }
        }

        /// <summary>
        /// The <c>cargoToLoad</c> field, for the one MUTATION-C write that stays at its own call
        /// site. Null unless <see cref="Ready"/>.
        /// </summary>
        public static FieldInfo CargoToLoadField
        {
            get { return members.Ensure() ? cargoToLoadField : null; }
        }

        /// <summary>
        /// The vehicle's VehicleStatHandler, boxed: each reader binds its own members on the handler
        /// type and null-checks this result, since an unspawned vehicle has no handler yet.
        /// </summary>
        public static object StatHandler(object vehicle)
        {
            if (!members.Ensure() || vehicle == null)
            {
                return null;
            }
            return statHandlerField.GetValue(vehicle);
        }

        /// <summary>The cargo queued for loading, exactly as the field holds it -- null until the vehicle has been given a manifest.</summary>
        public static List<TransferableOneWay> CargoToLoad(object vehicle)
        {
            if (!members.Ensure() || vehicle == null)
            {
                return null;
            }
            return cargoToLoadField.GetValue(vehicle) as List<TransferableOneWay>;
        }

        /// <summary>The pawns currently aboard, exactly as the property returns them (VehiclePawn declares it <c>List&lt;Pawn&gt;</c>).</summary>
        public static List<Pawn> AllPawnsAboard(object vehicle)
        {
            if (!members.Ensure() || vehicle == null)
            {
                return null;
            }
            return allPawnsAboardProperty.GetValue(vehicle) as List<Pawn>;
        }

        private static bool ResolveMembers(ReflectionSurface surface)
        {
            Type type = surface.Supplied("Vehicles.VehiclePawn", VehiclePawnType);

            FieldInfo statHandler = surface.Field(type, "statHandler");
            FieldInfo cargoToLoad = surface.Field(type, "cargoToLoad");
            PropertyInfo allPawnsAboard = surface.Property(type, "AllPawnsAboard");

            if (!surface.Ready)
            {
                return false;
            }

            statHandlerField = statHandler;
            cargoToLoadField = cargoToLoad;
            allPawnsAboardProperty = allPawnsAboard;
            return true;
        }
    }
}
