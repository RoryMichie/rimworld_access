using System;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// IME composition funnel for the CJK languages. The mod captures text synthetically from
    /// <c>Event.current.character</c>, which works for direct keyboard layouts but cannot work for
    /// composition-based input: pinyin keystrokes must be composed through an OS candidate window
    /// into a real, focused Unity <c>GUI.TextField</c> before a finished character exists at all.
    ///
    /// While a text sink is active in a CJK language, this host draws an offscreen focused
    /// TextField every OnGUI pass and turns on <c>Input.imeCompositionMode</c>. Committed characters
    /// are harvested by diffing the field's returned string and fed back through the mod's normal
    /// character routing; the field is purely a commit catcher, never a buffer — its cursor,
    /// selection and editing are unused.
    ///
    /// Routing (<see cref="TryRouteKeyDown"/>): while composing, EVERY key belongs to the IME, which
    /// uses it to navigate candidates, commit, or edit the pinyin. While not composing, only letter
    /// keys route to the field, so digits, punctuation, arrows, Enter and hotkeys behave exactly as
    /// they do for non-IME players. Gated to CJK languages, so a direct-layout player never gets a
    /// hidden field or focus management at all.
    /// </summary>
    public static class ImeInputHost
    {
        // The field must actually be drawn — IMGUI is immediate-mode, and an undrawn control
        // processes no events — but never needs to be visible: the player perceives it only through
        // the OS candidate window their screen reader already reads.
        private const string FieldName = "RWA_IME_FunnelField";
        private static readonly Rect OffscreenRect = new Rect(-400f, -400f, 200f, 30f);

        private static bool active;
        // Seed string for the field, reset to empty whenever no composition is in progress so a
        // commit always appears as a fresh append and the buffer never grows unbounded. Left alone
        // while composing, where the commit is still pending.
        private static string fieldValue = string.Empty;
        private static bool composingLastDraw;
        private static IMECompositionMode savedMode;
        private static bool hasSavedMode;

        /// <summary>True while the funnel is engaged (a text sink is active in a CJK language).</summary>
        public static bool IsActive => active;

        /// <summary>Whether a composition was in progress at the most recent field draw — the routing rule's own test.</summary>
        public static bool IsComposing => composingLastDraw;

        /// <summary>
        /// Once per OnGUI pass, from the top of the keyboard prefix: manages the active state and,
        /// on non-KeyDown passes, draws the hidden field so it keeps focus and its composition state
        /// between keystrokes. KeyDown passes draw through <see cref="TryRouteKeyDown"/> instead, so
        /// the field is drawn exactly once per pass — twice in one pass is an IMGUI error.
        /// </summary>
        public static void Pump(bool sinkActive, Action<char> onCommitted)
        {
            bool shouldBeActive = sinkActive && LanguageUsesIme();
            if (shouldBeActive && !active) Activate();
            else if (!shouldBeActive && active) Deactivate();

            if (!active) return;

            // Layout and repaint passes only: TryRouteKeyDown draws the KeyDowns, and an event
            // another prefix already consumed arrives here as Used, which would otherwise draw the
            // field a second time in one pass.
            if (Event.current.type == EventType.Layout || Event.current.type == EventType.Repaint)
                DrawAndHarvest(onCommitted);
        }

        /// <summary>
        /// Whether this KeyDown belongs to the IME; when it does, the field is drawn so Unity can
        /// process it and any committed characters are harvested, and the caller should
        /// <c>Event.current.Use()</c>. False leaves the key on its normal path.
        /// </summary>
        public static bool TryRouteKeyDown(Event evt, Action<char> onCommitted)
        {
            if (!active || evt.type != EventType.KeyDown) return false;

            bool routeToField;
            if (composingLastDraw)
            {
                // Mid-composition: candidate navigation, commit and pinyin editing all belong to
                // the IME.
                routeToField = true;
            }
            else
            {
                // Not composing: only a letter starts composition, arriving either as a letter
                // KeyCode or as the keyCode==None character twin Unity fires for printable input. A
                // held Ctrl or Alt makes it a shortcut, never composition text.
                bool modified = KeyboardHelper.IsAltHeld || KeyboardHelper.IsCtrlHeld;
                bool isLetterKeyCode = evt.keyCode >= KeyCode.A && evt.keyCode <= KeyCode.Z;
                bool isLetterChar = evt.keyCode == KeyCode.None && evt.character != '\0'
                                    && char.IsLetter(evt.character);
                routeToField = !modified && (isLetterKeyCode || isLetterChar);
            }

            if (!routeToField) return false;

            DrawAndHarvest(onCommitted);
            return true;
        }

        /// <summary>
        /// Draws the offscreen field, forces focus to it, and feeds newly-committed characters to
        /// <paramref name="onCommitted"/>, updating <see cref="IsComposing"/> from the live
        /// composition string. Must run inside an OnGUI context.
        /// </summary>
        public static void DrawAndHarvest(Action<char> onCommitted)
        {
            if (!active) return;

            // Some platforms misbehave when the composition cursor is off-screen.
            Input.compositionCursorPos = new Vector2(100f, 100f);

            GUI.SetNextControlName(FieldName);
            string newValue = GUI.TextField(OffscreenRect, fieldValue);

            // Keep the hidden field focused so the OS IME always has a target to compose into.
            if (GUI.GetNameOfFocusedControl() != FieldName)
                GUI.FocusControl(FieldName);

            // Committed text arrives appended, the cursor always being at the end, so the appended
            // slice is exactly what the IME committed this pass.
            if (newValue.Length > fieldValue.Length)
            {
                string committed = newValue.Substring(fieldValue.Length);
                for (int i = 0; i < committed.Length; i++)
                {
                    char c = committed[i];
                    if (!char.IsControl(c))
                        onCommitted?.Invoke(c);
                }
            }

            composingLastDraw = !string.IsNullOrEmpty(Input.compositionString);
            // Reset between commits so the next reads as a fresh append; kept while composing, so
            // the in-progress composition is not disturbed.
            fieldValue = composingLastDraw ? newValue : string.Empty;
        }

        private static void Activate()
        {
            active = true;
            fieldValue = string.Empty;
            composingLastDraw = false;
            savedMode = Input.imeCompositionMode;
            hasSavedMode = true;
            Input.imeCompositionMode = IMECompositionMode.On;
        }

        private static void Deactivate()
        {
            active = false;
            fieldValue = string.Empty;
            composingLastDraw = false;
            Input.imeCompositionMode = hasSavedMode ? savedMode : IMECompositionMode.Auto;
            hasSavedMode = false;
            if (GUI.GetNameOfFocusedControl() == FieldName)
                GUI.FocusControl(null);
        }

        /// <summary>
        /// Whether the active language is composition-based, where direct <c>Event.character</c>
        /// capture cannot work. Matched on both the current and legacy folder names, so it holds
        /// whichever form the folder uses.
        /// </summary>
        internal static bool LanguageUsesIme()
        {
            LoadedLanguage lang = LanguageDatabase.activeLanguage;
            if (lang == null) return false;
            string name = ((lang.folderName ?? string.Empty) + "|" + (lang.LegacyFolderName ?? string.Empty))
                .ToLowerInvariant();
            return name.Contains("chinese") || name.Contains("japanese") || name.Contains("korean");
        }
    }
}
