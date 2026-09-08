using HarmonyLib;

namespace RimWorldAccess
{
    internal sealed class JecsModule : CompatModule
    {
        public override string TargetPackageId => "jecrell.jecstools";

        public override bool ShouldActivate =>
            AccessTools.TypeByName("AbilityUser.Verb_UseAbility") != null;

        public override void Activate(Harmony harmony)
        {
            JecsAbilityCompat.Register(harmony);
            JecsAbilityCompat.RegisterGizmoHandlers();
            JecsGearSlotCompat.TryRegister();
        }
    }
}
