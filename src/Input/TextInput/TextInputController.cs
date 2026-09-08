using System;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// One editing session: owns the working text buffer, the field's spec, and
    /// confirm/cancel callbacks, routing the events the shell dispatcher's text funnel hands it.
    /// Modal — only one controller is active at a time via <see cref="TextInputManager"/>.
    /// Carries a text-review cursor (char/word moves, Home/End, Shift selection, Delete,
    /// Up/Down re-read) on top of the per-character typing announcements.
    /// A <see cref="TextFieldSpec.ReadOnlyText"/> spec turns the same session into a read-only
    /// browse of text the game owns: the caret starts at the top, every key that would change
    /// the buffer refuses with one voice, and navigation, selection and copy are unchanged.
    /// </summary>
    public sealed class TextInputController
    {
        private string currentText = string.Empty;
        private string initialText = string.Empty;
        private bool replaceOnFirstKeystroke;
        private int cursorPos;
        private int selectionAnchor;
        private bool modal;
        private bool announceOnCommit;
        private string displayLabel;

        public TextFieldSpec Spec { get; private set; }
        public string CurrentText => currentText;
        public bool IsEmpty => string.IsNullOrEmpty(currentText);
        public int CursorPos => cursorPos;
        public bool HasSelection => cursorPos != selectionAnchor;

        private Action<string> onConfirm;
        private Action onCancel;
        private Action<bool> onTabExit;

        /// <summary>
        /// Begin an editing session. With <paramref name="modal"/> the controller registers as
        /// <see cref="TextInputManager.Active"/> and the dispatcher routes ALL keys to it;
        /// otherwise the embedding caller must route keys to the handlers itself.
        /// <paramref name="replaceOnType"/> makes the first character or paste replace non-empty
        /// initial text. <paramref name="displayLabel"/> overrides the spec's label key in the
        /// editing prompt and commit announcement.
        /// <paramref name="onTabExit"/>, when supplied, makes Tab/Shift+Tab leave the value in
        /// place (like Escape) and hand control to the callback instead of confirming; Tab is
        /// inert without it.
        /// </summary>
        public void Begin(
            string initialText,
            TextFieldSpec spec,
            Action<string> onConfirm,
            Action onCancel = null,
            bool replaceOnType = true,
            bool modal = true,
            bool announceOnCommit = true,
            string displayLabel = null,
            bool announceBegin = true,
            Action<bool> onTabExit = null)
        {
            currentText = initialText ?? string.Empty;
            this.initialText = currentText;
            Spec = spec;
            replaceOnFirstKeystroke = replaceOnType && !ReadOnly && !string.IsNullOrEmpty(currentText);
            // A reader starts at the top of the text; an editor starts where typing continues.
            cursorPos = ReadOnly ? 0 : currentText.Length;
            selectionAnchor = cursorPos;
            this.onConfirm = onConfirm;
            this.onCancel = onCancel;
            this.onTabExit = onTabExit;
            this.modal = modal;
            this.announceOnCommit = announceOnCommit;
            this.displayLabel = displayLabel;
            if (modal) TextInputManager.SetActive(this);

            // A scope composing its own field announcement passes announceBegin: false so the
            // session opening has exactly one voice.
            if (!announceBegin)
            {
                return;
            }
            string label = ResolveLabel();
            if (ReadOnly)
            {
                // Only the line under the caret: the rest of the buffer is one arrow key away.
                string firstLine = string.IsNullOrEmpty(currentText)
                    ? "RimWorldAccess.TextInput.Empty".Translate().ToString()
                    : TextCursor.LineAt(currentText, cursorPos);
                TolkHelper.Speak("RimWorldAccess.TextInput.BrowsingField".Loc(label, firstLine), SpeechPriority.High);
                return;
            }
            string preview = string.IsNullOrEmpty(currentText)
                ? "RimWorldAccess.TextInput.Empty".Translate().ToString()
                : currentText;
            string announceKey = spec != null && spec.MultiLine
                ? "RimWorldAccess.TextInput.EditingMultiLineField"
                : "RimWorldAccess.TextInput.EditingField";
            TolkHelper.Speak(announceKey.Loc(label, preview), SpeechPriority.High);
        }

        /// <summary>True while this session browses text the game owns (see the class remarks).</summary>
        private bool ReadOnly => Spec != null && Spec.ReadOnly;

        /// <summary>One voice for every refused edit key, so a read-only field answers a keystroke
        /// rather than swallowing it. True when the caller must do nothing further.</summary>
        private bool RefuseWhenReadOnly()
        {
            if (!ReadOnly) return false;
            TolkHelper.Speak("RimWorldAccess.TextInput.ReadOnlyField".Loc(), SpeechPriority.High);
            return true;
        }

        public void HandleCharacter(char c)
        {
            if (RefuseWhenReadOnly()) return;
            if (c == '\n')
            {
                if (Spec == null || !Spec.MultiLine) return;
            }
            else if (char.IsControl(c)) return;

            if (replaceOnFirstKeystroke)
            {
                currentText = string.Empty;
                cursorPos = 0;
                selectionAnchor = 0;
                replaceOnFirstKeystroke = false;
            }
            if (HasSelection) DeleteSelectionInternal();
            currentText = currentText.Insert(cursorPos, c.ToString());
            cursorPos++;
            selectionAnchor = cursorPos;
            if (c == '\n')
                TolkHelper.Speak("RimWorldAccess.TextInput.NewLine".Loc(), SpeechPriority.High);
            else
                TolkHelper.SpeakData(c.ToString(), SpeechPriority.High);
        }

        public void HandleBackspace()
        {
            if (RefuseWhenReadOnly()) return;
            replaceOnFirstKeystroke = false;
            if (HasSelection)
            {
                string removed = GetSelectedText();
                DeleteSelectionInternal();
                TolkHelper.Speak("RimWorldAccess.TextInput.Deleted".Loc(removed), SpeechPriority.High);
                return;
            }
            if (cursorPos == 0) return;
            char c = currentText[cursorPos - 1];
            currentText = currentText.Remove(cursorPos - 1, 1);
            cursorPos--;
            selectionAnchor = cursorPos;
            TolkHelper.Speak("RimWorldAccess.TextInput.Deleted".Loc(c), SpeechPriority.High);
        }

        public void HandleDelete()
        {
            if (RefuseWhenReadOnly()) return;
            replaceOnFirstKeystroke = false;
            if (HasSelection)
            {
                string removed = GetSelectedText();
                DeleteSelectionInternal();
                TolkHelper.Speak("RimWorldAccess.TextInput.Deleted".Loc(removed), SpeechPriority.High);
                return;
            }
            if (cursorPos >= currentText.Length) return;
            char c = currentText[cursorPos];
            currentText = currentText.Remove(cursorPos, 1);
            selectionAnchor = cursorPos;
            TolkHelper.Speak("RimWorldAccess.TextInput.Deleted".Loc(c), SpeechPriority.High);
        }

        public void HandleArrowLeft(bool shift, bool ctrl)
        {
            replaceOnFirstKeystroke = false;
            int oldCursor = cursorPos;
            int newCursor;
            if (ctrl)
                newCursor = FindPreviousWordBoundary(cursorPos);
            else if (HasSelection && !shift)
                newCursor = Math.Min(cursorPos, selectionAnchor);
            else
                newCursor = Math.Max(0, cursorPos - 1);

            cursorPos = newCursor;
            if (!shift) selectionAnchor = cursorPos;
            AnnounceCursorMove(oldCursor, cursorPos, shift, ctrl, leftward: true);
        }

        public void HandleArrowRight(bool shift, bool ctrl)
        {
            replaceOnFirstKeystroke = false;
            int oldCursor = cursorPos;
            int newCursor;
            if (ctrl)
                newCursor = FindNextWordBoundary(cursorPos);
            else if (HasSelection && !shift)
                newCursor = Math.Max(cursorPos, selectionAnchor);
            else
                newCursor = Math.Min(currentText.Length, cursorPos + 1);

            cursorPos = newCursor;
            if (!shift) selectionAnchor = cursorPos;
            AnnounceCursorMove(oldCursor, cursorPos, shift, ctrl, leftward: false);
        }

        /// <summary>Jumps to the start of the field, or to the start of the current line in
        /// multi-line mode without Ctrl.</summary>
        public void HandleHome(bool shift, bool ctrl = false)
        {
            replaceOnFirstKeystroke = false;
            int oldCursor = cursorPos;
            if (Spec != null && Spec.MultiLine && !ctrl)
                cursorPos = FindStartOfCurrentLine(cursorPos);
            else
                cursorPos = 0;
            if (!shift) selectionAnchor = cursorPos;
            AnnounceCursorMove(oldCursor, cursorPos, shift, ctrl: false, leftward: true);
        }

        /// <summary>Jumps to the end of the field, or to the end of the current line in
        /// multi-line mode without Ctrl.</summary>
        public void HandleEnd(bool shift, bool ctrl = false)
        {
            replaceOnFirstKeystroke = false;
            int oldCursor = cursorPos;
            if (Spec != null && Spec.MultiLine && !ctrl)
                cursorPos = FindEndOfCurrentLine(cursorPos);
            else
                cursorPos = currentText.Length;
            if (!shift) selectionAnchor = cursorPos;
            AnnounceCursorMove(oldCursor, cursorPos, shift, ctrl: false, leftward: false);
        }

        /// <summary>Multi-line only: moves up one line, preserving column; on the first line it
        /// stays at the line start.</summary>
        public void HandleArrowUp(bool shift)
        {
            if (Spec == null || !Spec.MultiLine)
            {
                ReadCurrentText();
                return;
            }
            replaceOnFirstKeystroke = false;
            cursorPos = TextCursor.LineUp(currentText, cursorPos);
            if (!shift) selectionAnchor = cursorPos;
            AnnounceLine(cursorPos);
        }

        /// <summary>Multi-line only: moves down one line, preserving column; on the last line it
        /// snaps to the end of the field.</summary>
        public void HandleArrowDown(bool shift)
        {
            if (Spec == null || !Spec.MultiLine)
            {
                ReadCurrentText();
                return;
            }
            replaceOnFirstKeystroke = false;
            cursorPos = TextCursor.LineDown(currentText, cursorPos);
            if (!shift) selectionAnchor = cursorPos;
            AnnounceLine(cursorPos);
        }

        public void HandleCopy()
        {
            string toCopy = HasSelection ? GetSelectedText() : (currentText ?? string.Empty);
            GUIUtility.systemCopyBuffer = toCopy;
            TolkHelper.Speak("RimWorldAccess.TextInput.Copied".Loc(), SpeechPriority.High);
        }

        /// <summary>Cuts the selection, or the entire field when there is none.</summary>
        public void HandleCut()
        {
            if (RefuseWhenReadOnly()) return;
            replaceOnFirstKeystroke = false;
            if (HasSelection)
            {
                GUIUtility.systemCopyBuffer = GetSelectedText();
                DeleteSelectionInternal();
                TolkHelper.Speak("RimWorldAccess.TextInput.Cut".Loc(), SpeechPriority.High);
                return;
            }
            if (currentText.Length == 0) return;
            GUIUtility.systemCopyBuffer = currentText;
            currentText = string.Empty;
            cursorPos = 0;
            selectionAnchor = 0;
            TolkHelper.Speak("RimWorldAccess.TextInput.Cut".Loc(), SpeechPriority.High);
        }

        /// <summary>Deletes the word left of the cursor, or the selection when there is one.</summary>
        public void HandleBackspaceWord()
        {
            if (RefuseWhenReadOnly()) return;
            replaceOnFirstKeystroke = false;
            if (HasSelection)
            {
                HandleBackspace();
                return;
            }
            if (cursorPos == 0) return;
            int prev = FindPreviousWordBoundary(cursorPos);
            if (prev >= cursorPos) return;
            string removed = currentText.Substring(prev, cursorPos - prev);
            currentText = currentText.Remove(prev, cursorPos - prev);
            cursorPos = prev;
            selectionAnchor = cursorPos;
            TolkHelper.Speak("RimWorldAccess.TextInput.Deleted".Loc(removed), SpeechPriority.High);
        }

        /// <summary>Deletes the word right of the cursor, or the selection when there is one.</summary>
        public void HandleDeleteWord()
        {
            if (RefuseWhenReadOnly()) return;
            replaceOnFirstKeystroke = false;
            if (HasSelection)
            {
                HandleDelete();
                return;
            }
            if (cursorPos >= currentText.Length) return;
            int next = FindNextWordBoundary(cursorPos);
            if (next <= cursorPos) return;
            string removed = currentText.Substring(cursorPos, next - cursorPos);
            currentText = currentText.Remove(cursorPos, next - cursorPos);
            selectionAnchor = cursorPos;
            TolkHelper.Speak("RimWorldAccess.TextInput.Deleted".Loc(removed), SpeechPriority.High);
        }

        public void HandleSelectAll()
        {
            replaceOnFirstKeystroke = false;
            if (currentText.Length == 0)
            {
                TolkHelper.Speak("RimWorldAccess.TextInput.Empty".Loc(), SpeechPriority.High);
                return;
            }
            selectionAnchor = 0;
            cursorPos = currentText.Length;
            // A large buffer announces its char count instead; Up/Down re-reads it in full.
            const int LongTextThreshold = 200;
            if (currentText.Length > LongTextThreshold)
                TolkHelper.Speak("RimWorldAccess.TextInput.SelectedAllLong".Loc(currentText.Length), SpeechPriority.High);
            else
                TolkHelper.Speak("RimWorldAccess.TextInput.SelectedAll".Loc(currentText), SpeechPriority.High);
        }

        public void HandlePaste()
        {
            if (RefuseWhenReadOnly()) return;
            string clipboard = GUIUtility.systemCopyBuffer;
            if (string.IsNullOrEmpty(clipboard))
            {
                TolkHelper.Speak("RimWorldAccess.TextInput.ClipboardEmpty".Loc(), SpeechPriority.High);
                return;
            }

            string candidate;
            int insertionEnd;
            if (replaceOnFirstKeystroke)
            {
                candidate = clipboard;
                insertionEnd = clipboard.Length;
            }
            else if (HasSelection)
            {
                int lo = Math.Min(cursorPos, selectionAnchor);
                int hi = Math.Max(cursorPos, selectionAnchor);
                candidate = currentText.Substring(0, lo) + clipboard + currentText.Substring(hi);
                insertionEnd = lo + clipboard.Length;
            }
            else
            {
                candidate = currentText.Substring(0, cursorPos) + clipboard + currentText.Substring(cursorPos);
                insertionEnd = cursorPos + clipboard.Length;
            }

            var result = TextFieldValidator.Validate(candidate, Spec);
            if (!result.IsOk)
            {
                TolkHelper.SpeakData(TextFieldValidator.AnnounceRejection(result, Spec), SpeechPriority.High);
                return;
            }

            currentText = candidate;
            cursorPos = insertionEnd;
            selectionAnchor = cursorPos;
            replaceOnFirstKeystroke = false;
            TolkHelper.SpeakData(SummarizePaste(clipboard), SpeechPriority.High);
        }

        public void HandleEnter()
        {
            if (ReadOnly)
            {
                // Nothing to validate or write back: Enter just leaves the text.
                var exit = onConfirm;
                string browsed = currentText;
                Close();
                exit?.Invoke(browsed);
                return;
            }
            var result = TextFieldValidator.Validate(currentText, Spec);
            if (!result.IsOk)
            {
                TolkHelper.SpeakData(TextFieldValidator.AnnounceRejection(result, Spec), SpeechPriority.High);
                return;
            }
            var cb = onConfirm;
            string text = currentText;

            // Capture the dismissal announcement's inputs before Close() clears Spec.
            bool announce = announceOnCommit;
            bool changed = text != initialText;
            string label = ResolveLabel();

            Close();
            cb?.Invoke(text);

            // Last, so it interrupts any re-announcement the confirm handler made.
            if (announce)
                AnnounceCommit(label, text, changed);
        }

        /// <summary>The label for the editing prompt and commit announcement: the display label
        /// passed to <see cref="Begin"/>, else the spec's translated label key.</summary>
        private string ResolveLabel()
        {
            if (!string.IsNullOrEmpty(displayLabel))
                return displayLabel;
            return Spec?.LabelKey != null ? Spec.LabelKey.Translate().ToString() : string.Empty;
        }

        /// <summary>Speaks the field's value on dismissal, set-to or unchanged. Never truncated.</summary>
        private void AnnounceCommit(string label, string text, bool changed)
        {
            string value = string.IsNullOrEmpty(text)
                ? "RimWorldAccess.TextInput.Empty".Translate().ToString()
                : text;
            if (!changed)
            {
                // Fall back to the value so a missing label never speaks a bare " unchanged".
                string subject = string.IsNullOrEmpty(label) ? value : label;
                TolkHelper.Speak("RimWorldAccess.TextInput.FieldUnchanged".Loc(subject), SpeechPriority.High);
                return;
            }
            if (string.IsNullOrEmpty(label))
                TolkHelper.SpeakData(value, SpeechPriority.High);
            else
                TolkHelper.Speak("RimWorldAccess.TextInput.FieldSet".Loc(label, value), SpeechPriority.High);
        }

        public void HandleEscape()
        {
            var cb = onCancel;
            Close();
            cb?.Invoke();
        }

        public void Cancel() => HandleEscape();

        public void ReadCurrentText()
        {
            if (string.IsNullOrEmpty(currentText))
                TolkHelper.Speak("RimWorldAccess.TextInput.Empty".Loc());
            else
                TolkHelper.SpeakData(currentText);
        }

        /// <summary>Cursor-review subset of <see cref="HandleEvent"/>: Left/Right/Home/End/Delete
        /// only. Embedded sites whose list still owns Up/Down and Enter/Escape call this before
        /// their own arrow-nav branch.</summary>
        public bool HandleCursorNavEvent(Event evt)
        {
            if (evt.type != EventType.KeyDown) return false;
            bool shift = evt.shift;
            bool ctrl = KeyboardHelper.IsCtrlHeld;
            switch (evt.keyCode)
            {
                case KeyCode.LeftArrow: HandleArrowLeft(shift, ctrl); return true;
                case KeyCode.RightArrow: HandleArrowRight(shift, ctrl); return true;
                case KeyCode.Home: HandleHome(shift, ctrl); return true;
                case KeyCode.End: HandleEnd(shift, ctrl); return true;
                case KeyCode.Delete:
                    if (ctrl) HandleDeleteWord();
                    else HandleDelete();
                    return true;
            }
            return false;
        }

        /// <summary>Dispatches an IMGUI key event. True when consumed, meaning the caller should
        /// call <c>Event.current.Use()</c>.</summary>
        public bool HandleEvent(Event evt)
        {
            if (evt.type != EventType.KeyDown) return false;

            bool shift = evt.shift;
            bool ctrl = KeyboardHelper.IsCtrlHeld;

            if (ctrl && !shift)
            {
                if (evt.keyCode == KeyCode.C) { HandleCopy(); return true; }
                if (evt.keyCode == KeyCode.V) { HandlePaste(); return true; }
                if (evt.keyCode == KeyCode.X) { HandleCut(); return true; }
                if (evt.keyCode == KeyCode.A) { HandleSelectAll(); return true; }
            }

            switch (evt.keyCode)
            {
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    if (shift && Spec != null && Spec.MultiLine)
                    {
                        HandleCharacter('\n');
                        return true;
                    }
                    HandleEnter();
                    return true;
                case KeyCode.Escape:
                    HandleEscape();
                    return true;
                case KeyCode.Backspace:
                    if (ctrl) HandleBackspaceWord();
                    else HandleBackspace();
                    return true;
                case KeyCode.Delete:
                    if (ctrl) HandleDeleteWord();
                    else HandleDelete();
                    return true;
                case KeyCode.LeftArrow:
                    HandleArrowLeft(shift, ctrl);
                    return true;
                case KeyCode.RightArrow:
                    HandleArrowRight(shift, ctrl);
                    return true;
                case KeyCode.Home:
                    HandleHome(shift, ctrl);
                    return true;
                case KeyCode.End:
                    HandleEnd(shift, ctrl);
                    return true;
                case KeyCode.UpArrow:
                    // Single-line, or Ctrl held: re-read the whole field instead of moving.
                    if (Spec != null && Spec.MultiLine && !ctrl) HandleArrowUp(shift);
                    else ReadCurrentText();
                    return true;
                case KeyCode.DownArrow:
                    if (Spec != null && Spec.MultiLine && !ctrl) HandleArrowDown(shift);
                    else ReadCurrentText();
                    return true;
                case KeyCode.Tab:
                    // Opt-in only: without a callback Tab falls through unclaimed.
                    if (onTabExit == null) return false;
                    Action<bool> tabExit = onTabExit;
                    Close();
                    tabExit(shift);
                    return true;
            }

            // Layout-aware character; control chars are already handled by their key cases.
            if (evt.keyCode == KeyCode.None && evt.character != '\0' && !char.IsControl(evt.character))
            {
                HandleCharacter(evt.character);
                return true;
            }

            return false;
        }

        private void Close()
        {
            currentText = string.Empty;
            cursorPos = 0;
            selectionAnchor = 0;
            replaceOnFirstKeystroke = false;
            Spec = null;
            displayLabel = null;
            onConfirm = null;
            onCancel = null;
            onTabExit = null;
            if (TextInputManager.Active == this)
                TextInputManager.Clear();
        }

        private string GetSelectedText()
        {
            int lo = Math.Min(cursorPos, selectionAnchor);
            int hi = Math.Max(cursorPos, selectionAnchor);
            return currentText.Substring(lo, hi - lo);
        }

        private void DeleteSelectionInternal()
        {
            int lo = Math.Min(cursorPos, selectionAnchor);
            int hi = Math.Max(cursorPos, selectionAnchor);
            currentText = currentText.Remove(lo, hi - lo);
            cursorPos = lo;
            selectionAnchor = lo;
        }

        private int FindStartOfCurrentLine(int pos)
        {
            return TextCursor.StartOfLine(currentText, pos);
        }

        private int FindEndOfCurrentLine(int pos)
        {
            return TextCursor.EndOfLine(currentText, pos);
        }

        /// <summary>Speaks the line any position on it identifies.</summary>
        private void AnnounceLine(int posOnLine)
        {
            string line = TextCursor.LineAt(currentText, posOnLine);
            if (line.Length == 0)
            {
                TolkHelper.Speak("RimWorldAccess.TextInput.BlankLine".Loc(), SpeechPriority.High);
                return;
            }
            TolkHelper.SpeakData(line, SpeechPriority.High);
        }

        private string GetWordAt(int pos)
        {
            return TextCursor.WordAt(currentText, pos);
        }

        private int FindNextWordBoundary(int from)
        {
            return TextCursor.NextWordBoundary(currentText, from);
        }

        private int FindPreviousWordBoundary(int from)
        {
            return TextCursor.PreviousWordBoundary(currentText, from);
        }

        private void AnnounceCursorMove(int oldCursor, int newCursor, bool shift, bool ctrl, bool leftward)
        {
            if (currentText.Length == 0) return; // nothing to announce on an empty field

            // Shift-selection: speak the range just added or removed; at a boundary, the edge char.
            if (shift)
            {
                int lo = Math.Min(oldCursor, newCursor);
                int hi = Math.Max(oldCursor, newCursor);
                if (lo < hi)
                {
                    TolkHelper.SpeakData(currentText.Substring(lo, hi - lo), SpeechPriority.High);
                    return;
                }
                int edge = leftward ? 0 : currentText.Length - 1;
                TolkHelper.SpeakData(currentText[edge].ToString(), SpeechPriority.High);
                return;
            }

            // Word jump: the word at the cursor, else the edge word, else the edge char.
            if (ctrl)
            {
                string wordAtCursor = GetWordAt(newCursor);
                if (!string.IsNullOrEmpty(wordAtCursor))
                {
                    TolkHelper.SpeakData(wordAtCursor, SpeechPriority.High);
                    return;
                }
                string edgeWord = leftward ? GetFirstWord() : GetLastWord();
                if (!string.IsNullOrEmpty(edgeWord))
                {
                    TolkHelper.SpeakData(edgeWord, SpeechPriority.High);
                    return;
                }
                int edge = leftward ? 0 : currentText.Length - 1;
                TolkHelper.SpeakData(currentText[edge].ToString(), SpeechPriority.High);
                return;
            }

            // The char at the cursor, or the last char when the cursor sits past the end.
            int idx = newCursor < currentText.Length ? newCursor : currentText.Length - 1;
            TolkHelper.SpeakData(currentText[idx].ToString(), SpeechPriority.High);
        }

        private string GetFirstWord()
        {
            return TextCursor.FirstWord(currentText);
        }

        private string GetLastWord()
        {
            return TextCursor.LastWord(currentText);
        }

        private static string SummarizePaste(string clipboard)
        {
            int lineCount = 1;
            int wordCount = 0;
            bool inWord = false;
            for (int i = 0; i < clipboard.Length; i++)
            {
                char c = clipboard[i];
                if (c == '\n') lineCount++;
                bool isWs = char.IsWhiteSpace(c);
                if (!isWs && !inWord) { wordCount++; inWord = true; }
                else if (isWs) inWord = false;
            }

            if (lineCount > 1)
                return "RimWorldAccess.TextInput.PastedLines".Translate(lineCount, wordCount);
            if (wordCount > 5)
                return "RimWorldAccess.TextInput.PastedWords".Translate(wordCount);
            return "RimWorldAccess.TextInput.PastedShort".Translate(clipboard);
        }
    }
}
