using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Ambient claim: T announces time, Alt+T announces sim
    /// performance (actual vs requested TPS). Both retired UKP rung 6.55
    /// handlers (<see cref="TimeAnnouncementState"/>/<see cref="PerformanceAnnouncementState"/>)
    /// are stateless announcers with no menu of their own, so this is the
    /// shared-registrar shape (see
    /// <see cref="MapScope.RegisterQuickInfoClaims(FocusScope)"/>'s own class
    /// remarks): one static registrar called from both MapScope's and
    /// WorldScope's constructors, guard and handlers byte-identical either
    /// way. World-view parity is not an inference here — the legacy handler's
    /// own written gate already carried
    /// <c>Find.CurrentMap != null || WorldNavigationState.IsActive</c>
    /// explicitly, so T/Alt+T were reachable from world view on legacy too.
    ///
    /// The legacy handler's own <c>!RimWorldAccess.Shell.FocusStack.AnyLiveModal</c>
    /// term is dropped, not preserved: any live MODAL scope above this
    /// ambient base already stops FocusStackCore.Dispatch's walk before it
    /// ever reaches this claim, so the term was always true at the point
    /// this claim would otherwise evaluate. The legacy handler's own outer test
    /// (<c>key == KeyCode.T &amp;&amp; !ctrl &amp;&amp; !shift</c>) needs no
    /// replication either: exact-chord dispatch only offers this claim a
    /// bare-T or Alt+T KeyEventSnapshot in the first place.
    ///
    /// Position protection (no menu-active term of its own, same as the
    /// I/P bottom-of-ladder openers): every still-open menu's own CharSink
    /// swallows a typed 'T' as typeahead/search input before chord matching
    /// even runs (ShellDispatcherPatch.Prefix offers characters to the stack
    /// top-down first), and every live MODAL scope stops the dispatch walk
    /// outright — so this claim only ever fires when nothing above the
    /// ambient base wants the key, exactly reproducing the legacy handler's
    /// bottom-of-ladder position.
    /// </summary>
    public sealed partial class MapScope
    {
        private void RegisterTimeAnnounceClaims()
        {
            RegisterTimeAnnounceClaims(this);
        }

        /// <summary>
        /// The shared registrar — see the class remarks above. Called
        /// once from MapScope's own constructor and once from WorldScope's.
        /// </summary>
        internal static void RegisterTimeAnnounceClaims(FocusScope scope)
        {
            scope.RegisterExternalClaim("map.info.time",
                delegate (KeyEventSnapshot e) { TimeAnnouncementState.AnnounceTime(); }, when: TimeAnnounceLive);
            scope.RegisterExternalClaim("map.info.performance",
                delegate (KeyEventSnapshot e) { PerformanceAnnouncementState.AnnouncePerformance(); }, when: TimeAnnounceLive);
        }

        /// <summary>The legacy handler's gate, verbatim, minus !AnyLiveModal (see class remarks).</summary>
        private static bool TimeAnnounceLive()
        {
            return Current.ProgramState == ProgramState.Playing
                && (Find.CurrentMap != null || WorldNavigationState.IsActive)
                && (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion);
        }
    }
}
