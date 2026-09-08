using System;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Thin facade for the whole <see cref="Dialog_BeginLordJob"/> family (Dialog_BeginRitual,
    /// Dialog_BeginPsychicRitual, Dialog_BeginGravshipLaunch, and any modded subclass).
    ///
    /// This class owns ONLY what an external Harmony patch or another mod-side class genuinely
    /// needs from OUTSIDE the scope's own lifecycle: adapter resolution (<see cref="Open"/>/<see cref="Close"/>/
    /// <see cref="IsActive"/>/<see cref="Adapter"/>), <see cref="ClosingAnnouncement"/>,
    /// <see cref="StartLordJob"/> (kept here verbatim per the migration blueprint — its
    /// vehicle-A OnAcceptKeyPressed choreography plus the still-open recovery branch), and
    /// <see cref="QualityInstructionsShown"/> (the one genuinely cross-dialog, cross-session
    /// flag — see its own remarks). Every row list (roles/pawns/quality), cursor, typeahead
    /// search, and announcement composition that the retired monolithic NavigationMode router
    /// used to own now lives on <see cref="LordJobDialogScope"/> as ordinary per-instance state
    /// (auto-reset every dialog open, since a fresh scope is constructed each time — no more
    /// manual "Close() must zero every field" discipline). See LordJobDialogScope's own class
    /// remarks for the three-region model (Roles/Pawns/QualityStats) that replaced the switch.
    ///
    /// LOAD-BEARING BRIDGE: RitualPatch's two Harmony blockers
    /// (<see cref="RimWorldAccess.RitualPatch.Dialog_BeginLordJob_OnAcceptKeyPressed_Patch"/> and
    /// <see cref="RimWorldAccess.RitualPatch.Window_OnCancelKeyPressed_LordJob_Patch"/>) are kept
    /// UNCHANGED by this migration and read
    /// <see cref="CurrentNavigationMode"/>/<see cref="NavigationMode"/>.RoleList/
    /// <see cref="HasActiveTypeahead"/> directly. Since the actual navigation state now lives on
    /// the scope instance, these three members are backed by a live-scope bridge
    /// (<see cref="NotifyScopeAttached"/>/<see cref="NotifyScopeDetached"/>, the same pattern
    /// <c>CaravanFormationState.NotifyScopeAttached</c> already uses) rather than local fields —
    /// RitualPatch.cs itself needed zero edits. <see cref="IsActive"/> ALSO stays a public static
    /// with identical semantics: <c>InfoCardTreeBuilder.cs:1358</c> and
    /// <c>ShellGuards.Game.cs:102</c> both read it directly (grep-verified, no other external
    /// consumers of this class).
    /// </summary>
    public static class LordJobDialogState
    {
        /// <summary>
        /// Preserved only because <see cref="RimWorldAccess.RitualPatch.Window_OnCancelKeyPressed_LordJob_Patch"/>
        /// (UNTOUCHED by this migration) reads <c>NavigationMode.RoleList</c> by name. The scope no
        /// longer stores a mode field of this type internally (it owns a plain region index instead);
        /// <see cref="LordJobDialogScope.BridgeNavigationMode"/> maps its region index back onto this
        /// enum solely so the external patch keeps compiling and reading a live answer.
        /// </summary>
        public enum NavigationMode
        {
            RoleList,
            QualityStats,
        }

        private static bool isActive = false;
        private static ILordJobDialogAdapter adapter = null;
        private static LordJobDialogScope liveScope;

        /// <summary>
        /// Spoken only once per PROCESS lifetime (not per-dialog) — preserved verbatim from the
        /// pre-migration behavior (the old field was a bare <c>static bool</c>, never reset in
        /// Close()). Whether "explain quality stats once per game session, across every ritual /
        /// psychic ritual / gravship dialog" is a deliberate teach-once design or an overlooked
        /// per-dialog reset is not determinable from the source alone. Needs a live-QA answer;
        /// NOT changed here.
        /// </summary>
        public static bool QualityInstructionsShown = false;

        public static bool IsActive
        {
            get { return isActive; }
        }

        /// <summary>
        /// NEW accessor (this migration): the resolved adapter for the live dialog, so
        /// <see cref="LordJobDialogScope"/> can pull role/pawn/quality data directly instead of the
        /// retired design's duplicate list-building methods on this class.
        /// </summary>
        public static ILordJobDialogAdapter Adapter
        {
            get { return adapter; }
        }

        /// <summary>Bridge target for RitualPatch's OnCancelKeyPressed blocker — see the class remarks.</summary>
        public static NavigationMode CurrentNavigationMode
        {
            get { return liveScope != null ? liveScope.BridgeNavigationMode : NavigationMode.RoleList; }
        }

        /// <summary>Bridge target for RitualPatch's OnCancelKeyPressed blocker — see the class remarks.</summary>
        public static bool HasActiveTypeahead
        {
            get { return liveScope != null && liveScope.BridgeHasActiveTypeahead; }
        }

        /// <summary>Called from LordJobDialogScope.OnPush — see the class remarks.</summary>
        internal static void NotifyScopeAttached(LordJobDialogScope scope)
        {
            liveScope = scope;
        }

        /// <summary>Called from LordJobDialogScope.OnPop — see the class remarks.</summary>
        internal static void NotifyScopeDetached(LordJobDialogScope scope)
        {
            if (liveScope == scope)
            {
                liveScope = null;
            }
        }

        public static void Open(Dialog_BeginLordJob dialog)
        {
            if (dialog == null) return;

            try
            {
                if (!LordJobAdapterFactory.TryCreate(dialog, out adapter))
                {
                    isActive = false;
                    return;
                }

                isActive = true;
            }
            catch (Exception ex)
            {
                Log.Error($"[LordJobDialogState] Error opening: {ex.Message}");
                isActive = false;
                adapter = null;
            }
        }

        public static void Close()
        {
            isActive = false;
            adapter = null;
        }

        public static string ClosingAnnouncement
        {
            get
            {
                return adapter != null && !string.IsNullOrEmpty(adapter.ClosingAnnouncement)
                    ? adapter.ClosingAnnouncement
                    : (string)"RimWorldAccess.Rituals.LordJob.DialogClosed".Translate();
            }
        }

        /// <summary>
        /// Alt+S (lordJobDialog.start), claimed by the scope only in the Roles region. Preserved
        /// verbatim per the migration blueprint. Vehicle A: invokes the dialog's OWN
        /// <c>OnAcceptKeyPressed()</c> — the same method vanilla's own Enter/OK path calls — after
        /// honoring <c>adapter.TryStart</c>'s BlockingIssues gate (vehicle B). The recovery branch
        /// is load-bearing: if the dialog is STILL open afterward, <c>Start()</c>'s own internal
        /// validation rejected the action for a reason <c>BlockingIssues</c> didn't catch, so
        /// <see cref="IsActive"/> is restored rather than assumed closed.
        /// </summary>
        public static void StartLordJob()
        {
            if (adapter == null)
            {
                TolkHelper.Speak("RimWorldAccess.Rituals.LordJob.NoDialogOpen".Loc());
                return;
            }

            try
            {
                if (!adapter.TryStart(out var blocking))
                {
                    TolkHelper.SpeakData((string)"RimWorldAccess.Rituals.LordJob.CannotStart".Translate(string.Join(". ", blocking)));
                    return;
                }

                isActive = false;
                var dialog = adapter.Dialog;
                dialog.OnAcceptKeyPressed();
                if (Find.WindowStack.IsOpen(dialog))
                {
                    isActive = true; // dialog still open -> validation failed elsewhere; restore state
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[LordJobDialogState] Start failed: {ex.Message}");
                isActive = true;
            }
        }
    }
}
