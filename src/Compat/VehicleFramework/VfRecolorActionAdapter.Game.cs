namespace RimWorldAccess
{
    /// <summary>Synthetic "VF Recolor" category adapter: re-checks CanRecolor live (ChangeColor itself carries no gate) before opening the painter.</summary>
    internal sealed class VfRecolorActionAdapter : InspectNodeAdapter
    {
        public override string CategoryKey => "VF Recolor";

        public override TabHandlerType Handler => TabHandlerType.Action;

        public override void ExecuteAction(object obj)
        {
            if (!VfVehicleActionsCompat.IsVehicle(obj) || !VfVehicleActionsCompat.CanRecolor(obj))
            {
                TolkHelper.Speak("RimWorldAccess.Compat.Vf.RecolorUnavailable".Loc(), SpeechPriority.High);
                return;
            }
            VfVehicleActionsCompat.OpenRecolor(obj);
        }
    }
}
