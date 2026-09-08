using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Orders-tab designator that marks pawns for attack (RWA_AttackTarget designations):
    /// standing priority targets for seek-and-destroy autopilot pawns, drawn as an overlay
    /// for sighted players and cancelled with the vanilla Cancel designator. Modeled on
    /// Designator_Hunt so it rides the keyboard shape flow and the mouse drag alike.
    ///
    /// Area sweeps filter by hostility, read live from the held modifiers so the mouse
    /// drag highlight and the keyboard confirm agree: plain marks hostiles only, Shift
    /// adds factionless creatures, Ctrl+Shift takes everything, own pawns included. A
    /// single-cell pick (keyboard manual mode, single mouse click) is exempt — the player
    /// chose that pawn deliberately.
    /// </summary>
    public class Designator_EngageTarget : Designator
    {
        private bool multiCellSweep;

        protected override DesignationDef Designation => CombatAutopilotJobDefOf.RWA_AttackTarget;

        public override DrawStyleCategoryDef DrawStyleCategory => DrawStyleCategoryDefOf.FilledRectangle;

        public Designator_EngageTarget()
        {
            defaultLabel = "RimWorldAccess.Autopilot.AttackDesignator.Label".Translate();
            defaultDesc = "RimWorldAccess.Autopilot.AttackDesignator.Desc".Translate();
            icon = TexCommand.Attack;
            soundDragSustain = SoundDefOf.Designate_DragStandard;
            soundDragChanged = SoundDefOf.Designate_DragStandard_Changed;
            useMouseIcon = true;
            soundSucceeded = SoundDefOf.Designate_Hunt;
        }

        public override AcceptanceReport CanDesignateCell(IntVec3 c)
        {
            if (!c.InBounds(Map))
            {
                return false;
            }
            foreach (Pawn pawn in TargetsInCell(c))
            {
                return true;
            }
            return "RimWorldAccess.Autopilot.AttackDesignator.MustTarget".Translate();
        }

        public override void DesignateSingleCell(IntVec3 loc)
        {
            foreach (Pawn pawn in TargetsInCell(loc))
            {
                DesignateThing(pawn);
            }
        }

        public override void DesignateMultiCell(IEnumerable<IntVec3> cells)
        {
            // The count decides whether the hostility filter applies; a one-cell "drag" is
            // vanilla's encoding of a plain click.
            int count = 0;
            foreach (IntVec3 cell in cells)
            {
                if (++count > 1)
                {
                    break;
                }
            }
            multiCellSweep = count > 1;
            try
            {
                base.DesignateMultiCell(cells);
            }
            finally
            {
                multiCellSweep = false;
            }
        }

        public override AcceptanceReport CanDesignateThing(Thing t)
        {
            if (!(t is Pawn pawn) || pawn.Dead || !pawn.Spawned
                || pawn.IsPrisonerInPrisonCell()
                || Map.designationManager.DesignationOn(pawn, Designation) != null)
            {
                return false;
            }
            return !FilterActive || PassesFilter(pawn);
        }

        public override void DesignateThing(Thing t)
        {
            Map.designationManager.AddDesignation(new Designation(t, Designation));
        }

        /// <summary>The filter binds only area sweeps: a keyboard shape or a multi-cell mouse drag.</summary>
        private bool FilterActive
        {
            get
            {
                if (multiCellSweep)
                {
                    return true;
                }
                if (ShapePlacementState.IsActive && ShapePlacementState.CurrentShape != ShapeType.Manual)
                {
                    return true;
                }
                DesignationDragger dragger = Find.DesignatorManager?.Dragger;
                return dragger != null && dragger.Dragging && dragger.DragCells.Count > 1;
            }
        }

        private static bool PassesFilter(Pawn pawn)
        {
            if (pawn.HostileTo(Faction.OfPlayer))
            {
                return true;
            }
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)
                || Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);
            if (shift && ctrl)
            {
                return true;
            }
            return shift && pawn.Faction == null;
        }

        private IEnumerable<Pawn> TargetsInCell(IntVec3 c)
        {
            if (c.Fogged(Map))
            {
                yield break;
            }
            List<Thing> things = c.GetThingList(Map);
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i] is Pawn pawn && CanDesignateThing(pawn).Accepted)
                {
                    yield return pawn;
                }
            }
        }
    }
}
