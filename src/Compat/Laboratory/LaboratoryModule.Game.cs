using HarmonyLib;

namespace RimWorldAccess
{
    internal sealed class LaboratoryModule : CompatModule
    {
        public override string TargetPackageId => "ilyvion.laboratory";

        public override bool ShouldActivate =>
            AccessTools.TypeByName("ilyvion.Laboratory.UI.IlyvionWidgets") != null;

        public override void Activate(Harmony harmony)
        {
            LaboratoryCompat.Register(harmony);
        }
    }
}
