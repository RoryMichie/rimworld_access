using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorldAccess.Shell;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// The mod's fumble-recovery curve editor is pure cursor geometry, invisible to the capture
    /// layer. This bracket hosts it as one Button row whose activation offers the editor's own
    /// add/move/remove actions with coordinates chosen through vanilla Dialog_Sliders.
    /// </summary>
    internal static class SidearmsCurveEditorCompat
    {
        public static void Register(Harmony harmony)
        {
            MethodInfo target = AccessTools.Method(
                "PeteTimesSix.SimpleSidearms.UI.Verse.CurveEditorPublic:DoCurveEditor");
            if (target == null)
            {
                return;
            }
            harmony.Patch(target,
                prefix: new HarmonyMethod(typeof(SidearmsCurveEditorCompat), nameof(Prefix)),
                postfix: new HarmonyMethod(typeof(SidearmsCurveEditorCompat), nameof(Postfix)));
        }

        // Mirrors the mod's click clamps: whole x on [0,20], y on [0,1], no remove at the edges.
        private const int CurveXMax = 20;

        private static string RowLabel(SimpleCurve curve)
        {
            return "RimWorldAccess.Compat.SimpleSidearms.CurveRow"
                .Translate(curve?.PointsCount ?? 0).ToString();
        }

        public static void Prefix(Rect screenRect, SimpleCurve curve, out int __state)
        {
            string label = RowLabel(curve);
            __state = WidgetCapture.RecordButton(screenRect, label, false);
            WidgetCapture.EnterSelfCaptioned(label);
        }

        public static void Postfix(int __state, SimpleCurve curve, int displayMult)
        {
            WidgetCapture.ExitSelfCaptioned();
            bool clicked = false;
            WidgetCapture.MaybeForceActivate(__state, ref clicked);
            if (clicked && curve != null)
            {
                OpenPointMenu(curve, displayMult);
            }
        }

        private static void OpenPointMenu(SimpleCurve curve, int displayMult)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            for (int i = 0; i < curve.PointsCount; i++)
            {
                CurvePoint point = curve[i];
                int x = Mathf.RoundToInt(point.x);
                int percent = Mathf.RoundToInt(point.y * displayMult);
                options.Add(new FloatMenuOption(
                    "RimWorldAccess.Compat.SimpleSidearms.OptCurveSet".Translate(x, percent).ToString(),
                    () => PromptPercent(curve, displayMult, x, percent)));
                if (x != 0 && x != CurveXMax)
                {
                    options.Add(new FloatMenuOption(
                        "RimWorldAccess.Compat.SimpleSidearms.OptCurveRemove".Translate(x, percent).ToString(),
                        () =>
                        {
                            // MUTATION-C: mirrors DoCurveEditor's remove action; A/B impossible,
                            // the mod builds its actions from live cursor coordinates mid-draw.
                            curve.RemovePointNear(point);
                            MarkSettingsChanged();
                        }));
                }
            }
            options.Add(new FloatMenuOption(
                "RimWorldAccess.Compat.SimpleSidearms.OptCurveAdd".Translate().ToString(),
                () => Find.WindowStack.Add(new Dialog_Slider(
                    x => "RimWorldAccess.Compat.SimpleSidearms.CurveXPrompt".Translate(x).ToString(),
                    0, CurveXMax,
                    x => PromptPercent(curve, displayMult, x, Mathf.RoundToInt(curve.Evaluate(x) * displayMult))))));
            WindowlessFloatMenuState.OpenTitled(RowLabel(curve), options);
        }

        private static void PromptPercent(SimpleCurve curve, int displayMult, int x, int startingPercent)
        {
            Find.WindowStack.Add(new Dialog_Slider(
                percent => "RimWorldAccess.Compat.SimpleSidearms.CurveYPrompt".Translate(x, percent).ToString(),
                0, displayMult,
                percent =>
                {
                    // MUTATION-C: mirrors DoCurveEditor's add/move actions (add at a free x, move =
                    // re-add at an occupied one), same clamps; A/B impossible as above.
                    for (int i = curve.PointsCount - 1; i >= 0; i--)
                    {
                        if (Mathf.RoundToInt(curve[i].x) == x)
                        {
                            curve.RemovePointNear(curve[i]);
                        }
                    }
                    curve.Add(new CurvePoint(x, (float)percent / displayMult));
                    MarkSettingsChanged();
                },
                startingValue: startingPercent));
        }

        /// <summary>The settings screen's own onChange consequence: any edit demotes the active preset to Custom.</summary>
        private static void MarkSettingsChanged()
        {
            object settings = SidearmsReflection.Settings;
            if (settings != null && SidearmsReflection.SettingsApplyPreset != null
                && SidearmsReflection.SettingsPresetType != null)
            {
                SidearmsReflection.SettingsApplyPreset.Invoke(settings,
                    new[] { Enum.Parse(SidearmsReflection.SettingsPresetType, "Custom") });
            }
        }
    }
}
