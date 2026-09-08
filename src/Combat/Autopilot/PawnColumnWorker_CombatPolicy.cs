using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Assign-tab combat-policy column on PawnColumnWorker_FoodRestriction's shape: manage
    /// button in the header, paintable dropdown per cell. The policy is the autopilot's
    /// switch, so the "None" row that clears the assignment is the off state.
    /// </summary>
    public class PawnColumnWorker_CombatPolicy : PawnColumnWorker
    {
        private const int TopAreaHeight = 65;
        private const int ManageButtonHeight = 32;

        public override void DoHeader(Rect rect, PawnTable table)
        {
            base.DoHeader(rect, table);
            MouseoverSounds.DoRegion(rect);
            var buttonRect = new Rect(rect.x, rect.y + (rect.height - TopAreaHeight), Mathf.Min(rect.width, 360f), ManageButtonHeight);
            if (Widgets.ButtonText(buttonRect, "RimWorldAccess.Autopilot.ManagePolicies".Translate()))
            {
                Find.WindowStack.Add(new Dialog_ManageCombatPolicies(null));
            }
        }

        public override void DoCell(Rect rect, Pawn pawn, PawnTable table)
        {
            if (!AppliesTo(pawn))
            {
                return;
            }
            Rect cellRect = rect.ContractedBy(0f, 2f);
            string label = CurrentLabel(pawn);
            Widgets.Dropdown(cellRect, pawn, CurrentPolicy, Button_GenerateMenu,
                label.Truncate(cellRect.width), null, label, null, null, paintable: true);
        }

        /// <summary>Player pawns that can fight at all — weapon violence or abilities — may hold doctrine.</summary>
        public static bool AppliesTo(Pawn pawn)
        {
            return pawn?.Faction != null && pawn.Faction.IsPlayer && CombatAutopilotOrders.CanFight(pawn);
        }

        public static CombatPolicy CurrentPolicy(Pawn pawn)
        {
            CombatAutopilotData data = CombatAutopilotComponent.TryGetData(pawn);
            return data != null ? data.AssignedPolicy : null;
        }

        public static string CurrentLabel(Pawn pawn)
        {
            CombatPolicy policy = CurrentPolicy(pawn);
            return policy != null ? policy.label : (string)"RimWorldAccess.Autopilot.Policy.None".Translate();
        }

        private IEnumerable<Widgets.DropdownMenuElement<CombatPolicy>> Button_GenerateMenu(Pawn pawn)
        {
            yield return new Widgets.DropdownMenuElement<CombatPolicy>
            {
                option = new FloatMenuOption("RimWorldAccess.Autopilot.Policy.None".Translate(), delegate
                {
                    CombatAutopilotComponent.GetData(pawn).AssignedPolicy = null;
                    CombatAutopilotGizmoPatch.InterruptForRethink(pawn);
                })
                {
                    tooltip = (TipSignal)(string)"RimWorldAccess.Autopilot.Policy.None.Tip".Translate(),
                },
            };
            foreach (CombatPolicy policy in CombatAutopilotComponent.AllPolicies)
            {
                CombatPolicy assigned = policy;
                yield return new Widgets.DropdownMenuElement<CombatPolicy>
                {
                    option = new FloatMenuOption(assigned.label, delegate
                    {
                        // Doctrine never unpauses; only unchecking Pause or a fresh order does.
                        CombatAutopilotComponent.GetData(pawn).AssignedPolicy = assigned;
                        CombatAutopilotGizmoPatch.InterruptForRethink(pawn);
                    })
                    {
                        tooltip = (TipSignal)assigned.SummaryLine(),
                    },
                    payload = assigned,
                };
            }
            yield return new Widgets.DropdownMenuElement<CombatPolicy>
            {
                option = new FloatMenuOption(string.Format("{0}...", "AssignTabEdit".Translate()), delegate
                {
                    Find.WindowStack.Add(new Dialog_ManageCombatPolicies(CurrentPolicy(pawn)));
                }),
            };
        }

        public override int GetMinWidth(PawnTable table)
        {
            return Mathf.Max(base.GetMinWidth(table), Mathf.CeilToInt(194f));
        }

        public override int GetOptimalWidth(PawnTable table)
        {
            return Mathf.Clamp(Mathf.CeilToInt(251f), GetMinWidth(table), GetMaxWidth(table));
        }

        public override int GetMinHeaderHeight(PawnTable table)
        {
            return Mathf.Max(base.GetMinHeaderHeight(table), TopAreaHeight);
        }

        public override int Compare(Pawn a, Pawn b)
        {
            return GetValueToCompare(a).CompareTo(GetValueToCompare(b));
        }

        private static int GetValueToCompare(Pawn pawn)
        {
            CombatPolicy policy = CurrentPolicy(pawn);
            return policy != null ? policy.id : int.MinValue;
        }
    }

    /// <summary>
    /// The Animals/Mechs tables list pawns that are usually not draftable, so their
    /// policy column stays hidden until at least one listed pawn can be drafted, and
    /// only draftable rows get a cell. Each variant mirrors its own table's population.
    /// </summary>
    public abstract class PawnColumnWorker_CombatPolicyDraftGated : PawnColumnWorker_CombatPolicy
    {
        protected abstract IEnumerable<Pawn> TablePawns { get; }

        public override bool VisibleCurrently
        {
            get
            {
                foreach (Pawn pawn in TablePawns)
                {
                    if (Draftable(pawn))
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        public override void DoCell(Rect rect, Pawn pawn, PawnTable table)
        {
            if (Draftable(pawn))
            {
                base.DoCell(rect, pawn, table);
            }
        }

        /// <summary>The colonist-bar controllables signal: a live drafter whose gizmo shows.</summary>
        public static bool Draftable(Pawn pawn)
        {
            return pawn?.drafter != null && pawn.drafter.ShowDraftGizmo;
        }
    }

    public class PawnColumnWorker_CombatPolicyAnimals : PawnColumnWorker_CombatPolicyDraftGated
    {
        protected override IEnumerable<Pawn> TablePawns
        {
            get
            {
                return Find.CurrentMap != null
                    ? Find.CurrentMap.mapPawns.ColonyAnimals
                    : (IEnumerable<Pawn>)System.Array.Empty<Pawn>();
            }
        }
    }

    public class PawnColumnWorker_CombatPolicyMechs : PawnColumnWorker_CombatPolicyDraftGated
    {
        protected override IEnumerable<Pawn> TablePawns
        {
            get
            {
                if (Find.CurrentMap == null)
                {
                    yield break;
                }
                List<Pawn> pawns = Find.CurrentMap.mapPawns.PawnsInFaction(Faction.OfPlayer);
                for (int i = 0; i < pawns.Count; i++)
                {
                    if (pawns[i].RaceProps.IsMechanoid && pawns[i].OverseerSubject != null)
                    {
                        yield return pawns[i];
                    }
                }
            }
        }
    }
}
