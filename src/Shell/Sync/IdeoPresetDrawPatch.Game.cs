using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Records the three families of clickable box the ideoligion preset page draws
    /// (decompiled RimWorld/Page_ChooseIdeoPreset.cs) so <see cref="IdeoPresetScreenScope"/> can
    /// ring the focused one and scroll it into view: the four option cards and every preset card,
    /// both drawn by the page's private <c>DrawSelectable</c> (:248), and the structure box, style
    /// slots and add-style button of <c>DrawStructureAndStyleSelection</c> (:320). Vanilla's own
    /// <c>DrawOptionBackground</c> marks the CHOSEN card only, and the structure/style widget marks
    /// nothing at all, so the keyboard cursor has no indicator of its own on any of them.
    ///
    /// Identity comes from vanilla's draw order, which is not the same order the scope lists rows
    /// in — see <see cref="TryGetStructureStyleRect"/> — so every family checks the count it
    /// recorded against the count the scope expects and publishes nothing on a mismatch rather
    /// than risk ringing the wrong box.
    /// </summary>
    internal static class IdeoPresetDrawPatch
    {
        private struct Box
        {
            /// <summary>Absolute UI points, already clipped; empty while the box is scrolled out of view.</summary>
            public Rect Screen;

            /// <summary>The box as vanilla laid it out, in its own group's space (scroll-independent).</summary>
            public Rect Raw;
        }

        private struct Card
        {
            public IdeoPresetDef Preset;
            public Rect Screen;
            public Rect Raw;
        }

        /// <summary>
        /// Read once per <c>Widgets.ButtonInvisible</c> call game-wide, so it stays a plain field
        /// and is the tap's first statement.
        /// </summary>
        internal static bool RecordingStructure;

        /// <summary>
        /// Armed for the length of one page pass. Read once per <c>Widgets.BeginScrollView</c>
        /// call game-wide by the scroll tap, so it is a plain field there too.
        /// </summary>
        internal static bool Recording;

        private static readonly List<Box> recOptions = new List<Box>(4);
        private static readonly List<Box> recStructure = new List<Box>(5);
        private static readonly List<Card> recPresets = new List<Card>();

        private static readonly List<Box> pubOptions = new List<Box>(4);
        private static readonly List<Box> pubStructure = new List<Box>(5);
        private static readonly List<Card> pubPresets = new List<Card>();

        private static IdeoPresetDef pendingPreset;
        private static bool scrollCaptured;
        private static Rect recScrollOutRect;
        private static Rect recScrollViewRect;

        private static bool published;
        private static bool publishedScroll;
        private static Rect pubScrollOutRect;
        private static Rect pubScrollViewRect;

        /// <summary>The option card at <paramref name="index"/>, false unless the last pass drew exactly <paramref name="expectedCount"/> of them.</summary>
        internal static bool TryGetOptionRect(int index, int expectedCount, out Rect screen, out Rect raw)
        {
            return TryGetBox(pubOptions, index, expectedCount, out screen, out raw);
        }

        /// <summary>
        /// The structure/style box at <paramref name="drawOrdinal"/>. Vanilla draws them in an
        /// order of its own (decompiled :325-357): the add-style plus button first and only while
        /// fewer than three slots exist, then the style slots from LAST to first, then the
        /// structure box — the caller maps its row onto that order.
        /// </summary>
        internal static bool TryGetStructureStyleRect(int drawOrdinal, int expectedCount, out Rect screen)
        {
            Rect raw;
            return TryGetBox(pubStructure, drawOrdinal, expectedCount, out screen, out raw);
        }

        /// <summary>The card <paramref name="preset"/> was drawn in, matched by def reference rather than by ordinal.</summary>
        internal static bool TryGetPresetRect(IdeoPresetDef preset, out Rect screen, out Rect raw)
        {
            screen = default(Rect);
            raw = default(Rect);
            if (!published || preset == null)
            {
                return false;
            }
            for (int i = 0; i < pubPresets.Count; i++)
            {
                if (!ReferenceEquals(pubPresets[i].Preset, preset))
                {
                    continue;
                }
                screen = pubPresets[i].Screen;
                raw = pubPresets[i].Raw;
                return true;
            }
            return false;
        }

        /// <summary>The category list's scroll view, the band the option and preset cards live in.</summary>
        internal static bool TryGetScrollBand(out Rect outRect, out Rect viewRect)
        {
            outRect = pubScrollOutRect;
            viewRect = pubScrollViewRect;
            return published && publishedScroll;
        }

        private static bool TryGetBox(List<Box> boxes, int index, int expectedCount, out Rect screen, out Rect raw)
        {
            screen = default(Rect);
            raw = default(Rect);
            if (!published || boxes.Count != expectedCount || index < 0 || index >= boxes.Count)
            {
                return false;
            }
            screen = boxes[index].Screen;
            raw = boxes[index].Raw;
            return true;
        }

        internal static void BeginPage(Window page)
        {
            try
            {
                Event ev = Event.current;
                if (ev == null || ev.type == EventType.Layout)
                {
                    return;
                }
                IdeoPresetScreenScope scope = FocusStack.Top as IdeoPresetScreenScope;
                if (scope == null || !scope.Owns(page))
                {
                    return;
                }
                Recording = true;
                pendingPreset = null;
                scrollCaptured = false;
                recOptions.Clear();
                recStructure.Clear();
                recPresets.Clear();
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Ideo preset capture error", ex);
            }
        }

        /// <summary>Arrives from a finalizer, so a modded preset def that throws mid-grid still disarms the pass.</summary>
        internal static void EndPage()
        {
            if (!Recording)
            {
                return;
            }
            Recording = false;
            RecordingStructure = false;
            pendingPreset = null;
            Publish();
        }

        internal static void BeginStructure()
        {
            if (Recording)
            {
                RecordingStructure = true;
            }
        }

        internal static void EndStructure()
        {
            RecordingStructure = false;
        }

        internal static void BeginIdeo(IdeoPresetDef ideo)
        {
            if (Recording)
            {
                pendingPreset = ideo;
            }
        }

        internal static void EndIdeo()
        {
            pendingPreset = null;
        }

        /// <summary>
        /// One call per option card and one per preset card. <c>DrawIdeo</c> brackets the preset
        /// call, so a pending def claims the first selectable inside that bracket and is cleared
        /// immediately; everything else is an option card, in draw order.
        /// </summary>
        internal static void RecordSelectable(Rect rect)
        {
            if (!Recording)
            {
                return;
            }
            try
            {
                Rect screen = GuiSpace.VisibleScreenRect(rect);
                if (pendingPreset != null)
                {
                    recPresets.Add(new Card { Preset = pendingPreset, Screen = screen, Raw = rect });
                    pendingPreset = null;
                    return;
                }
                recOptions.Add(new Box { Screen = screen, Raw = rect });
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Ideo preset card capture error", ex);
            }
        }

        internal static void RecordStructureBox(Rect rect)
        {
            try
            {
                recStructure.Add(new Box { Screen = GuiSpace.VisibleScreenRect(rect), Raw = rect });
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Ideo preset structure capture error", ex);
            }
        }

        /// <summary>The page opens exactly one scroll view (decompiled :130); the first one inside the pass is it.</summary>
        internal static void RecordScrollView(Rect outRect, Rect viewRect)
        {
            if (scrollCaptured)
            {
                return;
            }
            scrollCaptured = true;
            recScrollOutRect = outRect;
            recScrollViewRect = viewRect;
        }

        private static void Publish()
        {
            try
            {
                published = false;
                pubOptions.Clear();
                pubStructure.Clear();
                pubPresets.Clear();
                pubOptions.AddRange(recOptions);
                pubStructure.AddRange(recStructure);
                pubPresets.AddRange(recPresets);
                pubScrollOutRect = recScrollOutRect;
                pubScrollViewRect = recScrollViewRect;
                publishedScroll = scrollCaptured;
                published = true;
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Ideo preset publish error", ex);
            }
        }
    }

    /// <summary>Brackets one page pass: everything the recorders collect belongs to it.</summary>
    [HarmonyPatch(typeof(Page_ChooseIdeoPreset), nameof(Page_ChooseIdeoPreset.DoWindowContents))]
    internal static class IdeoPresetPageBracketPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Page_ChooseIdeoPreset __instance)
        {
            IdeoPresetDrawPatch.BeginPage(__instance);
        }

        [HarmonyFinalizer]
        public static void Finalizer()
        {
            IdeoPresetDrawPatch.EndPage();
        }
    }

    /// <summary>The one widget every option card and every preset card is drawn by (decompiled :248).</summary>
    [HarmonyPatch(typeof(Page_ChooseIdeoPreset), "DrawSelectable")]
    internal static class IdeoPresetSelectablePatch
    {
        [HarmonyPostfix]
        public static void Postfix(Rect rect)
        {
            IdeoPresetDrawPatch.RecordSelectable(rect);
        }
    }

    /// <summary>Names the preset the next <c>DrawSelectable</c> belongs to (decompiled :270).</summary>
    [HarmonyPatch(typeof(Page_ChooseIdeoPreset), "DrawIdeo")]
    internal static class IdeoPresetIdeoBracketPatch
    {
        [HarmonyPrefix]
        public static void Prefix(IdeoPresetDef ideo)
        {
            IdeoPresetDrawPatch.BeginIdeo(ideo);
        }

        [HarmonyFinalizer]
        public static void Finalizer()
        {
            IdeoPresetDrawPatch.EndIdeo();
        }
    }

    /// <summary>
    /// The structure/style widget hands out no rects of its own (decompiled :320-409), so it is
    /// bracketed and its boxes arrive through the one widget each of them calls exactly once,
    /// <c>Widgets.ButtonInvisible</c> — the deity-box shape <see cref="IdeoBoxDrawPatch"/>
    /// documents. The trailing "Styles"/"Structure" labels draw no button and so record nothing.
    /// </summary>
    [HarmonyPatch(typeof(Page_ChooseIdeoPreset), "DrawStructureAndStyleSelection")]
    internal static class IdeoPresetStructureBracketPatch
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            IdeoPresetDrawPatch.BeginStructure();
        }

        [HarmonyFinalizer]
        public static void Finalizer()
        {
            IdeoPresetDrawPatch.EndStructure();
        }
    }

    /// <summary>
    /// One structure/style box per call, in draw order. This runs on every invisible button the
    /// game draws anywhere, so the static flag the bracket sets is literally the first statement
    /// and is all an off-path click target ever costs.
    /// </summary>
    [HarmonyPatch(typeof(Widgets), nameof(Widgets.ButtonInvisible), new[] { typeof(Rect), typeof(bool) })]
    internal static class IdeoPresetStructureBoxPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Rect butRect)
        {
            if (!IdeoPresetDrawPatch.RecordingStructure)
            {
                return;
            }
            IdeoPresetDrawPatch.RecordStructureBox(butRect);
        }
    }

    /// <summary>
    /// The category list's scroll view, whose band the option and preset cards are laid out in.
    /// TargetMethod because the by-ref Vector2 parameter type is not a compile-time constant
    /// (CS0182), the <c>BillListingScrollFollowPatch</c> precedent.
    /// </summary>
    [HarmonyPatch]
    internal static class IdeoPresetScrollViewPatch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Widgets), "BeginScrollView",
                new Type[] { typeof(Rect), typeof(Vector2).MakeByRefType(), typeof(Rect), typeof(bool) });
        }

        [HarmonyPrefix]
        public static void Prefix(Rect outRect, Rect viewRect)
        {
            if (!IdeoPresetDrawPatch.Recording)
            {
                return;
            }
            IdeoPresetDrawPatch.RecordScrollView(outRect, viewRect);
        }
    }
}
