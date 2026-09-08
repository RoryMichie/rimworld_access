using HarmonyLib;
using Verse;

namespace RimWorldAccess
{
    internal sealed class VpeModule : CompatModule
    {
        public override string TargetPackageId => "VanillaExpanded.VPsycastsE";

        public override bool ShouldActivate =>
            ModsConfig.IsActive(TargetPackageId)
            || AccessTools.TypeByName("VanillaPsycastsExpanded.UI.ITab_Pawn_Psycasts") != null;

        public override void Activate(Harmony harmony)
        {
            VpeGizmoCompat.RegisterGizmoHandlers();
            VpeTabCompat.RegisterTabAdapters();
        }
    }
}
