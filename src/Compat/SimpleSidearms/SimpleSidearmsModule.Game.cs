using HarmonyLib;

namespace RimWorldAccess
{
    internal sealed class SimpleSidearmsModule : CompatModule
    {
        public override string TargetPackageId => "PeteTimesSix.SimpleSidearms";

        public override bool ShouldActivate =>
            AccessTools.TypeByName("SimpleSidearms.rimworld.Gizmo_SidearmsList") != null;

        public override void Activate(Harmony harmony)
        {
            if (!SidearmsReflection.Resolve())
            {
                return;
            }
            GizmoHandlerRegistry.Register(SidearmsReflection.GizmoType, new SidearmsListGizmoHandler());
            SidearmsCurveEditorCompat.Register(harmony);
        }
    }
}
