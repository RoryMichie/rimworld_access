using HarmonyLib;

namespace RimWorldAccess
{
    internal sealed class IsekaiModule : CompatModule
    {
        public override string TargetPackageId => "JellyCreative.IsekaiLeveling";

        public override bool ShouldActivate =>
            AccessTools.TypeByName("IsekaiLeveling.IsekaiComponent") != null;

        public override void Activate(Harmony harmony)
        {
            IsekaiCompat.RegisterTabAdapters();
            IsekaiCompat.RegisterWindowScopes();
            IsekaiCompat.RegisterCharacterCreation();
            IsekaiCompat.RegisterAffectedStatsCapture(harmony);
        }
    }
}
