using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Gizmo handler for VEF.AI.Command_ToggleWithRClick (DraftedJobUtils.cs
    /// lines 16-31): a Command_Toggle whose ProcessInput override runs a
    /// second delegate on right-click, with no visible label or tooltip for
    /// that branch at all (e.g. the drafted-insectoid hunt toggle's "auto for
    /// all" right-click). ToggleGizmoHandler on the vanilla Command_Toggle
    /// base already covers the toggle itself; this handler only surfaces the
    /// hidden branch as a keyboard menu option.
    /// </summary>
    internal sealed class VefToggleRClickHandler : GizmoHandlerBase
    {
        private readonly Type commandType;
        private readonly FieldInfo rightClickActionField;
        private readonly bool ready;

        public VefToggleRClickHandler(Type commandType)
        {
            this.commandType = commandType;

            var surface = new ReflectionSurface("VefToggleRClickHandler");
            surface.Supplied("VEF.AI.Command_ToggleWithRClick", commandType);
            rightClickActionField = surface.Field(commandType, "rightClickAction");
            ready = surface.Ready;
        }

        public override bool TryGetExtraOptions(Gizmo gizmo, List<FloatMenuOption> options)
        {
            if (!ready || !commandType.IsInstanceOfType(gizmo))
                return false;

            // Vanilla's Command.GizmoOnGUIInt rejects every click on a disabled
            // command with a reject-input message and GizmoState.Mouseover
            // (decompiled Verse/Command.cs lines 218-227) before ProcessInput
            // ever runs, so a sighted player cannot right-click into the hidden
            // rightClickAction branch while the command is disabled. Offering
            // the toggle here would exceed sighted parity.
            if (gizmo.Disabled)
                return false;

            try
            {
                if (!(rightClickActionField.GetValue(gizmo) is Action))
                    return false;

                string label = "RimWorldAccess.Compat.Vef.AlternateAction".Translate();
                options.Add(new FloatMenuOption(label, () => ExecuteAlternateAction(gizmo)));
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VefToggleRClickHandler.TryGetExtraOptions failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Vehicle A: rides Command_ToggleWithRClick's own ProcessInput override
        /// (its `ev.button == 1` branch runs rightClickAction) rather than
        /// invoking the delegate field directly, so any future override logic
        /// stays honored.
        /// </summary>
        private void ExecuteAlternateAction(Gizmo gizmo)
        {
            try
            {
                var ev = new Event { button = 1 };
                gizmo.ProcessInput(ev);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VefToggleRClickHandler alternate action failed: {ex.Message}");
            }
        }

        // Everything else DECLINES: ToggleGizmoHandler on the vanilla
        // Command_Toggle base covers toggle state + execution.
    }
}
