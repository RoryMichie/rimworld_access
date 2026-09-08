using HarmonyLib;

namespace RimWorldAccess
{
    internal sealed class RwomModule : CompatModule
    {
        public override string TargetPackageId => "torann.arimworldofmagic";

        public override bool ShouldActivate =>
            AccessTools.TypeByName("TorannMagic.CompAbilityUserMagic") != null;

        public override void Activate(Harmony harmony)
        {
            RwomClassCardAdapter.TryRegister();
            RwomGizmoCompat.TryRegister();
            RwomWorldAbilityCompat.TryRegister(harmony);
            RwomGolemCompat.TryRegister();
            RwomGolemTabAdapter.TryRegister();
            RwomEnchantmentAdapter.TryRegister();
        }
    }
}
