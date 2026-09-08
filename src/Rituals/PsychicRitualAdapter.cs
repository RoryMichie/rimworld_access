using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace RimWorldAccess
{
    public class PsychicRitualAdapter : LordJobDialogAdapterBase
    {
        protected static readonly FieldInfo AssignmentsField =
            AccessTools.Field(typeof(Dialog_BeginPsychicRitual), "assignments");
        protected static readonly FieldInfo PsychicRitualDefField =
            AccessTools.Field(typeof(Dialog_BeginPsychicRitual), "psychicRitualDef");
        protected static readonly FieldInfo MapField =
            AccessTools.Field(typeof(Dialog_BeginPsychicRitual), "map");

        protected readonly Dialog_BeginPsychicRitual psychicDialog;
        protected readonly PsychicRitualRoleAssignments assignments;
        protected readonly PsychicRitualDef psychicRitualDef;
        protected readonly Map map;

        public PsychicRitualAdapter(Dialog_BeginPsychicRitual dialog) : base(dialog)
        {
            psychicDialog = dialog;
            assignments = AssignmentsField?.GetValue(dialog) as PsychicRitualRoleAssignments;
            psychicRitualDef = PsychicRitualDefField?.GetValue(dialog) as PsychicRitualDef;
            map = MapField?.GetValue(dialog) as Map;
        }

        public override TargetInfo Target => assignments?.Target ?? TargetInfo.Invalid;

        public override string LocalizedDialogName =>
            psychicRitualDef?.LabelCap.Resolve() ?? (string)"RimWorldAccess.Rituals.Psychic.FallbackName".Translate();

        public override string ClosingAnnouncement => "RimWorldAccess.Rituals.Psychic.DialogClosed".Translate();

        public override string OutcomeDescriptionText
        {
            get
            {
                if (psychicRitualDef == null || assignments == null) return null;
                try
                {
                    GetQualityFactors(out var range);
                    string qualityNumber = System.Math.Abs(range.min - range.max) < 0.01f
                        ? range.min.ToStringPercent("F0")
                        : $"{range.min.ToStringPercent("F0")}-{range.max.ToStringPercent("F0")}";
                    string raw = psychicRitualDef.OutcomeDescription(range, qualityNumber, assignments).Resolve();
                    return string.IsNullOrEmpty(raw) ? null : SanitizeText(raw);
                }
                catch { return null; }
            }
        }

        protected override void AppendDialogSpecificWarnings(List<string> warnings)
        {
            if (assignments == null || psychicRitualDef == null) return;

            try
            {
                // Sleeping pawns assigned to roles that disallow Sleeping.
                var sleeping = SleepingAssignedPawns();
                if (sleeping.Count > 0)
                {
                    string names = sleeping.Select(p => p.LabelShortCap).ToCommaList(useAnd: true);
                    string key = sleeping.Count > 1 ? "PsychicRitualWakingPawnsWarning" : "PsychicRitualWakingPawnWarning";
                    warnings.Add(key.Translate(names).Resolve());
                }

                // Drafted pawns assigned to roles that disallow Drafted.
                var drafted = DraftedAssignedPawns();
                if (drafted.Count > 0)
                {
                    string names = drafted.Select(p => p.LabelShortCap).ToCommaList(useAnd: true);
                    string key = drafted.Count > 1 ? "PsychicRitualUndraftPawnsWarning" : "PsychicRitualUndraftPawnWarning";
                    warnings.Add(key.Translate(names).Resolve());
                }
            }
            catch { /* defensive */ }

            try
            {
                foreach (var w in psychicRitualDef.OutcomeWarnings(assignments))
                {
                    string s = w.Resolve();
                    if (!string.IsNullOrEmpty(s)) warnings.Add(s);
                }
            }
            catch { /* defensive */ }
        }

        private List<Pawn> SleepingAssignedPawns()
        {
            var result = new List<Pawn>();
            foreach (var kvp in assignments.RoleAssignments)
            {
                var roleDef = kvp.Key;
                if (roleDef.ConditionAllowed(PsychicRitualRoleDef.Condition.Sleeping)) continue;
                foreach (var p in kvp.Value)
                {
                    if (!p.Awake() && p.health.capacities.CanBeAwake) result.Add(p);
                }
            }
            return result;
        }

        private List<Pawn> DraftedAssignedPawns()
        {
            var result = new List<Pawn>();
            foreach (var kvp in assignments.RoleAssignments)
            {
                var roleDef = kvp.Key;
                if (roleDef.ConditionAllowed(PsychicRitualRoleDef.Condition.Drafted)) continue;
                foreach (var p in kvp.Value)
                {
                    if (p.Drafted) result.Add(p);
                }
            }
            return result;
        }

        public override bool TryStart(out IReadOnlyList<string> blockingReasons)
        {
            var reasons = new List<string>();
            try
            {
                var enumerable = BlockingIssuesMethod?.Invoke(dialog, null) as System.Collections.IEnumerable;
                if (enumerable != null)
                {
                    foreach (var item in enumerable)
                    {
                        string s = item?.ToString();
                        if (!string.IsNullOrEmpty(s)) reasons.Add(s);
                    }
                }
            }
            catch { /* fall through */ }
            blockingReasons = reasons;
            return reasons.Count == 0;
        }
    }
}
