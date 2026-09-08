using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Gizmo handler for MVCF.Commands.Command_VerbTargetExtended (Multiverse Combat Framework's
    /// multi-verb weapon command). The type derives from vanilla Command_VerbTarget, so `verb` is
    /// compile-time accessible and its ammo status and ammo-type right-click options already ride
    /// the existing CommandGizmoHandler status facet and the vanilla RightClickFloatMenuOptions
    /// path; no facets are added for those.
    ///
    /// This handler closes MVCF's two hidden-widget interactions instead: the per-part
    /// auto-reload checkbox (CommandPart_Reloadable.DrawExtraGUIButtons) and the integrated-verb
    /// auto-use checkbox (DrawUtility.DrawToggle).
    ///
    /// Every member is resolved against the Type VefGizmoCompat hands in, plus one TypeByName for
    /// the optional Reloading feature's CommandPart, and every facet declines rather than throws
    /// on a failed resolution, read, or invoke.
    /// </summary>
    internal sealed class MvcfVerbTargetHandler : GizmoHandlerBase
    {
        private readonly Type commandType;
        private readonly FieldInfo managedVerbField;
        private readonly FieldInfo groupedVerbsField;
        private readonly PropertyInfo partsProperty;
        private readonly MethodInfo getToggleTypeMethod;
        private readonly MethodInfo getToggleStatusMethod;
        private readonly MethodInfo toggleMethod;
        private readonly bool autoUseReady;

        // Reloading is an optional MVCF feature; these stay null (and the
        // auto-reload option silently declines) when it is disabled.
        private readonly Type commandPartReloadableType;
        private readonly FieldInfo reloadableField;
        private readonly FieldInfo autoReloadField;

        public MvcfVerbTargetHandler(Type commandVerbTargetExtendedType)
        {
            commandType = commandVerbTargetExtendedType;

            var surface = new ReflectionSurface("MvcfVerbTargetHandler");
            surface.Supplied("MVCF.Commands.Command_VerbTargetExtended", commandType);

            managedVerbField = surface.Field(commandType, "managedVerb");
            partsProperty = surface.Property(commandType, "Parts");

            Type managedVerbType = managedVerbField?.FieldType;
            getToggleTypeMethod = surface.Method(managedVerbType, "GetToggleType");
            getToggleStatusMethod = surface.Method(managedVerbType, "GetToggleStatus");
            toggleMethod = surface.Method(managedVerbType, "Toggle");

            autoUseReady = surface.Ready;

            // Tolerated missing: ToggleAutoUse skips the grouped pass and still
            // toggles the primary verb, so this stays out of the surface.
            groupedVerbsField = AccessTools.Field(commandType, "groupedVerbs");

            commandPartReloadableType = AccessTools.TypeByName("MVCF.Reloading.Comps.CommandPart_Reloadable");
            if (commandPartReloadableType != null)
            {
                reloadableField = AccessTools.Field(commandPartReloadableType, "Reloadable");
                Type verbCompReloadableType = reloadableField?.FieldType;
                if (verbCompReloadableType != null)
                    autoReloadField = AccessTools.Field(verbCompReloadableType, "AutoReload");
            }
        }

        public override bool TryGetExtraOptions(Gizmo gizmo, List<FloatMenuOption> options)
        {
            if (!commandType.IsInstanceOfType(gizmo))
                return false;

            // Vanilla rejects every click on a disabled command (24a9f3b rule) —
            // previously enforced for this handler only by ExecuteRightClick's
            // chain-level block, which now consults handler extras on disabled
            // gizmos so parity exceptions (VF's turret quota button) can survive.
            if (gizmo.Disabled)
                return false;

            bool any = false;

            try
            {
                if (TryAddAutoReloadOption(gizmo, options))
                    any = true;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"MvcfVerbTargetHandler auto-reload option failed: {ex.Message}");
            }

            try
            {
                if (TryAddAutoUseOption(gizmo, options))
                    any = true;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"MvcfVerbTargetHandler auto-use option failed: {ex.Message}");
            }

            return any;
        }

        /// <summary>
        /// Mirrors CommandPart_Reloadable.DrawExtraGUIButtons (VerbComp_Reloadable.cs
        /// lines 121-132): walks the command's Parts for a CommandPart_Reloadable
        /// and surfaces its AutoReload checkbox. Declines silently when the
        /// Reloading feature is disabled (commandPartReloadableType unresolved).
        /// </summary>
        private bool TryAddAutoReloadOption(Gizmo gizmo, List<FloatMenuOption> options)
        {
            if (commandPartReloadableType == null || reloadableField == null || autoReloadField == null)
                return false;

            if (!(partsProperty.GetValue(gizmo) is IEnumerable parts))
                return false;

            foreach (object part in parts)
            {
                if (!commandPartReloadableType.IsInstanceOfType(part))
                    continue;

                object reloadableObj = reloadableField.GetValue(part);
                if (reloadableObj == null)
                    return false;

                bool autoReload = (bool)autoReloadField.GetValue(reloadableObj);
                string label = (autoReload
                    ? "RimWorldAccess.Compat.Mvcf.DisableAutoReload"
                    : "RimWorldAccess.Compat.Mvcf.EnableAutoReload").Translate();

                options.Add(new FloatMenuOption(label, () => ToggleAutoReload(reloadableObj)));
                return true;
            }
            return false;
        }

        private void ToggleAutoReload(object reloadableObj)
        {
            try
            {
                bool newState = !(bool)autoReloadField.GetValue(reloadableObj);
                // MUTATION-C: mirrors MVCF CommandPart_Reloadable.DrawExtraGUIButtons
                // (raw bool flip on checkbox click); no gated vanilla method exists —
                // the checkbox body IS the mutation.
                autoReloadField.SetValue(reloadableObj, newState);

                TolkHelper.Speak((newState
                    ? "RimWorldAccess.Compat.Mvcf.AutoReloadOn"
                    : "RimWorldAccess.Compat.Mvcf.AutoReloadOff").Loc());
            }
            catch (Exception ex)
            {
                ModLogger.Error($"MvcfVerbTargetHandler auto-reload toggle failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Mirrors DrawUtility.DrawToggle's gates (lines 32-51) for the
        /// integrated-verb toggle: caster is a player pawn and
        /// managedVerb.GetToggleType() == Integrated (compared by ToString() to
        /// avoid pulling in the MVCF enum type). verb comes from the vanilla
        /// Command_VerbTarget base, so no reflection is needed for the gates.
        /// </summary>
        private bool TryAddAutoUseOption(Gizmo gizmo, List<FloatMenuOption> options)
        {
            if (!autoUseReady)
                return false;

            if (!(gizmo is Command_VerbTarget baseCmd) || baseCmd.verb == null)
                return false;

            Verb verb = baseCmd.verb;
            if (!verb.CasterIsPawn || verb.CasterPawn.Faction != Faction.OfPlayer)
                return false;

            object managedVerbObj = managedVerbField.GetValue(gizmo);
            if (managedVerbObj == null)
                return false;

            object toggleType = getToggleTypeMethod.Invoke(managedVerbObj, null);
            if (toggleType?.ToString() != "Integrated")
                return false;

            bool status = (bool)getToggleStatusMethod.Invoke(managedVerbObj, null);
            string label = (status
                ? "RimWorldAccess.Compat.Mvcf.DisableAutoUse"
                : "RimWorldAccess.Compat.Mvcf.EnableAutoUse").Translate();

            options.Add(new FloatMenuOption(label, () => ToggleAutoUse(gizmo, managedVerbObj)));
            return true;
        }

        /// <summary>
        /// Vehicle A: invokes the same public ManagedVerb.Toggle() the widget
        /// calls, on every grouped verb then the primary one, verbatim the
        /// DrawToggle body (minus its own Event.current.Use(), which only
        /// matters to the mouse draw loop).
        /// </summary>
        private void ToggleAutoUse(Gizmo gizmo, object managedVerbObj)
        {
            try
            {
                if (groupedVerbsField?.GetValue(gizmo) is IEnumerable groupedVerbs)
                {
                    foreach (object mv in groupedVerbs)
                        toggleMethod.Invoke(mv, null);
                }
                toggleMethod.Invoke(managedVerbObj, null);

                bool status = (bool)getToggleStatusMethod.Invoke(managedVerbObj, null);
                TolkHelper.Speak((status
                    ? "RimWorldAccess.Compat.Mvcf.AutoUseOn"
                    : "RimWorldAccess.Compat.Mvcf.AutoUseOff").Loc());
            }
            catch (Exception ex)
            {
                ModLogger.Error($"MvcfVerbTargetHandler auto-use toggle failed: {ex.Message}");
            }
        }
    }
}
