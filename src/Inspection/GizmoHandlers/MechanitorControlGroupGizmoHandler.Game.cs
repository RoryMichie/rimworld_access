using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Handler for MechanitorControlGroupGizmo (Biotech mechanitor control
    /// groups).
    ///
    /// Execution is a verbatim port of the original ExecuteSelected ladder's
    /// branch 2b (matched by GetType().Name: opens the accessible control group
    /// menu). It declines when the control group cannot be resolved
    /// (mcg == null), the one case in the original ladder that falls all the
    /// way through to the generic Command fallback.
    ///
    /// Label and status delegate to the existing MechControlGroupState helpers
    /// the legacy announcement switch already used ("Control group N, mode,
    /// count" / per-kind mech counts with average energy), so announcement
    /// behavior is unchanged by the handler migration.
    ///
    /// The description surfaces the vanilla hover tooltip
    /// (MechanitorControlGroupGizmo.GizmoOnGUI's TipRegion delegate): current
    /// work mode with its full description, the per-mech assigned list with
    /// each mech's energy percentage, and the disabled reason when the
    /// mechanitor cannot control mechs. The tooltip's "ControlGroup #N" title
    /// line is intentionally omitted — the label facet already announces the
    /// group number, and echoing it in the description would repeat what the
    /// user just heard.
    ///
    /// Lazy-populate trap: this gizmo stamps
    /// disabled/disabledReason inside GizmoOnGUI from Tracker.CanControlMechs,
    /// so gizmo.Disabled is stale without a render pass. The disabled reason is
    /// read live from controlGroup.Tracker.CanControlMechs instead — the same
    /// source vanilla copies it from.
    /// </summary>
    internal sealed class MechanitorControlGroupGizmoHandler : GizmoHandlerBase
    {
        public override bool TryExecute(Gizmo gizmo, GizmoHandlerContext ctx, out bool runEpilogue)
        {
            runEpilogue = false;

            var mcg = MechControlGroupState.GetControlGroupFromGizmo(gizmo);
            if (mcg == null)
                return false;

            GizmoNavigationState.Close();
            MechControlGroupState.Open(mcg, gizmo);
            return true;
        }

        public override bool TryGetLabel(Gizmo gizmo, out string label)
        {
            label = null;

            if (!(gizmo is MechanitorControlGroupGizmo))
                return false;

            // Legacy behavior preserved: MechControlGroupState.GetGizmoLabel
            // handles single and merged groups, and returns its own translated
            // fallback when the control group cannot be resolved.
            string resolved = MechControlGroupState.GetGizmoLabel(gizmo);
            if (resolved.NullOrEmpty())
                return false;

            label = resolved;
            return true;
        }

        public override bool TryGetStatus(Gizmo gizmo, out string status)
        {
            status = null;

            if (!(gizmo is MechanitorControlGroupGizmo))
                return false;

            // Legacy behavior preserved: per-mech-kind counts with average
            // energy. Empty (no group / no mechs) means no status facet.
            string resolved = MechControlGroupState.GetGizmoStatus(gizmo);
            if (resolved.NullOrEmpty())
                return false;

            status = resolved;
            return true;
        }

        public override bool TryGetDescription(Gizmo gizmo, out string description)
        {
            description = null;

            if (!(gizmo is MechanitorControlGroupGizmo))
                return false;

            var group = MechControlGroupState.GetControlGroupFromGizmo(gizmo);
            if (group == null)
                return false;

            var parts = new List<string>();

            // Vanilla tooltip section 2: "Current mode: X" plus the work mode
            // def's full description (MechanitorControlGroupGizmo.GizmoOnGUI).
            MechWorkModeDef workMode = group.WorkMode;
            if (workMode != null)
            {
                string modeLine = "CurrentMechWorkMode".Translate() + ": " + workMode.LabelCap;
                if (!workMode.description.NullOrEmpty())
                    modeLine += ". " + workMode.description;
                parts.Add(modeLine);
            }

            // Vanilla tooltip section 3: the assigned-mech list with each
            // mech's energy percentage, mirroring vanilla's filter (only mechs
            // with an energy need) and its "Label (75% energy)" entry format.
            List<Pawn> mechs = group.MechsForReading;
            if (mechs != null)
            {
                var entries = new List<string>();
                foreach (Pawn mech in mechs)
                {
                    if (mech?.needs?.energy == null)
                        continue;
                    entries.Add((mech.LabelCap + " ("
                        + mech.needs.energy.CurLevelPercentage.ToStringPercent()
                        + " " + "EnergyLower".Translate() + ")").Resolve());
                }
                if (entries.Count > 0)
                    parts.Add("AssignedMechs".Translate() + ": " + string.Join(", ", entries));
            }

            // Vanilla tooltip section 4: the disabled reason. Read live from
            // Tracker.CanControlMechs — never from gizmo.Disabled, which this
            // type only stamps during its render pass.
            Pawn_MechanitorTracker tracker = group.Tracker;
            if (tracker != null)
            {
                AcceptanceReport canControl = tracker.CanControlMechs;
                if (!canControl.Accepted && !canControl.Reason.NullOrEmpty())
                    parts.Add("DisabledCommand".Translate() + ": " + canControl.Reason);
            }

            if (parts.Count == 0)
                return false;

            description = FlattenForSpeech(string.Join(". ", parts));
            return true;
        }

        /// <summary>
        /// Strips markup tags and collapses newline runs to period+space so the
        /// composed tooltip reads as one spoken sentence (announcements never
        /// use newlines as separators). Work mode descriptions and disabled
        /// reasons are game-authored text that may contain line breaks.
        /// </summary>
        private static string FlattenForSpeech(string text)
        {
            if (text.NullOrEmpty())
                return "";

            text = text.StripTags();
            var sb = new System.Text.StringBuilder(text.Length);
            bool inBreak = false;
            foreach (char c in text)
            {
                if (c == '\n' || c == '\r')
                {
                    if (!inBreak)
                    {
                        sb.Append(". ");
                        inBreak = true;
                    }
                    continue;
                }
                inBreak = false;
                sb.Append(c);
            }
            return sb.ToString().Trim();
        }
    }
}
