using System;
using System.Reflection;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Compat for Keyz' Allow Utilities. Its designators are ordinary cell designators, so shape
    /// placement already drives them; what the keyboard lacked was the mod's map-wide Home/End
    /// hotkeys staying out of the scanner's way (<see cref="KauScannerKeyGuard"/>), Select
    /// Similar's cursor-seeded filter (<see cref="KauSelectSimilarRectHandler"/>), the floor
    /// picker's hand-off to the picked floor, the strip-mine spacing panel
    /// (<see cref="KauStripMineOptionsProvider"/>), and the bulk actions its gizmos hide behind
    /// right-click (<see cref="KauThingGizmoHandler"/>, <see cref="KauForbidToggleHandler"/>).
    /// </summary>
    internal static class KauCompat
    {
        internal static Type SelectSimilarType;
        internal static Type FloorPickerType;

        internal static readonly LazyReflectionGate DesignatorGate =
            new LazyReflectionGate("Keyz' Allow Utilities designators", surface =>
            {
                Type selectSimilar = surface.Type("KeyzAllowUtilities.Designator_SelectSimilar");
                Type floorPicker = surface.Type("KeyzAllowUtilities.Designator_Floors");
                if (!surface.Ready)
                    return false;

                SelectSimilarType = selectSimilar;
                FloorPickerType = floorPicker;
                return true;
            });

        internal static Type StripMineType;
        internal static FieldInfo SpacingXField;
        internal static FieldInfo SpacingZField;
        internal static FieldInfo OffsetXField;
        internal static FieldInfo OffsetZField;

        internal static readonly LazyReflectionGate StripMineGate =
            new LazyReflectionGate("Keyz' Allow Utilities strip mine", surface =>
            {
                Type stripMine = surface.Type("KeyzAllowUtilities.Designator_StripMine");
                FieldInfo spacingX = surface.Field(stripMine, "SpacingX");
                FieldInfo spacingZ = surface.Field(stripMine, "SpacingZ");
                FieldInfo offsetX = surface.Field(stripMine, "OffsetX");
                FieldInfo offsetZ = surface.Field(stripMine, "OffsetZ");
                if (!surface.Ready)
                    return false;

                StripMineType = stripMine;
                SpacingXField = spacingX;
                SpacingZField = spacingZ;
                OffsetXField = offsetX;
                OffsetZField = offsetZ;
                return true;
            });

        internal static Assembly ModAssembly;
        internal static FieldInfo MultiSelectIconField;
        internal static FieldInfo HaulUrgentlyIconField;
        internal static FieldInfo HaulUrgentlyDisableIconField;

        /// <summary>
        /// The gizmos Thing_Patches adds are plain Command_Actions whose right-click branch lives
        /// inside the action delegate; the mod's own static icon textures are how those gizmos
        /// can be told apart, the same way the mod itself finds vanilla's forbid toggle.
        /// </summary>
        internal static readonly LazyReflectionGate GizmoGate =
            new LazyReflectionGate("Keyz' Allow Utilities gizmos", surface =>
            {
                Type thingPatches = surface.Type("KeyzAllowUtilities.HarmonyPatches.Thing_Patches");
                FieldInfo multiSelect = surface.Field(thingPatches, "KUA_MultiSelectIcon");
                FieldInfo haulUrgently = surface.Field(thingPatches, "KUA_ToggleHaulUrgentlyIcon");
                FieldInfo haulUrgentlyDisable = surface.Field(thingPatches, "KUA_ToggleHaulUrgentlyDisableIcon");
                if (!surface.Ready)
                    return false;

                ModAssembly = thingPatches.Assembly;
                MultiSelectIconField = multiSelect;
                HaulUrgentlyIconField = haulUrgently;
                HaulUrgentlyDisableIconField = haulUrgentlyDisable;
                return true;
            });

        /// <summary>Reads a static texture field; null until the mod's static constructor has run.</summary>
        internal static Texture IconOf(FieldInfo field)
        {
            return field == null ? null : field.GetValue(null) as Texture;
        }

        public static void RegisterScannerKeyGuard(HarmonyLib.Harmony harmony)
        {
            try
            {
                KauScannerKeyGuard.Register(harmony);
            }
            catch (Exception ex)
            {
                ModLogger.Error("Keyz' Allow Utilities compat (scanner keys) registration failed: " + ex.Message);
            }
        }

        public static void RegisterRectDesignationHandlers()
        {
            try
            {
                RectDesignationRouter.Register(new KauSelectSimilarRectHandler());
                if (DesignatorGate.Ensure())
                    RectDesignationRouter.Register(new DesignatorHandoffRectHandler(FloorPickerType));
            }
            catch (Exception ex)
            {
                ModLogger.Error("Keyz' Allow Utilities compat (designators) registration failed: " + ex.Message);
            }
        }

        public static void RegisterStripMineOptions()
        {
            try
            {
                DesignatorContextMenuRouter.Register(new KauStripMineOptionsProvider());
            }
            catch (Exception ex)
            {
                ModLogger.Error("Keyz' Allow Utilities compat (strip mine) registration failed: " + ex.Message);
            }
        }

        public static void RegisterGizmoHandlers()
        {
            try
            {
                int count = CompatRegistration.GizmoHandler("KeyzAllowUtilities.Command_Toggle_WithContext",
                    type => new KauForbidToggleHandler(type));
                GizmoHandlerRegistry.Register(typeof(Command_Action), new KauThingGizmoHandler());
            }
            catch (Exception ex)
            {
                ModLogger.Error("Keyz' Allow Utilities compat (gizmos) registration failed: " + ex.Message);
            }
        }
    }
}
