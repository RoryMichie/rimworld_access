using RimWorldAccess;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Per-keystroke echo for a text field that VANILLA owns (the "native-field adoption" mechanism):
    /// the real IMGUI control keeps keyboard focus, so typing, caret movement, OS clipboard, and IME
    /// all work natively — the scope never runs a TextInputController. This helper watches the field's
    /// backing string once per frame and speaks the difference using the same conventions
    /// TextInputController's own echo uses (typed char via SpeakData; deletions via the
    /// RimWorldAccess.TextInput.Deleted key), so native-mode fields sound identical to mirrored ones.
    ///
    /// One instance per scope; call <see cref="Reset"/> when the observed
    /// field changes (attach, focus moved to a different field) and
    /// <see cref="Observe"/> each frame with the field's current value.
    /// </summary>
    public sealed class NativeFieldEcho
    {
        private string lastValue;

        /// <summary>Start tracking a field silently from its current value.</summary>
        public void Reset(string initial)
        {
            lastValue = initial ?? "";
        }

        /// <summary>
        /// Speak whatever changed since the last call. Single-character
        /// appends and deletes at the end (the overwhelmingly common case for
        /// a caret at the end of a short name field) echo per char; anything
        /// larger (paste, select-all replace, mid-string edits) re-reads the
        /// full new value, or announces emptiness when cleared.
        /// </summary>
        public void Observe(string current)
        {
            current = current ?? "";
            string previous = lastValue ?? "";
            if (current == previous)
            {
                return;
            }
            lastValue = current;

            if (current.Length == previous.Length + 1 && current.StartsWith(previous))
            {
                TolkHelper.SpeakData(current[current.Length - 1].ToString(), SpeechPriority.High);
                return;
            }
            if (current.Length == previous.Length - 1 && previous.StartsWith(current))
            {
                string removed = previous[previous.Length - 1].ToString();
                TolkHelper.Speak("RimWorldAccess.TextInput.Deleted".Loc(removed), SpeechPriority.High);
                return;
            }
            if (current.Length == 0)
            {
                TolkHelper.Speak("RimWorldAccess.TextInput.Empty".Loc(), SpeechPriority.High);
                return;
            }
            TolkHelper.SpeakData(current, SpeechPriority.High);
        }
    }
}
