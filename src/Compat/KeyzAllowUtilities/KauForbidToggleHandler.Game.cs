using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Gizmo handler for Command_Toggle_WithContext, the mod's replacement for the forbid
    /// toggle: a Command_Toggle whose ProcessInput override opens an allow-all/forbid-all menu
    /// on right-click. ToggleGizmoHandler on the vanilla base still owns the toggle itself; this
    /// lists that menu's entries as keyboard options.
    /// </summary>
    internal sealed class KauForbidToggleHandler : GizmoHandlerBase
    {
        private readonly Type commandType;

        public KauForbidToggleHandler(Type commandType)
        {
            this.commandType = commandType;
        }

        public override bool TryGetExtraOptions(Gizmo gizmo, List<FloatMenuOption> options)
        {
            if (commandType == null || !commandType.IsInstanceOfType(gizmo))
                return false;

            // Vanilla rejects every click on a disabled command before ProcessInput runs, so the
            // right-click menu is out of a sighted player's reach too.
            if (gizmo.Disabled)
                return false;

            try
            {
                // Vehicle A: the override's own button-1 branch, which opens the menu and nothing
                // else; the harvest keeps it off the screen.
                List<FloatMenuOption> captured = FloatMenuHarvest.Capture(delegate
                {
                    gizmo.ProcessInput(new Event { button = 1 });
                });
                if (captured == null || captured.Count == 0)
                    return false;

                options.AddRange(captured);
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("KauForbidToggleHandler.TryGetExtraOptions", ex);
                return false;
            }
        }
    }
}
