using System;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The browse/edit model behind every editable text field in the mod. A scope owns one and
    /// re-points it at whichever field the cursor sits on.
    /// BROWSE (session inactive): the scope navigates its own menu and runs its own typeahead; a
    /// text row just announces its value, so it can be arrowed past without being entered. Enter
    /// — or, for scopes that opt in, the first typed character — enters EDIT.
    /// EDIT: <see cref="EnterEdit"/> begins a MODAL <see cref="TextInputController"/> session and
    /// the dispatcher routes every key to it, so the owning scope needs zero edit-key claims.
    /// Every spoken edit string comes from <c>RimWorldAccess.TextInput.*</c> via the controller;
    /// nothing here or in a scope hard-codes edit vocabulary.
    /// Enter and Escape both keep the typed value — <see cref="MirrorLive"/> has been writing it
    /// into the backing store all along, so there is no discard — and fire <c>onExit</c> so the
    /// scope re-announces the row in browse form, keeping one voice.
    /// <see cref="EnterEdit"/> stamps <see cref="ShellFrameStamps.MarkAcceptConsumed"/> so the
    /// Enter that opened the session cannot also reach a vanilla dialog's raw in-draw Return
    /// poll; see <see cref="TextFieldRawPollGuard"/> for the in-draw swallow those scopes install.
    /// </summary>
    public sealed class TextFieldEditSession
    {
        private readonly TextInputController controller = new TextInputController();
        private Action<string> apply;
        private Action onExit;
        private Action onConfirm;
        private Action<bool> onTabExit;
        private bool silentConfirmExit;
        private bool editing;

        /// <summary>True while a modal edit session is live for this field.</summary>
        public bool Editing => editing;

        /// <summary>The live buffer (valid only while <see cref="Editing"/>).</summary>
        public string CurrentText => controller.CurrentText;

        /// <summary>Enters edit mode for one field.</summary>
        /// <param name="spec">Length/character/multi-line rules; drives validation and Up/Down.</param>
        /// <param name="displayLabel">The field's caption, spoken in the prompt and on rejection.</param>
        /// <param name="apply">Writes the buffer back, live and on confirm.</param>
        /// <param name="onExit">Fired when the session ends, so the scope re-announces the row.</param>
        /// <param name="announcePrompt">False stays silent, so a triggering character is the only voice.</param>
        /// <param name="onConfirm">
        /// Extra commit action fired exactly once on an Enter-confirm, never on Escape or the live
        /// mirror; for fields whose Enter carries a semantic commit, such as a file write. It runs
        /// after this session's state is torn down, so it may close the window or pop the scope.
        /// </param>
        /// <param name="onTabExit">
        /// Null leaves Tab inert during editing. Supplying it lets Tab/Shift+Tab (the bool is
        /// shift-held) leave the field: the session tears down keeping the live value, never runs
        /// <paramref name="onConfirm"/>, and skips <paramref name="onExit"/> because the caller's
        /// own callback is about to speak.
        /// </param>
        /// <param name="silentConfirmExit">
        /// True for a send-and-stay field: the confirm exit skips <paramref name="onExit"/>, since
        /// <paramref name="onConfirm"/> re-enters a fresh session and only the send should be
        /// heard. Escape exits are unaffected.
        /// </param>
        public void EnterEdit(
            string currentValue,
            TextFieldSpec spec,
            string displayLabel,
            Action<string> apply,
            Action onExit,
            bool announcePrompt = true,
            Action onConfirm = null,
            Action<bool> onTabExit = null,
            bool silentConfirmExit = false)
        {
            this.apply = apply;
            this.onExit = onExit;
            this.onConfirm = onConfirm;
            this.onTabExit = onTabExit;
            this.silentConfirmExit = silentConfirmExit;
            editing = true;

            // Stamp first: on a real vanilla dialog the opening Enter must not also reach the
            // window's own raw Return poll this frame.
            ShellFrameStamps.MarkAcceptConsumed();

            controller.Begin(
                initialText: currentValue ?? string.Empty,
                spec: spec,
                onConfirm: OnControllerConfirm,
                onCancel: OnControllerCancel,
                replaceOnType: true,
                modal: true,
                announceOnCommit: false,
                displayLabel: displayLabel,
                announceBegin: announcePrompt,
                onTabExit: onTabExit != null ? (Action<bool>)OnControllerTabExit : null);
        }

        /// <summary>
        /// Feeds the character that triggered a browse-type-to-edit transition. Only this first
        /// one needs handing over; later characters reach the live session through the dispatcher.
        /// </summary>
        public void FeedChar(char c)
        {
            if (editing)
            {
                controller.HandleCharacter(c);
                apply?.Invoke(controller.CurrentText ?? string.Empty);
            }
        }

        /// <summary>
        /// Keeps the backing store equal to the live buffer. Call once per GUI pass while the
        /// scope draws, so a vanilla field renders what the player is typing and Escape, which
        /// keeps the value, leaves the store already current.
        /// </summary>
        public void MirrorLive()
        {
            if (editing)
            {
                apply?.Invoke(controller.CurrentText ?? string.Empty);
            }
        }

        /// <summary>End any live session without announcing (scope pop / teardown).</summary>
        public void CancelIfActive()
        {
            if (!editing)
            {
                return;
            }
            editing = false;
            apply = null;
            onExit = null;
            onConfirm = null;
            onTabExit = null;
            controller.Cancel();
        }

        private void OnControllerConfirm(string text)
        {
            // The controller has already validated the text and closed its own session, so none
            // is live here. Apply, tear this state down, and run the commit action LAST: a commit
            // that closes the window re-enters CancelIfActive, which then finds nothing to do.
            Action<string> applyRef = apply;
            Action confirmRef = onConfirm;
            bool silent = silentConfirmExit;
            applyRef?.Invoke(text);
            if (silent)
            {
                // Send-and-stay: tear down without the onExit re-announcement.
                editing = false;
                apply = null;
                onExit = null;
                onConfirm = null;
                onTabExit = null;
            }
            else
            {
                Finish();
            }
            confirmRef?.Invoke();
        }

        private void OnControllerCancel()
        {
            // Escape keeps the live-mirrored value; the commit action never fires on cancel.
            Finish();
        }

        /// <summary>
        /// Tab/Shift+Tab exit: keeps the live value like Escape, runs neither onExit nor
        /// onConfirm, and fires the callback only after this session's state is cleared, so a
        /// callback that opens a session of its own cannot collide with a live one.
        /// </summary>
        private void OnControllerTabExit(bool shiftHeld)
        {
            Action<bool> tabExitRef = onTabExit;
            editing = false;
            apply = null;
            onExit = null;
            onConfirm = null;
            onTabExit = null;
            tabExitRef?.Invoke(shiftHeld);
        }

        private void Finish()
        {
            editing = false;
            Action exit = onExit;
            apply = null;
            onExit = null;
            onConfirm = null;
            exit?.Invoke();
        }
    }
}
