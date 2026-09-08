using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Announces whatever the mouse pointer has moved onto, read from the per-frame
    /// <see cref="WidgetCapture"/> stream. Announce-only: it never moves the keyboard cursor, never
    /// touches a scope's Model, never consumes a one-shot the keyboard is owed, never invokes a
    /// vanilla widget and never uses the event.
    ///
    /// There is exactly one evaluation site per capture bracket, so adding a screen never means
    /// adding hover code, and composition runs through the SAME path the keyboard reads with
    /// (<see cref="RimWorldAccess.CapturedRowFolder"/> → <see cref="CapturedExtrasRows"/> →
    /// <see cref="AnnouncementComposer"/>) rather than a parallel describer.
    ///
    /// The contract, from NVDA's mouse tracking: hover speaks on pointer MOVEMENT onto a different
    /// target, immediately and interrupting; a stationary pointer never speaks, even when the text
    /// under it changes. Identity (<see cref="HoverTargetKey"/>), never text, decides whether a
    /// target is different, so two rows that read identically both speak. Movement and identity are
    /// the whole of the flood control: no settle delay, no rate limit.
    /// </summary>
    internal static class HoverSpeech
    {
        private static HoverTargetKey spokenTarget = HoverTargetKey.None;

        /// <summary>
        /// Evaluations of SOME channel, counted once per frame in which any ran, and the count at
        /// which one last resolved a target. Several channels evaluate in the same frame and only
        /// one can hold the pointer, so a miss must not clear the target another just spoke.
        /// Counted evaluations rather than <c>Time.frameCount</c>, because a stationary pointer
        /// evaluates nothing and real frames say nothing about whether it has left its target.
        /// </summary>
        private static int evaluations;
        private static int evaluationFrame = -1;
        private static int lastHitEvaluation = -1;

        // Reused across frames: the hot path runs on every Repaint of every armed window.
        private static readonly List<CapturedWidget> passWidgets = new List<CapturedWidget>();
        private static readonly List<CapturedExtraMember> pureMembers = new List<CapturedExtraMember>();
        private static readonly List<string> attachedTips = new List<string>();
        private static readonly List<string> liveTips = new List<string>();
        private static readonly List<int> narrowedCell = new List<int>();

        /// <summary>Whether the hover channel is live at all — the one bool a disabled feature costs the capture brackets.</summary>
        internal static bool Enabled
        {
            get
            {
                RimWorldAccessSettings settings = RimWorldAccessMod_Settings.Settings;
                return settings != null && settings.HoverSpeech;
            }
        }

        /// <summary>
        /// Called from a capture bracket's postfix, after the armed window's draw and BEFORE
        /// <c>WidgetCapture.EndPass()</c>: at that instant the item stream is complete, the
        /// detached tooltip index is still this pass's, and the GUI matrix that produced the rects
        /// is still current.
        /// </summary>
        /// <param name="detachedTips">
        /// True where the bracket ran <c>TooltipCapture.BeginDetachedPass</c>. False for the generic
        /// reader, whose tips live in the ATTACHED index; resolving those against the detached one
        /// would read a leftover registration from an unrelated surface.
        /// </param>
        internal static void EvaluateWidgetPass(bool detachedTips)
        {
            EvaluatePass(WidgetCapture.Items, detachedTips);
        }

        /// <summary>
        /// The evaluation itself, over whichever item stream the bracket filled:
        /// <see cref="WidgetCapture.Items"/> for a live pass, a caller-owned sink for a detached one.
        /// </summary>
        internal static void EvaluatePass(IReadOnlyList<CapturedWidget> items, bool detachedTips)
        {
            try
            {
                if (!Enabled)
                {
                    Reset();
                    return;
                }
                // Rects only mean anything on Repaint, which also collapses the several IMGUI
                // events per frame down to one evaluation.
                if (Event.current == null || Event.current.type != EventType.Repaint)
                {
                    return;
                }
                // The off-screen measurement pass redraws the whole surface where no pointer can be.
                if (GuiSpace.ClipIsOffscreen())
                {
                    return;
                }

                if (!PointerMotion.MovedThisFrame())
                {
                    return;
                }
                CountEvaluation();

                // A surface that captured nothing is a MISS, not a reset: another pass this frame
                // may hold the pointer and have spoken it already.
                if (items == null || items.Count == 0)
                {
                    ForgetTargetAfterMiss();
                    return;
                }

                int hitIndex = HitTest(items, UI.MousePositionOnUIInverted);
                if (hitIndex >= 0 && SpeakHit(items, hitIndex, detachedTips))
                {
                    lastHitEvaluation = evaluations;
                    return;
                }
                // Silence is right for empty space, but only once no channel at all has resolved a
                // target for a whole evaluation.
                ForgetTargetAfterMiss();
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Hover speech error", ex);
            }
        }

        /// <summary>
        /// The command gizmo bar, which draws straight into the map UI with no window and no widget
        /// capture, so the stream-based pass above can never see it. Called from inside
        /// <see cref="RimWorldAccess.GizmoNavigationPatch"/>'s bracket, where
        /// <see cref="RimWorldAccess.GizmoRectRegistry"/> holds the rects vanilla just drew.
        /// </summary>
        internal static void EvaluateGizmoBar()
        {
            try
            {
                if (!ChromeGateOpen())
                {
                    return;
                }
                if (!RimWorldAccess.GizmoRectRegistry.TryHitTest(
                        UI.MousePositionOnUIInverted, out Gizmo gizmo, out int drawOrdinal))
                {
                    ForgetTargetAfterMiss();
                    return;
                }

                // No WithGizmoOwnerSelected wrap, unlike the keyboard's callers: this runs inside
                // vanilla's own gizmo-bar draw, where the live selection is already the resolving
                // context, and swapping it to read a label would break the announce-only contract.
                SpeakChrome(GizmoBandOrdinal, drawOrdinal, CapturedExtraKind.Button,
                    GizmoNavigationState.DescribeGizmo(gizmo, null), "gizmo-bar");
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Hover gizmo bar error", ex);
            }
        }

        /// <summary>
        /// The bottom main button bar. Its buttons are <c>Widgets.ButtonTextSubtle</c> calls whose
        /// caption is a separate widget drawn PAST the button's own right edge, and icon buttons
        /// draw no caption at all, so the capture stream can neither separate one button from the
        /// next nor name an icon button. <see cref="MainButtonRectRegistry"/> answers both.
        /// </summary>
        internal static void EvaluateMainButtonBar()
        {
            try
            {
                if (!ChromeGateOpen())
                {
                    return;
                }
                if (!MainButtonRectRegistry.TryHitTest(
                        UI.MousePositionOnUIInverted, out MainButtonDef def, out int drawOrdinal))
                {
                    ForgetTargetAfterMiss();
                    return;
                }
                SpeakChrome(MainButtonBandOrdinal, drawOrdinal, CapturedExtraKind.Button,
                    MainButtonRectRegistry.Describe(def), "main-buttons");
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Hover main button bar error", ex);
            }
        }

        /// <summary>
        /// The letter stack. Its caption is a bare <c>Widgets.Label</c> outside any capture pass and
        /// its hit area is a separate animating rect, so <see cref="LetterRectRegistry"/> is the hit
        /// test.
        /// </summary>
        internal static void EvaluateLetterStack()
        {
            try
            {
                if (!ChromeGateOpen())
                {
                    return;
                }
                if (!LetterRectRegistry.TryHitTest(
                        UI.MousePositionOnUIInverted, out Letter letter, out int drawOrdinal))
                {
                    ForgetTargetAfterMiss();
                    return;
                }
                SpeakChrome(LetterStackBandOrdinal, drawOrdinal, CapturedExtraKind.Button,
                    LetterRectRegistry.Describe(letter), "letter-stack");
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Hover letter stack error", ex);
            }
        }

        /// <summary>
        /// The colonist bar: portraits, mood and health painted with raw <c>GUI.DrawTexture</c>
        /// calls no capture tap sees. Vanilla's <c>ColonistOrCorpseAt</c> is the hit test, the same
        /// one <c>Selector</c> uses for a click, so hover resolves the entry the click would act on.
        /// </summary>
        internal static void EvaluateColonistBar()
        {
            try
            {
                if (!ChromeGateOpen())
                {
                    return;
                }
                ColonistBar bar = Find.ColonistBar;
                Thing entry = bar != null ? bar.ColonistOrCorpseAt(UI.MousePositionOnUIInverted) : null;
                if (entry == null)
                {
                    ForgetTargetAfterMiss();
                    return;
                }
                var description = new ElementDescription
                {
                    Label = RimWorldAccess.ColonistBarState.ComposeEntryLine(entry),
                    Role = ElementRole.Button,
                };
                liveTips.Clear();
                LiveTooltips.CollectSinceBracket(liveTips);
                description.Extras = TooltipTextJoin.Append(description.Extras, liveTips);
                // thingIDNumber, not the draw ordinal: entries reorder under the pointer and two
                // colonists can share a nickname.
                SpeakChrome(ColonistBarBandOrdinal, entry.thingIDNumber, CapturedExtraKind.Button,
                    description, "colonist-bar");
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Hover colonist bar error", ex);
            }
        }

        /// <summary>
        /// The guards every chrome bar shares. The window gate is what keeps a dialog over a bar
        /// from being read through from underneath.
        /// </summary>
        private static bool ChromeGateOpen()
        {
            if (!Enabled || Event.current == null || Event.current.type != EventType.Repaint)
            {
                return false;
            }
            if (GuiSpace.ClipIsOffscreen() || !PointerMotion.MovedThisFrame())
            {
                return false;
            }
            if (ShellGuards.NonImmediateWindowUnderPointer())
            {
                return false;
            }
            CountEvaluation();
            return true;
        }

        /// <summary>One tick per frame in which any channel got as far as hit-testing.</summary>
        private static void CountEvaluation()
        {
            if (evaluationFrame != Time.frameCount)
            {
                evaluationFrame = Time.frameCount;
                evaluations++;
            }
        }

        /// <summary>The shared tail of every chrome hit: one identity check, then the shared composition.</summary>
        private static void SpeakChrome(int bandOrdinal, int drawOrdinal, CapturedExtraKind kind,
            ElementDescription description, string qaBand)
        {
            lastHitEvaluation = evaluations;
            if (description == null || string.IsNullOrEmpty(description.Label))
            {
                return;
            }
            var key = new HoverTargetKey(bandOrdinal, drawOrdinal, kind, description.Label);
            if (key.Equals(spokenTarget))
            {
                return;
            }
            spokenTarget = key;
            string text = AnnouncementComposer.ComposeFocus(
                description,
                TranslatedShellVocabulary.Instance,
                TextDialogShared.StandardComposeOptions());
            if (!string.IsNullOrEmpty(text))
            {
                TolkHelper.SpeakData(text, SpeechPriority.High);
                FlightRecorder.Record("hover", qaBand + ": " + drawOrdinal + " \"" + key.RawLabel + "\""
                    + AtPointer());
            }
        }

        /// <summary>
        /// Forgets the spoken target after a miss, under the cross-channel rule
        /// <see cref="evaluations"/> documents.
        /// </summary>
        private static void ForgetTargetAfterMiss()
        {
            if (evaluations - lastHitEvaluation > 1)
            {
                spokenTarget = HoverTargetKey.None;
                HoverLanding.Clear();
            }
        }

        /// <summary>
        /// Band ordinals for the surfaces that have no capture pass and therefore no bands. Disjoint
        /// from every pass's and from <see cref="HoverTargetKey.None"/>, so moving between a window
        /// and a bar, or between two bars, always reads as a target change.
        /// </summary>
        private const int GizmoBandOrdinal = -2;
        private const int MainButtonBandOrdinal = -3;
        private const int ColonistBarBandOrdinal = -4;
        private const int LetterStackBandOrdinal = -5;

        /// <summary>
        /// Where the pointer was when a hover line was recorded. A re-announced target reads as a
        /// defect in the trace unless the coordinates show the pointer leaving and coming back.
        /// </summary>
        private static string AtPointer()
        {
            Vector2 pos = UI.MousePositionOnUIInverted;
            return " at " + (int)pos.x + "," + (int)pos.y;
        }

        /// <summary>What hover last announced, for the QA recorder's click channel.</summary>
        internal static string DebugSpokenTarget
        {
            get
            {
                return spokenTarget.Equals(HoverTargetKey.None)
                    ? "none"
                    : spokenTarget.Kind + " \"" + spokenTarget.RawLabel + "\"";
            }
        }

        private static void Reset()
        {
            spokenTarget = HoverTargetKey.None;
            HoverLanding.Clear();
            passWidgets.Clear();
            attachedTips.Clear();
            liveTips.Clear();
        }

        /// <summary>
        /// The deepest widget containing the point, ties broken by latest draw order. Deepest wins
        /// because the deepest container IS the innermost control under the pointer. An empty
        /// visible rect means scrolled out of its container and can never be under the pointer.
        /// </summary>
        private static int HitTest(IReadOnlyList<CapturedWidget> items, Vector2 mouse)
        {
            int best = -1;
            int bestDepth = int.MinValue;
            for (int i = 0; i < items.Count; i++)
            {
                Rect rect = items[i].VisibleScreenRect;
                if (rect.width <= 0f || rect.height <= 0f || !rect.Contains(mouse))
                {
                    continue;
                }
                int depth = items[i].Clip.Depth;
                if (best < 0 || depth >= bestDepth)
                {
                    best = i;
                    bestDepth = depth;
                }
            }
            return best;
        }

        /// <summary>False when the hit resolved to nothing readable, which the caller treats as a miss.</summary>
        private static bool SpeakHit(IReadOnlyList<CapturedWidget> items, int hitIndex, bool detachedTips)
        {
            passWidgets.Clear();
            for (int i = 0; i < items.Count; i++)
            {
                passWidgets.Add(items[i]);
            }

            List<List<int>> bands = RimWorldAccess.CapturedRowFolder.BandsFor(passWidgets);
            int bandOrdinal = BandOf(bands, hitIndex);
            if (bandOrdinal < 0)
            {
                return false;
            }
            List<int> cell = NarrowToHitCell(bands[bandOrdinal], hitIndex);
            RimWorldAccess.FoldedRow folded = FoldBandWithTips(cell ?? bands[bandOrdinal], detachedTips);
            if (folded == null)
            {
                return false;
            }

            pureMembers.Clear();
            List<RimWorldAccess.InteractiveMember> interactives = folded.Interactives;
            if (interactives != null)
            {
                for (int i = 0; i < interactives.Count; i++)
                {
                    pureMembers.Add(ScreenScope.ToPureMember(interactives[i]));
                }
            }
            List<CapturedExtraRow> expanded = CapturedExtrasRows.Expand(folded.Text, folded.Tip, pureMembers);
            if (expanded.Count == 0)
            {
                return false;
            }

            // Expand is index-aligned with its members, so the control under the pointer picks its
            // own expanded row; a pointer on the row's plain label falls back to the row itself.
            int memberOrdinal = -1;
            if (interactives != null)
            {
                for (int i = 0; i < interactives.Count && i < expanded.Count; i++)
                {
                    if (ReferenceEquals(interactives[i].Source, passWidgets[hitIndex]))
                    {
                        memberOrdinal = i;
                        break;
                    }
                }
            }
            int pick = memberOrdinal >= 0 ? memberOrdinal : 0;

            CapturedWidget hit = passWidgets[hitIndex];
            // Before the already-spoken early return: a pointer resting on a target it already
            // announced must still be routable.
            HoverLanding.Record(hit);
            var key = new HoverTargetKey(bandOrdinal, memberOrdinal, cell != null ? hitIndex : -1,
                ScreenScope.MapCapturedKind(hit.Kind), hit.Label);
            if (key.Equals(spokenTarget))
            {
                return true;
            }

            // A label hover in a multi-member line reads THAT label, not the first control's row.
            if (memberOrdinal < 0 && hit.Kind == WidgetKind.Label
                && !string.IsNullOrWhiteSpace(hit.SpokenLabel) && pureMembers.Count != 1)
            {
                expanded[pick] = new CapturedExtraRow
                {
                    Label = hit.SpokenLabel,
                    Role = ElementRole.None,
                    ReadOnly = true,
                    Tip = expanded[pick].Tip,
                };
            }
            else
            {
                // A narrowed cell IS the click target the pointer is on, so it reads with the role
                // a sighted player sees, not as the inert line the fold would make of it.
                if (cell != null && expanded[pick].Role == ElementRole.None)
                {
                    expanded[pick].Role = CapturedExtrasRows.RoleFor(key.Kind);
                    expanded[pick].ReadOnly = false;
                }
                // Unlabeled control in a multi-control row: the row's leading label, never Expand's whole-row fallback.
                if (pick < pureMembers.Count && string.IsNullOrEmpty(pureMembers[pick].RawLabel)
                    && (cell != null || pureMembers.Count > 1))
                {
                    expanded[pick].Label = LeadingBandLabel(bands[bandOrdinal]);
                }
            }

            // Live tips add the caller-gated registrations; Append's containment rule stops repeats.
            ElementDescription description = CapturedExtrasRows.Describe(expanded[pick]);
            liveTips.Clear();
            LiveTooltips.CollectSinceBracket(liveTips);
            description.Extras = TooltipTextJoin.Append(description.Extras, liveTips);

            // No AnnouncePrefix, entry-region frame, position fragment, or one-shot state: hover
            // must never consume an announcement the keyboard is owed.
            string text = AnnouncementComposer.ComposeFocus(
                description,
                TranslatedShellVocabulary.Instance,
                TextDialogShared.StandardComposeOptions());
            spokenTarget = key;
            if (!string.IsNullOrEmpty(text))
            {
                // High is the only priority that interrupts, and a hover utterance queued behind
                // the previous one would describe a place the pointer has already left.
                TolkHelper.SpeakData(text, SpeechPriority.High);
                PlayTabTick(key.Kind);
                FlightRecorder.Record("hover", (FocusStack.Top != null ? FocusStack.Top.Name : "none")
                    + ": " + key.Kind + " \"" + key.RawLabel + "\""
                    + (key.CellOrdinal >= 0 ? " cell " + key.CellOrdinal : "")
                    + ", live tips " + liveTips.Count + AtPointer());
            }
            return true;
        }

        /// <summary>
        /// The band members the hit widget's own rect covers, or null when the whole band should be
        /// folded as it is. A click target with no label of its own draws its caption as a separate
        /// widget INSIDE its rect — <c>Widgets.ButtonTextSubtle</c> is exactly this shape — and a bar
        /// of them lands on one screen line, so the band is the whole bar; narrowing to what the hit
        /// target covers reads the one button under the pointer.
        ///
        /// Membership is by CENTRE, not overlap: ButtonTextSubtle draws its caption inset while
        /// keeping the button's full width, so every caption rect spills into the NEXT button and an
        /// overlap test would hand each button its left neighbour's caption too. A caption's centre
        /// always stays inside the button that drew it.
        ///
        /// Deliberately narrow: it triggers only for an unlabeled BUTTON that really does draw
        /// another band member inside itself, so multi-cell rows, whole-row invisible buttons, and
        /// controls whose caption sits BESIDE them all fold as before.
        /// </summary>
        private static List<int> NarrowToHitCell(List<int> band, int hitIndex)
        {
            CapturedWidget hit = passWidgets[hitIndex];
            if (band.Count < 2 || !IsUnlabeledClickTarget(hit))
            {
                return null;
            }
            narrowedCell.Clear();
            for (int i = 0; i < band.Count; i++)
            {
                int index = band[i];
                if (index == hitIndex || hit.ScreenRect.Contains(passWidgets[index].ScreenRect.center))
                {
                    narrowedCell.Add(index);
                }
            }
            return narrowedCell.Count > 1 && narrowedCell.Count < band.Count ? narrowedCell : null;
        }

        private static string LeadingBandLabel(List<int> band)
        {
            for (int i = 0; i < band.Count; i++)
            {
                CapturedWidget w = passWidgets[band[i]];
                if (w.Kind == WidgetKind.Label && !string.IsNullOrWhiteSpace(w.SpokenLabel))
                {
                    return w.SpokenLabel;
                }
            }
            return "";
        }

        private static bool IsUnlabeledClickTarget(CapturedWidget widget)
        {
            return (widget.Kind == WidgetKind.InvisibleButton || widget.Kind == WidgetKind.Button)
                && string.IsNullOrWhiteSpace(widget.SpokenLabel);
        }

        /// <summary>
        /// Vanilla's own tab mouseover sound on a hovered tab. Additive, and scoped to the tab kind
        /// because a tab strip is the only widget family vanilla gives this sound to.
        /// </summary>
        private static void PlayTabTick(CapturedExtraKind kind)
        {
            if (kind == CapturedExtraKind.Tab && SoundDefOf.Mouseover_Tab != null)
            {
                SoundDefOf.Mouseover_Tab.PlayOneShotOnCamera();
            }
        }

        /// <summary>
        /// Folds one band, resolving its tooltips from whichever index this bracket filled — see
        /// <see cref="EvaluateWidgetPass"/>'s parameter remarks. The attached path pre-resolves per
        /// widget because <see cref="RimWorldAccess.CapturedRowFolder.FoldSubset"/> is the only fold
        /// that takes tips from the caller.
        /// </summary>
        private static RimWorldAccess.FoldedRow FoldBandWithTips(List<int> band, bool detachedTips)
        {
            if (detachedTips)
            {
                return RimWorldAccess.CapturedRowFolder.FoldBand(passWidgets, band);
            }
            while (attachedTips.Count < passWidgets.Count)
            {
                attachedTips.Add(null);
            }
            for (int i = 0; i < band.Count; i++)
            {
                int index = band[i];
                attachedTips[index] = TooltipCapture.TryResolveAtScreen(
                    passWidgets[index].ScreenRect, passWidgets[index].Clip);
            }
            return RimWorldAccess.CapturedRowFolder.FoldSubset(passWidgets, band, attachedTips);
        }

        private static int BandOf(List<List<int>> bands, int widgetIndex)
        {
            for (int i = 0; i < bands.Count; i++)
            {
                List<int> band = bands[i];
                for (int j = 0; j < band.Count; j++)
                {
                    if (band[j] == widgetIndex)
                    {
                        return i;
                    }
                }
            }
            return -1;
        }
    }
}
