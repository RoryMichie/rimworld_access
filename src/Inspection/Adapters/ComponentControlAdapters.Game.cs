using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Adapter for the dynamically discovered refuelable component category. The
    /// display name is the comp's per-def fuel gizmo label, resolved live so the
    /// dispatch key stays stable and language-independent.
    /// </summary>
    internal sealed class RefuelableAdapter : InspectNodeAdapter
    {
        public override string CategoryKey => "Refuelable";

        public override TabHandlerType Handler => TabHandlerType.Action;

        public override string CategoryDisplayName(object obj)
        {
            // The display name is the comp's fuel gizmo label ("fuel", "barrel
            // durability", ...), matching BuildingComponentsHelper's discovery.
            var refuelable = (obj as ThingWithComps)?.TryGetComp<CompRefuelable>();
            return (refuelable?.Props?.FuelGizmoLabel ?? "Fuel".TranslateSimple()).CapitalizeFirst();
        }

        public override void ExecuteAction(object obj)
        {
            if (obj is Building building && building.TryGetComp<CompRefuelable>() != null)
            {
                RefuelableComponentState.Open(building);
            }
        }
    }

    /// <summary>
    /// Adapter for the dynamically discovered door controls category from
    /// BuildingComponentsHelper.
    /// </summary>
    internal sealed class DoorControlsAdapter : InspectNodeAdapter
    {
        public override string CategoryKey => "Door Controls";

        public override TabHandlerType Handler => TabHandlerType.Action;

        public override void ExecuteAction(object obj)
        {
            if (obj is Building building && building is Building_Door)
            {
                DoorControlState.Open(building);
            }
        }
    }

    /// <summary>
    /// Adapter for the dynamically discovered forbid controls category from
    /// BuildingComponentsHelper.
    /// </summary>
    internal sealed class ForbidControlsAdapter : InspectNodeAdapter
    {
        public override string CategoryKey => "Forbid Controls";

        public override TabHandlerType Handler => TabHandlerType.Action;

        public override void ExecuteAction(object obj)
        {
            if (obj is Building building && building.TryGetComp<CompForbiddable>() != null)
            {
                ForbidControlState.Open(building);
            }
        }
    }
}
