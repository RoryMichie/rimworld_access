using HarmonyLib;

namespace RimWorldAccess
{
    internal sealed class HugsLibModule : CompatModule
    {
        public override string TargetPackageId => "UnlimitedHugs.HugsLib";

        public override bool ShouldActivate =>
            AccessTools.TypeByName("HugsLib.HugsLibController") != null;

        public override void Activate(Harmony harmony)
        {
            HugsLibCompat.Register(harmony);
            HugsLibNewsCompat.TryRegister();
        }
    }
}
