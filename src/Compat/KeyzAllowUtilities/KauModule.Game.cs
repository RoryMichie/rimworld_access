using HarmonyLib;

namespace RimWorldAccess
{
    internal sealed class KauModule : CompatModule
    {
        public override string TargetPackageId => "keyz182.KeyzAllowUtilities";

        public override bool ShouldActivate =>
            AccessTools.TypeByName("KeyzAllowUtilities.KeyHandler") != null;

        public override void Activate(Harmony harmony)
        {
            KauCompat.RegisterScannerKeyGuard(harmony);
            KauCompat.RegisterRectDesignationHandlers();
            KauCompat.RegisterStripMineOptions();
            KauCompat.RegisterGizmoHandlers();
        }
    }
}
