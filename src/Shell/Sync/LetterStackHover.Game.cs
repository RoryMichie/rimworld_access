using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The rect vanilla itself drew for each letter of the screen-corner letter stack, keyed by the
    /// letter, so the pointer can read a letter the way it reads a main button or a gizmo.
    ///
    /// A registry rather than the capture stream, for the same reason the main button bar needs one:
    /// the stack draws from <c>LetterStack.LettersOnGUI</c> with no window and no capture pass
    /// around it, and the letter's own caption goes out through a bare <c>Widgets.Label</c> that
    /// only runs on Repaint while its hit area is a separate <c>Widgets.ButtonInvisible</c> on a
    /// rect that bounces and slides as the letter animates (decompiled Verse/Letter.cs:83-146).
    /// Nothing downstream could pair those two, and the rect the click lands on is the animated one
    /// — which is the rect recorded here.
    /// </summary>
    internal static class LetterRectRegistry
    {
        private static readonly Dictionary<Letter, Rect> rects = new Dictionary<Letter, Rect>();

        /// <summary>Draw order of <see cref="rects"/>' keys — the ordinal hover identifies a hit letter by.</summary>
        private static readonly List<Letter> drawOrder = new List<Letter>();

        private static bool recording;

        /// <summary>Frame of the most recent stack draw — see <see cref="TryHitTest"/>.</summary>
        private static int lastDrawFrame = -1;

        /// <summary>
        /// Opens the recording window and drops the previous draw's rects, so a letter that has
        /// since been dismissed can never be hit-tested against a stale rect.
        /// </summary>
        internal static void BeginFrame()
        {
            rects.Clear();
            drawOrder.Clear();
            lastDrawFrame = Time.frameCount;
            recording = true;
        }

        /// <summary>Closes the recording window. Called from a <c>finally</c> so a thrown draw pass cannot leave it open.</summary>
        internal static void EndFrame()
        {
            recording = false;
        }

        /// <summary>No-op unless called between <see cref="BeginFrame"/> and <see cref="EndFrame"/>.</summary>
        internal static void Record(Letter letter, Rect rect)
        {
            if (!recording || letter == null)
            {
                return;
            }
            if (!rects.ContainsKey(letter))
            {
                drawOrder.Add(letter);
            }
            rects[letter] = rect;
        }

        /// <summary>
        /// The letter whose drawn rect contains <paramref name="point"/> (UI space), with its draw
        /// ordinal. Last drawn wins an overlap, matching the topmost thing a pointer would hit. A
        /// stack that has not drawn since the previous frame owns nothing.
        /// </summary>
        internal static bool TryHitTest(Vector2 point, out Letter letter, out int ordinal)
        {
            if (Time.frameCount - lastDrawFrame > 1)
            {
                letter = null;
                ordinal = -1;
                return false;
            }
            for (int i = drawOrder.Count - 1; i >= 0; i--)
            {
                if (rects.TryGetValue(drawOrder[i], out Rect rect) && rect.Contains(point))
                {
                    letter = drawOrder[i];
                    ordinal = i;
                    return true;
                }
            }
            letter = null;
            ordinal = -1;
            return false;
        }

        // Both are protected on Letter and both are virtual, so they are invoked on the instance:
        // a timeout letter appends its remaining days to the label, and the mouseover text is
        // abstract, so only the letter's own override can answer either.
        private static readonly MethodInfo PostProcessedLabelMethod =
            AccessTools.Method(typeof(Letter), "PostProcessedLabel");
        private static readonly MethodInfo MouseoverTextMethod =
            AccessTools.Method(typeof(Letter), "GetMouseoverText");

        /// <summary>
        /// What the letter says: the label a sighted player reads off the stack, and the hover text
        /// vanilla itself would show for it — both from the letter's own methods, so a modded letter
        /// that overrides either is read as it presents itself.
        /// </summary>
        internal static ElementDescription Describe(Letter letter)
        {
            var description = new ElementDescription();
            if (letter == null)
            {
                return description;
            }
            description.Role = ElementRole.Button;
            description.Label = Invoke(letter, PostProcessedLabelMethod) ?? letter.Label.Resolve();
            description.Extras = Invoke(letter, MouseoverTextMethod);
            return description;
        }

        /// <summary>A broken modded letter must not cost the row its label, so each half stands alone.</summary>
        private static string Invoke(Letter letter, MethodInfo method)
        {
            if (method == null)
            {
                return null;
            }
            try
            {
                return method.Invoke(letter, null) as string;
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Letter describe error", ex);
                return null;
            }
        }
    }

    /// <summary>
    /// Evaluates letter hover once the whole stack has drawn, so the topmost letter under the
    /// pointer wins rather than whichever one happened to draw last.
    /// </summary>
    [HarmonyPatch(typeof(LetterStack), nameof(LetterStack.LettersOnGUI))]
    internal static class LetterStackHoverPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        public static void Prefix()
        {
            try
            {
                LetterRectRegistry.BeginFrame();
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Letter stack hover error", ex);
            }
        }

        /// <summary>
        /// A finalizer, not a postfix: the recording window must close even when a modded letter's
        /// draw throws, or the next frame's hit test would run against half a stack.
        /// </summary>
        [HarmonyFinalizer]
        public static Exception Finalizer(Exception __exception)
        {
            try
            {
                if (__exception == null)
                {
                    HoverSpeech.EvaluateLetterStack();
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Letter stack hover error", ex);
            }
            finally
            {
                LetterRectRegistry.EndFrame();
            }
            return __exception;
        }
    }

    /// <summary>
    /// The per-letter half. <see cref="Letter.DrawButtonAt"/> is virtual and both vanilla's own
    /// bundle letter and modded letters may override it without calling base, which would escape a
    /// patch on the base type (the CLAUDE.md inherited-method rule), so every subclass that
    /// DECLARES the method is patched, found by walking the type graph rather than by hand-listing.
    ///
    /// The rect is read AFTER the body rather than recomputed: the animated y-slide and the bounce
    /// offset are computed inside it, and the click lands on the animated rect, not the laid-out one
    /// (decompiled Verse/Letter.cs:88-101, :142). Vanilla exposes no accessor for it, so the
    /// animation is mirrored here — the one thing in this file that has to be kept in step with
    /// vanilla, and the reason the mirror lives next to the hit test that depends on it.
    /// </summary>
    [HarmonyPatch]
    internal static class LetterRectCapturePatch
    {
        internal static IEnumerable<MethodBase> TargetMethods()
        {
            // Routed through SafeTypeSweep for the same reason the gizmo and main-button rect
            // captures are (see their headers): a single poisoned mod type can throw out of the
            // assignability test or the method lookup, not just out of the initial enumeration.
            foreach (Type type in SafeTypeSweep.AssignableFromSafe(typeof(Letter),
                (_, ex) => ModLogger.LimitedError("Letter rect capture error", ex)))
            {
                if (!SafeTypeSweep.TryEvaluate(type,
                        t => AccessTools.DeclaredMethod(t, "DrawButtonAt", new[] { typeof(float) }),
                        out MethodInfo declared,
                        (_, ex) => ModLogger.LimitedError("Letter rect capture error", ex)))
                {
                    continue;
                }
                if (declared != null && !declared.IsAbstract && !declared.ContainsGenericParameters)
                {
                    yield return declared;
                }
            }
        }

        // Positional binding: an override is free to rename the parameter, and Harmony's by-name
        // binding throws at patch time when it does.
        [HarmonyPostfix]
        internal static void Postfix(Letter __instance, [HarmonyArgument(0)] float topY)
        {
            try
            {
                LetterRectRegistry.Record(__instance, AnimatedRect(__instance, topY));
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Letter rect capture error", ex);
            }
        }

        /// <summary>
        /// Mirrors <see cref="Letter.DrawButtonAt"/>'s own rect arithmetic, including the arrival
        /// slide and the periodic bounce (decompiled Verse/Letter.cs:85-101) — the rect its own
        /// ButtonInvisible is placed on, so hover and click agree on where the letter is.
        /// </summary>
        private static Rect AnimatedRect(Letter letter, float topY)
        {
            float x = UI.screenWidth - 38f - 12f;
            Rect rect = new Rect(x, topY, 38f, 30f);
            Rect animated = new Rect(rect);
            float since = Time.time - letter.arrivalTime;
            if (since < 1f)
            {
                animated.y -= (1f - since) * 200f;
            }
            if (!Mouse.IsOver(rect) && letter.def != null && letter.def.bounce && since > 15f && since % 5f < 1f)
            {
                float amplitude = UI.screenWidth * 0.06f;
                float phase = 2f * (since % 1f) - 1f;
                animated.x -= amplitude * (1f - phase * phase);
            }
            return animated;
        }
    }
}
