using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Lists the right-click branches of the Command_Actions Thing_Patches adds to every thing
    /// (Selection tools, Select rotting corpses, Haul urgently): each action delegate branches on
    /// Event.current.button, opening an on-screen/on-map menu for a right click. Registered on
    /// Command_Action, so it declines fast for every gizmo whose action is not the mod's.
    /// </summary>
    internal sealed class KauThingGizmoHandler : GizmoHandlerBase
    {
        public override bool TryGetExtraOptions(Gizmo gizmo, List<FloatMenuOption> options)
        {
            Command_Action command = gizmo as Command_Action;
            if (command == null || command.action == null || gizmo.Disabled)
                return false;
            if (!KauCompat.GizmoGate.Ensure())
                return false;
            if (command.action.Method.DeclaringType == null
                || command.action.Method.DeclaringType.Assembly != KauCompat.ModAssembly)
                return false;

            bool selection = ReferenceEquals(command.icon, KauCompat.IconOf(KauCompat.MultiSelectIconField));
            bool haul = ReferenceEquals(command.icon, KauCompat.IconOf(KauCompat.HaulUrgentlyIconField))
                || ReferenceEquals(command.icon, KauCompat.IconOf(KauCompat.HaulUrgentlyDisableIconField));
            if (!selection && !haul)
                return false;

            Event current = Event.current;
            if (current == null)
                return false;

            try
            {
                List<FloatMenuOption> captured = Harvest(command, current);
                if (captured == null || captured.Count == 0)
                    return false;

                foreach (FloatMenuOption option in captured)
                {
                    if (selection && option.action != null)
                        AnnounceSelectionAfter(option);
                    options.Add(option);
                }
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("KauThingGizmoHandler.TryGetExtraOptions", ex);
                return false;
            }
        }

        /// <summary>
        /// Runs the action's right-click branch under a menu capture. The delegate reads
        /// Event.current, so the button is staged there; Shift is cleared because the haul
        /// action's Shift branch selects the designator outright instead of opening a menu. The
        /// action is invoked directly rather than through ProcessInput so a probe plays no click.
        /// </summary>
        private static List<FloatMenuOption> Harvest(Command_Action command, Event current)
        {
            int savedButton = current.button;
            bool savedShift = current.shift;
            try
            {
                current.button = 1;
                current.shift = false;
                return FloatMenuHarvest.Capture(command.action);
            }
            finally
            {
                current.button = savedButton;
                current.shift = savedShift;
            }
        }

        /// <summary>
        /// The mod's bulk selections give no message, so the count is measured; an option that
        /// selects a designator instead (Select in rect) already announced its placement.
        /// </summary>
        private static void AnnounceSelectionAfter(FloatMenuOption option)
        {
            Action original = option.action;
            option.action = delegate
            {
                Selector selector = Find.Selector;
                Designator designatorBefore = Find.DesignatorManager?.SelectedDesignator;
                int before = selector != null ? selector.NumSelected : 0;
                original();
                if (selector == null || Find.DesignatorManager?.SelectedDesignator != designatorBefore)
                    return;
                int added = selector.NumSelected - before;
                TolkHelper.Speak(added > 0
                    ? "RimWorldAccess.Compat.AllowTool.SelectionAdded".Loc(added, selector.NumSelected)
                    : "RimWorldAccess.Compat.AllowTool.NothingSelected".Loc());
            };
        }
    }
}
