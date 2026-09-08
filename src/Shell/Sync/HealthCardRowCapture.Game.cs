using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// A scope that can name the medical-settings row its keyboard cursor sits
    /// on, so <see cref="HealthCardRowCapture"/> can ring vanilla's own control
    /// for that row. The health tab's windowless menu never hides the real
    /// health card — it stays painted the whole time the menu drives — so the
    /// card only lacked a mark for the cursor's row.
    /// </summary>
    internal interface IHealthCardRowFocusSource
    {
        /// <summary>One of <see cref="HealthCardRowCapture"/>'s row keys, or null when the cursor is off those rows.</summary>
        string FocusedHealthCardRow { get; }
    }

    /// <summary>
    /// Paints the shared keyboard focus ring over the health card's own
    /// medical-settings controls.
    ///
    /// The three rows are raw <see cref="Widgets"/> calls with explicit rects
    /// rather than a <c>Listing</c> (decompiled RimWorld/HealthCardUtility.cs:451,
    /// <c>DrawOverviewTab</c>), so there is no listing capture to ride: each
    /// row's own draw call is tapped and the ring painted from its postfix,
    /// which is both the moment the rect's GUI space is right and after vanilla
    /// has drawn the control the ring goes around. Nothing here computes
    /// geometry.
    ///
    /// Rows are keyed structurally — one key per call site — rather than by
    /// ordinal, so a row vanilla's own gates left undrawn simply rings nothing.
    /// Vanilla makes each of the three calls exactly once inside the bracket
    /// (:471, :505, :524); a second call for the same key means the body has
    /// changed under us, and that key degrades to NO ring rather than a wrong
    /// one.
    /// </summary>
    internal static class HealthCardRowCapture
    {
        /// <summary>The food-policy button (decompiled RimWorld/HealthCardUtility.cs:471).</summary>
        internal const string FoodRow = "food";

        /// <summary>The medical-care dropdown (:505).</summary>
        internal const string CareRow = "care";

        /// <summary>The self-tend checkbox (:524).</summary>
        internal const string SelfTendRow = "self-tend";

        // Read as the first statement of every tap below: outside the bracket
        // this is one static bool check for widget calls made anywhere in the game.
        private static bool armed;
        private static string focusedRow;

        private static int foodCalls;
        private static int careCalls;
        private static int selfTendCalls;

        internal static void BeginPass()
        {
            if (Event.current == null || Event.current.type == EventType.Layout)
            {
                return;
            }
            var source = FocusStack.Top as IHealthCardRowFocusSource;
            focusedRow = source == null ? null : source.FocusedHealthCardRow;
            foodCalls = 0;
            careCalls = 0;
            selfTendCalls = 0;
            armed = focusedRow != null;
        }

        internal static void EndPass()
        {
            armed = false;
            focusedRow = null;
        }

        internal static void RecordFoodButton(Rect rect)
        {
            if (!armed)
            {
                return;
            }
            foodCalls++;
            RingIfFocused(FoodRow, foodCalls, rect);
        }

        internal static void RecordCareButton(Rect rect)
        {
            if (!armed)
            {
                return;
            }
            careCalls++;
            RingIfFocused(CareRow, careCalls, rect);
        }

        internal static void RecordSelfTendCheckbox(float x, float y, float size)
        {
            if (!armed)
            {
                return;
            }
            selfTendCalls++;
            RingIfFocused(SelfTendRow, selfTendCalls, new Rect(x, y, size, size));
        }

        private static void RingIfFocused(string row, int callCount, Rect rect)
        {
            try
            {
                if (callCount != 1 || !string.Equals(row, focusedRow, StringComparison.Ordinal))
                {
                    return;
                }
                if (Event.current.type != EventType.Repaint)
                {
                    return;
                }
                FocusRing.Draw(rect);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Health card row focus ring error", ex);
            }
        }
    }

    /// <summary>
    /// Brackets the health card's overview tab so the widget taps below
    /// recognize its three settings rows. A finalizer rather than a postfix: it
    /// still runs when a row's own draw throws, so the arm can never latch on.
    /// </summary>
    [HarmonyPatch(typeof(HealthCardUtility), "DrawOverviewTab")]
    internal static class HealthCardOverviewBracketPatch
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            HealthCardRowCapture.BeginPass();
        }

        [HarmonyFinalizer]
        public static void Finalizer()
        {
            HealthCardRowCapture.EndPass();
        }
    }

    /// <summary>
    /// The medical-care dropdown. Bound by name on the declaring type; the rect
    /// is vanilla's own argument (decompiled RimWorld/MedicalCareUtility.cs:86).
    /// </summary>
    [HarmonyPatch(typeof(MedicalCareUtility), nameof(MedicalCareUtility.MedicalCareSelectButton))]
    internal static class HealthCardCareButtonPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Rect rect)
        {
            HealthCardRowCapture.RecordCareButton(rect);
        }
    }

    /// <summary>
    /// The food-policy button. Both public ButtonText overloads funnel through
    /// this one (decompiled Verse/Widgets.cs:1434-1441), the same binding
    /// <c>WidgetCaptureButtonTextPatch</c> already uses.
    /// </summary>
    [HarmonyPatch(typeof(Widgets), "ButtonText",
        new Type[] { typeof(Rect), typeof(string), typeof(bool), typeof(bool), typeof(Color), typeof(bool), typeof(TextAnchor?) })]
    internal static class HealthCardFoodButtonPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Rect rect)
        {
            HealthCardRowCapture.RecordFoodButton(rect);
        }
    }

    /// <summary>
    /// The self-tend checkbox. Vanilla passes a corner and a size rather than a
    /// rect (decompiled Verse/Widgets.cs:1197); TargetMethod because the by-ref
    /// bool parameter type is not a compile-time constant (CS0182).
    /// </summary>
    [HarmonyPatch]
    internal static class HealthCardSelfTendCheckboxPatch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Widgets), "Checkbox",
                new Type[]
                {
                    typeof(float), typeof(float), typeof(bool).MakeByRefType(), typeof(float),
                    typeof(bool), typeof(bool), typeof(Texture2D), typeof(Texture2D),
                });
        }

        [HarmonyPostfix]
        public static void Postfix(float x, float y, float size)
        {
            HealthCardRowCapture.RecordSelfTendCheckbox(x, y, size);
        }
    }
}
