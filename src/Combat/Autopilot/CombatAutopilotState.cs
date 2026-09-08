using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Per-pawn autopilot state, split orders-from-doctrine: the assigned
    /// <see cref="CombatPolicy"/> is doctrine (HOW to fight: cover, kiting, safety),
    /// while SeekAndDestroy, the engage queue, hunt animals and the autocast marks are
    /// per-pawn orders set from the drafted gizmo strip, active while drafted regardless
    /// of the policy. Paused suspends ALL of it without losing the setup; only unchecking
    /// it (or a fresh explicit order) resumes — assigning doctrine never does. The hunting
    /// policy is doctrine for the HuntAnimals order; null follows the save's default.
    /// </summary>
    public class CombatAutopilotData : IExposable
    {
        public bool HuntAnimals;
        public bool SeekAndDestroy;
        public bool Paused;
        public CombatPolicy AssignedPolicy;
        public HuntingPolicy AssignedHuntPolicy;
        public List<AbilityDef> AutocastAbilities = new List<AbilityDef>();

        /// <summary>The engage queue: explicit player targets, served front-first ahead of everything else.</summary>
        public List<Thing> OrderedTargets = new List<Thing>();

        /// <summary>
        /// Where a holding pawn belongs: set when automation displaces it (a safety move
        /// or an engage order), walked back to once nothing demands otherwise. Cleared by
        /// arrival, a drafted move order, undrafting, or seek-and-destroy.
        /// </summary>
        public IntVec3 HoldPost = IntVec3.Invalid;

        /// <summary>The area this pawn walks a beat in while drafted and otherwise idle; null = no patrol.</summary>
        public Area PatrolArea;

        /// <summary>The current patrol leg's destination; transient, re-picked after loads.</summary>
        public IntVec3 PatrolDest = IntVec3.Invalid;

        /// <summary>Committed firing position against <see cref="CastDestEnemy"/>; transient, re-picked after loads.</summary>
        public IntVec3 CastDest = IntVec3.Invalid;
        public Thing CastDestEnemy;

        /// <summary>Target the squad spread genuinely redirected this pawn onto (reports say "spreading fire"); transient.</summary>
        public Thing SpreadFireTarget;

        /// <summary>True while anything is configured — the pause checkbox only appears then.</summary>
        public bool HasAnyAutomation
        {
            get
            {
                return AssignedPolicy != null || AssignedHuntPolicy != null || SeekAndDestroy
                    || HuntAnimals || PatrolArea != null || OrderedTargets.Count > 0
                    || AutocastAbilities.Count > 0;
            }
        }

        /// <summary>
        /// The front of the engage queue after pruning entries that are down, dead or
        /// gone; an unreachable-but-alive target stays queued.
        /// </summary>
        public Thing CurrentOrderedTarget()
        {
            while (OrderedTargets.Count > 0 && !IsLiveTarget(OrderedTargets[0]))
            {
                OrderedTargets.RemoveAt(0);
            }
            return OrderedTargets.Count > 0 ? OrderedTargets[0] : null;
        }

        private static bool IsLiveTarget(Thing thing)
        {
            if (thing == null || thing.Destroyed || !thing.Spawned)
            {
                return false;
            }
            return !(thing is Pawn pawn) || !pawn.DeadOrDowned;
        }

        /// <summary>Plain order replaces the queue; a queued order appends. Duplicates collapse.</summary>
        public void GiveEngageOrder(Thing target, bool queue)
        {
            if (!queue)
            {
                OrderedTargets.Clear();
            }
            if (!OrderedTargets.Contains(target))
            {
                OrderedTargets.Add(target);
            }
        }

        /// <summary>
        /// Autocast marks act only under a policy whose Autocast checkbox permits them:
        /// the marks say WHICH abilities, the policy says WHETHER. No policy, no autopilot.
        /// </summary>
        public bool AutocastActive
        {
            get { return AssignedPolicy != null && AssignedPolicy.AutocastMarked; }
        }

        public bool AutocastFor(AbilityDef def)
        {
            return AutocastAbilities.Contains(def);
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref HuntAnimals, "huntAnimals", false);
            Scribe_Values.Look(ref SeekAndDestroy, "seekAndDestroy", false);
            Scribe_Values.Look(ref Paused, "paused", false);
            Scribe_References.Look(ref AssignedPolicy, "policy");
            Scribe_References.Look(ref AssignedHuntPolicy, "huntPolicy");
            Scribe_Collections.Look(ref AutocastAbilities, "autocastAbilities", LookMode.Def);
            Scribe_Collections.Look(ref OrderedTargets, "orderedTargets", LookMode.Reference);
            Scribe_Values.Look(ref HoldPost, "holdPost", IntVec3.Invalid);
            Scribe_References.Look(ref PatrolArea, "patrolArea");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (AutocastAbilities == null)
                {
                    AutocastAbilities = new List<AbilityDef>();
                }
                // A def can vanish when the mod providing it is removed mid-save.
                AutocastAbilities.RemoveAll(def => def == null);
                if (OrderedTargets == null)
                {
                    OrderedTargets = new List<Thing>();
                }
                OrderedTargets.RemoveAll(t => t == null || t.Destroyed);
            }
        }
    }

    /// <summary>
    /// Save-scoped store of every pawn's autopilot switches, keyed by thingIDNumber, plus
    /// the save's combat policies (DrugPolicyDatabase pattern: slot 0 default, deletion
    /// gated on live assignees). The think tree reads through <see cref="TryGetData"/>,
    /// which never allocates; only the gizmo toggles create entries via <see cref="GetData"/>.
    /// </summary>
    public class CombatAutopilotComponent : GameComponent
    {
        private static CombatAutopilotComponent instance;

        private Dictionary<int, CombatAutopilotData> store = new Dictionary<int, CombatAutopilotData>();
        private List<int> scribeKeys;
        private List<CombatAutopilotData> scribeValues;
        private List<CombatPolicy> policies;
        private List<HuntingPolicy> huntingPolicies;

        public CombatAutopilotComponent(Game game)
        {
            instance = this;
        }

        public static List<CombatPolicy> AllPolicies
        {
            get
            {
                if (instance == null)
                {
                    return new List<CombatPolicy>();
                }
                if (instance.policies == null)
                {
                    instance.SeedStartingPolicies();
                }
                return instance.policies;
            }
        }

        public static CombatPolicy MakeNewPolicy()
        {
            List<CombatPolicy> all = AllPolicies;
            int id = all.Count == 0 ? 1 : all.Max(p => p.id) + 1;
            var policy = new CombatPolicy(id, "RimWorldAccess.Autopilot.PolicyName".Translate() + " " + id);
            all.Add(policy);
            return policy;
        }

        public static CombatPolicy DefaultPolicy()
        {
            List<CombatPolicy> all = AllPolicies;
            if (all.Count == 0)
            {
                MakeNewPolicy();
            }
            return all[0];
        }

        public static void SetDefaultPolicy(CombatPolicy policy)
        {
            List<CombatPolicy> all = AllPolicies;
            int index = all.IndexOf(policy);
            if (index > 0)
            {
                CombatPolicy previous = all[0];
                all[0] = policy;
                all[index] = previous;
            }
        }

        public static List<HuntingPolicy> AllHuntingPolicies
        {
            get
            {
                if (instance == null)
                {
                    return new List<HuntingPolicy>();
                }
                if (instance.huntingPolicies == null)
                {
                    instance.SeedStartingHuntingPolicies();
                }
                return instance.huntingPolicies;
            }
        }

        public static HuntingPolicy MakeNewHuntingPolicy()
        {
            List<HuntingPolicy> all = AllHuntingPolicies;
            int id = all.Count == 0 ? 1 : all.Max(p => p.id) + 1;
            var policy = new HuntingPolicy(id, "RimWorldAccess.Autopilot.HuntPolicyName".Translate() + " " + id);
            all.Add(policy);
            return policy;
        }

        public static HuntingPolicy DefaultHuntingPolicy()
        {
            List<HuntingPolicy> all = AllHuntingPolicies;
            if (all.Count == 0)
            {
                MakeNewHuntingPolicy();
            }
            return all[0];
        }

        public static void SetDefaultHuntingPolicy(HuntingPolicy policy)
        {
            List<HuntingPolicy> all = AllHuntingPolicies;
            int index = all.IndexOf(policy);
            if (index > 0)
            {
                HuntingPolicy previous = all[0];
                all[0] = policy;
                all[index] = previous;
            }
        }

        /// <summary>The rules this pawn hunts by: its assignment, or the save's default policy.</summary>
        public static HuntingPolicy EffectiveHuntPolicy(Pawn pawn)
        {
            CombatAutopilotData data = TryGetData(pawn);
            return data?.AssignedHuntPolicy ?? DefaultHuntingPolicy();
        }

        public static AcceptanceReport TryDeleteHuntingPolicy(HuntingPolicy policy)
        {
            if (policy == DefaultHuntingPolicy())
            {
                return new AcceptanceReport("RimWorldAccess.Autopilot.HuntPolicyIsDefault".Translate());
            }
            foreach (Pawn pawn in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive)
            {
                CombatAutopilotData data = TryGetData(pawn);
                if (data != null && data.AssignedHuntPolicy == policy)
                {
                    return new AcceptanceReport("RimWorldAccess.Autopilot.HuntPolicyInUse".Translate(pawn.LabelShort));
                }
            }
            if (instance != null)
            {
                foreach (CombatAutopilotData data in instance.store.Values)
                {
                    if (data.AssignedHuntPolicy == policy)
                    {
                        data.AssignedHuntPolicy = null;
                    }
                }
            }
            AllHuntingPolicies.Remove(policy);
            return AcceptanceReport.WasAccepted;
        }

        /// <summary>DrugPolicyDatabase.TryDelete's shape: live assignees block; stale entries are cleared.</summary>
        public static AcceptanceReport TryDeletePolicy(CombatPolicy policy)
        {
            foreach (Pawn pawn in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive)
            {
                CombatAutopilotData data = TryGetData(pawn);
                if (data != null && data.AssignedPolicy == policy)
                {
                    return new AcceptanceReport("RimWorldAccess.Autopilot.PolicyInUse".Translate(pawn.LabelShort));
                }
            }
            if (instance != null)
            {
                foreach (CombatAutopilotData data in instance.store.Values)
                {
                    if (data.AssignedPolicy == policy)
                    {
                        data.AssignedPolicy = null;
                    }
                }
            }
            AllPolicies.Remove(policy);
            return AcceptanceReport.WasAccepted;
        }

        /// <summary>Graded starters: each tier hands the AI a little less than the one above.</summary>
        private void SeedStartingPolicies()
        {
            policies = new List<CombatPolicy>();
            // Slot 0 default: everything on.
            Seed("RimWorldAccess.Autopilot.Policy.FullControl", p =>
            {
                p.TakeCover = true;
                p.PursueAnywhere = true;
                p.BashDoors = true;
                p.KeepDistance = true;
                p.FallBackInjured = true;
                p.AvoidBlasts = true;
            });
            // Fights well but stays where the action already is: no map-wide pursuit, no doors.
            Seed("RimWorldAccess.Autopilot.Policy.FightNearby", p =>
            {
                p.TakeCover = true;
                p.KeepDistance = true;
                p.FallBackInjured = true;
                p.AvoidBlasts = true;
            });
            // Self-preservation only: holds where placed, no charging or repositioning for cover.
            Seed("RimWorldAccess.Autopilot.Policy.StaySafe", p =>
            {
                p.MeleeCharge = false;
                p.FightAsSquad = false;
                p.KeepDistance = true;
                p.FallBackInjured = true;
                p.AvoidBlasts = true;
            });
        }

        private void Seed(string labelKey, Action<CombatPolicy> configure)
        {
            var policy = new CombatPolicy(policies.Count + 1, labelKey.Translate());
            configure(policy);
            policies.Add(policy);
        }

        private void SeedStartingHuntingPolicies()
        {
            huntingPolicies = new List<HuntingPolicy>();
            // Slot 0 is the default every unassigned pawn follows: only game below the hunt
            // designator's own may-retaliate warning threshold (1.5%).
            var safeGame = new HuntingPolicy(1,
                "RimWorldAccess.Autopilot.HuntPolicy.SafeGame".Translate())
            {
                RevengeChance = new FloatRange(0f, 0.015f),
            };
            huntingPolicies.Add(safeGame);
            var anyAnimal = new HuntingPolicy(2,
                "RimWorldAccess.Autopilot.HuntPolicy.AnyAnimal".Translate());
            huntingPolicies.Add(anyAnimal);
        }

        public static CombatAutopilotData GetData(Pawn pawn)
        {
            if (instance == null || pawn == null)
            {
                return null;
            }
            if (!instance.store.TryGetValue(pawn.thingIDNumber, out CombatAutopilotData data))
            {
                data = new CombatAutopilotData();
                instance.store[pawn.thingIDNumber] = data;
            }
            return data;
        }

        /// <summary>Read-only twin of <see cref="GetData"/>: null when the pawn was never configured.</summary>
        public static CombatAutopilotData TryGetData(Pawn pawn)
        {
            if (instance == null || pawn == null)
            {
                return null;
            }
            instance.store.TryGetValue(pawn.thingIDNumber, out CombatAutopilotData data);
            return data;
        }

        /// <summary>Entries with nothing configured are dead weight (thing ids never recur), so saves drop them.</summary>
        private void PruneIdleEntries()
        {
            List<int> idle = null;
            foreach (KeyValuePair<int, CombatAutopilotData> entry in store)
            {
                if (!entry.Value.HasAnyAutomation && !entry.Value.HoldPost.IsValid)
                {
                    (idle ?? (idle = new List<int>())).Add(entry.Key);
                }
            }
            if (idle == null)
            {
                return;
            }
            for (int i = 0; i < idle.Count; i++)
            {
                store.Remove(idle[i]);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                PruneIdleEntries();
            }
            Scribe_Collections.Look(ref policies, "policies", LookMode.Deep);
            Scribe_Collections.Look(ref huntingPolicies, "huntingPolicies", LookMode.Deep);
            Scribe_Collections.Look(ref store, "store", LookMode.Value, LookMode.Deep, ref scribeKeys, ref scribeValues);
            if (store == null)
            {
                store = new Dictionary<int, CombatAutopilotData>();
            }
            // A missing list seeds starters on first access; a deliberately emptied one stays empty.
        }
    }
}
