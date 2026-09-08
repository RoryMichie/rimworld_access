using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Adapter for the Meditation Focus category. Lists nearby meditation
    /// focus objects for a building (e.g. a meditation spot or pen marker),
    /// with focus type, strength, distance, and line-of-sight detail.
    /// </summary>
    internal sealed class MeditationFocusAdapter : InspectNodeAdapter
    {
        public override string CategoryKey => "Meditation Focus";

        public override TabHandlerType Handler => TabHandlerType.RichNavigation;

        /// <summary>
        /// Meditation Focus expands only when there are focus objects near the spawned building
        /// (verbatim from InspectionTreeBuilder.IsExpandableCategory).
        /// </summary>
        public override bool CanExpand(object obj)
        {
            return obj is Building meditationBuilding && meditationBuilding.Spawned
                && HasNearbyMeditationFocusObjects(meditationBuilding);
        }

        /// <summary>
        /// With no focus objects nearby the node renders as a non-expandable warning row instead of
        /// falling back to detailed info (the Meditation Focus special case in
        /// BuildCategoryItemFromInfo).
        /// </summary>
        public override string NoExpandLabel(object obj)
        {
            return "RimWorldAccess.Inspection.Tree.MeditationFocusNoFocus".Translate();
        }

        public override void BuildChildren(InspectionTreeItem categoryItem, object obj, InspectionMode mode)
        {
            if (!(obj is Building building))
                return;
            BuildMeditationFocusChildren(categoryItem, building);
        }

        /// <summary>
        /// Builds children for the Meditation Focus category, listing nearby focus objects.
        /// </summary>
        private static void BuildMeditationFocusChildren(InspectionTreeItem parentItem, Building building)
        {
            if (parentItem.Children.Count > 0)
                return;

            int indent = parentItem.IndentLevel + 1;

            if (!building.Spawned)
                return;

            var map = building.Map;
            var center = building.Position;
            float searchRadius = MeditationUtility.FocusObjectSearchRadius;

            foreach (Thing thing in GenRadial.RadialDistinctThingsAround(center, map, searchRadius, useCenter: false))
            {
                CompMeditationFocus focusComp = thing.TryGetComp<CompMeditationFocus>();
                if (focusComp == null)
                    continue;

                if (thing is Building_Throne)
                    continue;

                var sb = new System.Text.StringBuilder();
                sb.Append(thing.LabelCap.ToString().StripTags());

                // Focus types
                if (focusComp.Props.focusTypes != null && focusComp.Props.focusTypes.Count > 0)
                {
                    string types = string.Join(", ",
                        focusComp.Props.focusTypes.Select(f => f.label.CapitalizeFirst()));
                    sb.Append("RimWorldAccess.Inspection.Tree.MeditationFocusTypesSuffix".Translate(types));
                }

                // Focus strength
                float strength = thing.GetStatValue(StatDefOf.MeditationFocusStrength);
                sb.Append("RimWorldAccess.Inspection.Tree.MeditationFocusStrengthSuffix".Translate(strength.ToStringPercent()));

                // Distance
                float distance = center.DistanceTo(thing.Position);
                sb.Append("RimWorldAccess.Inspection.Tree.MeditationFocusDistanceSuffix".Translate(distance.ToString("F1")));

                // Line of sight
                if (!GenSight.LineOfSightToThing(center, thing, map))
                {
                    sb.Append("RimWorldAccess.Inspection.Tree.MeditationFocusNoLineOfSightSuffix".Translate());
                }

                InspectNodeFactory.Attach(parentItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = sb.ToString(),
                    IndentLevel = indent,
                    IsExpandable = false,
                    LinkedDef = thing.def,
                    Data = thing
                });
            }

            if (parentItem.Children.Count == 0)
            {
                InspectNodeFactory.Attach(parentItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = "RimWorldAccess.Inspection.Tree.PenMeditationNoFocusObjects".Translate(searchRadius.ToString("F0")),
                    IndentLevel = indent,
                    IsExpandable = false
                });
            }
        }

        /// <summary>
        /// Checks if there are any meditation focus objects near a building.
        /// </summary>
        internal static bool HasNearbyMeditationFocusObjects(Building building)
        {
            if (!building.Spawned)
                return false;

            foreach (Thing thing in GenRadial.RadialDistinctThingsAround(
                building.Position, building.Map, MeditationUtility.FocusObjectSearchRadius, useCenter: false))
            {
                if (thing is Building_Throne)
                    continue;

                if (thing.TryGetComp<CompMeditationFocus>() != null)
                    return true;
            }

            return false;
        }
    }
}
