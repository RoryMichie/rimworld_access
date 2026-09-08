#if DEBUG
using System;
using System.Text;
using UnityEngine;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// ShellDev's text-injection surface: replays typed text through the SAME character-consuming
    /// routing the shell dispatcher uses for a real keystroke
    /// (<see cref="ShellDispatcherPatch.RouteCharacterEvent"/>) — IME funnel, CJK menu-search prompt,
    /// modal text session, then live CharSinks (typeahead/text entry) — so a bridge script can drive
    /// typeahead search or a text field without a human at the keyboard.
    ///
    /// <see cref="Inject"/> already replays a KeyDown through the WHOLE dispatcher (chords
    /// included), and in isolation its character path works fine. The reason it was unreliable
    /// for typeahead specifically, diagnosed from <see cref="ShellDispatcherPatch.Prefix"/>
    /// itself: the opener-twin-char suppression stamp (<c>charSinkChangedFrame</c>) samples
    /// <see cref="FocusStack.TopCharSinkScope"/> once per <c>Prefix</c> call and, when it changes
    /// to a non-null scope, stamps the CURRENT <c>Time.frameCount</c> so that same render frame's
    /// characters are treated as the opener keystroke's twins and dropped. A dev-bridge sequence
    /// like "open a menu, then type into it" runs both <c>Inject</c> calls synchronously inside
    /// one drained main-thread action — no real Unity frame advances between them, so
    /// <c>Time.frameCount</c> is IDENTICAL for the open and the very next injected character. The
    /// menu's CharSink appearing on the stack between those two calls looks, to the suppression stamp,
    /// exactly like the same-frame opener-twin case it exists to filter — so the first typed character
    /// was silently eaten by a guard meant to protect against something else entirely. This method
    /// doesn't defeat that guard (doing so would test a path real keystrokes never take); the
    /// <c>StartSequence</c>/<c>AdvanceSequence</c> engine is the actual fix, spacing steps across real
    /// frames so the stamp ages out naturally.
    /// </summary>
    public static partial class ShellDev
    {
        /// <summary>
        /// Routes each character of <paramref name="text"/> through
        /// <see cref="ShellDispatcherPatch.RouteCharacterEvent"/> in turn and reports what
        /// happened to each, plus a trailing line for the row the cursor now rests on.
        /// </summary>
        public static string InjectText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                string nothingToInject = "(no characters to inject)";
                return nothingToInject;
            }

            var sb = new StringBuilder();
            Event prev = Event.current;
            KeyboardHelper.InjectionOverrideActive = true;
            KeyboardHelper.InjectedCtrl = false;
            KeyboardHelper.InjectedAlt = false;
            try
            {
                for (int i = 0; i < text.Length; i++)
                {
                    char c = text[i];
                    Event synthetic = new Event();
                    synthetic.type = EventType.KeyDown;
                    synthetic.keyCode = KeyCode.None;
                    synthetic.character = c;
                    synthetic.modifiers = EventModifiers.None;
                    Event.current = synthetic;

                    sb.Append(c).Append(" -> ");
                    try
                    {
                        bool consumed = ShellDispatcherPatch.RouteCharacterEvent(synthetic, out string stage);
                        sb.Append(consumed ? stage : "fell through");
                    }
                    catch (Exception ex)
                    {
                        sb.Append("threw ").Append(ex.GetType().Name);
                    }
                    sb.Append('\n');
                }
            }
            finally
            {
                KeyboardHelper.InjectionOverrideActive = false;
                KeyboardHelper.InjectedCtrl = false;
                KeyboardHelper.InjectedAlt = false;
                Event.current = prev;
            }

            sb.Append("row: ").Append(DescribeFocusedRow());
            return sb.ToString();
        }
    }
}
#endif
