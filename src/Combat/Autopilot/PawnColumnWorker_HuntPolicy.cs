using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Assign-tab hunting-policy column on PawnColumnWorker_FoodRestriction's shape: manage
    /// button in the header, paintable dropdown per cell. Unlike the combat policy there is
    /// no "None" row — an unassigned pawn follows the default policy, and the Hunt animals
    /// gizmo checkbox is the on/off switch. Humans only: animals and mechs never take the
    /// hunt order.
    /// </summary>
    public class PawnColumnWorker_HuntPolicy : PawnColumnWorker
    {
        private const int TopAreaHeight = 65;
        private const int ManageButtonHeight = 32;

        public override void DoHeader(Rect rect, PawnTable table)
        {
            base.DoHeader(rect, table);
            MouseoverSounds.DoRegion(rect);
            var buttonRect = new Rect(rect.x, rect.y + (rect.height - TopAreaHeight), Mathf.Min(rect.width, 360f), ManageButtonHeight);
            if (Widgets.ButtonText(buttonRect, "RimWorldAccess.Autopilot.ManageHuntPolicies".Translate()))
            {
                Find.WindowStack.Add(new Dialog_ManageHuntingPolicies(null));
            }
        }

        public override void DoCell(Rect rect, Pawn pawn, PawnTable table)
        {
            if (!AppliesTo(pawn))
            {
                return;
            }
            Rect cellRect = rect.ContractedBy(0f, 2f);
            string label = EffectivePolicy(pawn).label;
            Widgets.Dropdown(cellRect, pawn, AssignedPolicy, Button_GenerateMenu,
                label.Truncate(cellRect.width), null, label, null, null, paintable: true);
        }

        public static bool AppliesTo(Pawn pawn)
        {
            return pawn?.Faction != null && pawn.Faction.IsPlayer
                && pawn.RaceProps.Humanlike && CombatAutopilotOrders.ViolentCapable(pawn);
        }

        private static HuntingPolicy AssignedPolicy(Pawn pawn)
        {
            return CombatAutopilotComponent.TryGetData(pawn)?.AssignedHuntPolicy;
        }

        private static HuntingPolicy EffectivePolicy(Pawn pawn)
        {
            return CombatAutopilotComponent.EffectiveHuntPolicy(pawn);
        }

        private IEnumerable<Widgets.DropdownMenuElement<HuntingPolicy>> Button_GenerateMenu(Pawn pawn)
        {
            foreach (HuntingPolicy policy in CombatAutopilotComponent.AllHuntingPolicies)
            {
                HuntingPolicy chosen = policy;
                yield return new Widgets.DropdownMenuElement<HuntingPolicy>
                {
                    option = new FloatMenuOption(chosen.label, delegate
                    {
                        CombatAutopilotComponent.GetData(pawn).AssignedHuntPolicy = chosen;
                        CombatAutopilotGizmoPatch.InterruptForRethink(pawn);
                    }),
                    payload = chosen,
                };
            }
            yield return new Widgets.DropdownMenuElement<HuntingPolicy>
            {
                option = new FloatMenuOption(string.Format("{0}...", "AssignTabEdit".Translate()), delegate
                {
                    Find.WindowStack.Add(new Dialog_ManageHuntingPolicies(EffectivePolicy(pawn)));
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
            HuntingPolicy policy = AssignedPolicy(pawn);
            return policy != null ? policy.id : int.MinValue;
        }
    }
}
