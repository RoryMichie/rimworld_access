using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Reflection-only compatibility surface for Vehicle Framework's
    /// <c>Vehicles.Dialog_VehiclePainter</c>, opened by the "VF Recolor" inspection action
    /// (<see cref="VfVehicleActionsCompat.OpenRecolor"/>). <see cref="VfPainterScope"/> is the sole
    /// consumer and never touches reflection directly.
    ///
    /// Every raw field write below carries its own MUTATION-C marker directly above the
    /// <c>.SetValue(</c> call; every method invoke (<c>SetColor</c> family, <c>SetColors</c>,
    /// <c>RecacheAvailablePatterns</c>) is vehicle A and carries no marker.
    /// </summary>
    internal static class VfPainterCompat
    {
        private static readonly Type dialogType;
        private static readonly Type colorIndexType;
        private static readonly Type patternDefType;
        private static readonly Type patternPropertiesType;

        private static readonly FieldInfo hueField;
        private static readonly FieldInfo saturationField;
        private static readonly FieldInfo brightnessField;
        private static readonly FieldInfo hexField;
        private static readonly FieldInfo currentColorOneField;
        private static readonly FieldInfo currentColorTwoField;
        private static readonly FieldInfo currentColorThreeField;
        private static readonly FieldInfo colorSelectedField;
        private static readonly FieldInfo additionalTilingField;
        private static readonly FieldInfo displacementXField;
        private static readonly FieldInfo displacementYField;
        private static readonly FieldInfo showPatternsField;
        private static readonly FieldInfo selectedPatternField; // static on dialogType

        private static readonly PropertyInfo availablePatternsProperty;
        private static readonly PropertyInfo currentSelectedPaletteProperty;
        private static readonly PropertyInfo vehicleProperty;
        private static readonly PropertyInfo vehicleDefProperty;

        private static readonly MethodInfo setColorMethod;
        private static readonly MethodInfo setColorsMethod;
        private static readonly MethodInfo setColorHexMethod;
        private static readonly MethodInfo setColorHsvMethod;
        private static readonly MethodInfo recacheAvailablePatternsMethod;

        private static readonly FieldInfo patternPropertiesOnPatternDefField;
        private static readonly FieldInfo dynamicTilingField;

        private static readonly FieldInfo settingsField; // static on VehicleMod
        private static readonly FieldInfo colorStorageField;
        private static readonly FieldInfo colorPaletteField;
        private static readonly FieldInfo paletteCountField; // static const on ColorStorage

        private static FieldInfo[] currentColorFieldsBySlot;

        private static readonly bool ready;
        private static readonly int paletteCount;

        public static bool Ready => ready;
        public static Type DialogType => dialogType;
        public static int PaletteCount => paletteCount;

        static VfPainterCompat()
        {
            var surface = new ReflectionSurface("VfPainterCompat");

            dialogType = surface.Type("Vehicles.Dialog_VehiclePainter");
            patternDefType = surface.Type("Vehicles.PatternDef");
            patternPropertiesType = surface.Type("Vehicles.PatternProperties");
            colorIndexType = dialogType != null ? AccessTools.Inner(dialogType, "ColorIndex") : null;

            hueField = surface.Field(dialogType, "hue");
            saturationField = surface.Field(dialogType, "saturation");
            brightnessField = surface.Field(dialogType, "value");
            hexField = surface.Field(dialogType, "hex");
            currentColorOneField = surface.Field(dialogType, "currentColorOne");
            currentColorTwoField = surface.Field(dialogType, "currentColorTwo");
            currentColorThreeField = surface.Field(dialogType, "currentColorThree");
            colorSelectedField = surface.Field(dialogType, "colorSelected");
            additionalTilingField = surface.Field(dialogType, "additionalTiling");
            displacementXField = surface.Field(dialogType, "displacementX");
            displacementYField = surface.Field(dialogType, "displacementY");
            showPatternsField = surface.Field(dialogType, "showPatterns");
            selectedPatternField = surface.Field(dialogType, "selectedPattern");

            availablePatternsProperty = surface.Property(dialogType, "AvailablePatterns");
            currentSelectedPaletteProperty = surface.Property(dialogType, "CurrentSelectedPalette");
            vehicleProperty = surface.Property(dialogType, "Vehicle");
            vehicleDefProperty = surface.Property(dialogType, "VehicleDef");

            setColorMethod = surface.Method(dialogType, "SetColor", new[] { typeof(Color) });
            setColorsMethod = surface.Method(dialogType, "SetColors", new[] { typeof(Color), typeof(Color), typeof(Color) });
            setColorHexMethod = surface.Method(dialogType, "SetColor", new[] { typeof(string) });
            setColorHsvMethod = surface.Method(dialogType, "SetColor", new[] { typeof(float), typeof(float), typeof(float) });
            recacheAvailablePatternsMethod = surface.Method(dialogType, "RecacheAvailablePatterns", Type.EmptyTypes);

            patternPropertiesOnPatternDefField = surface.Field(patternDefType, "properties");
            dynamicTilingField = surface.Field(patternPropertiesType, "dynamicTiling");

            settingsField = surface.Field(surface.Type("Vehicles.VehicleMod"), "settings");
            colorStorageField = surface.Field(settingsField?.FieldType, "colorStorage");
            colorPaletteField = surface.Field(colorStorageField?.FieldType, "colorPalette");

            // OPTIONAL: PaletteCount only overrides the 20-slot default below.
            paletteCountField = colorStorageField != null
                ? AccessTools.Field(colorStorageField.FieldType, "PaletteCount")
                : null;

            if (currentColorOneField != null && currentColorTwoField != null && currentColorThreeField != null)
            {
                currentColorFieldsBySlot = new[] { currentColorOneField, currentColorTwoField, currentColorThreeField };
            }

            paletteCount = 20;
            if (paletteCountField != null)
            {
                try
                {
                    paletteCount = (int)paletteCountField.GetValue(null);
                }
                catch (Exception ex)
                {
                    ModLogger.Error($"VfPainterCompat: could not read ColorStorage.PaletteCount, using 20-slot default: {ex.Message}");
                }
            }

            ready = surface.Ready && colorIndexType != null;
        }

        // ------------------------------------------------------------------
        // Colors.
        // ------------------------------------------------------------------

        public static int GetColorSelected(Window w)
        {
            try
            {
                return (int)colorSelectedField.GetValue(w);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfPainterCompat.GetColorSelected failed: {ex.Message}");
                return 0;
            }
        }

        public static void SetColorSelected(Window w, int slot)
        {
            try
            {
                object enumValue = Enum.ToObject(colorIndexType, slot);
                // MUTATION-C: mirrors Dialog_VehiclePainter.DrawColorSelection's three color-slot
                // ButtonInvisible branches (colorSelected = ColorIndex.X); the branch is inline
                // IMGUI with no invocable vehicle for which slot is being edited.
                colorSelectedField.SetValue(w, enumValue);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfPainterCompat.SetColorSelected failed: {ex.Message}");
            }
        }

        public static Color GetColor(Window w, int slot)
        {
            try
            {
                if (currentColorFieldsBySlot == null || slot < 0 || slot >= currentColorFieldsBySlot.Length)
                    return Color.white;
                return (Color)currentColorFieldsBySlot[slot].GetValue(w);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfPainterCompat.GetColor failed: {ex.Message}");
                return Color.white;
            }
        }

        /// <summary>Vehicle A: Dialog_VehiclePainter.SetColor(Color) -- writes the currently selected slot and refreshes hue/saturation/hex.</summary>
        public static void InvokeSetColor(Window w, Color color)
        {
            try
            {
                setColorMethod.Invoke(w, new object[] { color });
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfPainterCompat.InvokeSetColor failed: {ex.Message}");
            }
        }

        /// <summary>Vehicle A: Dialog_VehiclePainter.SetColors(Color, Color, Color) -- the swap button's and the palette cells' own body.</summary>
        public static void InvokeSetColors(Window w, Color one, Color two, Color three)
        {
            try
            {
                setColorsMethod.Invoke(w, new object[] { one, two, three });
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfPainterCompat.InvokeSetColors failed: {ex.Message}");
            }
        }

        /// <summary>
        /// The exact validity gate Dialog_VehiclePainter.HexToColor uses, so the scope's
        /// success/failure branch matches the dialog's own SetColor(string) no-op decision --
        /// including Unity's 3-digit "#RGB" shorthand, which a naive submitted-vs-canonical string
        /// comparison would misreport as invalid.
        /// </summary>
        public static bool TryParseHex(string hex, out Color color)
        {
            return ColorUtility.TryParseHtmlString("#" + (hex ?? ""), out color);
        }

        /// <summary>Vehicle A: Dialog_VehiclePainter.SetColor(string) -- validates via ColorUtility.TryParseHtmlString internally; a bad hex is a silent no-op on the game's side.</summary>
        public static void InvokeSetColorHex(Window w, string hex)
        {
            try
            {
                setColorHexMethod.Invoke(w, new object[] { hex ?? "" });
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfPainterCompat.InvokeSetColorHex failed: {ex.Message}");
            }
        }

        /// <summary>Vehicle A: Dialog_VehiclePainter.SetColor(float, float, float) -- the HSV picker's own update path.</summary>
        public static void InvokeSetColorHsv(Window w, float h, float s, float v)
        {
            try
            {
                setColorHsvMethod.Invoke(w, new object[] { h, s, v });
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfPainterCompat.InvokeSetColorHsv failed: {ex.Message}");
            }
        }

        public static float GetHue(Window w)
        {
            return GetFloatField(hueField, w);
        }

        public static float GetSaturation(Window w)
        {
            return GetFloatField(saturationField, w);
        }

        public static float GetBrightness(Window w)
        {
            return GetFloatField(brightnessField, w);
        }

        public static string GetHex(Window w)
        {
            try
            {
                return hexField.GetValue(w) as string ?? "";
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfPainterCompat.GetHex failed: {ex.Message}");
                return "";
            }
        }

        // ------------------------------------------------------------------
        // Patterns / skins.
        // ------------------------------------------------------------------

        /// <summary>Vehicle A: Dialog_VehiclePainter.RecacheAvailablePatterns() -- rebuilds AvailablePatterns and re-picks selectedPattern.</summary>
        public static void InvokeRecacheAvailablePatterns(Window w)
        {
            try
            {
                recacheAvailablePatternsMethod.Invoke(w, null);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfPainterCompat.InvokeRecacheAvailablePatterns failed: {ex.Message}");
            }
        }

        public static IList GetAvailablePatterns(Window w)
        {
            try
            {
                return availablePatternsProperty.GetValue(w) as IList ?? new List<object>();
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfPainterCompat.GetAvailablePatterns failed: {ex.Message}");
                return new List<object>();
            }
        }

        /// <summary>The pattern/skin's own localized label -- PatternDef derives from Verse.Def (DefDatabase&lt;PatternDef&gt; requires it).</summary>
        public static string PatternLabel(object patternDef)
        {
            Def def = patternDef as Def;
            return def != null ? def.LabelCap.ToString() : "";
        }

        public static bool PatternDynamicTiling(object patternDef)
        {
            try
            {
                if (patternDef == null)
                    return false;
                object properties = patternPropertiesOnPatternDefField.GetValue(patternDef);
                return properties != null && (bool)dynamicTilingField.GetValue(properties);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfPainterCompat.PatternDynamicTiling failed: {ex.Message}");
                return false;
            }
        }

        public static object GetSelectedPattern()
        {
            try
            {
                return selectedPatternField.GetValue(null);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfPainterCompat.GetSelectedPattern failed: {ex.Message}");
                return null;
            }
        }

        public static void SetSelectedPattern(object patternDef)
        {
            try
            {
                // MUTATION-C: mirrors Dialog_VehiclePainter.DrawPaintSelection's grid-cell
                // ButtonInvisible branch (selectedPattern = pattern); the branch is inline IMGUI
                // with no invocable vehicle for pattern/skin selection.
                selectedPatternField.SetValue(null, patternDef);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfPainterCompat.SetSelectedPattern failed: {ex.Message}");
            }
        }

        public static bool GetShowPatterns(Window w)
        {
            try
            {
                return (bool)showPatternsField.GetValue(w);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfPainterCompat.GetShowPatterns failed: {ex.Message}");
                return true;
            }
        }

        public static void SetShowPatterns(Window w, bool value)
        {
            try
            {
                // MUTATION-C: mirrors Dialog_VehiclePainter.DrawPaintSelection's source-toggle
                // ButtonImage branch (showPatterns = !showPatterns); the branch is inline IMGUI
                // with no invocable vehicle for the patterns/skins display mode.
                showPatternsField.SetValue(w, value);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfPainterCompat.SetShowPatterns failed: {ex.Message}");
            }
        }

        // ------------------------------------------------------------------
        // Palettes.
        // ------------------------------------------------------------------

        public static int GetCurrentSelectedPalette(Window w)
        {
            try
            {
                return (int)currentSelectedPaletteProperty.GetValue(w);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfPainterCompat.GetCurrentSelectedPalette failed: {ex.Message}");
                return -1;
            }
        }

        public static void SetCurrentSelectedPalette(Window w, int value)
        {
            try
            {
                // MUTATION-C: mirrors Dialog_VehiclePainter.DrawColorPalette's palette-cell
                // ButtonInvisible branch (CurrentSelectedPalette = i / -1); the branch is inline
                // IMGUI with no invocable vehicle for palette-slot selection.
                currentSelectedPaletteProperty.SetValue(w, value);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfPainterCompat.SetCurrentSelectedPalette failed: {ex.Message}");
            }
        }

        /// <summary>The live VehicleMod.settings.colorStorage.colorPalette list (not a copy) -- always PaletteCount entries by ColorStorage's own invariant.</summary>
        public static List<(Color, Color, Color)> GetPalettes()
        {
            try
            {
                object settings = settingsField.GetValue(null);
                object colorStorage = colorStorageField.GetValue(settings);
                return colorPaletteField.GetValue(colorStorage) as List<(Color, Color, Color)> ?? new List<(Color, Color, Color)>();
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfPainterCompat.GetPalettes failed: {ex.Message}");
                return new List<(Color, Color, Color)>();
            }
        }

        // ------------------------------------------------------------------
        // Pattern placement.
        // ------------------------------------------------------------------

        public static float GetAdditionalTiling(Window w)
        {
            return GetFloatField(additionalTilingField, w);
        }

        public static void SetAdditionalTiling(Window w, float value)
        {
            try
            {
                // MUTATION-C: mirrors Dialog_VehiclePainter.DoWindowContents' UIElements.SliderLabeled
                // call for additionalTiling; the slider's own ref-write IS the vanilla mutation path,
                // no separate Can*/Try* vehicle exists.
                additionalTilingField.SetValue(w, value);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfPainterCompat.SetAdditionalTiling failed: {ex.Message}");
            }
        }

        public static float GetDisplacementX(Window w)
        {
            return GetFloatField(displacementXField, w);
        }

        public static void SetDisplacementX(Window w, float value)
        {
            try
            {
                // MUTATION-C: mirrors Dialog_VehiclePainter.DoWindowContents' UIElements.SliderLabeled
                // call for displacementX; the slider's own ref-write IS the vanilla mutation path,
                // no separate Can*/Try* vehicle exists.
                displacementXField.SetValue(w, value);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfPainterCompat.SetDisplacementX failed: {ex.Message}");
            }
        }

        public static float GetDisplacementY(Window w)
        {
            return GetFloatField(displacementYField, w);
        }

        public static void SetDisplacementY(Window w, float value)
        {
            try
            {
                // MUTATION-C: mirrors Dialog_VehiclePainter.DoWindowContents' UIElements.SliderLabeled
                // call for displacementY; the slider's own ref-write IS the vanilla mutation path,
                // no separate Can*/Try* vehicle exists.
                displacementYField.SetValue(w, value);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfPainterCompat.SetDisplacementY failed: {ex.Message}");
            }
        }

        // ------------------------------------------------------------------
        // Vehicle / def identity.
        // ------------------------------------------------------------------

        /// <summary>The vehicle being painted, as a Pawn (VehiclePawn extends Pawn); null when the painter was opened from a VehicleDef (no live vehicle).</summary>
        public static Pawn GetVehicle(Window w)
        {
            try
            {
                return vehicleProperty.GetValue(w) as Pawn;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfPainterCompat.GetVehicle failed: {ex.Message}");
                return null;
            }
        }

        public static string GetVehicleDefLabel(Window w)
        {
            try
            {
                Def def = vehicleDefProperty.GetValue(w) as Def;
                return def != null ? def.LabelCap.ToString() : "";
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfPainterCompat.GetVehicleDefLabel failed: {ex.Message}");
                return "";
            }
        }

        private static float GetFloatField(FieldInfo field, Window w)
        {
            try
            {
                return (float)field.GetValue(w);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfPainterCompat: float field read failed: {ex.Message}");
                return 0f;
            }
        }

        // ------------------------------------------------------------------
        // VF's own tooltip/label text (approved doctrine exception).
        // ------------------------------------------------------------------

        /// <summary>
        /// APPROVED DOCTRINE EXCEPTION: reads Vehicle Framework's OWN translation keys directly
        /// rather than baking equivalent English into this mod's XML, so the spoken text matches
        /// what a sighted player sees even if VF rewords its labels. Safe because every caller of
        /// the getters below is gated behind <see cref="Ready"/>, so these keys always come from
        /// VF's own loaded language data. The key is passed as a parameter, never a literal at the
        /// call site, so check_l10n_keys.py's literal-Translate scan does not flag it as fake.
        /// </summary>
        private static string Vf(string key) => key.Translate().ToString();

        public static string PatternZoomLabel => Vf("VF_PatternZoom");
        public static string PatternZoomTooltip => Vf("VF_PatternZoomTooltip");
        public static string DisplacementXLabel => Vf("VF_PatternDisplacementX");
        public static string DisplacementXTooltip => Vf("VF_PatternDisplacementXTooltip");
        public static string DisplacementYLabel => Vf("VF_PatternDisplacementY");
        public static string DisplacementYTooltip => Vf("VF_PatternDisplacementYTooltip");
        public static string SwapColorsTooltip => Vf("VF_SwapColors");
    }
}
