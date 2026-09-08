using HarmonyLib;

namespace RimWorldAccess
{
    internal sealed class BubblesModule : CompatModule
    {
        public override string TargetPackageId => "jaxe.bubbles";

        public override void Activate(Harmony harmony)
        {
            BubblesNarrativeCompat.Register(harmony);
        }
    }
}
