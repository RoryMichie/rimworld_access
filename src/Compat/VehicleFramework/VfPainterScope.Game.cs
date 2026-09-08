using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The keyboard focus scope for Vehicle Framework's <c>Vehicles.Dialog_VehiclePainter</c>
    /// (opened by the "VF Recolor" inspection action). All reflection lives in
    /// <see cref="VfPainterCompat"/>; this scope only reads its typed methods.
    ///
    /// Five content regions mirroring the dialog's own panels, plus the automatic Buttons region
    /// (all five real <c>Widgets.ButtonText</c> calls arrive through <see cref="ScreenScope"/>'s
    /// own capture/click-injection, so no <see cref="ScreenScope.DeclaredActions"/> is needed):
    /// <list type="number">
    /// <item>Colors: the three color-slot rows (Enter selects that slot, mirroring
    /// <c>DrawColorSelection</c>'s label-button branch) plus Swap colors.</item>
    /// <item>Adjust color: Hex (Enter opens a modal <see cref="TextFieldEditSession"/>),
    /// Hue/Saturation/Brightness (Left/Right step).</item>
    /// <item>Patterns: a source toggle (patterns/skins) plus one row per
    /// <c>AvailablePatterns</c> entry -- FLATTENED, since vanilla's 2x2 grid pagination is not
    /// reproduced.</item>
    /// <item>Palettes: the 20 saved palette slots.</item>
    /// <item>Pattern placement: Zoom (backed by <c>additionalTiling</c>), Displacement X,
    /// Displacement Y. Labels and tooltips read live from VF's own <c>VF_Pattern*</c> keys
    /// (approved doctrine exception, see <see cref="VfPainterCompat.Vf"/>). All three stay
    /// NAVIGABLE and announce "not available for this pattern" when
    /// <c>selectedPattern.properties.dynamicTiling</c> is false, mirroring vanilla's own
    /// <c>GUIState.Disable()</c> bracket around the three sliders.</item>
    /// </list>
    ///
    /// The rotate-display <c>ButtonImage</c> is deliberately NOT presented: it only spins the
    /// visual preview render and writes no saved state.
    ///
    /// Already-selected color slot: vanilla's <c>Widgets.ButtonInvisible</c> for a slot's label is
    /// gated behind <c>colorSelected != ColorIndex.X</c>, so clicking an already-selected slot
    /// never even registers. This scope re-announces the row instead of falling silent.
    ///
    /// Hex field: the session's <c>apply</c> callback only stores the buffer locally, never
    /// mirroring into the real <c>hex</c> field per keystroke, so a partial hex never repaints the
    /// dialog's swatch; <c>SetColor(string)</c> runs only on confirm, through the game's own
    /// <c>ColorUtility.TryParseHtmlString</c> gate. <c>onExit</c> fires on BOTH Escape and confirm
    /// ahead of <c>onConfirm</c>, so it is intentionally a no-op here -- the confirm path's one
    /// announcement lives entirely in <c>onConfirm</c>, and a generic row re-announcement from
    /// <c>onExit</c> would double-speak it. Escape alone stays silent, since nothing was mutated.
    /// </summary>
    internal sealed class VfPainterScope : ScreenScope
    {
        private const int ColorsRegion = 0;
        private const int AdjustRegion = 1;
        private const int PatternsRegion = 2;
        private const int PalettesRegion = 3;
        private const int PlacementRegion = 4;

        private const int ColorOneRow = 0;
        private const int ColorTwoRow = 1;
        private const int ColorThreeRow = 2;
        private const int SwapRow = 3;

        private const int HexRow = 0;
        private const int HueRow = 1;
        private const int SaturationRow = 2;
        private const int BrightnessRow = 3;

        private const int TilingRow = 0;
        private const int DisplacementXRow = 1;
        private const int DisplacementYRow = 2;

        private const float HsvStep = 0.05f;
        private const float TilingStep = 0.1f;
        private const float DisplacementStep = 0.1f;
        private const float TilingMin = 0.01f;
        private const float TilingMax = 2f;
        private const float DisplacementMin = -1.5f;
        private const float DisplacementMax = 1.5f;

        // Harvested from the dialog's own hex field: SmashTools UIElements.HexField caps
        // Widgets.TextField at MaxHexLength = 7 ('#' + 6 hex digits) and strips the '#', so the
        // bare RRGGBB value the painter edits is exactly six hex digits.
        private const int HexDigitCount = 6;

        private readonly Window dialog;
        private readonly List<object> availablePatterns = new List<object>();
        private readonly TextFieldEditSession hexSession = new TextFieldEditSession();
        private readonly TextFieldSpec hexSpec = new TextFieldSpec(
            labelKey: "RimWorldAccess.TextInput.LabelDefault",
            maxLength: HexDigitCount,
            minLength: 0,
            allowedChars: new Regex("^[0-9A-Fa-f]+$"));
        private string pendingHexEdit = "";
        private bool announcedOpen;

        public VfPainterScope(Window dialog)
        {
            this.dialog = dialog;

            RegisterPopTeardown(hexSession.CancelIfActive);
        }

        public override string Name => "vf-painter";

        /// <summary>Pattern/skin and palette rows are named items worth searching.</summary>
        protected override bool EnableTypeahead => true;

        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen)
                return;
            announcedOpen = true;

            string vehicleLabel = VfPainterCompat.GetVehicle(dialog)?.LabelShortCap;
            if (string.IsNullOrEmpty(vehicleLabel))
                vehicleLabel = VfPainterCompat.GetVehicleDefLabel(dialog);

            TolkHelper.SpeakData((string)"RimWorldAccess.Compat.Vf.PainterOpen".Translate(
                vehicleLabel, availablePatterns.Count));
            AnnounceCurrentItem();
        }

        // ------------------------------------------------------------------
        // ScreenScope content contract.
        // ------------------------------------------------------------------

        protected override int ContentRegionCount => 5;

        protected override string ContentRegionName(int region)
        {
            switch (region)
            {
                case ColorsRegion: return "RimWorldAccess.Compat.Vf.PainterColorsRegion".Translate().ToString();
                case AdjustRegion: return "RimWorldAccess.Compat.Vf.PainterAdjustRegion".Translate().ToString();
                case PatternsRegion: return "RimWorldAccess.Compat.Vf.PainterPatternsRegion".Translate().ToString();
                case PalettesRegion: return "RimWorldAccess.Compat.Vf.PainterPalettesRegion".Translate().ToString();
                case PlacementRegion: return "RimWorldAccess.Compat.Vf.PainterPlacementRegion".Translate().ToString();
                default: return "";
            }
        }

        protected override int ContentItemCount(int region)
        {
            switch (region)
            {
                case ColorsRegion: return 4;
                case AdjustRegion: return 4;
                case PatternsRegion: return 1 + availablePatterns.Count;
                case PalettesRegion: return VfPainterCompat.PaletteCount;
                case PlacementRegion: return 3;
                default: return 0;
            }
        }

        /// <summary>Rebuilds the flattened pattern/skin list every cycle; everything else is read live in DescribeContentItem.</summary>
        protected override void RefreshContent()
        {
            availablePatterns.Clear();
            if (!VfPainterCompat.Ready)
                return;
            IList patterns = VfPainterCompat.GetAvailablePatterns(dialog);
            foreach (object pattern in patterns)
            {
                availablePatterns.Add(pattern);
            }
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            switch (region)
            {
                case ColorsRegion: return DescribeColorsRow(index);
                case AdjustRegion: return DescribeAdjustRow(index);
                case PatternsRegion: return DescribePatternsRow(index);
                case PalettesRegion: return DescribePaletteRow(index);
                case PlacementRegion: return DescribePlacementRow(index);
                default: return new ElementDescription();
            }
        }

        protected override void ActivateContentItem(int region, int index)
        {
            switch (region)
            {
                case ColorsRegion: ActivateColorsRow(index); break;
                case AdjustRegion: ActivateAdjustRow(index); break;
                case PatternsRegion: ActivatePatternsRow(index); break;
                case PalettesRegion: ActivatePaletteRow(index); break;
                case PlacementRegion: AnnounceCurrentItem(); break; // stepper rows: Enter just re-reads the current value
            }
        }

        protected override bool CanAdjustContentItem(int region, int index)
        {
            if (region == AdjustRegion)
                return index != HexRow;
            return region == PlacementRegion;
        }

        protected override void AdjustContentItem(int region, int index, int direction)
        {
            if (region == AdjustRegion)
                AdjustHsvRow(index, direction);
            else if (region == PlacementRegion)
                AdjustPlacementRow(index, direction);
        }

        // ------------------------------------------------------------------
        // Region 0: Colors.
        // ------------------------------------------------------------------

        private ElementDescription DescribeColorsRow(int index)
        {
            var d = new ElementDescription();
            d.Role = ElementRole.Button;
            if (index == SwapRow)
            {
                d.Label = "RimWorldAccess.Compat.Vf.PainterSwapColors".Translate().ToString();
                // VF's own tooltip explains the button cycles all three slots rather than doing a
                // simple one-two swap; announcements use periods, never newlines.
                d.Extras = SpeechFlatten.ToSentences(VfPainterCompat.SwapColorsTooltip);
                return d;
            }

            Color color = VfPainterCompat.GetColor(dialog, index);
            string label = SlotLabel(index) + ": " + ColorNameHelper.NameForColor(color);
            if (VfPainterCompat.GetColorSelected(dialog) == index)
                label += "RimWorldAccess.Compat.Vf.PainterSelectedSuffix".Translate();
            d.Label = label;
            return d;
        }

        private void ActivateColorsRow(int index)
        {
            if (index == SwapRow)
            {
                Color one = VfPainterCompat.GetColor(dialog, ColorOneRow);
                Color two = VfPainterCompat.GetColor(dialog, ColorTwoRow);
                Color three = VfPainterCompat.GetColor(dialog, ColorThreeRow);
                VfPainterCompat.InvokeSetColors(dialog, two, three, one);
                SoundDefOf.Click.PlayOneShotOnCamera();
                RefreshModel();
                TolkHelper.SpeakData((string)"RimWorldAccess.Compat.Vf.PainterSwapped".Translate(
                    ColorNameHelper.NameForColor(VfPainterCompat.GetColor(dialog, ColorOneRow)),
                    ColorNameHelper.NameForColor(VfPainterCompat.GetColor(dialog, ColorTwoRow)),
                    ColorNameHelper.NameForColor(VfPainterCompat.GetColor(dialog, ColorThreeRow))));
                return;
            }

            if (VfPainterCompat.GetColorSelected(dialog) == index)
            {
                // The drawn label button is dead once already selected (see class remarks).
                AnnounceCurrentItem();
                return;
            }

            VfPainterCompat.SetColorSelected(dialog, index);
            VfPainterCompat.InvokeSetColor(dialog, VfPainterCompat.GetColor(dialog, index));
            SoundDefOf.Click.PlayOneShotOnCamera();
            RefreshModel();
            TolkHelper.SpeakData((string)"RimWorldAccess.Compat.Vf.PainterSlotSelected".Translate(SlotLabel(index)));
        }

        private static string SlotLabel(int slot)
        {
            switch (slot)
            {
                case ColorOneRow: return "RimWorldAccess.Compat.Vf.PainterColorOne".Translate().ToString();
                case ColorTwoRow: return "RimWorldAccess.Compat.Vf.PainterColorTwo".Translate().ToString();
                default: return "RimWorldAccess.Compat.Vf.PainterColorThree".Translate().ToString();
            }
        }

        // ------------------------------------------------------------------
        // Region 1: Adjust color.
        // ------------------------------------------------------------------

        private ElementDescription DescribeAdjustRow(int index)
        {
            var d = new ElementDescription();
            if (index == HexRow)
            {
                d.Label = (string)"RimWorldAccess.Compat.Vf.PainterHexRow".Translate(VfPainterCompat.GetHex(dialog));
                d.Role = ElementRole.TextField;
                return d;
            }

            float value = HsvRowValue(index);
            d.Label = HsvRowLabel(index);
            d.Value = value.ToStringPercent();
            d.Role = ElementRole.Stepper;
            d.AtMinimum = value <= 0f;
            d.AtMaximum = value >= 1f;
            return d;
        }

        private void ActivateAdjustRow(int index)
        {
            if (index == HexRow)
            {
                BeginHexEdit(announcePrompt: true);
                return;
            }
            AnnounceCurrentItem();
        }

        private void AdjustHsvRow(int index, int direction)
        {
            if (index == HexRow)
                return;

            float h = VfPainterCompat.GetHue(dialog);
            float s = VfPainterCompat.GetSaturation(dialog);
            float v = VfPainterCompat.GetBrightness(dialog);
            switch (index)
            {
                case HueRow: h = Mathf.Clamp01(h + HsvStep * direction); break;
                case SaturationRow: s = Mathf.Clamp01(s + HsvStep * direction); break;
                default: v = Mathf.Clamp01(v + HsvStep * direction); break;
            }

            VfPainterCompat.InvokeSetColorHsv(dialog, h, s, v);
            SoundDefOf.DragSlider.PlayOneShotOnCamera();
            RefreshModel();

            float newValue = index == HueRow ? h : index == SaturationRow ? s : v;
            int slot = VfPainterCompat.GetColorSelected(dialog);
            string colorName = ColorNameHelper.NameForColor(VfPainterCompat.GetColor(dialog, slot));
            TolkHelper.SpeakData(newValue.ToStringPercent() + ", " + colorName + ".");
        }

        private static string HsvRowLabel(int index)
        {
            switch (index)
            {
                case HueRow: return "RimWorldAccess.Compat.Vf.PainterHue".Translate().ToString();
                case SaturationRow: return "RimWorldAccess.Compat.Vf.PainterSaturation".Translate().ToString();
                default: return "RimWorldAccess.Compat.Vf.PainterBrightness".Translate().ToString();
            }
        }

        private float HsvRowValue(int index)
        {
            switch (index)
            {
                case HueRow: return VfPainterCompat.GetHue(dialog);
                case SaturationRow: return VfPainterCompat.GetSaturation(dialog);
                default: return VfPainterCompat.GetBrightness(dialog);
            }
        }

        // ------------------------------------------------------------------
        // Hex field: browse/edit session.
        // ------------------------------------------------------------------

        private void BeginHexEdit(bool announcePrompt)
        {
            string current = VfPainterCompat.GetHex(dialog) ?? "";
            pendingHexEdit = current;
            hexSession.EnterEdit(
                current,
                hexSpec,
                "RimWorldAccess.Compat.Vf.PainterHexFieldLabel".Translate().ToString(),
                ApplyHexEdit,
                OnHexExit,
                announcePrompt,
                onConfirm: OnHexConfirmed);
        }

        private void ApplyHexEdit(string value)
        {
            pendingHexEdit = value ?? "";
        }

        /// <summary>Intentionally silent -- see class remarks on why a generic re-announce here would double-speak the confirm path's own announcement.</summary>
        private void OnHexExit()
        {
        }

        private void OnHexConfirmed()
        {
            // Success is decided by the dialog's OWN parse gate, not by comparing the submitted
            // string to the canonicalized hex field: a valid 3-digit "#RGB" shorthand parses fine
            // yet would fail that string compare.
            if (!VfPainterCompat.TryParseHex(pendingHexEdit, out _))
            {
                TolkHelper.SpeakData("RimWorldAccess.Compat.Vf.PainterInvalidHex".Translate().ToString());
                return;
            }

            VfPainterCompat.InvokeSetColorHex(dialog, pendingHexEdit);
            RefreshModel();
            int slot = VfPainterCompat.GetColorSelected(dialog);
            TolkHelper.SpeakData(ColorNameHelper.NameForColor(VfPainterCompat.GetColor(dialog, slot)) + ".");
        }

        // ------------------------------------------------------------------
        // Region 2: Patterns.
        // ------------------------------------------------------------------

        private ElementDescription DescribePatternsRow(int index)
        {
            var d = new ElementDescription();
            d.Role = ElementRole.Button;
            if (index == 0)
            {
                bool showPatterns = VfPainterCompat.GetShowPatterns(dialog);
                d.Label = (showPatterns
                    ? "RimWorldAccess.Compat.Vf.PainterShowingPatterns"
                    : "RimWorldAccess.Compat.Vf.PainterShowingSkins").Translate().ToString();
                return d;
            }

            int patternIndex = index - 1;
            if (patternIndex < 0 || patternIndex >= availablePatterns.Count)
                return d;
            object pattern = availablePatterns[patternIndex];
            string label = VfPainterCompat.PatternLabel(pattern);
            if (ReferenceEquals(pattern, VfPainterCompat.GetSelectedPattern()))
                label += "RimWorldAccess.Compat.Vf.PainterSelectedSuffix".Translate();
            d.Label = label;
            return d;
        }

        private void ActivatePatternsRow(int index)
        {
            if (index == 0)
            {
                bool current = VfPainterCompat.GetShowPatterns(dialog);
                VfPainterCompat.SetShowPatterns(dialog, !current);
                VfPainterCompat.InvokeRecacheAvailablePatterns(dialog);
                SoundDefOf.Click.PlayOneShotOnCamera();
                RefreshModel();
                string modeLabel = ((!current)
                    ? "RimWorldAccess.Compat.Vf.PainterShowingPatterns"
                    : "RimWorldAccess.Compat.Vf.PainterShowingSkins").Translate().ToString();
                TolkHelper.SpeakData((string)"RimWorldAccess.Compat.Vf.PainterSourceSwitched".Translate(modeLabel, availablePatterns.Count));
                return;
            }

            int patternIndex = index - 1;
            if (patternIndex < 0 || patternIndex >= availablePatterns.Count)
                return;
            object pattern = availablePatterns[patternIndex];
            VfPainterCompat.SetSelectedPattern(pattern);
            SoundDefOf.Click.PlayOneShotOnCamera();
            RefreshModel();
            TolkHelper.SpeakData((string)"RimWorldAccess.Compat.Vf.PainterPatternSelected".Translate(VfPainterCompat.PatternLabel(pattern)));
        }

        // ------------------------------------------------------------------
        // Region 3: Palettes.
        // ------------------------------------------------------------------

        private ElementDescription DescribePaletteRow(int index)
        {
            var d = new ElementDescription();
            d.Role = ElementRole.Button;
            List<(Color, Color, Color)> palettes = VfPainterCompat.GetPalettes();
            if (index < 0 || index >= palettes.Count)
                return d;

            (Color c1, Color c2, Color c3) = palettes[index];
            List<string> names = ColorNameHelper.NamesForColors(new List<Color> { c1, c2, c3 });
            string label = (string)"RimWorldAccess.Compat.Vf.PainterPaletteRow".Translate(index + 1, names[0], names[1], names[2]);
            if (VfPainterCompat.GetCurrentSelectedPalette(dialog) == index)
                label += "RimWorldAccess.Compat.Vf.PainterSelectedSuffix".Translate();
            d.Label = label;
            return d;
        }

        /// <summary>Mirrors DrawColorPalette's palette-cell ButtonInvisible branch, including its lack of a click sound.</summary>
        private void ActivatePaletteRow(int index)
        {
            List<(Color, Color, Color)> palettes = VfPainterCompat.GetPalettes();
            if (index < 0 || index >= palettes.Count)
                return;

            int current = VfPainterCompat.GetCurrentSelectedPalette(dialog);
            if (current == index)
            {
                VfPainterCompat.SetCurrentSelectedPalette(dialog, -1);
                RefreshModel();
                TolkHelper.SpeakData((string)"RimWorldAccess.Compat.Vf.PainterPaletteDeselected".Translate(index + 1));
                return;
            }

            (Color c1, Color c2, Color c3) = palettes[index];
            VfPainterCompat.SetCurrentSelectedPalette(dialog, index);
            VfPainterCompat.InvokeSetColors(dialog, c1, c2, c3);
            RefreshModel();
            List<string> names = ColorNameHelper.NamesForColors(new List<Color> { c1, c2, c3 });
            TolkHelper.SpeakData((string)"RimWorldAccess.Compat.Vf.PainterPaletteApplied".Translate(index + 1, names[0], names[1], names[2]));
        }

        // ------------------------------------------------------------------
        // Region 4: Pattern placement.
        // ------------------------------------------------------------------

        private ElementDescription DescribePlacementRow(int index)
        {
            var d = new ElementDescription();
            object pattern = VfPainterCompat.GetSelectedPattern();
            bool dynamic = VfPainterCompat.PatternDynamicTiling(pattern);
            string rowName = PlacementRowLabel(index);
            d.Extras = PlacementRowTooltip(index);
            if (!dynamic)
            {
                d.Label = (string)"RimWorldAccess.Compat.Vf.PainterSliderUnavailable".Translate(rowName);
                return d;
            }

            float value = PlacementRowValue(index);
            float min = index == TilingRow ? TilingMin : DisplacementMin;
            float max = index == TilingRow ? TilingMax : DisplacementMax;
            d.Label = rowName;
            d.Value = value.ToString("0.00");
            d.Role = ElementRole.Stepper;
            d.AtMinimum = value <= min;
            d.AtMaximum = value >= max;
            return d;
        }

        private void AdjustPlacementRow(int index, int direction)
        {
            object pattern = VfPainterCompat.GetSelectedPattern();
            if (!VfPainterCompat.PatternDynamicTiling(pattern))
            {
                AnnounceCurrentItem();
                return;
            }

            switch (index)
            {
                case TilingRow:
                {
                    float t = Mathf.Clamp(VfPainterCompat.GetAdditionalTiling(dialog) + TilingStep * direction, TilingMin, TilingMax);
                    VfPainterCompat.SetAdditionalTiling(dialog, t);
                    break;
                }
                case DisplacementXRow:
                {
                    float x = Mathf.Clamp(VfPainterCompat.GetDisplacementX(dialog) + DisplacementStep * direction, DisplacementMin, DisplacementMax);
                    VfPainterCompat.SetDisplacementX(dialog, x);
                    break;
                }
                default:
                {
                    float y = Mathf.Clamp(VfPainterCompat.GetDisplacementY(dialog) + DisplacementStep * direction, DisplacementMin, DisplacementMax);
                    VfPainterCompat.SetDisplacementY(dialog, y);
                    break;
                }
            }

            SoundDefOf.DragSlider.PlayOneShotOnCamera();
            AnnounceCurrentItem();
        }

        /// <summary>VF's own control names (see VfPainterCompat.Vf), not this mod's XML, so the spoken name matches VF's slider labels exactly.</summary>
        private static string PlacementRowLabel(int index)
        {
            switch (index)
            {
                case TilingRow: return VfPainterCompat.PatternZoomLabel;
                case DisplacementXRow: return VfPainterCompat.DisplacementXLabel;
                default: return VfPainterCompat.DisplacementYLabel;
            }
        }

        /// <summary>VF's own hover tooltip for each placement slider (see VfPainterCompat.Vf).</summary>
        private static string PlacementRowTooltip(int index)
        {
            switch (index)
            {
                case TilingRow: return VfPainterCompat.PatternZoomTooltip;
                case DisplacementXRow: return VfPainterCompat.DisplacementXTooltip;
                default: return VfPainterCompat.DisplacementYTooltip;
            }
        }

        private float PlacementRowValue(int index)
        {
            switch (index)
            {
                case TilingRow: return VfPainterCompat.GetAdditionalTiling(dialog);
                case DisplacementXRow: return VfPainterCompat.GetDisplacementX(dialog);
                default: return VfPainterCompat.GetDisplacementY(dialog);
            }
        }
    }
}
