using HarmonyLib;
using Verse;

namespace RimWorldAccess
{
    internal sealed class VaeModule : CompatModule
    {
        public override string TargetPackageId => "VanillaExpanded.VanillaAspirationsExpanded";

        public override bool ShouldActivate =>
            ModsConfig.IsActive(TargetPackageId)
            || AccessTools.TypeByName("VAspirE.Need_Fulfillment") != null;

        public override void Activate(Harmony harmony)
        {
            VaeNeedsCompat.RegisterNeedExtenders();
        }
    }
}
