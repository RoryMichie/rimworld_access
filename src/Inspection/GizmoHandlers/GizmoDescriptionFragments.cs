using RimWorldAccess.Shell;

namespace RimWorldAccess
{
    /// <summary>
    /// Distinguishes the two announcement composition call sites: the spoken
    /// announcement, which is now an ElementDescription rendered by the shared
    /// AnnouncementComposer, and the FloatMenuOption label used for Shift+hotkey
    /// disambiguation, which stays a single hand-composed string because a menu
    /// row has no element grammar of its own. The two diverge in exact wording
    /// for the same semantic content, so handlers branch on this rather than
    /// unifying the text.
    /// </summary>
    public enum GizmoDescriptionMode
    {
        Speech,
        MenuLabel,
    }

    /// <summary>
    /// Carries the type-specific announcement fragments an IGizmoHandler.TryDescribe
    /// implementation contributes. The fragment CONTENT lives in handlers; where
    /// each fragment lands is the caller's business —
    /// GizmoNavigationState.DescribeGizmo maps them onto an ElementDescription's
    /// fields for speech, and BuildGizmoMenuLabelInner splices them into the
    /// float-menu row string. All fields are null/false unless the resolved
    /// handler is one of the three types this special-cases (Command_Toggle,
    /// Command_Target, Command_Ability). Pure: no game dependencies beyond the
    /// shell's own CheckState enum, so it is covered directly by tests.
    /// </summary>
    public sealed class GizmoDescriptionFragments
    {
        public GizmoDescriptionMode Mode { get; }

        /// <summary>Command_Toggle's on/off state suffix, pre-formatted with any
        /// separator the original branch always applied (mode-dependent). Still
        /// the MenuLabel path's toggle text; the speech path reads
        /// <see cref="ToggleState"/> instead.</summary>
        public string ToggleStateSuffix { get; set; }

        /// <summary>
        /// Command_Toggle's live state as the shell's own checkbox vocabulary,
        /// so a toggle gizmo speaks like every other checkbox in the mod
        /// ("checkbox, checked") instead of a bespoke ": ON" suffix.
        /// GizmoNavigationState.DescribeGizmo copies it into
        /// ElementDescription.Check. Null for every gizmo that is not a toggle.
        /// </summary>
        public CheckState? ToggleState { get; set; }

        /// <summary>Command_Target's animal-attack range suffix content (no
        /// separator — GizmoNavigationState applies the trailing-period check).</summary>
        public string TargetRangeSuffix { get; set; }

        /// <summary>True only when the resolved handler is Command_Ability AND its
        /// Ability was non-null — the gate on the whole cost/range/cooldown block.
        /// A Command_Ability with a null Ability has no such figures to speak and
        /// is described as an ordinary command instead.</summary>
        public bool AbilityInfoAvailable { get; set; }

        public string AbilityCostInfo { get; set; }
        public string AbilityRangeInfo { get; set; }
        public string AbilityCooldownInfo { get; set; }

        public GizmoDescriptionFragments(GizmoDescriptionMode mode)
        {
            Mode = mode;
        }
    }
}
