using System.Text;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Named preset of the autopilot's tactical behaviors, managed like vanilla's drug and
    /// food policies. The policy IS the per-pawn switch: a pawn with no policy assigned
    /// ("None") has no policy-driven automation; assigning any policy turns it on.
    /// </summary>
    public class CombatPolicy : Policy
    {
        public bool TakeCover;
        public bool MeleeCharge = true;
        public bool PursueAnywhere;
        public bool FightAsSquad = true;
        public bool BashDoors;
        public bool KeepDistance;
        public bool FallBackInjured;
        public bool AvoidBlasts;
        public bool AutocastMarked = true;

        protected override string LoadKey
        {
            get { return "RWA_CombatPolicy"; }
        }

        public CombatPolicy()
        {
        }

        public CombatPolicy(int id, string label) : base(id, label)
        {
        }

        public override void CopyFrom(Policy other)
        {
            if (other is CombatPolicy policy)
            {
                TakeCover = policy.TakeCover;
                MeleeCharge = policy.MeleeCharge;
                PursueAnywhere = policy.PursueAnywhere;
                FightAsSquad = policy.FightAsSquad;
                BashDoors = policy.BashDoors;
                KeepDistance = policy.KeepDistance;
                FallBackInjured = policy.FallBackInjured;
                AvoidBlasts = policy.AvoidBlasts;
                AutocastMarked = policy.AutocastMarked;
            }
        }

        /// <summary>The enabled behaviors as one sentence, so opaque preset names explain themselves in pickers.</summary>
        public string SummaryLine()
        {
            var sb = new StringBuilder();
            AppendEnabled(sb, TakeCover, "RimWorldAccess.Autopilot.TakeCover.Label");
            AppendEnabled(sb, MeleeCharge, "RimWorldAccess.Autopilot.MeleeCharge.Label");
            AppendEnabled(sb, PursueAnywhere, "RimWorldAccess.Autopilot.PursueAnywhere.Label");
            AppendEnabled(sb, FightAsSquad, "RimWorldAccess.Autopilot.FightAsSquad.Label");
            AppendEnabled(sb, BashDoors, "RimWorldAccess.Autopilot.BashDoors.Label");
            AppendEnabled(sb, KeepDistance, "RimWorldAccess.Autopilot.KeepDistance.Label");
            AppendEnabled(sb, FallBackInjured, "RimWorldAccess.Autopilot.FallBackInjured.Label");
            AppendEnabled(sb, AvoidBlasts, "RimWorldAccess.Autopilot.AvoidBlasts.Label");
            AppendEnabled(sb, AutocastMarked, "RimWorldAccess.Autopilot.AutocastMarked.Label");
            if (sb.Length == 0)
            {
                return "RimWorldAccess.Autopilot.SummaryNothing".Translate();
            }
            return "RimWorldAccess.Autopilot.SummaryPrefix".Translate(sb.ToString());
        }

        private static void AppendEnabled(StringBuilder sb, bool on, string labelKey)
        {
            if (!on)
            {
                return;
            }
            if (sb.Length > 0)
            {
                sb.Append("RimWorldAccess.Autopilot.SummarySeparator".Translate());
            }
            sb.Append(labelKey.Translate());
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref TakeCover, "takeCover", false);
            Scribe_Values.Look(ref MeleeCharge, "meleeCharge", true);
            Scribe_Values.Look(ref PursueAnywhere, "pursueAnywhere", false);
            Scribe_Values.Look(ref FightAsSquad, "fightAsSquad", true);
            Scribe_Values.Look(ref BashDoors, "bashDoors", false);
            Scribe_Values.Look(ref KeepDistance, "keepDistance", false);
            Scribe_Values.Look(ref FallBackInjured, "fallBackInjured", false);
            Scribe_Values.Look(ref AvoidBlasts, "avoidBlasts", false);
            Scribe_Values.Look(ref AutocastMarked, "autocastMarked", true);
        }
    }

    /// <summary>The behavior toggles in one fixed order (dialog rows and keyboard scope share it); key parts index the Label/Desc strings.</summary>
    public static class CombatPolicyToggles
    {
        public static readonly string[] KeyParts =
        {
            "TakeCover", "MeleeCharge", "PursueAnywhere", "FightAsSquad", "BashDoors",
            "KeepDistance", "FallBackInjured", "AvoidBlasts", "AutocastMarked",
        };

        public static bool Get(CombatPolicy policy, int index)
        {
            switch (index)
            {
                case 0: return policy.TakeCover;
                case 1: return policy.MeleeCharge;
                case 2: return policy.PursueAnywhere;
                case 3: return policy.FightAsSquad;
                case 4: return policy.BashDoors;
                case 5: return policy.KeepDistance;
                case 6: return policy.FallBackInjured;
                case 7: return policy.AvoidBlasts;
                default: return policy.AutocastMarked;
            }
        }

        public static void Set(CombatPolicy policy, int index, bool value)
        {
            switch (index)
            {
                case 0: policy.TakeCover = value; break;
                case 1: policy.MeleeCharge = value; break;
                case 2: policy.PursueAnywhere = value; break;
                case 3: policy.FightAsSquad = value; break;
                case 4: policy.BashDoors = value; break;
                case 5: policy.KeepDistance = value; break;
                case 6: policy.FallBackInjured = value; break;
                case 7: policy.AvoidBlasts = value; break;
                default: policy.AutocastMarked = value; break;
            }
        }
    }
}
