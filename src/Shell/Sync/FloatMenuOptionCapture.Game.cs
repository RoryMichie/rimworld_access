using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Captures each <see cref="FloatMenuOption.DoGUI"/> call made during a
    /// scope-driven float menu's own draw pass: the option instance and the
    /// rect vanilla gave it (view-space when the menu scrolls — DoGUI runs
    /// inside the scroll group, and so does the focus ring drawn here, so the
    /// coordinates always agree). Vanilla draws every option each pass (the
    /// scroll view clips, it does not cull), so the capture is complete.
    ///
    /// The pass is bracketed from FloatMenu.DoWindowContents (see
    /// FloatMenuDrawPatch in FloatMenuScope.Game.cs) for the same reasons as
    /// ButtonTextCapture: no cross-surface collisions, pinned coordinate space.
    /// </summary>
    public static class FloatMenuOptionCapture
    {
        private static bool passOpen;
        private static readonly Dictionary<FloatMenuOption, Rect> rects =
            new Dictionary<FloatMenuOption, Rect>();

        /// <summary>Option rects recorded by the current/most recent pass.</summary>
        public static bool TryGetRect(FloatMenuOption option, out Rect rect)
        {
            return rects.TryGetValue(option, out rect);
        }

        public static void BeginPass()
        {
            rects.Clear();
            passOpen = true;
        }

        public static void EndPass()
        {
            passOpen = false;
        }

        internal static void Record(FloatMenuOption option, Rect rect)
        {
            if (passOpen && option != null)
            {
                rects[option] = rect;
            }
        }
    }

    /// <summary>
    /// The capture tap plus the keyboard focus ring. DoGUI is virtual but no
    /// vanilla option type overrides it, so the base-method patch sees every
    /// option. The ring draws in the postfix — inside the menu's own GUI pass
    /// and inside its scroll group, over the option vanilla just painted.
    /// </summary>
    [HarmonyPatch(typeof(FloatMenuOption), "DoGUI")]
    public static class FloatMenuOptionDoGuiPatch
    {
        private const float FocusRingExpand = 1f;
        private static readonly Color FocusRingColor = new Color(0.45f, 0.78f, 1f);

        [HarmonyPrefix]
        public static void Prefix(FloatMenuOption __instance, Rect rect)
        {
            FloatMenuOptionCapture.Record(__instance, rect);
        }

        [HarmonyPostfix]
        public static void Postfix(FloatMenuOption __instance, Rect rect, FloatMenu floatMenu)
        {
            FloatMenuScope scope = FocusStack.Top as FloatMenuScope;
            if (scope != null && scope.Owns(floatMenu) && scope.IsFocusedOption(__instance))
            {
                DrawRing(rect);
                return;
            }
            // VEF's Dialog_FloatMenuOptions calls DoGUI with floatMenu == null; the vanilla
            // arm above already fails safely on that, so this arm is independent.
            VefPreceptOptionsScope vef = FocusStack.Top as VefPreceptOptionsScope;
            if (vef != null && vef.IsFocusedOption(__instance))
            {
                DrawRing(rect);
            }
        }

        private static void DrawRing(Rect rect)
        {
            Rect ring = rect.ExpandedBy(FocusRingExpand);
            Color previous = GUI.color;
            GUI.color = FocusRingColor;
            Widgets.DrawBox(ring, 2);
            GUI.color = previous;
        }
    }
}
