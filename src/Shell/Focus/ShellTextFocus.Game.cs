using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Central authority for Unity keyboard focus over the shell's text-input
    /// surfaces. The shell — not vanilla — owns text entry on every screen it
    /// drives: typed characters arrive through a scope's <see cref="ICharSink"/>
    /// (and, for CJK, the <see cref="ImeInputHost"/> composition funnel), never
    /// through a natively focused vanilla <c>Widgets.TextField</c>.
    ///
    /// This matters because a native IMGUI control holding Unity keyboard focus
    /// swallows arrow keys, Tab, Home/End and the like for its own caret and
    /// focus traversal BEFORE the shell dispatcher (a UIRootOnGUI prefix) ever
    /// sees them — verified live during the NamePawn QA. Any scope that lets a
    /// vanilla field keep focus therefore goes silently dead to navigation. The
    /// historical symptom was screen-specific (rename fields, the save/load
    /// search box): each scope hand-rolled its own suppression and some missed a
    /// field. This helper is the one shared mechanism they all route through.
    ///
    /// Two responsibilities:
    ///  - <see cref="ReleaseNativeFocus"/>: called every GUI pass from a
    ///    text-owning scope's draw pass, it drops focus from whatever vanilla
    ///    control currently holds it, so the next keystroke reaches the shell.
    ///    It stands down while the IME funnel is armed (an active CJK session),
    ///    because the funnel deliberately keeps its own hidden field focused to
    ///    preserve composition between keystrokes — fighting it there would
    ///    break pinyin/kana entry.
    ///
    /// Per-dialog one-shot auto-focus flags (Dialog_FileList.focusedSearch /
    /// focusedNameArea, Dialog_Rename.focusedRenameField, ...) are still set by
    /// each scope directly through its own reflected FieldRef in
    /// <c>OnPush</c> — those field names differ per dialog, so pre-empting the
    /// grab stays with the scope that knows the field. This helper covers the
    /// steady-state guarantee that no vanilla control clings to focus after it.
    /// </summary>
    internal static class ShellTextFocus
    {
        /// <summary>
        /// Reasserts shell ownership of Unity keyboard focus for the current
        /// GUI pass. No-op while the CJK composition funnel is active (it owns a
        /// hidden field on purpose); otherwise unfocuses any vanilla control so
        /// arrow/Tab/navigation keys flow to the dispatcher next event.
        /// </summary>
        internal static void ReleaseNativeFocus()
        {
            if (ImeInputHost.IsActive)
            {
                return;
            }
            UI.UnfocusCurrentControl();
        }

        private static string lastReleasedControl;

        /// <summary>
        /// Dispatcher-level twin of <see cref="ReleaseNativeFocus"/>: while the shell
        /// owns the keyboard (MenuOwnsInput) and no modal text session is live, no
        /// native IMGUI control may hold Unity keyboard focus - a mod that focuses a
        /// text field inside a scope-driven window (e.g. Sensible Bed Ownership's
        /// search box on Dialog_AssignBuildingOwner) would otherwise eat arrows and
        /// characters in the window pass before the dispatcher runs. Runs every
        /// UIRootOnGUI prefix; the keyboardControl==0 fast path keeps it free.
        /// </summary>
        internal static void ReleaseForeignFocus()
        {
            if (GUIUtility.keyboardControl == 0
                || ImeInputHost.IsActive
                || TextInputManager.IsActive
                || !ShellGuards.MenuOwnsInput())
            {
                return;
            }
            // A native-focus edit mode (NamePawn) owns the focused field on purpose; releasing
            // it makes vanilla refocus-and-select-all, so each keystroke replaces the value.
            FocusScope top = FocusStack.Top;
            if (top != null && top.OwnsNativeTextFocus)
            {
                return;
            }
            string controlName = GUI.GetNameOfFocusedControl();
            UI.UnfocusCurrentControl();
            if (controlName != lastReleasedControl)
            {
                lastReleasedControl = controlName;
                FlightRecorder.Record("scope", "release foreign text focus '" + controlName + "'");
            }
        }
    }
}
