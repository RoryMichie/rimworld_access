using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    public class IdeologyRitualAdapter : LordJobDialogAdapterBase
    {
        protected static readonly FieldInfo AssignmentsField =
            AccessTools.Field(typeof(Dialog_BeginRitual), "assignments");
        protected static readonly FieldInfo RitualField =
            AccessTools.Field(typeof(Dialog_BeginRitual), "ritual");
        protected static readonly FieldInfo TargetField =
            AccessTools.Field(typeof(Dialog_BeginRitual), "target");
        protected static readonly PropertyInfo SleepingWarningProp =
            AccessTools.Property(typeof(Dialog_BeginRitual), "SleepingWarning");

        protected readonly Dialog_BeginRitual ideologyDialog;
        protected readonly RitualRoleAssignments assignments;
        protected readonly Precept_Ritual ritual;
        protected readonly TargetInfo target;

        public IdeologyRitualAdapter(Dialog_BeginRitual dialog) : base(dialog)
        {
            ideologyDialog = dialog;
            assignments = AssignmentsField?.GetValue(dialog) as RitualRoleAssignments;
            ritual = RitualField?.GetValue(dialog) as Precept_Ritual;
            target = TargetField != null ? (TargetInfo)TargetField.GetValue(dialog) : TargetInfo.Invalid;
        }

        public override TargetInfo Target => target;

        public override string LocalizedDialogName
        {
            get
            {
                if (ritual?.behavior?.def?.label != null)
                    return ritual.behavior.def.label.CapitalizeFirst();
                return ritual?.Label?.CapitalizeFirst()
                    ?? (string)"RimWorldAccess.Rituals.Ritual.DialogNameFallback".Translate();
            }
        }

        public override string ClosingAnnouncement => "RimWorldAccess.Rituals.Ritual.DialogClosed".Translate();

        protected override void AppendDialogSpecificWarnings(List<string> warnings)
        {
            try
            {
                string sleeping = SleepingWarningProp?.GetValue(ideologyDialog)?.ToString();
                if (!string.IsNullOrEmpty(sleeping)) warnings.Add(sleeping);
            }
            catch { /* ignore */ }

            try
            {
                if (assignments != null && assignments.Participants.Any(p => p.Drafted))
                {
                    warnings.Add("RimWorldAccess.Rituals.Ritual.ParticipantsDrafted".Translate());
                }
            }
            catch { /* ignore */ }
        }

        public override IReadOnlyList<LordJobQualityRow> BuildExtraQualityRows()
        {
            var rows = new List<LordJobQualityRow>();
            if (ritual?.ideo == null || ritual.ideo.Fluid != true) return rows;

            try
            {
                var outcomeEffect = ritual.outcomeEffect;
                if (outcomeEffect?.def?.outcomeChances == null || outcomeEffect.def.outcomeChances.Count == 0)
                    return rows;

                var devPointsCurve = IdeoDevelopmentUtility.GetDevelopmentPointsOverOutcomeIndexCurveForRitual(ritual.ideo, ritual);
                if (devPointsCurve == null) return rows;

                // Vanilla's own heading sentence, then plain signed points per outcome: these are
                // not quality factors, so neither row carries the Bonus/Penalty grammar.
                rows.Add(new LordJobQualityRow
                {
                    Label = (string)"RitualDevelopmentPointRewards".Translate(),
                    Change = "",
                    IsInformational = true,
                });

                var outcomeChances = outcomeEffect.def.outcomeChances;
                for (int i = 0; i < outcomeChances.Count; i++)
                {
                    var oc = outcomeChances[i];
                    rows.Add(new LordJobQualityRow
                    {
                        Label = $"  {oc.label}",
                        Change = devPointsCurve.Evaluate(i).ToStringWithSign(),
                        IsInformational = true,
                    });
                }
            }
            catch { /* dev-points are optional info */ }
            return rows;
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
