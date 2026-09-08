using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Records each vehicle row of <c>Vehicles.World.Dialog_VehicleSelector</c> for
    /// <see cref="VfVehicleSelectorScope"/>'s focus ring. The dialog lays its grid out with raw
    /// rect math (ilspy Vehicles.dll 1.6: DrawVehicles), but funnels every cell through one of two
    /// per-row methods carrying both the row's identity and its geometry --
    /// <c>DrawVehicle(Rect rowRect, Rect iconRect, VehiclePawn)</c> and
    /// <c>DrawVehicleDef(Rect rowRect, Rect iconRect, VehicleDef)</c> -- which is all a ring needs.
    ///
    /// The mode row is the one control the dialog draws inline in <c>DoWindowContents</c>, but it
    /// draws it through <c>SmashTools.UIElements.ClickableLabel(Rect, ...)</c> -- a shared helper
    /// that carries the mod's OWN click rect. Bracketing the dialog's draw and tapping that helper
    /// inside the bracket records it exactly; the rect is the dialog's header band, which is
    /// literally the region a mouse click on the mode toggle has to land in.
    ///
    /// Reflection-only: a hard reference to Vehicles.dll would poison the type for the whole
    /// session on a startup type sweep.
    /// </summary>
    internal static class VfSelectorRowDrawPatch
    {
        internal static readonly RowDrawCapture Rows = new RowDrawCapture();

        /// <summary>Set while a <see cref="VfVehicleSelectorScope"/> drives; the postfixes are one static read otherwise.</summary>
        internal static bool Recording;

        /// <summary>The mode row's rect in absolute UI points, rewritten every draw pass.</summary>
        internal static Rect ModeRect;

        // True while the selector's own DoWindowContents body is on the call stack, so the shared
        // ClickableLabel helper is only read for this dialog's own draw.
        private static bool insideSelectorDraw;

        /// <summary>Called from VfDialogCompat after the selector's scope registers. No-op when anything fails to resolve.</summary>
        public static void Register()
        {
            try
            {
                Type selectorType = VfVehicleSelectorCompat.SelectorType;
                if (selectorType == null)
                {
                    return;
                }

                MethodInfo drawVehicle = AccessTools.Method(selectorType, "DrawVehicle");
                MethodInfo drawVehicleDef = AccessTools.Method(selectorType, "DrawVehicleDef");
                if (drawVehicle == null || drawVehicleDef == null)
                {
                    ModLogger.Error("VfSelectorRowDrawPatch: could not resolve Dialog_VehicleSelector's row methods; declining the ring patches.");
                    return;
                }

                var postfix = new HarmonyMethod(typeof(VfSelectorRowDrawPatch), nameof(RowPostfix));
                RimWorldAccessMod.HarmonyInstance.Patch(drawVehicle, postfix: postfix);
                RimWorldAccessMod.HarmonyInstance.Patch(drawVehicleDef, postfix: postfix);

                RegisterModeRing(selectorType);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfSelectorRowDrawPatch.Register failed: {ex.Message}");
            }
        }

        /// <summary>Arms the mode-row ring; declines quietly, leaving the row unringed, if anything is missing.</summary>
        private static void RegisterModeRing(Type selectorType)
        {
            Type uiElementsType = AccessTools.TypeByName("SmashTools.UIElements");
            MethodInfo doWindowContents = AccessTools.Method(selectorType, "DoWindowContents", new[] { typeof(Rect) });
            MethodInfo clickableLabel = uiElementsType == null ? null : AccessTools.Method(uiElementsType, "ClickableLabel");
            if (doWindowContents == null || clickableLabel == null)
            {
                ModLogger.Error("VfSelectorRowDrawPatch: could not resolve Dialog_VehicleSelector.DoWindowContents/UIElements.ClickableLabel; declining the mode-row ring.");
                return;
            }

            RimWorldAccessMod.HarmonyInstance.Patch(doWindowContents,
                prefix: new HarmonyMethod(typeof(VfSelectorRowDrawPatch), nameof(EnterSelectorDraw)),
                finalizer: new HarmonyMethod(typeof(VfSelectorRowDrawPatch), nameof(ExitSelectorDraw)));
            RimWorldAccessMod.HarmonyInstance.Patch(clickableLabel,
                postfix: new HarmonyMethod(typeof(VfSelectorRowDrawPatch), nameof(ModeLabelPostfix)));
        }

        public static void EnterSelectorDraw()
        {
            insideSelectorDraw = Recording;
            ModeRect = default(Rect);
        }

        public static void ExitSelectorDraw()
        {
            insideSelectorDraw = false;
        }

        /// <summary>The dialog makes exactly one ClickableLabel call, the mode toggle (ilspy Vehicles.dll 1.6: DoWindowContents).</summary>
        public static void ModeLabelPostfix(Rect rect)
        {
            if (insideSelectorDraw)
            {
                ModeRect = GuiSpace.VisibleScreenRect(rect);
            }
        }

        /// <summary>
        /// The icon and the label/checkbox band are two rects side by side; a sighted player reads
        /// the pair as one row, so the ring wraps their union. Deliberately does NOT draw here --
        /// the selector is a real Window, so ScreenScopeDrawPatch's own ring arm draws and
        /// pointer-follows from the scope's FocusedContentRect; drawing in the postfix is the
        /// windowless-scope shape.
        /// </summary>
        public static void RowPostfix(Rect rowRect, Rect iconRect, object __2)
        {
            if (!Recording || __2 == null)
            {
                return;
            }
            Rows.Record(__2, Rect.MinMaxRect(
                Mathf.Min(iconRect.xMin, rowRect.xMin), Mathf.Min(iconRect.yMin, rowRect.yMin),
                Mathf.Max(iconRect.xMax, rowRect.xMax), Mathf.Max(iconRect.yMax, rowRect.yMax)));
        }
    }
}
