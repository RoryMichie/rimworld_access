using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Speaks a slider while the mouse drags it — the handle moving and the
    /// caption next to it updating is the whole of what a sighted player is
    /// shown during that gesture.
    ///
    /// Identity comes from vanilla's own drag bookkeeping.
    /// <c>Widgets.HorizontalSlider</c> does not delegate the drag to Unity: it
    /// hashes the slider's screen position, size and bounds into
    /// <c>sliderDraggingID</c> on MouseDown and steers on that hash for the
    /// rest of the gesture (decompiled Verse/Widgets.cs:2081-2107). The prefix
    /// below recomputes that hash from the same inputs, so the postfix knows
    /// whether THIS call is the slider under the mouse. Nothing else in the
    /// call can tell two sliders apart, and nothing here reads a rect.
    ///
    /// What gets said follows what vanilla drew. The gesture opens with the
    /// slider's name (<see cref="SliderCaption.ResolveName"/> finds it in the
    /// widget's own captions, in the wrapper that discarded it, or in the
    /// previous pass's fused row) and every later tick speaks the value alone.
    /// The one exception is a LIVE caption that moves with the handle
    /// ("Pan speed 1.5", <c>pollution.ToStringPercent()</c>): that caption IS
    /// the value, so it is spoken in place of one.
    ///
    /// Announce-only, and ungated by the hover-speech setting: a drag is a gesture the player
    /// deliberately started.
    /// </summary>
    internal static class SliderDragSpeech
    {
        private static readonly AccessTools.FieldRef<int> SliderDraggingID =
            AccessTools.StaticFieldRefAccess<int>(
                AccessTools.Field(typeof(Widgets), "sliderDraggingID"));

        private static int gestureId;
        private static string spokenLabel;
        private static float spokenValue;

        /// <summary>
        /// The hash <c>Widgets.HorizontalSlider</c> identifies itself by,
        /// recomputed from the arguments before the body mutates its own copy
        /// of the rect (decompiled Verse/Widgets.cs:2074-2087).
        /// </summary>
        internal static int ControlId(Rect rect, float min, float max, bool middleAlignment, string label)
        {
            if (middleAlignment || !label.NullOrEmpty())
            {
                rect.y += Mathf.Round((rect.height - 10f) / 2f);
            }
            if (!label.NullOrEmpty())
            {
                rect.y += 5f;
            }
            int hash = UI.GUIToScreenPoint(new Vector2(rect.x, rect.y)).GetHashCode();
            hash = Gen.HashCombine(hash, rect.width);
            hash = Gen.HashCombine(hash, rect.height);
            hash = Gen.HashCombine(hash, min);
            hash = Gen.HashCombine(hash, max);
            return hash;
        }

        internal static void Observe(int controlId, string label, string leftAlignedLabel,
            string rightAlignedLabel, float min, float max, float value)
        {
            try
            {
                if (WidgetCapture.DetachedPass || SliderDraggingID() != controlId)
                {
                    return;
                }
                SliderCaptionIndex.Entry remembered = SliderCaptionIndex.Lookup(controlId);
                SliderName name = SliderCaption.ResolveName(label, leftAlignedLabel, rightAlignedLabel, min, max,
                    ListingRowCapture.CurrentSliderLabel, remembered.Name, remembered.CarriesValue);
                if (controlId != gestureId)
                {
                    gestureId = controlId;
                    spokenLabel = name.Text;
                    spokenValue = value;
                    if (name.Text.NullOrEmpty())
                    {
                        Speak(ValueText(leftAlignedLabel, rightAlignedLabel, min, max, value));
                    }
                    else if (name.CarriesValue)
                    {
                        // The remembered caption ends in the value it had a
                        // pass ago; the live one goes beside it instead.
                        TolkHelper.Speak("RimWorldAccess.UI.Drag.SliderLabelledValue".Loc(
                            name.Text, ValueText(leftAlignedLabel, rightAlignedLabel, min, max, value)),
                            SpeechPriority.High);
                    }
                    else
                    {
                        Speak(name.Text);
                    }
                    return;
                }
                if (value == spokenValue)
                {
                    return;
                }
                spokenValue = value;
                if (name.Live && !name.Text.NullOrEmpty()
                    && !string.Equals(name.Text, spokenLabel, StringComparison.Ordinal))
                {
                    // A live caption that moved with the handle IS the value.
                    spokenLabel = name.Text;
                    Speak(name.Text);
                    return;
                }
                Speak(ValueText(leftAlignedLabel, rightAlignedLabel, min, max, value));
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Slider drag speech error", ex);
            }
        }

        private static string ValueText(string leftAlignedLabel, string rightAlignedLabel, float min, float max, float value)
        {
            string drawn = SliderCaption.EffectiveValueText(leftAlignedLabel, rightAlignedLabel, min, max);
            return drawn.NullOrEmpty() ? SliderCaption.FormatValue(value) : drawn;
        }

        private static void Speak(string text)
        {
            if (!text.NullOrEmpty())
            {
                TolkHelper.SpeakData(text, SpeechPriority.High);
            }
        }
    }

    /// <summary>
    /// The one <c>Widgets.HorizontalSlider</c> body (decompiled
    /// Verse/Widgets.cs:2072); the <c>ref float</c>/<c>FloatRange</c> overload
    /// (:2065) and <c>FrequencyHorizontalSlider</c> (:2147) both funnel here,
    /// so this single tap covers the family.
    /// </summary>
    [HarmonyPatch(typeof(Widgets), "HorizontalSlider",
        new Type[] { typeof(Rect), typeof(float), typeof(float), typeof(float), typeof(bool), typeof(string), typeof(string), typeof(string), typeof(float) })]
    internal static class SliderDragSpeechPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Rect rect, float min, float max, bool middleAlignment, string label, out int __state)
        {
            __state = SliderDragSpeech.ControlId(rect, min, max, middleAlignment, label);
        }

        [HarmonyPostfix]
        public static void Postfix(int __state, float min, float max, string label,
            string leftAlignedLabel, string rightAlignedLabel, float __result)
        {
            SliderDragSpeech.Observe(__state, label, leftAlignedLabel, rightAlignedLabel, min, max, __result);
        }
    }

    /// <summary>
    /// Speaks the thumb a mouse drag is moving on the two-handle range
    /// controls. These are hand-written draggers rather than Unity sliders, so
    /// they hand identity over directly: <c>draggingId</c> holds the caller's
    /// own control id and <c>curDragEnd</c> names the grabbed thumb
    /// (decompiled Verse/Widgets.cs:2254-2280). The value is rendered by the
    /// same formatter the keyboard path speaks these controls through.
    /// </summary>
    internal static class RangeDragSpeech
    {
        private static readonly AccessTools.FieldRef<int> DraggingId =
            AccessTools.StaticFieldRefAccess<int>(AccessTools.Field(typeof(Widgets), "draggingId"));

        // Widgets.RangeEnd is private, so its byte value is read rather than cast: None/Min/Max.
        private static readonly FieldInfo CurDragEndField = AccessTools.Field(typeof(Widgets), "curDragEnd");
        private const int RangeEndMin = 1;
        private const int RangeEndMax = 2;

        private static int gestureId;
        private static int gestureEnd;
        private static float spokenValue;

        internal static void Observe(int id, RangeFamily family, ToStringStyle style, float low, float high)
        {
            try
            {
                if (WidgetCapture.DetachedPass || DraggingId() != id)
                {
                    return;
                }
                int end = Convert.ToInt32(CurDragEndField.GetValue(null));
                if (end != RangeEndMin && end != RangeEndMax)
                {
                    return;
                }
                float value = end == RangeEndMax ? high : low;
                if (id == gestureId && end == gestureEnd && value == spokenValue)
                {
                    return;
                }
                gestureId = id;
                gestureEnd = end;
                spokenValue = value;

                string valueText = GenericWindowScope.FormatRangeValue(family, style, value);
                string key = end == RangeEndMax
                    ? "RimWorldAccess.UI.Drag.RangeMaximum"
                    : "RimWorldAccess.UI.Drag.RangeMinimum";
                TolkHelper.Speak(key.Loc(valueText), SpeechPriority.High);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Range drag speech error", ex);
            }
        }
    }

    /// <summary>Widgets.FloatRange (decompiled Verse/Widgets.cs:2216).</summary>
    [HarmonyPatch]
    internal static class FloatRangeDragSpeechPatch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Widgets), "FloatRange",
                new Type[]
                {
                    typeof(Rect), typeof(int), typeof(FloatRange).MakeByRefType(), typeof(float),
                    typeof(float), typeof(string), typeof(ToStringStyle), typeof(float),
                    typeof(GameFont), typeof(Color?), typeof(float),
                });
        }

        [HarmonyPostfix]
        public static void Postfix(int id, ref FloatRange range, ToStringStyle valueStyle)
        {
            RangeDragSpeech.Observe(id, RangeFamily.Float, valueStyle, range.min, range.max);
        }
    }

    /// <summary>Widgets.IntRange (decompiled Verse/Widgets.cs:2318).</summary>
    [HarmonyPatch]
    internal static class IntRangeDragSpeechPatch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Widgets), "IntRange",
                new Type[]
                {
                    typeof(Rect), typeof(int), typeof(IntRange).MakeByRefType(), typeof(int),
                    typeof(int), typeof(string), typeof(int),
                });
        }

        [HarmonyPostfix]
        public static void Postfix(int id, ref IntRange range)
        {
            RangeDragSpeech.Observe(id, RangeFamily.Int, ToStringStyle.Integer, range.min, range.max);
        }
    }

    /// <summary>Widgets.QualityRange (decompiled Verse/Widgets.cs:2431).</summary>
    [HarmonyPatch]
    internal static class QualityRangeDragSpeechPatch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Widgets), "QualityRange",
                new Type[] { typeof(Rect), typeof(int), typeof(RimWorld.QualityRange).MakeByRefType() });
        }

        [HarmonyPostfix]
        public static void Postfix(int id, ref RimWorld.QualityRange range)
        {
            RangeDragSpeech.Observe(id, RangeFamily.Quality, ToStringStyle.Integer, (int)range.min, (int)range.max);
        }
    }
}
