namespace RimWorldAccess
{
    /// <summary>Synthetic "VF Rename" category adapter: invokes VehiclePawn.Rename() (vehicle A — self-gates on Nameable).</summary>
    internal sealed class VfRenameActionAdapter : InspectNodeAdapter
    {
        public override string CategoryKey => "VF Rename";

        public override TabHandlerType Handler => TabHandlerType.Action;

        public override void ExecuteAction(object obj)
        {
            if (VfVehicleActionsCompat.IsVehicle(obj))
                VfVehicleActionsCompat.OpenRename(obj);
        }
    }
}
