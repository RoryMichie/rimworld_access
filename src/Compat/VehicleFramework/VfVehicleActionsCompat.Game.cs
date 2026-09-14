using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Reflection-only compatibility surface for Vehicle Framework's vehicle Rename and Recolor
    /// inspect-pane buttons (<c>VehiclePawn_Rendering.DoInspectPaneButtons</c>), which draw as
    /// unlabeled icon buttons with no keyboard path. Registers two synthetic Action categories
    /// ("VF Rename"/"VF Recolor") through
    /// <see cref="InspectionInfoHelper.RegisterCategoryProvider"/> so a keyboard user reaches both
    /// from the inspection tree, gated exactly like the sighted buttons: Rename on
    /// <c>VehiclePawn.Nameable</c>, Recolor on <c>VehicleGraphic.Shader.SupportsRGBMaskTex()</c>
    /// (that extension method's own <c>ignoreSettings</c> parameter, left false, already checks
    /// <c>VehicleMod.settings.main.useCustomShaders</c> internally, so the one call reproduces the
    /// sighted button's compound gate).
    ///
    /// Mutation vehicle A: <c>Rename()</c> self-gates on <c>Nameable</c> internally (opens
    /// <c>Dialog_GiveVehicleName</c> only when true), so <see cref="OpenRename"/> invokes it
    /// unconditionally. <c>ChangeColor()</c> has NO internal gate (it unconditionally opens
    /// <c>Dialog_VehiclePainter.OpenColorPicker</c>), so <see cref="VfRecolorActionAdapter"/>
    /// re-checks <see cref="CanRecolor"/> live before calling <see cref="OpenRecolor"/>.
    /// </summary>
    internal static class VfVehicleActionsCompat
    {
        private static readonly Type vehiclePawnType;
        private static readonly PropertyInfo nameableProperty;
        private static readonly MethodInfo renameMethod;
        private static readonly MethodInfo changeColorMethod;
        private static readonly PropertyInfo vehicleGraphicProperty;
        private static readonly MethodInfo supportsRgbMaskTexMethod;

        private static readonly bool ready;

        public static bool Ready => ready;

        static VfVehicleActionsCompat()
        {
            var surface = new ReflectionSurface("VfVehicleActionsCompat");

            vehiclePawnType = surface.Supplied("Vehicles.VehiclePawn", VfVehiclePawn.VehiclePawnType);
            nameableProperty = surface.Property(vehiclePawnType, "Nameable");
            renameMethod = surface.Method(vehiclePawnType, "Rename", Type.EmptyTypes);
            changeColorMethod = surface.Method(vehiclePawnType, "ChangeColor", Type.EmptyTypes);
            vehicleGraphicProperty = surface.Property(vehiclePawnType, "VehicleGraphic");

            Type assetBundleDatabaseType = surface.Type("Vehicles.AssetBundleDatabase");
            supportsRgbMaskTexMethod = surface.Method(assetBundleDatabaseType, "SupportsRGBMaskTex",
                new[] { typeof(Shader), typeof(bool) });

            ready = surface.Ready;
        }

        public static bool IsVehicle(object obj)
        {
            return ready && vehiclePawnType.IsInstanceOfType(obj);
        }

        public static bool CanRename(object vehicle)
        {
            if (!ready)
                return false;
            try
            {
                return (bool)nameableProperty.GetValue(vehicle);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfVehicleActionsCompat.CanRename failed: {ex.Message}");
                return false;
            }
        }

        public static bool CanRecolor(object vehicle)
        {
            if (!ready)
                return false;
            try
            {
                object graphicObj = vehicleGraphicProperty.GetValue(vehicle);
                if (!(graphicObj is Graphic graphic) || graphic.Shader == null)
                    return false;
                return (bool)supportsRgbMaskTexMethod.Invoke(null, new object[] { graphic.Shader, false });
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfVehicleActionsCompat.CanRecolor failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>Vehicle A: VehiclePawn.Rename() self-gates on Nameable; invoked unconditionally.</summary>
        public static void OpenRename(object vehicle)
        {
            try
            {
                renameMethod.Invoke(vehicle, null);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfVehicleActionsCompat.OpenRename failed: {ex.Message}");
                TolkHelper.Speak("RimWorldAccess.Compat.Vf.VehicleActionFailed".Loc(), SpeechPriority.High);
            }
        }

        /// <summary>Vehicle A: VehiclePawn.ChangeColor() itself, called only after the caller has re-checked CanRecolor.</summary>
        public static void OpenRecolor(object vehicle)
        {
            try
            {
                changeColorMethod.Invoke(vehicle, null);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfVehicleActionsCompat.OpenRecolor failed: {ex.Message}");
                TolkHelper.Speak("RimWorldAccess.Compat.Vf.VehicleActionFailed".Loc(), SpeechPriority.High);
            }
        }

        /// <summary>No-ops unless Ready. Registers the two Action adapters and the category provider.</summary>
        public static void Register()
        {
            if (!ready)
                return;

            InspectNodeRegistry.RegisterCategory(new VfRenameActionAdapter());
            InspectNodeRegistry.RegisterCategory(new VfRecolorActionAdapter());
            InspectionInfoHelper.RegisterCategoryProvider(BuildCategories);

        }

        private static List<TabCategoryInfo> BuildCategories(object obj)
        {
            if (!ready || !IsVehicle(obj))
                return null;

            var categories = new List<TabCategoryInfo>();

            if (CanRename(obj))
            {
                categories.Add(new TabCategoryInfo
                {
                    Name = "RimWorldAccess.Compat.Vf.RenameCategory".Translate().ToString(),
                    Tab = null,
                    Handler = TabHandlerType.Action,
                    IsKnown = true,
                    OriginalCategoryName = "VF Rename"
                });
            }

            if (CanRecolor(obj))
            {
                categories.Add(new TabCategoryInfo
                {
                    Name = "RimWorldAccess.Compat.Vf.RecolorCategory".Translate().ToString(),
                    Tab = null,
                    Handler = TabHandlerType.Action,
                    IsKnown = true,
                    OriginalCategoryName = "VF Recolor"
                });
            }

            return categories;
        }
    }
}
