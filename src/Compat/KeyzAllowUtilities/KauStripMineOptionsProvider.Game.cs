using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Puts the strip-mine grid controls on the designator options key. The mod draws them as
    /// an ImmediateWindow of +/- buttons beside the architect menu (Designator_StripMine.
    /// DoExtraGuiControls), which no window reader can enter; the values are the designator's
    /// public static spacing and offset fields.
    /// </summary>
    internal sealed class KauStripMineOptionsProvider : IDesignatorContextMenuProvider
    {
        public bool HasOptions(Designator designator)
        {
            return designator != null
                && KauCompat.StripMineGate.Ensure()
                && KauCompat.StripMineType.IsInstanceOfType(designator);
        }

        public bool TryOpen(Designator designator)
        {
            if (!HasOptions(designator))
                return false;

            var options = new List<FloatMenuOption>();
            // Step and minimum per control mirror the panel's own IntAdjusterWithDisplay calls:
            // spacing steps by 1 with a floor of 2, offset steps by 1 with a floor of 0.
            AddAdjusters(options, KauCompat.SpacingXField, "RimWorldAccess.Compat.Kau.StripMine.HorizontalSpacing", 2);
            AddAdjusters(options, KauCompat.SpacingZField, "RimWorldAccess.Compat.Kau.StripMine.VerticalSpacing", 2);
            AddAdjusters(options, KauCompat.OffsetXField, "RimWorldAccess.Compat.Kau.StripMine.HorizontalOffset", 0);
            AddAdjusters(options, KauCompat.OffsetZField, "RimWorldAccess.Compat.Kau.StripMine.VerticalOffset", 0);

            IEnumerable<FloatMenuOption> vanilla = designator.RightClickFloatMenuOptions;
            if (vanilla != null)
                options.AddRange(vanilla);

            WindowlessFloatMenuState.Open(options, false);
            return true;
        }

        private static void AddAdjusters(List<FloatMenuOption> options, FieldInfo field, string nameKey, int minimum)
        {
            string name = nameKey.Translate();
            int value = (int)field.GetValue(null);
            options.Add(new FloatMenuOption(
                "RimWorldAccess.Compat.Kau.StripMine.Increase".Translate(name, value),
                delegate { Adjust(field, name, minimum, 1); }));
            options.Add(new FloatMenuOption(
                "RimWorldAccess.Compat.Kau.StripMine.Decrease".Translate(name, value),
                delegate { Adjust(field, name, minimum, -1); }));
        }

        private static void Adjust(FieldInfo field, string name, int minimum, int direction)
        {
            int value = (int)field.GetValue(null);
            int next = value + direction * GenUI.CurrentAdjustmentMultiplier();
            if (next < minimum)
                next = minimum;
            if (next == value)
            {
                NumericStepperHelper.SpeakBoundary(direction);
                return;
            }

            // MUTATION-C: mirrors Listing_Standard_Utils.IntAdjusterWithDisplay (Keyz' Allow
            // Utilities), the panel's own +/- buttons: countChange times CurrentAdjustmentMultiplier,
            // clamped to the button's minimum. The buttons are Widgets.ButtonText inside an
            // ImmediateWindow, so no delegate or gated method exists to press from the keyboard.
            field.SetValue(null, next);
            SoundDefOf.DragSlider.PlayOneShotOnCamera();
            TolkHelper.Speak("RimWorldAccess.Compat.Kau.StripMine.ValueNow".Loc(name, next));
        }
    }
}
