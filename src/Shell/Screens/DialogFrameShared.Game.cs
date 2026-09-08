using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// THE seam every dialog-box scope's framing speech flows through:
    /// the one-time "Dialog opened..." announcement and the "Description..."
    /// wording spoken whenever a dialog's body/description row is
    /// (re-)announced as the focused element. MessageBoxScope,
    /// ModMismatchScope, NodeTreeScope, and GiveNameScope all route their
    /// framing text here rather than hand-rolling it, so a future
    /// dialog-announcement style setting has exactly one choke point to
    /// change (qatrace-20260731-063147.log:1642-1652 caught NodeTreeScope's body-text
    /// row drifting bare while MessageBoxScope's equivalent row kept its
    /// Description frame). Callers strip tags from raw game text before
    /// calling in; this class only assembles the frame around already-clean
    /// strings.
    /// </summary>
    public static class DialogFrame
    {
        /// <summary>
        /// The dialog's one-time open announcement: optional title + body (terminal punctuation
        /// normalized to end in . ! or ?) + the NavInstructions tail, spoken only while the
        /// interaction-hints setting is on and the caller has an element count to report. A null
        /// elementCount omits the tail regardless — GiveNameScope's text-entry dialog has no
        /// navigable element list, so the arrow-keys wording would not fit.
        /// </summary>
        public static string Opened(string title, string body, int? elementCount)
        {
            string announcement = "";
            if (!string.IsNullOrEmpty(title))
            {
                announcement += title + ". ";
            }
            if (!string.IsNullOrEmpty(body))
            {
                announcement += body;
                if (!body.EndsWith(".") && !body.EndsWith("!") && !body.EndsWith("?"))
                {
                    announcement += ".";
                }
                announcement += " ";
            }
            if (elementCount.HasValue
                && RimWorldAccessMod_Settings.Settings != null
                && RimWorldAccessMod_Settings.Settings.AnnounceInteractionHints)
            {
                announcement += "RimWorldAccess.UI.Dialog.NavInstructions".Translate(elementCount.Value);
            }
            return announcement.TrimEnd();
        }

        /// <summary>
        /// The dialog's description/body row, as spoken when that row is the
        /// focused element (initial focus, re-navigation, or an explicit
        /// re-read): the DescriptionLabel frame word, then title and body
        /// joined the same way Opened joins them, minus the NavInstructions
        /// tail (a row re-read is not a fresh open).
        /// </summary>
        public static string Description(string title, string body)
        {
            string text = (string)"RimWorldAccess.UI.Dialog.DescriptionLabel".Translate() + " ";
            if (!string.IsNullOrEmpty(title))
            {
                text += title;
            }
            if (!string.IsNullOrEmpty(body))
            {
                if (!string.IsNullOrEmpty(title))
                {
                    text += ". ";
                }
                text += body;
            }
            return text;
        }
    }
}
