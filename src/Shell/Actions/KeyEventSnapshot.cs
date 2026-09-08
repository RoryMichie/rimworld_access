using UnityEngine;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The dispatcher's one immutable read of a keyboard event: the (already
    /// layout-remapped) key, the effective modifier states, and the typed
    /// character for text/typeahead sinks. Built once per KeyDown by the shell
    /// dispatcher from Event.current + KeyboardHelper's platform-aware modifier
    /// reads; handlers and chords only ever see this, never Event.current.
    ///
    /// PURE: links into the test project, where tests construct snapshots
    /// directly to drive dispatch without a game.
    /// </summary>
    public readonly struct KeyEventSnapshot
    {
        public readonly KeyCode Key;
        public readonly bool Ctrl;
        public readonly bool Shift;
        public readonly bool Alt;

        /// <summary>
        /// The layout-aware typed character ('\0' when none). Text and typeahead
        /// consumers gate on char.IsLetter/IsDigit of this — never on KeyCode
        /// ranges, which break on non-US layouts.
        /// </summary>
        public readonly char Character;

        public KeyEventSnapshot(KeyCode key, bool ctrl = false, bool shift = false, bool alt = false, char character = '\0')
        {
            Key = key;
            Ctrl = ctrl;
            Shift = shift;
            Alt = alt;
            Character = character;
        }

        /// <summary>Snapshot carrying the same key and modifiers as a chord — handy in tests.</summary>
        public static KeyEventSnapshot FromChord(KeyChord chord, char character = '\0')
        {
            return new KeyEventSnapshot(chord.Key, chord.Ctrl, chord.Shift, chord.Alt, character);
        }

        public override string ToString()
        {
            string mods = (Ctrl ? "Ctrl+" : "") + (Shift ? "Shift+" : "") + (Alt ? "Alt+" : "");
            string ch = Character == '\0' ? "" : " '" + Character + "'";
            return mods + Key + ch;
        }
    }
}
