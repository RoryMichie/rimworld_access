using RimWorld;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The keyboard focus scope for <c>Dialog_ChooseThingsForNewColony</c>, the Archonexus
    /// relocation selection screen, window-attached via ShellBootstrap. ONE CONTENT REGION PER
    /// NON-EMPTY SECTION, mirroring <see cref="RimWorldAccess.ArchonexusColonyState"/>'s section
    /// list, which already applies vanilla's <c>count &gt; 0</c> draw gate — so Tab/Shift+Tab cycle
    /// sections and the shared typeahead reaches every section at once. Left/Right also switch
    /// sections through the two archonexusColony.*Section ids; their Tab/Shift+Tab chords reach the
    /// base's region cycling first, which is registered ahead of every subclass claim.
    ///
    /// <b>Buttons region.</b> The dialog draws exactly two <c>Widgets.ButtonText</c> calls, both
    /// real actions and both after the rows (:369, :382; <c>DoRow</c> draws none), so blanket
    /// capture is exact and Enter on either row runs vanilla's own inline handler, questline
    /// <c>cancel</c> callback included.
    ///
    /// <b>IsModal, with no ForeignWindowAbove needed.</b> Every window this dialog can open is
    /// already ScopeForWindow-registered, so ordinary modal-stack masking covers them and
    /// <see cref="IsModal"/> stays the <see cref="FocusScope"/> default.
    ///
    /// <b>Escape posture (OwnsCancel is state-based, QA R6).</b> The dialog sets
    /// <c>closeOnCancel = true</c> and its vanilla Escape body — <c>Close()</c> plus the
    /// questline's <c>cancel</c> callback — must run for an idle Escape. This scope claims Cancel
    /// ONLY for search-clear, stamping <see cref="ShellFrameStamps.MarkCancelConsumed"/>. The stamp
    /// alone is not enough: the dialog overrides <c>OnCancelKeyPressed</c>
    /// (<c>base.OnCancelKeyPressed(); if (cancel != null) cancel();</c>, :400-407), so the general
    /// <see cref="WindowCancelKeyRouterPatch"/> on <c>Window</c> reaches only the <c>base</c> call
    /// and a false return there still leaves the <c>cancel()</c> tail running. The twin patched
    /// directly on the override
    /// (<see cref="RimWorldAccess.ArchonexusColonyPatch_OnCancelKeyPressed"/>) consults this
    /// property and can block the WHOLE override, base call and tail together — but it runs in the
    /// WINDOW pass, which can precede the dispatcher's main pass and shares Event state with it
    /// (QA R6), so the same-frame stamp can arrive too late for it too. <see cref="OwnsCancel"/>
    /// therefore owns cancel exactly when the search-clear claim WOULD fire, while an idle Escape
    /// stays unowned so both the twin and the general router defer and the override runs in full.
    /// The stamp remains as the belt for main-pass-first orderings. That gate IS
    /// <c>base.OwnsCancel</c>, so the property below folds the base answer rather than repeating
    /// the condition.
    ///
    /// <b>OwnsAccept = TRUE.</b> Enter always toggles the focused row rather than closing the
    /// dialog, done structurally by <see cref="WindowAcceptKeyRouterPatch"/> whether or not the
    /// Activate claim matched a given frame: OwnsAccept alone blocks vanilla while this scope is
    /// attached-and-top, independent of any stamp.
    ///
    /// <b>Empty sections.</b> With all four sections empty there are no content regions at all, so
    /// the navigation grammar has nothing to move, the remaining claims are gated on
    /// <see cref="ArchonexusColonyState.HasSections"/>, and this scope's own modality swallows the
    /// key regardless — a live modal scope with no matching claim stops the dispatch walk without
    /// falling through.
    ///
    /// <b>Pawn-info quintet.</b> Alt+H/M/N/G/K no-op on the Relics and Items tabs, reproduced by
    /// gating each claim on the focused row actually being a <see cref="Pawn"/>.
    /// </summary>
    public sealed class ArchonexusColonyScope : ScreenScope
    {
        private readonly Dialog_ChooseThingsForNewColony dialog;

        public ArchonexusColonyScope(Dialog_ChooseThingsForNewColony dialog)
        {
            this.dialog = dialog;

            // The two section ids also bind Tab/Shift+Tab, which the base's region cycling claims
            // first, so these reach the arrows only.
            Claim("archonexusColony.previousSection", delegate { MoveRegion(false); }, when: HasSections);
            Claim("archonexusColony.nextSection", delegate { MoveRegion(true); }, when: HasSections);

            Claim("archonexusColony.accept", delegate { ArchonexusColonyState.AttemptAccept(); }, when: HasSections);
            Claim("archonexusColony.announceStatus", delegate { ArchonexusColonyState.AnnounceStatus(); }, when: HasSections);
            Claim("archonexusColony.infoCard", delegate { ArchonexusColonyState.OpenInfoCard(FocusedThing); }, when: HasSections);

            Claim("archonexusColony.healthInfo", delegate { ArchonexusColonyState.ShowHealthInfo(FocusedPawn); }, when: HasSelectedPawn);
            Claim("archonexusColony.moodInfo", delegate { ArchonexusColonyState.ShowMoodInfo(FocusedPawn); }, when: HasSelectedPawn);
            Claim("archonexusColony.needsInfo", delegate { ArchonexusColonyState.ShowNeedsInfo(FocusedPawn); }, when: HasSelectedPawn);
            Claim("archonexusColony.gearInfo", delegate { ArchonexusColonyState.ShowGearInfo(FocusedPawn); }, when: HasSelectedPawn);
            Claim("archonexusColony.skillsInfo", delegate { ArchonexusColonyState.ShowSkillsInfo(FocusedPawn); }, when: HasSelectedPawn);
        }

        public override string Name
        {
            get { return "archonexus-colony"; }
        }

        /// <summary>
        /// STATE-BASED, not a constant (QA R6); see the class remarks. The base answer IS that
        /// state: the chassis owns cancel exactly while its search-clear claim would fire.
        /// </summary>
        public override bool OwnsCancel
        {
            get { return base.OwnsCancel; }
        }

        protected internal override Window OwnedWindow
        {
            get { return dialog; }
        }

        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        protected override int ContentRegionCount
        {
            get { return ArchonexusColonyState.SectionCount; }
        }

        protected override string ContentRegionName(int region)
        {
            return ArchonexusColonyState.SectionName(region);
        }

        protected override int ContentItemCount(int region)
        {
            return ArchonexusColonyState.EntryCount(region);
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            return ArchonexusColonyState.DescribeEntry(region, index);
        }

        protected override void ActivateContentItem(int region, int index)
        {
            ArchonexusColonyState.Toggle(region, index);
        }

        /// <summary>Shift+Enter presses Accept from anywhere on the screen, gated by the dialog's own AcceptanceReport.</summary>
        protected override ScreenAction CapturedDefaultAcceptAction
        {
            get
            {
                return new ScreenAction("AcceptButton".Translate().ToString(),
                    ArchonexusColonyState.AttemptAccept, "archonexusColony.accept");
            }
        }

        /// <summary>Teaches Alt+S on the captured Accept row; Cancel (capture index 1) has no chord of its own.</summary>
        protected override string CapturedButtonHotkey(int captureIndex)
        {
            return captureIndex == 0 ? ChordDisplay("archonexusColony.accept") : null;
        }

        protected override string ComposeOpenAnnouncement()
        {
            return ArchonexusColonyState.IsActive ? ArchonexusColonyState.BuildOpeningText() : null;
        }

        internal bool Owns(Window window)
        {
            return ReferenceEquals(window, dialog);
        }

        /// <summary>
        /// The row the keyboard is on, or null outside a section region: what
        /// <see cref="ArchonexusRowDrawPatch"/> rings and what the Alt+I/Alt+letter claims act on.
        /// </summary>
        internal Thing FocusedThing
        {
            get
            {
                ListModel region = Model.CurrentRegion;
                if (region == null || region.Index < 0 || Model.RegionIndex >= ArchonexusColonyState.SectionCount)
                {
                    return null;
                }
                return ArchonexusColonyState.EntryAt(Model.RegionIndex, region.Index);
            }
        }

        private Pawn FocusedPawn
        {
            get { return FocusedThing as Pawn; }
        }

        private static bool HasSections()
        {
            return ArchonexusColonyState.HasSections;
        }

        private bool HasSelectedPawn()
        {
            return FocusedPawn != null;
        }
    }
}
