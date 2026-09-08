using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Two grids with no per-row vanilla method draw every one of their boxes through
    /// <c>Widgets.DrawOptionBackground</c>, once per item, inside a per-frame method that can be
    /// bracketed — the storyteller portrait column
    /// (<c>StorytellerUI.DrawStorytellerSelectionInterface</c>, decompiled :53-70) and the Options
    /// window's Mods grid (<c>Dialog_Options.DoModOptions</c>, decompiled :741-768). So they are
    /// captured the way the deity boxes are (<see cref="IdeoBoxDrawPatch"/>): a bracket arms
    /// recording for one consumer, and every box the bracketed body draws contributes one rect in
    /// draw order.
    ///
    /// The mapping from a rect to a row is ORDINAL, which holds only while the bracketed body
    /// draws exactly one box per row of the list the consumer built. Each consumer therefore
    /// compares counts before trusting an ordinal: a mismatch (a game or mod change adding a box
    /// inside the bracket, a filter edited mid-frame) rings nothing rather than ringing the wrong
    /// row.
    ///
    /// Both raw and screen-space rects are kept. The raw ones are view-space, which is what a
    /// scroll-follow needs; the screen ones are what <c>FocusedContentRect</c> returns. A box
    /// scrolled fully out records <c>default(Rect)</c> in the screen list but still occupies its
    /// ordinal slot in both.
    /// </summary>
    internal static class OptionBackgroundRowCapture
    {
        internal enum Consumer
        {
            None,
            StorytellerPortraits,
            OptionsModsGrid,
        }

        private sealed class Pass
        {
            public readonly List<Rect> Screen = new List<Rect>();
            public readonly List<Rect> Raw = new List<Rect>();

            /// <summary>Height of the scroll view the boxes are drawn in, 0 when the consumer does not scroll-follow.</summary>
            public float ViewHeight;
        }

        private static readonly Pass storytellerPass = new Pass();
        private static readonly Pass modsGridPass = new Pass();

        /// <summary>Read once per <c>Widgets.DrawOptionBackground</c> call game-wide, so it stays a plain field.</summary>
        internal static bool Recording;

        private static Consumer armed;

        /// <summary>The ordinal the ring belongs on, for consumers that ring inline; -1 for none.</summary>
        private static int ringOrdinal = -1;

        /// <summary>The last completed pass's screen-space rects for <paramref name="consumer"/>, in draw order.</summary>
        internal static IReadOnlyList<Rect> ScreenRects(Consumer consumer)
        {
            Pass pass = PassFor(consumer);
            return pass != null ? (IReadOnlyList<Rect>)pass.Screen : Array.Empty<Rect>();
        }

        /// <summary>The same pass's rects in the space vanilla drew them in — view space inside the surface's scroll view.</summary>
        internal static IReadOnlyList<Rect> RawRects(Consumer consumer)
        {
            Pass pass = PassFor(consumer);
            return pass != null ? (IReadOnlyList<Rect>)pass.Raw : Array.Empty<Rect>();
        }

        /// <summary>Height of the scroll view <see cref="RawRects"/> are measured in.</summary>
        internal static float ViewHeight(Consumer consumer)
        {
            Pass pass = PassFor(consumer);
            return pass != null ? pass.ViewHeight : 0f;
        }

        private static Pass PassFor(Consumer consumer)
        {
            switch (consumer)
            {
                case Consumer.StorytellerPortraits: return storytellerPass;
                case Consumer.OptionsModsGrid: return modsGridPass;
                default: return null;
            }
        }

        internal static void BeginStorytellerPortraits(Rect interfaceRect)
        {
            if (IsLayoutPass() || !(FocusStack.Top is StorytellerScopeBase))
            {
                return;
            }
            // The portrait column's scroll view spans the whole interface rect's height
            // (decompiled RimWorld/StorytellerUI.cs:50-51).
            Arm(Consumer.StorytellerPortraits, interfaceRect.height, -1);
        }

        internal static void BeginOptionsModsGrid()
        {
            if (IsLayoutPass())
            {
                return;
            }
            OptionsScope scope = FocusStack.Top as OptionsScope;
            if (scope == null)
            {
                return;
            }
            // Resolved before Arm clears the pass it is asserted against.
            int ordinal = scope.ModsGridRingOrdinal();
            Arm(Consumer.OptionsModsGrid, 0f, ordinal);
        }

        private static void Arm(Consumer consumer, float viewHeight, int ringOrdinal)
        {
            Pass pass = PassFor(consumer);
            pass.Screen.Clear();
            pass.Raw.Clear();
            pass.ViewHeight = viewHeight;
            OptionBackgroundRowCapture.ringOrdinal = ringOrdinal;
            armed = consumer;
            Recording = true;
        }

        internal static void End()
        {
            Recording = false;
            armed = Consumer.None;
            ringOrdinal = -1;
        }

        internal static void RecordBox(Rect box)
        {
            try
            {
                Pass pass = PassFor(armed);
                if (pass == null)
                {
                    return;
                }
                Rect visible = GuiSpace.VisibleScreenRect(box);
                if (pass.Raw.Count == ringOrdinal && visible.width > 0f)
                {
                    FocusRing.Draw(box);
                    UiPointerFollow.NotifyFocusedRect(visible);
                }
                pass.Raw.Add(box);
                pass.Screen.Add(visible);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Option background capture error", ex);
            }
        }

        private static bool IsLayoutPass()
        {
            return Event.current != null && Event.current.type == EventType.Layout;
        }
    }

    /// <summary>
    /// The storyteller portrait bracket. Its rects are read back by
    /// <see cref="StorytellerScopeBase.FocusedContentRect"/> (the ring) and by the scope's
    /// scroll-follow, so nothing is drawn here.
    /// </summary>
    [HarmonyPatch(typeof(StorytellerUI), "DrawStorytellerSelectionInterface")]
    internal static class StorytellerPortraitCapturePatch
    {
        [HarmonyPrefix]
        public static void Prefix(Rect rect)
        {
            OptionBackgroundRowCapture.BeginStorytellerPortraits(rect);
        }

        /// <summary>
        /// Disarming is a finalizer, not a postfix: a mod's storyteller def throwing mid-draw
        /// would otherwise leave the recorder armed game-wide, with every option background
        /// anywhere feeding a list nothing reads.
        /// </summary>
        [HarmonyFinalizer]
        public static void Finalizer()
        {
            OptionBackgroundRowCapture.End();
        }
    }

    /// <summary>The Options window's Mods grid bracket; the ring is drawn inline by the tap below.</summary>
    [HarmonyPatch(typeof(Dialog_Options), "DoModOptions")]
    internal static class OptionsModsGridCapturePatch
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            OptionBackgroundRowCapture.BeginOptionsModsGrid();
        }

        [HarmonyFinalizer]
        public static void Finalizer()
        {
            OptionBackgroundRowCapture.End();
        }
    }

    /// <summary>
    /// Contributes one rect per box while a bracket above is armed. This runs on every option
    /// background the game draws anywhere — the category rail and the starting-pawn boxes among
    /// them — so the first statement is the one static bool the brackets set, and that is all an
    /// off-path call ever costs.
    /// </summary>
    [HarmonyPatch(typeof(Widgets), "DrawOptionBackground")]
    internal static class OptionBackgroundTapPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Rect rect, bool selected)
        {
            // Independent of the bracketed consumers below: a generic window's open capture
            // pass keeps every option background as a marker (WidgetCapture no-ops otherwise).
            WidgetCapture.RecordOptionBackground(rect, selected);
            if (!OptionBackgroundRowCapture.Recording)
            {
                return;
            }
            OptionBackgroundRowCapture.RecordBox(rect);
        }
    }
}
