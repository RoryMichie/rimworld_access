using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Gizmo handler for Vehicle Framework's Vehicles.Rendering.Command_CooldownAction (every VF
    /// vehicle turret's fire gizmo) and its subclass Command_TargeterCooldownAction (rotatable
    /// turrets that BeginTargeting instead of firing at a fixed max-range point) — registered on
    /// the base type only, since the registry's byType chain walk covers the subclass. Both extend
    /// the real Verse Command, so Disabled/disabledReason/Desc already work through the base
    /// chain; this handler closes the gaps DrawGizmoButton/DrawTopBar/DrawBottomBar add:
    /// ammo/reload/heat/cooldown/target status, the halt sub-icon and four SubGizmos row, and the
    /// auto-load quota configure button (feature-flag-false branch — Dialog_ConfigureTurret is
    /// dead code on installed release builds). Enter (TryExecute) only intercepts the two
    /// silent-click cases the drawn button swallows without a Message (on cooldown, still
    /// reloading); everything else falls through to the generic fallback's gizmo.ProcessInput, the
    /// same vanilla vehicle the drawn click uses.
    /// </summary>
    internal sealed class VfTurretGizmoHandler : GizmoHandlerBase
    {
        private readonly Type cooldownActionType;

        // Command_Turret fields (declared on the base type; inherited by both
        // Command_CooldownAction and Command_TargeterCooldownAction).
        private readonly FieldInfo vehicleField; // VehiclePawn : Pawn
        private readonly FieldInfo turretField;  // VehicleTurret (VF-only)

        // VehicleTurret members.
        private readonly FieldInfo defField;            // VehicleTurretDef : Def
        private readonly FieldInfo shellCountField;      // int
        private readonly FieldInfo loadedAmmoField;      // ThingDef
        private readonly FieldInfo currentHeatRateField; // float
        private readonly FieldInfo targetInfoField;      // LocalTargetInfo
        private readonly FieldInfo autoTargetingField;   // bool
        private readonly PropertyInfo canOverheatProperty;
        private readonly PropertyInfo onCooldownProperty;
        private readonly PropertyInfo reloadTicksProperty;
        private readonly PropertyInfo maxTicksProperty;
        private readonly PropertyInfo currentFireModeProperty; // FireMode (VF-only)
        private readonly PropertyInfo autoTargetProperty;      // bool
        private readonly PropertyInfo subGizmosProperty;       // IEnumerable<SubGizmo>
        private readonly FieldInfo maxHeatCapacityField;       // const float, static
        private readonly MethodInfo cycleFireModeMethod;
        private readonly MethodInfo switchAutoTargetMethod;
        private readonly MethodInfo tryClearChamberMethod;
        private readonly MethodInfo setTargetMethod; // (LocalTargetInfo)

        // VehicleTurret.SubGizmo nested record members.
        private readonly FieldInfo subGizmoCanClickField; // Func<bool>
        private readonly FieldInfo subGizmoOnClickField;  // Action

        // FireMode members (resolved from CurrentFireMode's declared return type).
        private readonly FieldInfo fireModeLabelField; // string

        // VehicleTurretDef members.
        private readonly FieldInfo magazineCapacityField; // int
        private readonly FieldInfo ammunitionField;        // ThingFilter

        // VehiclePawn.CompVehicleTurrets + CompVehicleTurrets members, for the
        // quota-configure sub-option (VehiclePawn.GetStatValue(VehicleStatDef) is
        // also VF-only — VehicleStatDef does not derive from the real Verse StatDef).
        private readonly PropertyInfo compVehicleTurretsProperty;
        private readonly MethodInfo setQuotaLevelMethod;
        private readonly MethodInfo getQuotaLevelMethod;
        private readonly MethodInfo getVehicleStatValueMethod; // VehiclePawn.GetStatValue(VehicleStatDef)
        private readonly object cargoCapacityStatDef;          // VehicleStatDefOf.CargoCapacity

        private readonly bool ready;

        internal bool Ready => ready;

        public VfTurretGizmoHandler(Type cooldownActionType)
        {
            this.cooldownActionType = cooldownActionType;

            var surface = new ReflectionSurface("VfTurretGizmoHandler");

            vehicleField = surface.Field(cooldownActionType, "vehicle");
            turretField = surface.Field(cooldownActionType, "turret");

            Type turretType = turretField?.FieldType;
            defField = surface.Field(turretType, "def");
            shellCountField = surface.Field(turretType, "shellCount");
            loadedAmmoField = surface.Field(turretType, "loadedAmmo");
            currentHeatRateField = surface.Field(turretType, "currentHeatRate");
            targetInfoField = surface.Field(turretType, "targetInfo");
            autoTargetingField = surface.Field(turretType, "autoTargeting");
            canOverheatProperty = surface.Property(turretType, "CanOverheat");
            onCooldownProperty = surface.Property(turretType, "OnCooldown");
            reloadTicksProperty = surface.Property(turretType, "ReloadTicks");
            maxTicksProperty = surface.Property(turretType, "MaxTicks");
            currentFireModeProperty = surface.Property(turretType, "CurrentFireMode");
            autoTargetProperty = surface.Property(turretType, "AutoTarget");
            subGizmosProperty = surface.Property(turretType, "SubGizmos");
            maxHeatCapacityField = surface.Field(turretType, "MaxHeatCapacity");
            cycleFireModeMethod = surface.Method(turretType, "CycleFireMode");
            switchAutoTargetMethod = surface.Method(turretType, "SwitchAutoTarget");
            tryClearChamberMethod = surface.Method(turretType, "TryClearChamber");
            setTargetMethod = turretType != null
                ? surface.Method(turretType, "SetTarget", new[] { typeof(LocalTargetInfo) })
                : null;

            Type subGizmosReturnType = subGizmosProperty?.PropertyType;
            Type subGizmoType = subGizmosReturnType != null && subGizmosReturnType.IsGenericType
                ? subGizmosReturnType.GetGenericArguments().FirstOrDefault()
                : null;
            subGizmoCanClickField = surface.Field(subGizmoType, "canClick");
            subGizmoOnClickField = surface.Field(subGizmoType, "onClick");

            fireModeLabelField = surface.Field(currentFireModeProperty?.PropertyType, "label");

            Type turretDefType = defField?.FieldType;
            magazineCapacityField = surface.Field(turretDefType, "magazineCapacity");
            ammunitionField = surface.Field(turretDefType, "ammunition");

            Type vehiclePawnType = vehicleField?.FieldType;
            compVehicleTurretsProperty = surface.Property(vehiclePawnType, "CompVehicleTurrets");
            Type compVehicleTurretsType = compVehicleTurretsProperty?.PropertyType;
            setQuotaLevelMethod = turretType != null
                ? surface.Method(compVehicleTurretsType, "SetQuotaLevel", new[] { turretType, typeof(int) })
                : null;
            getQuotaLevelMethod = turretType != null
                ? surface.Method(compVehicleTurretsType, "GetQuotaLevel", new[] { turretType })
                : null;

            Type vehicleStatDefOfType = surface.Type("Vehicles.VehicleStatDefOf");
            FieldInfo cargoCapacityDefOfField = surface.Field(vehicleStatDefOfType, "CargoCapacity");
            cargoCapacityStatDef = cargoCapacityDefOfField?.GetValue(null);
            Type vehicleStatDefType = cargoCapacityDefOfField?.FieldType;
            getVehicleStatValueMethod = vehicleStatDefType != null
                ? surface.Method(vehiclePawnType, "GetStatValue", new[] { vehicleStatDefType })
                : null;

            ready = surface.Ready && subGizmoType != null && cargoCapacityStatDef != null;
        }

        /// <summary>Shared gate + lookup: resolves the turret, its owning vehicle, and its def.</summary>
        private bool Applies(Gizmo gizmo, out object turretObj, out object vehicleObj, out object defObj)
        {
            turretObj = null;
            vehicleObj = null;
            defObj = null;

            if (!ready || !cooldownActionType.IsInstanceOfType(gizmo))
                return false;

            turretObj = turretField.GetValue(gizmo);
            if (turretObj == null)
                return false;

            vehicleObj = vehicleField.GetValue(gizmo);
            defObj = defField.GetValue(turretObj);
            return defObj != null;
        }

        public override bool TryGetStatus(Gizmo gizmo, out string status)
        {
            status = null;
            if (!Applies(gizmo, out object turretObj, out object _, out object defObj))
                return false;

            try
            {
                var parts = new List<string>();

                int magazineCapacity = (int)magazineCapacityField.GetValue(defObj);
                int shellCount = (int)shellCountField.GetValue(turretObj);
                parts.Add(magazineCapacity <= 0
                    ? "RimWorldAccess.Compat.Vf.TurretAmmoUnlimited".Translate()
                    : "RimWorldAccess.Compat.Vf.TurretAmmo".Translate(shellCount, magazineCapacity));

                int reloadTicks = (int)reloadTicksProperty.GetValue(turretObj);
                if (reloadTicks > 0)
                {
                    int maxTicks = (int)maxTicksProperty.GetValue(turretObj);
                    int reloadPercent = maxTicks > 0 ? Mathf.RoundToInt(100f * reloadTicks / maxTicks) : 0;
                    parts.Add("RimWorldAccess.Compat.Vf.TurretReloading".Translate(reloadPercent));
                }

                if ((bool)onCooldownProperty.GetValue(turretObj))
                    parts.Add("RimWorldAccess.Compat.Vf.TurretOnCooldown".Translate());

                if ((bool)canOverheatProperty.GetValue(turretObj))
                {
                    float currentHeatRate = (float)currentHeatRateField.GetValue(turretObj);
                    float maxHeatCapacity = (float)maxHeatCapacityField.GetValue(null);
                    int heatPercent = maxHeatCapacity > 0 ? Mathf.RoundToInt(100f * currentHeatRate / maxHeatCapacity) : 0;
                    parts.Add("RimWorldAccess.Compat.Vf.TurretHeat".Translate(heatPercent));
                }

                var targetInfo = (LocalTargetInfo)targetInfoField.GetValue(turretObj);
                if (targetInfo.IsValid)
                {
                    string targetLabel = targetInfo.HasThing
                        ? targetInfo.Thing.LabelShort
                        : (string)"RimWorldAccess.Combat.Target.GenericLocationLabel".Translate();
                    parts.Add("RimWorldAccess.Compat.Vf.TurretTargetLocked".Translate(targetLabel));
                }

                status = string.Join(". ", parts);
                return !string.IsNullOrEmpty(status);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfTurretGizmoHandler.TryGetStatus failed: {ex.Message}");
                status = null;
                return false;
            }
        }

        // No TryGetDescription override: the chain's base Command handler already surfaces Desc
        // and the disabled reason, and claiming this facet here would shadow them.

        public override bool TryExecute(Gizmo gizmo, GizmoHandlerContext ctx, out bool runEpilogue)
        {
            runEpilogue = false;
            if (!Applies(gizmo, out object turretObj, out object _, out object _))
                return false;

            try
            {
                // The two cases DrawGizmoButton's own click check swallows silently (fireTurret is
                // only set when !turret.OnCooldown, and FireTurret's body no-ops when
                // ReloadTicks > 0). A sighted player gets no feedback either, but a screen reader
                // user pressing Enter needs SOME response rather than apparent silence.
                if ((bool)onCooldownProperty.GetValue(turretObj))
                {
                    TolkHelper.Speak("RimWorldAccess.Compat.Vf.TurretFireBlockedCooldown".Loc());
                    return true;
                }

                if ((int)reloadTicksProperty.GetValue(turretObj) > 0)
                {
                    TolkHelper.Speak("RimWorldAccess.Compat.Vf.TurretFireBlockedReloading".Loc());
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfTurretGizmoHandler.TryExecute failed: {ex.Message}");
                return false;
            }
        }

        public override bool TryGetExtraOptions(Gizmo gizmo, List<FloatMenuOption> options)
        {
            if (!Applies(gizmo, out object turretObj, out object vehicleObj, out object defObj))
                return false;

            // The halt sub-icon and the DrawTopBar SubGizmos are all gated on !disabled in VF's own
            // draw code, so a sighted player cannot reach them while disabled either. The
            // quota/configure button is the exception: DrawBottomBar has no disabled check, and it
            // is the sighted player's only way to arm an empty (and therefore disabled) turret.
            bool disabled = gizmo.Disabled;

            try
            {
                bool any = false;

                var targetInfo = (LocalTargetInfo)targetInfoField.GetValue(turretObj);
                if (!disabled && targetInfo.IsValid)
                {
                    string haltLabel = "RimWorldAccess.Compat.Vf.TurretHaltOption".Translate();
                    options.Add(new FloatMenuOption(haltLabel, () => HaltTurret(turretObj)));
                    any = true;
                }

                // Rebuild VehicleTurret.SubGizmos' own yield conditions (magazineCapacity>0 gates
                // remove-ammo [additionally gated on ammunition!=null] and reload; fire mode always
                // yields; autoTargeting gates auto-target) to identify each record by position --
                // the record carries no discriminator, only a draw delegate, a canClick gate, an
                // onClick action and a tooltip.
                int magazineCapacity = (int)magazineCapacityField.GetValue(defObj);
                object ammunitionObj = ammunitionField.GetValue(defObj);
                bool autoTargetingOn = (bool)autoTargetingField.GetValue(turretObj);

                var subGizmoObjs = new List<object>();
                foreach (object subGizmo in (IEnumerable)subGizmosProperty.GetValue(turretObj))
                    subGizmoObjs.Add(subGizmo);

                int idx = 0;
                object removeAmmoGizmo = null;
                object reloadGizmo = null;
                if (magazineCapacity > 0)
                {
                    if (ammunitionObj != null)
                        removeAmmoGizmo = SafeGet(subGizmoObjs, idx++);
                    reloadGizmo = SafeGet(subGizmoObjs, idx++);
                }
                object fireModeGizmo = SafeGet(subGizmoObjs, idx++);
                object autoTargetGizmo = autoTargetingOn ? SafeGet(subGizmoObjs, idx++) : null;

                if (!disabled && removeAmmoGizmo != null && SubGizmoCanClick(removeAmmoGizmo))
                {
                    var loadedAmmo = loadedAmmoField.GetValue(turretObj) as ThingDef;
                    string unloadLabelArg = loadedAmmo != null ? loadedAmmo.LabelCap.ToString() : "";
                    string unloadOptionLabel = "RimWorldAccess.Compat.Vf.TurretUnloadOption".Translate(unloadLabelArg);
                    options.Add(new FloatMenuOption(unloadOptionLabel, () => UnloadTurret(turretObj, loadedAmmo)));
                    any = true;
                }

                if (!disabled && reloadGizmo != null)
                {
                    string reloadOptionLabel = "RimWorldAccess.Compat.Vf.TurretReloadOption".Translate();
                    options.Add(new FloatMenuOption(reloadOptionLabel, () => ReloadTurret(turretObj, defObj, reloadGizmo)));
                    any = true;
                }

                if (!disabled && fireModeGizmo != null && SubGizmoCanClick(fireModeGizmo))
                {
                    string currentFireModeLabel = GetFireModeLabel(turretObj);
                    string fireModeOptionLabel = "RimWorldAccess.Compat.Vf.TurretFireModeOption".Translate(currentFireModeLabel);
                    options.Add(new FloatMenuOption(fireModeOptionLabel, () => CycleFireMode(turretObj)));
                    any = true;
                }

                if (!disabled && autoTargetGizmo != null && SubGizmoCanClick(autoTargetGizmo))
                {
                    bool autoTargetOn = (bool)autoTargetProperty.GetValue(turretObj);
                    string stateWord = autoTargetOn ? "On".Translate() : "Off".Translate();
                    string autoTargetOptionLabel = "RimWorldAccess.Compat.Vf.TurretAutoTargetOption".Translate(stateWord);
                    options.Add(new FloatMenuOption(autoTargetOptionLabel, () => SwitchAutoTarget(turretObj)));
                    any = true;
                }

                if (ammunitionObj != null)
                {
                    string quotaOptionLabel = "RimWorldAccess.Compat.Vf.TurretSetQuotaOption".Translate();
                    options.Add(new FloatMenuOption(quotaOptionLabel, () => OpenSetQuotaDialog(turretObj, vehicleObj, defObj, ammunitionObj)));
                    any = true;
                }

                return any;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfTurretGizmoHandler.TryGetExtraOptions failed: {ex.Message}");
                return false;
            }
        }

        private static object SafeGet(List<object> list, int index) =>
            index >= 0 && index < list.Count ? list[index] : null;

        private bool SubGizmoCanClick(object subGizmoObj)
        {
            var canClick = subGizmoCanClickField.GetValue(subGizmoObj) as Func<bool>;
            return canClick != null && canClick();
        }

        private string GetFireModeLabel(object turretObj)
        {
            object fireMode = currentFireModeProperty.GetValue(turretObj);
            return fireMode != null ? fireModeLabelField.GetValue(fireMode) as string ?? "" : "";
        }

        private void HaltTurret(object turretObj)
        {
            try
            {
                // Mutation A: the exact body the drawn halt sub-icon's click runs.
                setTargetMethod.Invoke(turretObj, new object[] { LocalTargetInfo.Invalid });
                TolkHelper.Speak("RimWorldAccess.Compat.Vf.TurretHalted".Loc());
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfTurretGizmoHandler halt failed: {ex.Message}");
            }
        }

        private void UnloadTurret(object turretObj, ThingDef loadedAmmo)
        {
            try
            {
                string unloadedLabel = loadedAmmo != null ? loadedAmmo.LabelCap.ToString() : "";
                // Mutation A: the exact body SubGizmo_RemoveAmmo's onClick runs.
                tryClearChamberMethod.Invoke(turretObj, null);
                TolkHelper.Speak("RimWorldAccess.Compat.Vf.TurretUnloaded".Loc(unloadedLabel));
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfTurretGizmoHandler unload failed: {ex.Message}");
            }
        }

        private void ReloadTurret(object turretObj, object defObj, object reloadGizmoObj)
        {
            try
            {
                int shellCountBefore = (int)shellCountField.GetValue(turretObj);

                // Mutation A: SubGizmo_ReloadFromInventory's own onClick delegate owns the
                // direct-reload / genericAmmo reject-Message / FloatMenu-open branches itself.
                // WindowlessFloatMenuState.IsExecutingOption arms DialogInterceptionPatch so a
                // FloatMenu opened here becomes windowless instead of a real window.
                var onClick = subGizmoOnClickField.GetValue(reloadGizmoObj) as Action;
                onClick?.Invoke();

                int shellCountAfter = (int)shellCountField.GetValue(turretObj);
                if (shellCountAfter > shellCountBefore)
                {
                    int magazineCapacity = (int)magazineCapacityField.GetValue(defObj);
                    TolkHelper.Speak("RimWorldAccess.Compat.Vf.TurretReloaded".Loc(shellCountAfter, magazineCapacity));
                }
                // A shellCount that didn't increase means either nothing to reload (the onClick's
                // own reject Message already spoke, or the magazine was full -- vanilla stays
                // silent there too), or a windowless menu is now open and announces its own pick.
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfTurretGizmoHandler reload failed: {ex.Message}");
            }
        }

        private void CycleFireMode(object turretObj)
        {
            try
            {
                // Mutation A: the exact body SubGizmo_FireMode's onClick runs.
                cycleFireModeMethod.Invoke(turretObj, null);
                string newFireModeLabel = GetFireModeLabel(turretObj);
                TolkHelper.Speak("RimWorldAccess.Compat.Vf.TurretFireModeChanged".Loc(newFireModeLabel));
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfTurretGizmoHandler fire mode cycle failed: {ex.Message}");
            }
        }

        private void SwitchAutoTarget(object turretObj)
        {
            try
            {
                // Mutation A: the exact body SubGizmo_AutoTarget's onClick runs. Reads the
                // resulting state rather than assuming the flip took effect -- SwitchAutoTarget
                // re-checks CanAutoTarget itself and can silently no-op.
                switchAutoTargetMethod.Invoke(turretObj, null);
                bool nowOn = (bool)autoTargetProperty.GetValue(turretObj);
                string stateWord = nowOn ? "On".Translate() : "Off".Translate();
                TolkHelper.Speak("RimWorldAccess.Compat.Vf.TurretAutoTargetChanged".Loc(stateWord));
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfTurretGizmoHandler auto-target switch failed: {ex.Message}");
            }
        }

        /// <summary>
        /// MUTATION-C: mirrors Command_CooldownAction.DrawBottomBar's configure button body,
        /// feature-flag-false branch — Vehicles.Config.FeatureFlags.IsFeatureEnabled(
        /// "BetterAutoLoadConfig") is false on installed release builds, so the
        /// Dialog_ConfigureTurret branch is dead code and deliberately not covered. The button
        /// click itself is inline IMGUI with no invocable vehicle; this reproduces its exact
        /// ammo-def/max-quota math and Dialog_Slider construction, with the same starting value
        /// (GetQuotaLevel) and round-to (5) the drawn button passes.
        /// </summary>
        private void OpenSetQuotaDialog(object turretObj, object vehicleObj, object defObj, object ammunitionObj)
        {
            try
            {
                var filter = (ThingFilter)ammunitionObj;
                var loadedAmmo = loadedAmmoField.GetValue(turretObj) as ThingDef;
                ThingDef ammoDef = loadedAmmo ?? filter.AllowedThingDefs.FirstOrDefault();
                if (ammoDef == null)
                {
                    ModLogger.Error("VfTurretGizmoHandler: no ammo def resolved for the quota dialog.");
                    return;
                }

                if (!(vehicleObj is Pawn vehiclePawn))
                    return;

                float cargoCapacity = (float)getVehicleStatValueMethod.Invoke(vehiclePawn, new[] { cargoCapacityStatDef });
                float ammoMass = ammoDef.GetStatValueAbstract(StatDefOf.Mass);
                int maxQuota = ammoMass > 0f ? Mathf.RoundToInt(cargoCapacity / ammoMass) : 0;

                object compVehicleTurrets = compVehicleTurretsProperty.GetValue(vehiclePawn);
                if (compVehicleTurrets == null)
                    return;

                int currentQuota = (int)getQuotaLevelMethod.Invoke(compVehicleTurrets, new[] { turretObj });

                var slider = new Dialog_Slider(
                    amount => "RimWorldAccess.Compat.Vf.TurretSetQuotaLabel".Translate(amount),
                    0, maxQuota,
                    value => setQuotaLevelMethod.Invoke(compVehicleTurrets, new[] { turretObj, (object)value }),
                    currentQuota, 5f);
                Find.WindowStack.Add(slider);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfTurretGizmoHandler quota dialog failed: {ex.Message}");
            }
        }
    }
}
