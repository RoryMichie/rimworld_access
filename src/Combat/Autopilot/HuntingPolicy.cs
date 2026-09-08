using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Named preset of the drafted-hunting rules, managed like vanilla's food policies:
    /// which animals a pawn may chase while its Hunt animals checkbox is on. Pure doctrine —
    /// the gizmo checkbox is the switch, and every pawn without an assignment follows the
    /// default policy (slot 0). The policy outranks hunt designations entirely: drafted
    /// hunting reads only these rules, undrafted work hunting keeps using designators.
    /// </summary>
    public class HuntingPolicy : Policy
    {
        public bool SpareVenerated = true;

        /// <summary>
        /// Allowed band of the animal's revenge chance on harm — the same difficulty-scaled
        /// number the info card shows (<see cref="PawnUtility.GetManhunterOnDamageChance(Pawn, Thing, float)"/>).
        /// </summary>
        public FloatRange RevengeChance = FloatRange.ZeroToOne;

        /// <summary>Hard allowlist over the Animals category; animals outside it (no filter row to show) always pass.</summary>
        public ThingFilter AllowedAnimals = new ThingFilter();

        private static ThingFilter animalGlobalFilter;

        /// <summary>Every filterable animal race, the parent filter of every policy's allowlist (FoodGlobalFilter pattern).</summary>
        public static ThingFilter AnimalGlobalFilter
        {
            get
            {
                if (animalGlobalFilter == null)
                {
                    animalGlobalFilter = new ThingFilter();
                    foreach (ThingDef def in DefDatabase<ThingDef>.AllDefs.Where(IsFilterableAnimalDef))
                    {
                        animalGlobalFilter.SetAllow(def, allow: true);
                    }
                }
                return animalGlobalFilter;
            }
        }

        public static bool IsFilterableAnimalDef(ThingDef def)
        {
            return def.race != null && def.race.Animal
                && def.IsWithinCategory(ThingCategoryDefOf.Animals);
        }

        protected override string LoadKey
        {
            get { return "RWA_HuntingPolicy"; }
        }

        public HuntingPolicy()
        {
        }

        public HuntingPolicy(int id, string label) : base(id, label)
        {
            AllowedAnimals.CopyAllowancesFrom(AnimalGlobalFilter);
        }

        public override void CopyFrom(Policy other)
        {
            if (other is HuntingPolicy policy)
            {
                SpareVenerated = policy.SpareVenerated;
                RevengeChance = policy.RevengeChance;
                AllowedAnimals.CopyAllowancesFrom(policy.AllowedAnimals);
            }
        }

        public bool Allows(Pawn animal)
        {
            if (IsFilterableAnimalDef(animal.def) && !AllowedAnimals.Allows(animal.def))
            {
                return false;
            }
            return RevengeChance.Includes(PawnUtility.GetManhunterOnDamageChance(animal));
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref SpareVenerated, "spareVenerated", true);
            Scribe_Values.Look(ref RevengeChance, "revengeChance", FloatRange.ZeroToOne);
            Scribe_Deep.Look(ref AllowedAnimals, "allowedAnimals");
            if (Scribe.mode == LoadSaveMode.PostLoadInit && AllowedAnimals == null)
            {
                AllowedAnimals = new ThingFilter();
                AllowedAnimals.CopyAllowancesFrom(AnimalGlobalFilter);
            }
        }
    }
}
