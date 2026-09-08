using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Base announcement handler for every Command: label from LabelCap (falling
    /// back to defaultLabel, then a translated unknown-command placeholder) and
    /// description from Desc (falling back to defaultDesc). Registered against
    /// typeof(Command), so any command subtype whose exact-type handler declines
    /// these facets resolves here via the registry's chain walk — this is the
    /// registry replacement for GetGizmoLabel/GetGizmoDescription's `is Command`
    /// branches. Execution is deliberately not implemented: an unhandled command
    /// falls through to the generic ProcessInput fallback exactly as before.
    /// </summary>
    internal sealed class CommandGizmoHandler : GizmoHandlerBase
    {
        public override bool TryGetLabel(Gizmo gizmo, out string label)
        {
            label = null;
            if (!(gizmo is Command cmd))
                return false;

            string text = cmd.LabelCap;
            if (string.IsNullOrEmpty(text))
                text = cmd.defaultLabel;
            if (string.IsNullOrEmpty(text))
                text = "RimWorldAccess.Inspection.Gizmo.UnknownCommand".Translate();

            // Strip color tags (e.g., <color=#...>text</color>) from gizmo labels.
            label = text.StripTags();
            return true;
        }

        public override bool TryGetDescription(Gizmo gizmo, out string description)
        {
            description = null;
            if (!(gizmo is Command cmd))
                return false;

            string desc = cmd.Desc;
            if (string.IsNullOrEmpty(desc))
                desc = cmd.defaultDesc;

            // The gizmo tooltip ends with DescPostfix (Verse/Command.cs:210) — a
            // ritual's TipExtraPart, the weather range cap warning, a bossgroup's
            // cooldown line. Real content, and only ever reachable on hover.
            string postfix = cmd.DescPostfix;
            if (!string.IsNullOrEmpty(postfix))
                desc = (desc ?? "") + postfix;

            // Claim even when empty: a Command with no description had an empty
            // description under the legacy branch too, and callers treat empty as
            // "nothing to speak".
            description = (desc ?? "").StripTags();
            return true;
        }

        /// <summary>
        /// Command.TopRightLabel (Verse/Command.cs line 69, public virtual, default
        /// null) is the small counter sighted players see in the gizmo's top-right
        /// corner — reloadable apparel's "3/5" (Command_VerbOwner), MVCF's ammo
        /// counts, and similar modded status text. Vanilla itself never spoke this
        /// anywhere; claiming it here closes that gap for every Command subtype
        /// whose exact-type handler declines the status facet.
        /// </summary>
        public override bool TryGetStatus(Gizmo gizmo, out string status)
        {
            status = null;
            if (!(gizmo is Command cmd))
                return false;

            // Command_Ability/Command_Psycast put GizmoExtraLabel / neural-heat-
            // and-psyfocus text there, and AbilityGizmoHandler's cost/range/cooldown
            // fragments already announce that content — claiming it here would
            // double-speak every psycast.
            if (cmd is Command_Ability)
                return false;

            string text;
            try
            {
                // Arbitrary modded getters run here; a broken override must not
                // break the whole announcement.
                text = cmd.TopRightLabel;
            }
            catch
            {
                return false;
            }

            if (string.IsNullOrEmpty(text))
                return false;

            status = GizmoTextUtility.FlattenNewlines(text.StripTags());
            return !string.IsNullOrEmpty(status);
        }
    }
}
