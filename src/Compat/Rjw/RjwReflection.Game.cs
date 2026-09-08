using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Reflection surfaces for the mod's UI internals, split per feature so a mod
    /// update that reshapes one area degrades only that area's support: the job
    /// status gizmo, the designations button-group gizmo, and the mod's own
    /// checkbox pawn-column base. Dialog types are probed by name at patch time
    /// and are not part of these surfaces.
    /// </summary>
    internal static class RjwReflection
    {
        private static readonly ReflectionSurface statusSurface = new ReflectionSurface("RjwStatusGizmo");
        private static readonly ReflectionSurface designationsSurface = new ReflectionSurface("RjwDesignationsGizmo");
        private static readonly ReflectionSurface columnsSurface = new ReflectionSurface("RjwCheckboxColumn");

        // --- job status gizmo ---

        public static readonly Type StatusGizmoType;
        public static readonly FieldInfo StatusGizmoPawn;
        public static readonly Type JobDriverType;
        public static readonly Type JobDriverInitiatorType;
        public static readonly Type JobDriverForcedType;
        public static readonly PropertyInfo JobDriverProgress;
        public static readonly FieldInfo JobDriverOverdrive;
        public static readonly FieldInfo JobDriverStrikeOnce;
        public static readonly MethodInfo CanChangeDesignationColonist;

        // --- designations gizmo ---

        public static readonly Type DesignationsGizmoType;
        public static readonly FieldInfo DesignationsParent;
        public static readonly FieldInfo DesignationsSubIcons;
        public static readonly MethodInfo SubIconApplicable;
        public static readonly MethodInfo SubIconApplied;
        public static readonly MethodInfo SubIconDesc;
        public static readonly MethodInfo SubIconApply;
        public static readonly MethodInfo SubIconUnapply;

        // --- checkbox pawn column ---

        public static readonly Type CheckboxColumnType;
        public static readonly MethodInfo ColumnHasCheckbox;
        public static readonly MethodInfo ColumnGetValue;
        public static readonly MethodInfo ColumnSetValue;
        public static readonly MethodInfo ColumnGetTip;
        public static readonly MethodInfo ColumnGetDisabled;

        // Multiplayer API ships bundled with the mod, but a fork could drop it;
        // absent means single-player, which is the permissive answer.
        private static readonly PropertyInfo mpIsInMultiplayer;

        static RjwReflection()
        {
            StatusGizmoType = statusSurface.Type("rjw.SexGizmo");
            StatusGizmoPawn = statusSurface.Field(StatusGizmoType, "pawn");
            JobDriverType = statusSurface.Type("rjw.JobDriver_Sex");
            JobDriverInitiatorType = statusSurface.Type("rjw.JobDriver_SexBaseInitiator");
            JobDriverForcedType = statusSurface.Type("rjw.JobDriver_Rape");
            JobDriverProgress = statusSurface.Property(JobDriverType, "OrgasmProgress");
            JobDriverOverdrive = statusSurface.Field(JobDriverType, "neverendingsex");
            JobDriverStrikeOnce = statusSurface.Field(JobDriverType, "beatonce");
            CanChangeDesignationColonist = statusSurface.Method(
                statusSurface.Type("rjw.PawnDesignations_Utility"),
                "CanChangeDesignationColonist",
                new[] { typeof(Pawn) });

            DesignationsGizmoType = designationsSurface.Type("rjw.RJWdesignations");
            DesignationsParent = designationsSurface.Field(DesignationsGizmoType, "parent");
            DesignationsSubIcons = designationsSurface.Field(DesignationsGizmoType, "subIcons");
            Type subIconType = designationsSurface.Type("rjw.RJWdesignations+SubIcon");
            SubIconApplicable = designationsSurface.Method(subIconType, "applicable", new[] { typeof(Pawn) });
            SubIconApplied = designationsSurface.Method(subIconType, "applied", new[] { typeof(Pawn) });
            SubIconDesc = designationsSurface.Method(subIconType, "desc", new[] { typeof(Pawn) });
            SubIconApply = designationsSurface.Method(subIconType, "apply", new[] { typeof(Pawn) });
            SubIconUnapply = designationsSurface.Method(subIconType, "unapply", new[] { typeof(Pawn) });

            CheckboxColumnType = columnsSurface.Type("rjw.MainTab.Checkbox.PawnColumnCheckbox");
            ColumnHasCheckbox = columnsSurface.Method(CheckboxColumnType, "HasCheckbox", new[] { typeof(Pawn) });
            ColumnGetValue = columnsSurface.Method(CheckboxColumnType, "GetValue", new[] { typeof(Pawn) });
            ColumnSetValue = columnsSurface.Method(CheckboxColumnType, "SetValue", new[] { typeof(Pawn), typeof(bool) });
            ColumnGetTip = columnsSurface.Method(CheckboxColumnType, "GetTip", new[] { typeof(Pawn) });
            ColumnGetDisabled = columnsSurface.Method(CheckboxColumnType, "GetDisabled", new[] { typeof(Pawn) });

            Type mpType = AccessTools.TypeByName("Multiplayer.API.MP");
            mpIsInMultiplayer = mpType != null
                ? ReflectionSurface.TryFieldOrProperty(mpType, "IsInMultiplayer") as PropertyInfo
                : null;
        }

        public static bool StatusGizmoReady => statusSurface.Ready;
        public static bool DesignationsReady => designationsSurface.Ready;
        public static bool ColumnsReady => columnsSurface.Ready;

        public static bool IsInMultiplayer
        {
            get
            {
                return mpIsInMultiplayer != null
                    && Guarded.Get(mpIsInMultiplayer.GetGetMethod(), null, "Rjw multiplayer probe", false);
            }
        }
    }
}
