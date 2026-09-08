using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Reads the info card Vehicle Framework draws for a vehicle, instead of the vanilla one it
    /// suppresses.
    ///
    /// VF prefix-patches <c>Dialog_InfoCard.DoWindowContents</c>
    /// (Patch_Gizmos.VehicleInfoCardOverride) and, for a card whose def is a
    /// <c>VehicleBuildDef</c> or whose thing is a <c>VehicleBuilding</c> / <c>VehiclePawn</c>,
    /// draws its own static card via <c>Vehicles.VehicleInfoCard.DrawFor</c> and returns false.
    /// Two consequences drive this reader:
    ///
    /// 1. Vanilla's body never runs, so <c>StatsReportUtility.cachedDrawEntries</c> — which
    ///    <see cref="InfoCardDataExtractor.GetStatEntries"/> reads — is never filled, and
    ///    <c>Dialog_InfoCard</c>'s own constructor calls <c>StatsReportUtility.Reset()</c>, so
    ///    for a vehicle card that cache is reliably EMPTY, not merely stale. The real report
    ///    lives in VF's private static <c>VehicleInfoCard.cachedDrawEntries</c>, read here.
    /// 2. VF's card draws only three tabs and fills only Stats: <c>DrawHealthScreen</c> is an
    ///    empty method and the Records case falls through, so pawn-shaped Health/Records content
    ///    is content no sighted player can see.
    ///
    /// Everything is read from VF's own statics rather than re-derived, so the rows are exactly
    /// the ones on screen — including the case where VF itself keeps a stale target (<c>DrawFor</c>
    /// only re-inits when the target changes). Mirroring that is deliberate.
    /// </summary>
    internal static class VfInfoCardCompat
    {
        private static readonly Type vehiclePawnType;
        private static readonly Type vehicleDefType;
        private static readonly Type vehicleBuildDefType;
        private static readonly Type vehicleBuildingType;

        private static readonly FieldInfo cachedDrawEntriesField;
        private static readonly FieldInfo staticVehicleField;
        private static readonly FieldInfo staticVehicleDefField;

        private static readonly PropertyInfo entryLabelCapProperty;
        private static readonly PropertyInfo entryValueStringProperty;
        private static readonly PropertyInfo entryCategoryLabelProperty;
        private static readonly MethodInfo entryExplanationMethod;
        private static readonly MethodInfo entryHyperlinksMethod;

        private static readonly bool ready;

        internal static bool Ready => ready;

        static VfInfoCardCompat()
        {
            var surface = new ReflectionSurface("VfInfoCardCompat");

            vehiclePawnType = surface.Supplied("Vehicles.VehiclePawn", VfVehiclePawn.VehiclePawnType);
            vehicleDefType = surface.Type("Vehicles.VehicleDef");
            vehicleBuildDefType = surface.Type("Vehicles.VehicleBuildDef");
            vehicleBuildingType = surface.Type("Vehicles.VehicleBuilding");
            Type infoCardType = surface.Type("Vehicles.VehicleInfoCard");
            Type entryType = surface.Type("Vehicles.VehicleStatDrawEntry");

            cachedDrawEntriesField = surface.Field(infoCardType, "cachedDrawEntries");
            staticVehicleField = surface.Field(infoCardType, "vehicle");
            staticVehicleDefField = surface.Field(infoCardType, "vehicleDef");

            entryLabelCapProperty = surface.Property(entryType, "LabelCap");
            entryValueStringProperty = surface.Property(entryType, "ValueString");
            entryCategoryLabelProperty = surface.Property(entryType, "CategoryLabel");
            entryExplanationMethod = vehicleDefType != null && vehiclePawnType != null
                ? surface.Method(entryType, "GetExplanationText", new[] { vehicleDefType, vehiclePawnType })
                : null;
            entryHyperlinksMethod = vehiclePawnType != null
                ? surface.Method(entryType, "GetHyperlinks", new[] { vehiclePawnType })
                : null;

            ready = surface.Ready;
        }

        /// <summary>
        /// True when VF draws its own card over this dialog. Mirrors
        /// Patch_Gizmos.VehicleInfoCardOverride's own ladder, in its order. A bare VehicleDef card
        /// is deliberately NOT matched — VF's prefix does not match it either, so vanilla draws it.
        /// </summary>
        internal static bool OwnsCard(Dialog_InfoCard dialog)
        {
            if (!ready || dialog == null)
                return false;

            if (vehicleBuildDefType.IsInstanceOfType(InfoCardDataExtractor.GetDef(dialog)))
                return true;

            Thing thing = InfoCardDataExtractor.GetThing(dialog);
            return vehicleBuildingType.IsInstanceOfType(thing) || vehiclePawnType.IsInstanceOfType(thing);
        }

        /// <summary>
        /// The title VF's card prints: the vehicle's own label, or the vehicle def's when the card
        /// is about a def or a placeholder building. Null when VF's statics hold no target yet —
        /// the frame before its first draw pass, in which its card is blank on screen too.
        /// </summary>
        internal static string CardTitle()
        {
            if (!ready)
                return null;

            try
            {
                if (staticVehicleField.GetValue(null) is Thing vehicle)
                    return vehicle.LabelCapNoCount;
                if (staticVehicleDefField.GetValue(null) is Def vehicleDef)
                    return vehicleDef.LabelCap;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfInfoCardCompat.CardTitle failed: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// The rows of VF's stats report, in the order it draws them (its own
        /// FinalizeCachedDrawEntries already sorted by category display order, then priority within
        /// category, then label). Empty when VF has not drawn yet or its report is genuinely empty.
        /// </summary>
        internal static List<VehicleStatRow> ReadStatRows()
        {
            var rows = new List<VehicleStatRow>();
            if (!ready)
                return rows;

            try
            {
                var entries = cachedDrawEntriesField.GetValue(null) as IList;
                if (entries == null)
                    return rows;

                // The same two targets VF passes to GetExplanationText/GetHyperlinks
                // while drawing, so every row reads exactly as rendered.
                object vehicle = staticVehicleField.GetValue(null);
                object vehicleDef = staticVehicleDefField.GetValue(null);

                foreach (object entry in entries)
                {
                    if (entry == null)
                        continue;

                    VehicleStatRow row = ReadRow(entry, vehicleDef, vehicle);
                    if (row != null)
                        rows.Add(row);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfInfoCardCompat.ReadStatRows failed: {ex.Message}");
            }

            return rows;
        }

        /// <summary>One entry's read, isolated so a single bad row cannot empty the whole report.</summary>
        private static VehicleStatRow ReadRow(object entry, object vehicleDef, object vehicle)
        {
            try
            {
                var row = new VehicleStatRow
                {
                    Label = entryLabelCapProperty.GetValue(entry) as string ?? string.Empty,
                    Value = entryValueStringProperty.GetValue(entry) as string ?? string.Empty,
                    CategoryLabel = entryCategoryLabelProperty.GetValue(entry) as string ?? string.Empty,
                    Explanation = ReadExplanation(entry, vehicleDef, vehicle),
                };
                row.Hyperlinks.AddRange(ReadHyperlinks(entry, vehicle));
                return row;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfInfoCardCompat: skipping an unreadable vehicle stat row: {ex.Message}");
                return null;
            }
        }

        private static string ReadExplanation(object entry, object vehicleDef, object vehicle)
        {
            try
            {
                return entryExplanationMethod.Invoke(entry, new[] { vehicleDef, vehicle }) as string;
            }
            catch
            {
                return null;
            }
        }

        private static List<Dialog_InfoCard.Hyperlink> ReadHyperlinks(object entry, object vehicle)
        {
            var links = new List<Dialog_InfoCard.Hyperlink>();

            try
            {
                var enumerable = entryHyperlinksMethod.Invoke(entry, new[] { vehicle }) as IEnumerable;
                if (enumerable == null)
                    return links;

                foreach (object link in enumerable)
                {
                    if (link is Dialog_InfoCard.Hyperlink hyperlink)
                        links.Add(hyperlink);
                }
            }
            catch
            {
                // A stat part throwing mid-iteration leaves whatever it already yielded.
            }

            return links;
        }
    }
}
