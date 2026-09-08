using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// The words for "can this designator act on this cell, and which way is the ghost facing" —
    /// one set, spoken identically whether the cell was reached by the keyboard cursor or by the
    /// mouse pointer. Every verdict comes from the designator's own
    /// <see cref="Designator.CanDesignateCell"/> report, the same object vanilla colours its ghost
    /// from (Designator_Place.SelectedUpdate).
    /// </summary>
    internal static class PlacementDescriber
    {
        /// <summary>The report's own translated reason, or our noun when it carries none.</summary>
        internal static string ReasonOrFallback(AcceptanceReport report)
        {
            return report.Reason.NullOrEmpty()
                ? "RimWorldAccess.Building.View.CannotPlaceHere".Translate().ToString()
                : report.Reason;
        }

        /// <summary>The rejection as the placement flow speaks it.</summary>
        internal static Localized DescribeRejection(AcceptanceReport report)
        {
            return "RimWorldAccess.Building.ArchitectPlace.InvalidReason".Loc(ReasonOrFallback(report));
        }

        /// <summary>The verdict for a cell, accepted or not.</summary>
        internal static string DescribeValidity(Designator designator, IntVec3 cell)
        {
            AcceptanceReport report = designator.CanDesignateCell(cell);
            return report.Accepted
                ? "RimWorldAccess.Building.ArchitectPlace.CanPlaceHere".Loc().ToString()
                : DescribeRejection(report).ToString();
        }

        /// <summary>
        /// The ghost's rotation, when the player can actually turn it — vanilla's own test
        /// (Designator_Place.DoExtraGuiControls / SelectedProcessInput) for offering rotation
        /// controls at all.
        /// </summary>
        internal static bool TryGetPlacingRot(Designator designator, out Rot4 rot)
        {
            rot = Rot4.North;
            if (!BuildingReflection.HasPlacingRotField
                || !(designator is Designator_Place place)
                || !(place.PlacingDef is ThingDef thingDef)
                || !thingDef.rotatable)
            {
                return false;
            }
            rot = BuildingReflection.GetPlacingRot(place);
            return true;
        }

        /// <summary>
        /// The gerund a designation is announced with ("hauling", "hunting", "mining").
        /// Classifies by the designator's actual runtime type instead of Contains-matching
        /// hardcoded English keywords against its (possibly translated) label, so this works in
        /// every language. <paramref name="designatorLabel"/> is the sanitized label, used as the
        /// fallback for types with no dedicated action verb (e.g. Install).
        /// </summary>
        internal static string DescribeAction(Designator designator, string designatorLabel)
        {
            if (designator is Designator_Haul)
                return "RimWorldAccess.Building.Place.Action.haul".Translate();
            if (designator is Designator_Hunt)
                return "RimWorldAccess.Building.Place.Action.hunt".Translate();
            if (designator is Designator_Mine)
                return "RimWorldAccess.Building.Place.Action.mine".Translate();
            if (designator is Designator_Deconstruct)
                return "RimWorldAccess.Building.Place.Action.deconstruct".Translate();
            if (designator is Designator_PlantsCut)
                return "RimWorldAccess.Building.Place.Action.cut".Translate();
            // Designator_Smooth is the abstract base of SmoothSurface/SmoothFloors/SmoothWalls -
            // all three should read as "smoothing".
            if (designator is Designator_Smooth)
                return "RimWorldAccess.Building.Place.Action.smooth".Translate();
            if (designator is Designator_Tame)
                return "RimWorldAccess.Building.Place.Action.tame".Translate();
            if (designator is Designator_Cancel)
                return "RimWorldAccess.Building.Place.Action.cancel".Translate();

            // No dedicated vanilla type maps to an action verb (e.g. Install) - fall back to the
            // designator's own label, matching the original behavior for unrecognized designators.
            return designatorLabel.ToLower();
        }

        /// <summary>Facing, footprint and spatial requirements, as the rotate key speaks them.</summary>
        internal static string DescribeRotation(Designator designator, Rot4 rot)
        {
            return ArchitectState.GetRotationAnnouncementForDef(
                (designator as Designator_Place)?.PlacingDef, rot);
        }
    }
}
