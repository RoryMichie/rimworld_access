#if DEBUG
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// ShellDev's multi-frame sequence engine. The problem <see cref="InjectText"/>'s own header
    /// documents — an <see cref="Inject"/> sequence runs synchronously inside one drained main-thread
    /// action, so a chord that opens a menu and the very next chord/text race the SAME render frame the
    /// menu's first frame has (Index -1, a stray Enter no-ops) and the opener-twin-char suppression
    /// stamp eats the first typed character — this engine fixes it by spacing steps across REAL frames:
    /// <see cref="AdvanceSequence"/> runs once per <c>UIRootOnGUI</c> pass
    /// (<see cref="RimWorldAccess.DevBridge.DevBridgeDrainPatch"/>), so a "wait N" between two
    /// steps is N real frames, not zero.
    ///
    /// Script grammar: semicolon-separated tokens, each one of a chord (anything
    /// <see cref="KeyChord.Parse"/> accepts, e.g. "F12", "Ctrl+Shift+G"), "wait N" (an explicit
    /// frame count, added to the default), or "text:abc" (routed through <see cref="InjectText"/>
    /// character by character). The default wait applies BETWEEN two action steps that don't have
    /// an explicit "wait" between them; the very first action fires as soon as
    /// <see cref="AdvanceSequence"/> is first called unless the script itself opens with a "wait".
    /// </summary>
    public static partial class ShellDev
    {
        private struct SequenceAction
        {
            public bool IsText;
            public string Payload;
            public int WaitFramesBefore;
        }

        private static List<SequenceAction> sequenceActions;
        private static int sequenceActionIndex;
        private static int sequenceWaitRemaining;
        private static List<string> sequenceSummaries;
        private static int sequenceStartSpeechStamp;
        private static bool sequenceRunningFlag;

        /// <summary>Last frame <see cref="AdvanceSequence"/> did work — see the multi-pass guard inside it.</summary>
        private static int lastAdvanceFrame = -1;
        private static string sequenceResultValue;

        /// <summary>True while a sequence armed by <see cref="StartSequence"/> is still executing.</summary>
        internal static bool SequenceRunning
        {
            get { return sequenceRunningFlag; }
        }

        /// <summary>Null until the running sequence finishes (or throws); then the full report.</summary>
        internal static string SequenceResult
        {
            get { return sequenceResultValue; }
        }

        // l10n-exempt: dev-bridge-only diagnostic text, never player-facing.
        private const string SequenceAlreadyRunningError = "ERROR: a sequence is already running";

        /// <summary>
        /// Parses <paramref name="script"/> and arms it for <see cref="AdvanceSequence"/> to run
        /// one frame at a time. Returns an error string immediately (without arming anything) when
        /// a sequence is already running; returns null on a successful arm — poll
        /// <see cref="SequenceRunning"/>/<see cref="SequenceResult"/> for the outcome.
        /// </summary>
        internal static string StartSequence(string script, int defaultWaitFrames)
        {
            if (sequenceRunningFlag)
            {
                return SequenceAlreadyRunningError;
            }

            sequenceActions = ParseSequenceScript(script, Math.Max(0, defaultWaitFrames));
            sequenceActionIndex = 0;
            sequenceWaitRemaining = sequenceActions.Count > 0 ? sequenceActions[0].WaitFramesBefore : 0;
            sequenceSummaries = new List<string>();
            sequenceStartSpeechStamp = SpeechRingStamp();
            sequenceResultValue = null;
            sequenceRunningFlag = true;

            if (sequenceActions.Count == 0)
            {
                FinishSequence();
            }
            return null;
        }

        /// <summary>
        /// Called once per real frame (after <c>MainThreadDispatcher.DrainPending</c>). Executes
        /// the next due step, or counts down its wait by one frame. Any step throwing finishes the
        /// sequence immediately with what ran so far — it never leaves <see cref="SequenceRunning"/>
        /// stuck true.
        /// </summary>
        internal static void AdvanceSequence()
        {
            if (!sequenceRunningFlag)
            {
                return;
            }
            // UIRootOnGUI runs SEVERAL passes per frame (Layout, Repaint, one per queued input
            // event), and this is called from its postfix — without this guard a "wait N" melts
            // to a fraction of N real frames and two zero-wait steps can execute in the SAME
            // frame, reintroducing the exact same-frame race this engine exists to dodge.
            if (Time.frameCount == lastAdvanceFrame)
            {
                return;
            }
            lastAdvanceFrame = Time.frameCount;
            if (sequenceActionIndex >= sequenceActions.Count)
            {
                FinishSequence();
                return;
            }
            if (sequenceWaitRemaining > 0)
            {
                sequenceWaitRemaining--;
                return;
            }

            SequenceAction action = sequenceActions[sequenceActionIndex];
            try
            {
                sequenceSummaries.Add(action.IsText ? InjectText(action.Payload) : Inject(action.Payload));
            }
            catch (Exception ex)
            {
                sequenceSummaries.Add(action.Payload + " -> threw " + ex.GetType().Name);
            }

            sequenceActionIndex++;
            if (sequenceActionIndex < sequenceActions.Count)
            {
                sequenceWaitRemaining = sequenceActions[sequenceActionIndex].WaitFramesBefore;
            }
            else
            {
                FinishSequence();
            }
        }

        private static void FinishSequence()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < sequenceSummaries.Count; i++)
            {
                sb.Append(sequenceSummaries[i]);
                if (sequenceSummaries[i].Length == 0 || sequenceSummaries[i][sequenceSummaries[i].Length - 1] != '\n')
                {
                    sb.Append('\n');
                }
            }
            sb.Append("speech:\n").Append(SpeechSince(sequenceStartSpeechStamp));

            sequenceResultValue = sb.ToString();
            sequenceRunningFlag = false;
            sequenceActions = null;
            sequenceSummaries = null;
        }

        private static List<SequenceAction> ParseSequenceScript(string script, int defaultWaitFrames)
        {
            var actions = new List<SequenceAction>();
            string[] tokens = (script ?? string.Empty).Split(';');
            int pendingWait = 0;
            bool waitExplicit = false;
            bool first = true;

            for (int i = 0; i < tokens.Length; i++)
            {
                string token = tokens[i].Trim();
                if (token.Length == 0)
                {
                    continue;
                }
                if (TryParseWaitToken(token, out int frames))
                {
                    pendingWait += frames;
                    waitExplicit = true;
                    continue;
                }

                int waitBefore = waitExplicit ? pendingWait : (first ? 0 : defaultWaitFrames);
                bool isText = token.StartsWith("text:", StringComparison.OrdinalIgnoreCase);
                string payload = isText ? token.Substring(5) : token;
                if (isText)
                {
                    // One character per FRAME, not one text blob per step: typeahead's
                    // per-keystroke auto-expand rebuilds the search space, and feeding a whole
                    // word inside one frame races that rebuild (smoke-caught: "weap" typed
                    // same-frame found nothing where per-frame keys match fine). Two frames
                    // between characters mirrors a fast human typist.
                    for (int c = 0; c < payload.Length; c++)
                    {
                        actions.Add(new SequenceAction
                        {
                            IsText = true,
                            Payload = payload[c].ToString(),
                            WaitFramesBefore = c == 0 ? waitBefore : 2,
                        });
                    }
                }
                else
                {
                    actions.Add(new SequenceAction { IsText = isText, Payload = payload, WaitFramesBefore = waitBefore });
                }

                pendingWait = 0;
                waitExplicit = false;
                first = false;
            }
            return actions;
        }

        private static bool TryParseWaitToken(string token, out int frames)
        {
            frames = 0;
            if (!token.StartsWith("wait", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            string rest = token.Substring(4).Trim();
            return int.TryParse(rest, out frames);
        }
    }
}
#endif
