using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Keyboard focus scope for the vanilla color-picker family (<see cref="Dialog_ColorPickerBase"/>:
    /// the allowed-area picker and the glower picker, plus any mod subclass), registered by
    /// hierarchy. Three content regions plus the captured Cancel/Accept buttons:
    /// <list type="bullet">
    /// <item>Palette — the dialog's own Default box, its Darklight box where vanilla shows one,
    /// then every <c>PickableColors</c> swatch, all named through <see cref="ColorNameHelper"/>.
    /// Enter stages the swatch exactly as a click would.</item>
    /// <item>Adjust — Hue and Saturation stepper rows (vanilla's own field captions), the
    /// keyboard stand-in for the HSV wheel and the color-temperature bar. Both of those only
    /// ever write hue/saturation at the dialog's <c>ForcedColorValue</c>, so two steppers reach
    /// the wheel's whole space.</item>
    /// <item>Readback — vanilla's Current/Old color rows, plus its darklight verdict where
    /// shown.</item>
    /// </list>
    /// Everything stages into the dialog's protected <c>color</c>; nothing commits until
    /// vanilla's own Accept (captured; also the Shift+Enter default) runs <c>SaveColor</c>.
    /// </summary>
    public sealed class ColorPickerScope : ScreenScope
    {
        private const int PaletteRegion = 0;
        private const int AdjustRegion = 1;
        private const int ReadbackRegion = 2;

        private const int HueStep = 10;
        private const int SatStep = 5;

        private static readonly AccessTools.FieldRef<Dialog_ColorPickerBase, Color> colorField =
            AccessTools.FieldRefAccess<Dialog_ColorPickerBase, Color>("color");
        private static readonly AccessTools.FieldRef<Dialog_ColorPickerBase, Color> oldColorField =
            AccessTools.FieldRefAccess<Dialog_ColorPickerBase, Color>("oldColor");
        private static readonly PropertyInfo showDarklightProperty =
            AccessTools.Property(typeof(Dialog_ColorPickerBase), "ShowDarklight");
        private static readonly PropertyInfo defaultColorProperty =
            AccessTools.Property(typeof(Dialog_ColorPickerBase), "DefaultColor");
        private static readonly PropertyInfo pickableColorsProperty =
            AccessTools.Property(typeof(Dialog_ColorPickerBase), "PickableColors");
        private static readonly PropertyInfo forcedColorValueProperty =
            AccessTools.Property(typeof(Dialog_ColorPickerBase), "ForcedColorValue");
        private static readonly MethodInfo saveColorMethod =
            AccessTools.Method(typeof(Dialog_ColorPickerBase), "SaveColor");

        private readonly Dialog_ColorPickerBase dialog;
        private readonly List<Color> palette;
        private readonly List<string> paletteNames;
        private readonly bool showDarklight;
        private readonly float forcedValue;
        // Vanilla's per-frame defaultColor (DoWindowContents): DefaultColor re-forced to the
        // dialog's value plane, alpha 1. Both inputs are fixed for a dialog's lifetime.
        private readonly Color defaultColor;

        public ColorPickerScope(Dialog_ColorPickerBase dialog)
        {
            this.dialog = dialog;
            palette = pickableColorsProperty?.GetValue(dialog) as List<Color> ?? new List<Color>();
            paletteNames = ColorNameHelper.NamesForColors(palette);
            showDarklight = (bool)(showDarklightProperty?.GetValue(dialog) ?? false);
            forcedValue = (float)(forcedColorValueProperty?.GetValue(dialog) ?? 1f);
            Color raw = (Color)(defaultColorProperty?.GetValue(dialog) ?? Color.white);
            Color.RGBToHSV(raw, out float h, out float s, out _);
            Color forced = Color.HSVToRGB(h, s, forcedValue);
            forced.a = 1f;
            defaultColor = forced;
        }

        public override string Name
        {
            get { return "color-picker"; }
        }

        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        /// <summary>
        /// The live scope owning <paramref name="window"/>, for this dialog's patches
        /// (<see cref="TextDialogShared.ScopeOwning{T}"/>).
        /// </summary>
        internal static ColorPickerScope OwningScope(Window window)
        {
            return TextDialogShared.ScopeOwning<ColorPickerScope>(
                window, delegate(ColorPickerScope s, Window w) { return ReferenceEquals(s.dialog, w); });
        }

        protected override string ComposeOpenAnnouncement()
        {
            return "ChooseAColor".Translate().CapitalizeFirst().ToString();
        }

        private Color CurrentColor
        {
            get { return colorField(dialog); }
        }

        // MUTATION-C: mirrors the `ref color` writes of Widgets.ColorBox / Widgets.ColorSelector /
        // Widgets.HSVColorWheel (staging only; the game change stays behind vanilla's Accept ->
        // SaveColor). No invocable vehicle exists for a widget's click-side ref write.
        private void StageColor(Color value)
        {
            colorField(dialog) = value;
        }

        // --- Row model ---

        /// <summary>Palette rows ahead of the swatches: Default, then Darklight when vanilla shows it.</summary>
        private int SpecialRows
        {
            get { return showDarklight ? 2 : 1; }
        }

        protected override int ContentRegionCount
        {
            get { return 3; }
        }

        protected override string ContentRegionName(int region)
        {
            switch (region)
            {
                case PaletteRegion:
                    return "RimWorldAccess.Shell.ColorPicker.PaletteRegion".Translate().ToString();
                case AdjustRegion:
                    return "RimWorldAccess.Shell.ColorPicker.AdjustRegion".Translate().ToString();
                default:
                    return "RimWorldAccess.Shell.ColorPicker.ReadbackRegion".Translate().ToString();
            }
        }

        protected override int ContentItemCount(int region)
        {
            switch (region)
            {
                case PaletteRegion:
                    return SpecialRows + palette.Count;
                case AdjustRegion:
                    return 2;
                default:
                    return showDarklight ? 3 : 2;
            }
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            switch (region)
            {
                case PaletteRegion:
                    return DescribePaletteRow(index);
                case AdjustRegion:
                    return DescribeAdjustRow(index);
                default:
                    return DescribeReadbackRow(index);
            }
        }

        private ElementDescription DescribePaletteRow(int index)
        {
            var d = new ElementDescription { Role = ElementRole.RadioButton };
            Color rowColor;
            if (index == 0)
            {
                d.Label = "Default".Translate().CapitalizeFirst().ToString();
                rowColor = defaultColor;
                d.Value = ColorNameHelper.NameForColor(rowColor);
            }
            else if (showDarklight && index == 1)
            {
                d.Label = "Darklight".Translate().CapitalizeFirst().ToString();
                rowColor = DarklightUtility.DefaultDarklight;
                d.Value = ColorNameHelper.NameForColor(rowColor);
            }
            else
            {
                int swatch = index - SpecialRows;
                if (swatch < 0 || swatch >= palette.Count)
                {
                    return new ElementDescription();
                }
                d.Label = paletteNames[swatch];
                rowColor = palette[swatch];
            }
            d.Selected = CurrentColor.IndistinguishableFrom(rowColor);
            return d;
        }

        private ElementDescription DescribeAdjustRow(int index)
        {
            Color.RGBToHSV(CurrentColor, out float h, out float s, out _);
            var d = new ElementDescription { Role = ElementRole.Slider };
            if (index == 0)
            {
                // Vanilla's own field captions (Widgets.ColorTextfields) and integer ranges.
                d.Label = "Hue".Translate().CapitalizeFirst().ToString();
                d.Value = Mathf.RoundToInt(h * 360f).ToString();
            }
            else
            {
                d.Label = "Saturation".Translate().CapitalizeFirst().ToString();
                d.Value = Mathf.RoundToInt(s * 100f).ToString();
            }
            d.Extras = ColorNameHelper.NameForColor(CurrentColor);
            return d;
        }

        private ElementDescription DescribeReadbackRow(int index)
        {
            var d = new ElementDescription { ReadOnly = true };
            if (index == 0)
            {
                d.Label = "CurrentColor".Translate().CapitalizeFirst().ToString();
                d.Value = ColorNameHelper.NameForColor(CurrentColor);
            }
            else if (index == 1)
            {
                d.Label = "OldColor".Translate().CapitalizeFirst().ToString();
                d.Value = ColorNameHelper.NameForColor(oldColorField(dialog));
            }
            else
            {
                d.Label = (DarklightUtility.IsDarklight(CurrentColor)
                    ? "Darklight"
                    : "NotDarklight").Translate().CapitalizeFirst().ToString();
            }
            return d;
        }

        protected override void ActivateContentItem(int region, int index)
        {
            if (region != PaletteRegion)
            {
                AnnounceCurrentItem();
                return;
            }
            Color target;
            if (index == 0)
            {
                target = defaultColor;
            }
            else if (showDarklight && index == 1)
            {
                target = DarklightUtility.DefaultDarklight;
            }
            else
            {
                int swatch = index - SpecialRows;
                if (swatch < 0 || swatch >= palette.Count)
                {
                    return;
                }
                target = palette[swatch];
            }
            StageColor(target);
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            AnnounceCurrentItem();
        }

        protected override bool CanAdjustContentItem(int region, int index)
        {
            return region == AdjustRegion;
        }

        /// <summary>Left/Right on a Hue/Saturation row: step, restage, speak the new value and the color it lands on.</summary>
        protected override void AdjustContentItem(int region, int index, int direction)
        {
            if (region != AdjustRegion)
            {
                return;
            }
            Color.RGBToHSV(CurrentColor, out float h, out float s, out _);
            int hue = Mathf.RoundToInt(h * 360f);
            int sat = Mathf.RoundToInt(s * 100f);
            string value;
            if (index == 0)
            {
                // Hue is circular, exactly as the wheel treats it.
                hue = (hue + direction * HueStep + 360) % 360;
                value = hue.ToString();
            }
            else
            {
                int stepped = Mathf.Clamp(sat + direction * SatStep, 0, 100);
                if (stepped == sat)
                {
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    return;
                }
                sat = stepped;
                value = sat.ToString();
            }
            StageColor(Color.HSVToRGB(hue / 360f, sat / 100f, forcedValue));
            TolkHelper.SpeakData(value + ". " + ColorNameHelper.NameForColor(CurrentColor));
        }

        /// <summary>Shift+Enter (and the inert-row double-Enter) presses vanilla's own Accept.</summary>
        protected override ScreenAction CapturedDefaultAcceptAction
        {
            get
            {
                return new ScreenAction("Accept".Translate().ToString(), AcceptAndClose);
            }
        }

        // Vehicle A: the same two calls vanilla's Accept button body makes.
        private void AcceptAndClose()
        {
            saveColorMethod?.Invoke(dialog, new object[] { CurrentColor });
            dialog.Close();
        }

        // --- Focus ring over the swatches vanilla's own ColorSelector pass records ---

        public override void OnPush()
        {
            base.OnPush();
            ColorSelectorDrawPatch.AddInterest();
        }

        public override void OnPop()
        {
            ColorSelectorDrawPatch.RemoveInterest();
            base.OnPop();
        }

        protected internal override Rect FocusedContentRect()
        {
            if (Model.RegionIndex != PaletteRegion)
            {
                return default(Rect);
            }
            ListModel list = Model.CurrentRegion;
            if (list == null || list.IsEmpty)
            {
                return default(Rect);
            }
            int swatch = list.Index - SpecialRows;
            if (swatch < 0 || swatch >= palette.Count)
            {
                return default(Rect);
            }
            Rect screen, local, groupRect;
            if (!ColorSelectorDrawPatch.TryGetSoleSwatch(palette.Count, swatch, out screen, out local, out groupRect))
            {
                return default(Rect);
            }
            return screen;
        }
    }

    /// <summary>
    /// Masks Tab around the dialog's own draw while this scope owns it. The dialog's private
    /// TabControl polls Event.current for a Tab KeyDown and Use()s it to cycle its native
    /// text-field focus; that GUI pass can run BEFORE the dispatcher (QA R6), which would starve
    /// the scope's region cycling. Prefix stash + postfix restore, never Use().
    /// </summary>
    [HarmonyPatch(typeof(Dialog_ColorPickerBase), "DoWindowContents")]
    public static class ColorPickerTabGuardPatch
    {
        private static KeyCode maskedKeyCode = KeyCode.None;

        [HarmonyPrefix]
        public static void Prefix(Dialog_ColorPickerBase __instance)
        {
            maskedKeyCode = KeyCode.None;
            if (ColorPickerScope.OwningScope(__instance) == null)
            {
                return;
            }
            Event e = Event.current;
            if (e == null || e.type != EventType.KeyDown || e.keyCode != KeyCode.Tab)
            {
                return;
            }
            maskedKeyCode = e.keyCode;
            e.keyCode = KeyCode.None;
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            if (maskedKeyCode == KeyCode.None)
            {
                return;
            }
            Event e = Event.current;
            if (e != null)
            {
                e.keyCode = maskedKeyCode;
            }
            maskedKeyCode = KeyCode.None;
        }
    }
}
