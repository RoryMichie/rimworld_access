using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Captures every <see cref="DiaOption.OptOnGUI"/> call made during the
    /// currently-owned node tree's own DrawNode pass: the option instance,
    /// the label AS DISPLAYED (vanilla's own disabled-reason append
    /// replicated — DiaOption.cs:83-87 — not re-derived from separate
    /// fields), the final clickable rect, and whether the row draws as a
    /// hyperlink (<c>hyperlink.def != null</c>, DiaOption.cs:91).
    ///
    /// Rect mechanics (verified against decompiled DiaOption.cs): OptOnGUI
    /// receives its Rect BY VALUE at height 999f (a "tell me how tall you
    /// needed" sentinel) and overwrites its OWN copy's height via
    /// <c>rect.height = Text.CalcHeight(...)</c> before drawing. A
    /// same-named, non-ref postfix parameter observes that mutated value,
    /// because Harmony reads the live parameter slot at postfix time rather
    /// than the value the caller originally passed in.
    ///
    /// Recording is bracketed to Dialog_NodeTree.DrawNode (see
    /// NodeTreeDrawPatch in NodeTreeScope.Game.cs) for the same reasons as
    /// every other Shell/Sync capture: other UI drawn the same frame must
    /// not collide, and the bracket pins rects to DrawNode's own
    /// options-scroll content space. This tap itself runs across every
    /// Dialog_NodeTree in the game (armed or not), so it mirrors
    /// ButtonTextCapture/TooltipCapture's lean, uncaught shape rather than
    /// wrapping each call in a try/catch.
    /// </summary>
    public static class DiaOptionRowCapture
    {
        public struct CapturedOption
        {
            public DiaOption Instance;
            public Rect Rect;
            public string Label;
            public bool Disabled;
            public string DisabledReason;
            public bool IsHyperlink;
        }

        private static readonly FieldInfo textField = AccessTools.Field(typeof(DiaOption), "text");

        private static bool passOpen;
        private static readonly List<CapturedOption> items = new List<CapturedOption>();

        /// <summary>Options recorded by the current/most recent pass, in curNode.options order.</summary>
        public static IReadOnlyList<CapturedOption> Items
        {
            get { return items; }
        }

        /// <summary>Start recording; clears the previous pass. Call from the owning scope's DrawNode prefix.</summary>
        public static void BeginPass()
        {
            items.Clear();
            passOpen = true;
        }

        /// <summary>Stop recording. Call from the owning scope's DrawNode postfix.</summary>
        public static void EndPass()
        {
            passOpen = false;
        }

        internal static void Record(DiaOption instance, Rect rect)
        {
            if (!passOpen || instance == null)
            {
                return;
            }
            CapturedOption option;
            option.Instance = instance;
            option.Rect = rect;
            option.Disabled = instance.disabled;
            option.DisabledReason = instance.disabledReason;
            option.IsHyperlink = instance.hyperlink.def != null;
            string text = textField != null ? (textField.GetValue(instance) as string) ?? "" : "";
            option.Label = (instance.disabled && instance.disabledReason != null)
                ? text + " (" + instance.disabledReason + ")"
                : text;
            items.Add(option);
        }
    }

    /// <summary>
    /// The capture tap plus the keyboard focus ring — mirrors
    /// FloatMenuOptionDoGuiPatch's shape exactly, for the same reason: the
    /// per-option draw method runs inside DrawNode's own BeginGroup/
    /// BeginScrollView pair, so a ring painted here (and only here) lands in
    /// the same coordinate space vanilla just drew the option in. Drawing it
    /// instead from Dialog_NodeTree.DrawNode's own postfix would be too
    /// late: that postfix fires after DrawNode has already called
    /// EndScrollView/EndGroup, popping the transform the captured rects are
    /// relative to.
    /// </summary>
    [HarmonyPatch(typeof(DiaOption), "OptOnGUI")]
    public static class DiaOptionOptOnGUICapturePatch
    {
        private const float FocusRingExpand = 1f;
        private static readonly Color FocusRingColor = new Color(0.45f, 0.78f, 1f);

        [HarmonyPostfix]
        public static void Postfix(DiaOption __instance, Rect rect)
        {
            DiaOptionRowCapture.Record(__instance, rect);

            NodeTreeScope scope = FocusStack.Top as NodeTreeScope;
            if (scope == null || !scope.Owns(__instance.dialog) || !scope.IsFocusedOption(__instance))
            {
                return;
            }
            Rect ring = rect.ExpandedBy(FocusRingExpand);
            Color previous = GUI.color;
            GUI.color = FocusRingColor;
            Widgets.DrawBox(ring, 2);
            GUI.color = previous;
        }
    }
}
