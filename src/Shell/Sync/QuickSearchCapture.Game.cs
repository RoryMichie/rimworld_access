using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>One QuickSearchWidget a bracketed window pass actually drew.</summary>
    internal sealed class CapturedQuickSearch
    {
        public QuickSearchWidget Widget;
        public Rect Rect;
    }

    /// <summary>
    /// Captures the <see cref="QuickSearchWidget"/> instances drawn during one
    /// armed window's own draw pass, so a scope can offer a third party's search
    /// box as an ordinary browse/edit text-field row (the widget's
    /// <c>filter.Text</c> setter is the vanilla write path).
    ///
    /// The pass bracket is what scopes the tap to ONE window's draw: every other
    /// QuickSearchWidget in the game (main tabs behind the dialog, vanilla
    /// screens) reaches the postfix while the pass is closed and is ignored.
    ///
    /// ADOPTION by another scope: call <see cref="BeginPass"/> from that scope's
    /// DoWindowContents prefix and <see cref="EndPass"/> from its postfix, and
    /// give that postfix <c>Priority.Last</c> -- a mod's own postfix on the same
    /// DoWindowContents may be where it draws its widget, so ours must run after
    /// it or the draw lands outside the bracket.
    /// </summary>
    internal static class QuickSearchCapture
    {
        private const float FocusRingExpand = 2f;
        private static readonly Color FocusRingColor = new Color(0.45f, 0.78f, 1f);

        private static bool passOpen;
        private static bool drawFocusRing;
        private static readonly List<CapturedQuickSearch> items = new List<CapturedQuickSearch>();

        /// <summary>Widgets recorded by the current/most recent pass, in draw order.</summary>
        public static IReadOnlyList<CapturedQuickSearch> Items
        {
            get { return items; }
        }

        /// <summary>
        /// Start recording; clears the previous pass. Call from the armed
        /// surface's DoWindowContents prefix. <paramref name="drawFocusRing"/>
        /// says the scope's cursor sits on the search row this pass, so the tap
        /// draws the keyboard focus ring around the widget itself -- the correct
        /// GUI context and coordinate space exist only at draw time.
        /// </summary>
        public static void BeginPass(bool drawFocusRing)
        {
            items.Clear();
            QuickSearchCapture.drawFocusRing = drawFocusRing;
            passOpen = true;
        }

        /// <summary>Stop recording. Call from the armed surface's DoWindowContents postfix.</summary>
        public static void EndPass()
        {
            passOpen = false;
            drawFocusRing = false;
        }

        internal static void Record(QuickSearchWidget widget, Rect rect)
        {
            if (!passOpen || widget == null)
            {
                return;
            }
            items.Add(new CapturedQuickSearch { Widget = widget, Rect = rect });
            if (!drawFocusRing || items.Count != 1 || rect.width <= 0f)
            {
                return;
            }
            Rect expanded = rect.ExpandedBy(FocusRingExpand);
            Color previous = GUI.color;
            GUI.color = FocusRingColor;
            Widgets.DrawBox(expanded, 2);
            GUI.color = previous;
        }
    }

    /// <summary>OnGUI is the widget's single public draw entry, so one postfix sees every drawn search box.</summary>
    [HarmonyPatch(typeof(QuickSearchWidget), nameof(QuickSearchWidget.OnGUI))]
    internal static class QuickSearchTapPatch
    {
        [HarmonyPostfix]
        public static void Postfix(QuickSearchWidget __instance, Rect rect)
        {
            QuickSearchCapture.Record(__instance, rect);
        }
    }
}
