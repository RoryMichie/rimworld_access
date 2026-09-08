namespace RimWorldAccess
{
    /// <summary>
    /// Thin static bridge between the transfer-loading Harmony patches
    /// (TransportPodPatch, PortalPatch, plus the ambient IsActive gates) and
    /// the live <see cref="Shell.TransportPodLoadingScope"/> instance that now
    /// owns all navigation, announcement, and cursor state (screen-model
    /// refactor pilot — the 1,000-line hand-rolled state this file used to be
    /// lives in git history at 85d6a62^).
    ///
    /// The accept flags are static because they must survive the scope's pop:
    /// Window.PostClose patches read <see cref="AcceptAttempted"/> after the
    /// window (and with it the scope) is already torn down.
    /// </summary>
    public static class TransportPodLoadingState
    {
        internal static Shell.TransportPodLoadingScope ActiveScope;

        private static bool acceptAttempted;

        /// <summary>A transfer-loading scope is driving one of the two dialogs.</summary>
        public static bool IsActive => ActiveScope != null;

        /// <summary>Set when our Accept ran; read (then reset via <see cref="Close"/>) by the PostClose patches.</summary>
        public static bool AcceptAttempted => acceptAttempted;

        /// <summary>Bypass flag for the per-dialog OnAcceptKeyPressed blockers while our own Accept call runs.</summary>
        public static bool AcceptingFromOurCode { get; internal set; }

        /// <summary>Blocks vanilla Escape while a typeahead search is active (the per-dialog cancel blockers).</summary>
        public static bool HasActiveTypeahead => ActiveScope != null && ActiveScope.HasActiveTypeahead;

        /// <summary>Spoken when the dialog closes without accepting; captured by PostClose before teardown.</summary>
        public static string CancelAnnouncement => ActiveScope?.CancelAnnouncement ?? "Loading cancelled";

        internal static void NoteAcceptAttempted()
        {
            acceptAttempted = true;
        }

        /// <summary>Called by the PostClose patches once the dialog is really gone.</summary>
        public static void Close()
        {
            acceptAttempted = false;
        }
    }
}
