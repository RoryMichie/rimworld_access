using HarmonyLib;
using RimWorldAccess.Shell;

namespace RimWorldAccess
{
    /// <summary>
    /// The mod's surfaces are almost entirely generic-reader territory: its main
    /// tab is a MainTabWindow_PawnTable (GenericPawnTableScope), its right-click
    /// interactions ride a vanilla FloatMenuOptionProvider, its settings live in
    /// vanilla Dialog_ModSettings, and most of its pawn columns subclass vanilla
    /// workers. This module fills the four gaps: two dialogs that never absorb
    /// input (so no scope would attach), an Enter-eating close handler shared by
    /// three dialogs, two custom gizmos the gizmo reader cannot see into, and the
    /// mod's own checkbox column base — registered by base type so addon columns
    /// inherit support.
    /// </summary>
    internal sealed class RjwModule : CompatModule
    {
        public override string TargetPackageId => "rim.job.world";

        /// <summary>Type probe: this mod family circulates as renamed forks that keep the namespace.</summary>
        public override bool ShouldActivate => AccessTools.TypeByName("rjw.CompRJW") != null;

        public override void Activate(Harmony harmony)
        {
            RjwDialogCompat.Register(harmony);

            int gizmos = 0;
            if (RjwReflection.StatusGizmoReady)
            {
                GizmoHandlerRegistry.Register(RjwReflection.StatusGizmoType, new RjwStatusGizmoHandler());
                gizmos++;
            }
            if (RjwReflection.DesignationsReady)
            {
                GizmoHandlerRegistry.Register(RjwReflection.DesignationsGizmoType, new RjwDesignationsGizmoHandler());
                gizmos++;
            }
            if (RjwReflection.ColumnsReady)
            {
                PawnColumnHandlerRegistry.Register(RjwReflection.CheckboxColumnType, new RjwCheckboxColumnHandler());
            }
            if (gizmos > 0)
            {
            }
        }
    }
}
