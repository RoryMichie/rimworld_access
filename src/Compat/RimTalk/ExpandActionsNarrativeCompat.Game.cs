using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Narrative Feed producer P4: the
    /// ExpandActions extension's one silent surface. Its
    /// <c>ActionExecutor.ShowVisualFeedback</c> (decompiled source,
    /// RimTalkExpandActions.Memory.AI/ActionExecutor.cs:286) throws floating map text
    /// (<c>MoteMaker.ThrowText</c>) over the acting pawn carrying a bracketed Chinese action
    /// tag -- e.g. "[一起吃饭]" for social dining, "[恋人]" for a new romance -- with no toast or
    /// PlayLog counterpart on that path. It is the SOLE <c>MoteMaker.ThrowText</c> call site in
    /// the whole ExpandActions assembly (tree-wide grep confirmed), and every one of the mod's
    /// eight <c>Execute*</c> action handlers (recruit, romance accept/breakup, force rest,
    /// inspire fight/work, give item, social dining, social relax) funnels through it. A sighted
    /// player sees the tag float over the pawn; a blind player currently learns nothing.
    ///
    /// Resolved entirely by name/reflection so the ExpandActions assembly is never referenced at
    /// compile time (shipped-DLL drift safety, the <see cref="BubblesNarrativeCompat"/> pattern)
    /// -- a missing type or member self-disables the producer with one warning and never
    /// patches anything.
    ///
    /// LANGUAGE POLICY: this mod ships 100% hardcoded Chinese with
    /// no translation hooks. The bracketed tag is published AS-IS, brackets included -- parity
    /// with what a sighted player sees, never translation or English paraphrase.
    ///
    /// Every OTHER text channel this mod has (the recruitment letter, all sixteen action toasts,
    /// the two InteractionDefs it ships) already rides a vanilla vehicle already announced today:
    /// <see cref="NotificationAccessibilityPatch"/> covers Messages.Message and
    /// LetterStack.ReceiveLetter generically, and the vanilla PlayLog -&gt; Bubbler.Add pipeline
    /// (via P1 <see cref="BubblesNarrativeCompat"/>) covers its InteractionDefs. This producer is
    /// the only ExpandActions-specific code the mod needs.
    /// </summary>
    internal static class ExpandActionsNarrativeCompat
    {
        private const string PackageId = "sanguo.rimtalk.expandactions";

        public static void Register(Harmony harmony)
        {
            if (!ModsConfig.IsActive(PackageId))
            {
                return;
            }

            try
            {
                var surface = new ReflectionSurface("ExpandActions compat (mote narrative producer)");
                MethodInfo showVisualFeedbackMethod = surface.Method(
                    surface.Type("RimTalkExpandActions.Memory.AI.ActionExecutor"),
                    "ShowVisualFeedback",
                    new[] { typeof(Pawn), typeof(string), typeof(Color) });
                if (!surface.Ready)
                {
                    return;
                }

                harmony.Patch(showVisualFeedbackMethod,
                    postfix: new HarmonyMethod(typeof(ExpandActionsNarrativeCompat), nameof(Postfix)));
            }
            catch (Exception ex)
            {
                ModLogger.Error($"ExpandActions compat (mote narrative producer) registration failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Mirrors <c>ShowVisualFeedback</c>'s own gate (<c>pawn != null &amp;&amp; pawn.Spawned</c>)
        /// exactly -- a Harmony postfix always runs even when the original method's gate failed
        /// and no mote ever appeared, so a sighted player saw nothing and this must publish
        /// nothing either. Parameter names (<c>pawn</c>, <c>text</c>, <c>color</c>) match the
        /// decompiled source verbatim so Harmony binds them by position off the original method.
        /// </summary>
        private static void Postfix(Pawn pawn, string text, Color color)
        {
            try
            {
                if (pawn == null || !pawn.Spawned || string.IsNullOrEmpty(text))
                {
                    return;
                }

                // No object identity to key off (unlike a LogEntry) -- synthesize one from the
                // pawn, the tick it fired on, and the tag text itself. Good enough in practice:
                // ShowVisualFeedback fires at most once per Execute* call, and the same pawn
                // producing the same bracketed tag on the same game tick would be indistinguishable
                // to a sighted player too.
                string dedupeKey = $"expandactions-mote:{pawn.thingIDNumber}:{GenTicks.TicksGame}:{text}";

                var narrativeEvent = new NarrativeEvent(
                    pawn,
                    null,
                    text,
                    NarrativeSource.Extension,
                    dedupeKey,
                    // The mote is a pure visual effect -- ExpandActions never touches the RimTalk
                    // TTS addon for it, so this line is never vocalized.
                    vocalizedByTts: false);

                NarrativeFeed.Publish(narrativeEvent);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"ExpandActions compat (mote narrative producer) postfix failed: {ex.Message}");
            }
        }
    }
}
