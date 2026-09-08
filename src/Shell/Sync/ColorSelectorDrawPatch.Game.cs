using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Geometry for the colour-swatch family: every palette in the game is one
    /// <c>Widgets.ColorSelector</c> call (decompiled Verse/Widgets.cs:2946-2969), which opens a
    /// group on its own rect and then draws one <c>Widgets.ColorBox</c> per colour in list order
    /// at a group-local rect. Vanilla outlines only the SELECTED swatch (:2929), never the
    /// keyboard cursor, so the focused swatch has no indicator of its own.
    ///
    /// The selector hands out no per-swatch rect, so it is bracketed and the swatches arrive
    /// through the one widget each of them calls exactly once — the deity-box shape
    /// <see cref="IdeoBoxDrawPatch"/> documents and <see cref="StylingGridDrawPatch"/> reuses.
    /// <c>ColorBox</c> is hot game-wide (mods call it directly), so the tap's first statement is
    /// the static flag the bracket sets, and nothing is ever recorded unarmed.
    ///
    /// <b>A list of passes, not one.</b> The styling station's apparel tab draws one selector per
    /// garment in a single frame, so each bracket publishes its own pass and the whole list is
    /// cleared by the first armed prefix of a new frame/event (the implicit pass boundary
    /// <see cref="RowDrawCapture"/> documents). A consumer uses a pass only when its swatch count
    /// matches the palette it expects; anything else rings nothing.
    ///
    /// <b>Two rects per swatch.</b> The screen rect is clip-aware and goes empty the moment a
    /// swatch scrolls out of its dialog's view, which is right for a ring and useless for
    /// deciding where to scroll TO; the group-local rect vanilla laid out is scroll-independent,
    /// and is what the follow clamps against.
    ///
    /// <b>The apparel tab's bands.</b> <c>Dialog_StylingStation.DrawApparelColor</c> gives each
    /// garment a 92f band whether or not it draws a selector into it (locked garments draw a
    /// static icon instead, decompiled :430-484), so those bands cannot come from this tap and
    /// are computed closed-form by <see cref="StylingStationScope"/> from the tab rect recorded
    /// here.
    /// </summary>
    internal static class ColorSelectorDrawPatch
    {
        /// <summary>One <c>Widgets.ColorSelector</c> call's swatches, in vanilla's own draw order.</summary>
        private sealed class SwatchPass
        {
            /// <summary>The selector's own rect, i.e. the origin its swatch rects are relative to.</summary>
            internal Rect GroupRect;

            /// <summary>Swatches as vanilla laid them out, group-local and scroll-independent.</summary>
            internal readonly List<Rect> Local = new List<Rect>();

            /// <summary>The same swatches in absolute UI points, clipped to what is visible.</summary>
            internal readonly List<Rect> Screen = new List<Rect>();
        }

        /// <summary>
        /// Read once per <c>Widgets.ColorBox</c> call game-wide, so it stays a plain field and is
        /// the tap's first statement.
        /// </summary>
        internal static bool Recording;

        private static readonly List<SwatchPass> passes = new List<SwatchPass>();
        private static int passCount;
        private static int passFrame = -1;
        private static EventType passEvent = EventType.Ignore;
        private static SwatchPass current;

        private static int interest;

        private static Rect apparelTabRect;
        private static Rect apparelTabScreenRect;
        private static int apparelTabFrame = -1;

        /// <summary>Declared by a consumer scope in OnPush, dropped in OnPop; counted, so two live consumers never silence each other.</summary>
        internal static void AddInterest()
        {
            interest++;
        }

        internal static void RemoveInterest()
        {
            if (interest > 0)
            {
                interest--;
            }
        }

        internal static void Begin(Rect rect)
        {
            try
            {
                if (interest == 0)
                {
                    return;
                }
                Event ev = Event.current;
                if (ev == null || ev.type == EventType.Layout)
                {
                    return;
                }
                if (Time.frameCount != passFrame || ev.type != passEvent)
                {
                    passCount = 0;
                    passFrame = Time.frameCount;
                    passEvent = ev.type;
                }
                if (passCount == passes.Count)
                {
                    passes.Add(new SwatchPass());
                }
                current = passes[passCount];
                current.GroupRect = rect;
                current.Local.Clear();
                current.Screen.Clear();
                Recording = true;
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Color selector capture error", ex);
            }
        }

        /// <summary>Arrives from a finalizer, so a modded palette that throws mid-draw disarms the tap all the same.</summary>
        internal static void End()
        {
            if (!Recording)
            {
                return;
            }
            Recording = false;
            current = null;
            passCount++;
        }

        internal static void RecordSwatch(Rect rect)
        {
            try
            {
                if (current == null)
                {
                    return;
                }
                // A swatch scrolled out of view still holds its ordinal slot, with an empty
                // screen rect: ordinal alignment with the palette is the whole contract.
                current.Local.Add(rect);
                current.Screen.Add(GuiSpace.VisibleScreenRect(rect));
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Color swatch capture error", ex);
            }
        }

        /// <summary>
        /// The <paramref name="ordinal"/>-th swatch of the ONE palette the last pass drew, for a
        /// dialog whose whole content is a single selector. False when the pass drew a different
        /// number of swatches than <paramref name="expectedCount"/>, or when more than one
        /// palette drew — either way the ordinal mapping is no longer trustworthy.
        /// </summary>
        internal static bool TryGetSoleSwatch(int expectedCount, int ordinal, out Rect screen, out Rect local, out Rect groupRect)
        {
            screen = default(Rect);
            local = default(Rect);
            groupRect = default(Rect);
            if (passCount != 1 || ordinal < 0 || ordinal >= expectedCount)
            {
                return false;
            }
            SwatchPass pass = passes[0];
            if (pass.Local.Count != expectedCount)
            {
                return false;
            }
            screen = pass.Screen[ordinal];
            local = pass.Local[ordinal];
            groupRect = pass.GroupRect;
            return true;
        }

        /// <summary>Recorded outside the tab's own scroll view, so the screen rect is the whole visible band, unscrolled.</summary>
        internal static void RecordApparelTab(Rect rect)
        {
            try
            {
                if (interest == 0)
                {
                    return;
                }
                Event ev = Event.current;
                if (ev == null || ev.type == EventType.Layout)
                {
                    return;
                }
                apparelTabRect = rect;
                apparelTabScreenRect = GuiSpace.ToScreen(rect);
                apparelTabFrame = Time.frameCount;
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Apparel color tab capture error", ex);
            }
        }

        /// <summary>
        /// The apparel-colour tab's content rect and its screen counterpart, or false when no
        /// pass within <paramref name="maxFrameAge"/> frames drew that tab — a ring must come
        /// from the pass it draws into, while a follow answering a key event may be one frame
        /// behind the last repaint of geometry that has not moved.
        /// </summary>
        internal static bool TryGetApparelTab(int maxFrameAge, out Rect tabRect, out Rect tabScreenRect)
        {
            tabRect = apparelTabRect;
            tabScreenRect = apparelTabScreenRect;
            return apparelTabFrame >= 0 && Time.frameCount - apparelTabFrame <= maxFrameAge;
        }
    }

    /// <summary>The palette bracket: see <see cref="ColorSelectorDrawPatch"/>'s remarks.</summary>
    [HarmonyPatch(typeof(Widgets), nameof(Widgets.ColorSelector))]
    internal static class ColorSelectorBracketPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Rect rect)
        {
            ColorSelectorDrawPatch.Begin(rect);
        }

        [HarmonyFinalizer]
        public static void Finalizer()
        {
            ColorSelectorDrawPatch.End();
        }
    }

    /// <summary>
    /// One swatch per call, in draw order. This runs on every colour box the game draws
    /// anywhere, so the static flag the bracket sets is literally the first statement and is all
    /// an off-path box ever costs.
    /// </summary>
    [HarmonyPatch(typeof(Widgets), nameof(Widgets.ColorBox))]
    internal static class ColorSelectorSwatchPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Rect rect)
        {
            if (!ColorSelectorDrawPatch.Recording)
            {
                return;
            }
            ColorSelectorDrawPatch.RecordSwatch(rect);
        }
    }

    /// <summary>
    /// The apparel-colour tab's own rect, taken before its scroll view opens (decompiled
    /// RimWorld/Dialog_StylingStation.cs:431-433) so the conversion is the tab's fixed position
    /// on screen rather than a scrolled row's.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_StylingStation), "DrawApparelColor")]
    internal static class StylingApparelColorTabPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Rect rect)
        {
            ColorSelectorDrawPatch.RecordApparelTab(rect);
        }
    }
}
