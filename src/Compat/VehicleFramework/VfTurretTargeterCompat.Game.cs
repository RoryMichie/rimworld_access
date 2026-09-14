using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Keyboard support for Vehicle Framework's Vehicles.TurretTargeter — a BaseTargeter singleton
    /// parallel to vanilla's Verse.Targeter that vanilla targeting code never touches (VF's
    /// rotatable turret gizmo calls TurretTargeter.BeginTargeting directly, not
    /// Find.Targeter.BeginTargeting). Enter-confirm contract: virtual-cursor target resolution,
    /// the GenUI.TargetsAt MouseCell-fallback GOTCHA (force thingsOnly:true, do the cell fallback
    /// ourselves), and keep-open-on-failure (speak + Event.current.Use() + return false WITHOUT
    /// stopping). Registers a liveness probe with ExternalMapTargeting so shell code that stands
    /// down for "any map targeting session" also stands down for this one. Harmony patching is
    /// manual and applied only when every member resolves; a missing member declines the whole
    /// compat rather than patching partially.
    /// </summary>
    internal static class VfTurretTargeterCompat
    {
        private static Type turretTargeterType;
        private static PropertyInfo instanceProperty;      // static TurretTargeter Instance
        private static PropertyInfo turretStaticProperty;  // static VehicleTurret Turret
        private static PropertyInfo isTargetingProperty;   // instance bool IsTargeting
        private static FieldInfo actionField;               // private Action<LocalTargetInfo> action
        private static FieldInfo targetParamsField;          // private TargetingParameters targetParams
        private static PropertyInfo targeterValidProperty;  // private bool TargeterValid
        private static MethodInfo stopTargetingBoolMethod;   // StopTargeting(bool canceled)
        private static MethodInfo processInputEventsMethod;
        private static MethodInfo beginTargetingMethod;

        // VehicleTurret members needed for the confirm gate + started announcement.
        private static PropertyInfo turretMaxRangeProperty;
        private static PropertyInfo turretMinRangeProperty; // optional — absence must not clear ready
        private static FieldInfo turretDefField;             // VehicleTurretDef : Def
        private static FieldInfo defRawMaxRangeField;         // VehicleTurretDef.maxRange (raw, pre-DefaultMaxRange substitution)

        private static MethodInfo targetMeetsRequirementsMethod; // TargetingHelper.TargetMeetsRequirements

        private static bool ready;

        public static void Register(Harmony harmony)
        {
            try
            {
                var surface = new ReflectionSurface("VfTurretTargeterCompat");

                turretTargeterType = surface.Type("Vehicles.TurretTargeter");

                instanceProperty = surface.Property(turretTargeterType, "Instance");
                turretStaticProperty = surface.Property(turretTargeterType, "Turret");
                isTargetingProperty = surface.Property(turretTargeterType, "IsTargeting");
                actionField = surface.Field(turretTargeterType, "action");
                targetParamsField = surface.Field(turretTargeterType, "targetParams");
                targeterValidProperty = surface.Property(turretTargeterType, "TargeterValid");
                stopTargetingBoolMethod = surface.Method(turretTargeterType, "StopTargeting", new[] { typeof(bool) });
                processInputEventsMethod = surface.Method(turretTargeterType, "ProcessInputEvents");

                Type turretType = turretStaticProperty?.PropertyType;

                beginTargetingMethod = turretType != null
                    ? surface.Method(turretTargeterType, "BeginTargeting",
                        new[] { typeof(TargetingParameters), typeof(Action<LocalTargetInfo>), turretType, typeof(Action), typeof(Texture2D) })
                    : null;

                turretMaxRangeProperty = surface.Property(turretType, "MaxRange");
                turretMinRangeProperty = turretType != null ? AccessTools.Property(turretType, "MinRange") : null;
                turretDefField = surface.Field(turretType, "def");

                // VehicleTurret.MaxRange resolves to VF's DefaultMaxRange constant (9999) whenever
                // the raw def.maxRange was left <= 0 (unset in XML), and VF's own InRange() treats
                // that resolved-to-constant case as "unlimited". Reading the raw field and checking
                // <= 0 mirrors that branch instead of comparing the resolved value against a
                // literal, which would misclassify a modded turret with a finite maxRange of 9999+.
                Type turretDefType = turretDefField?.FieldType;
                defRawMaxRangeField = surface.Field(turretDefType, "maxRange");

                Type targetingHelperType = surface.Type("Vehicles.TargetingHelper");
                targetMeetsRequirementsMethod = turretType != null
                    ? surface.Method(targetingHelperType, "TargetMeetsRequirements",
                        new[] { turretType, typeof(LocalTargetInfo), typeof(IntVec3).MakeByRefType() })
                    : null;

                ready = surface.Ready;
                if (!ready)
                    return;

                ExternalMapTargeting.Register(IsTurretTargeterActive);

                harmony.Patch(processInputEventsMethod,
                    prefix: new HarmonyMethod(typeof(VfTurretTargeterCompat), nameof(ProcessInputEventsPrefix)));
                harmony.Patch(beginTargetingMethod,
                    postfix: new HarmonyMethod(typeof(VfTurretTargeterCompat), nameof(BeginTargetingPostfix)));
                harmony.Patch(stopTargetingBoolMethod,
                    postfix: new HarmonyMethod(typeof(VfTurretTargeterCompat), nameof(StopTargetingPostfix)));

            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorld Access] VF turret targeter compat registration failed: {ex.Message}");
                ready = false;
            }
        }

        private static bool IsTurretTargeterActive()
        {
            if (!ready)
                return false;
            object instance = instanceProperty.GetValue(null);
            return instance != null && (bool)isTargetingProperty.GetValue(instance);
        }

        /// <summary>
        /// Prefix on TurretTargeter.ProcessInputEvents. Enter at the virtual map
        /// cursor becomes a target confirm; every other event (mouse, right-click
        /// cancel, Escape) returns true so VF's own body — which owns those —
        /// keeps running unclaimed.
        /// </summary>
        public static bool ProcessInputEventsPrefix(object __instance)
        {
            if (!ready || __instance == null)
                return true;

            try
            {
                // Vehicle B: honor TargeterValid (vehicle/turret/selection sanity) before touching
                // IsTargeting — TurretTargeter validates lazily inside this same method
                // (StopTargeting(true) when invalid), so an Enter on a stale-but-still-IsTargeting
                // frame must defer to the original body rather than confirm against a dead caster.
                if (!(bool)targeterValidProperty.GetValue(__instance))
                    return true;

                if (!(bool)isTargetingProperty.GetValue(__instance))
                    return true;

                var action = actionField.GetValue(__instance) as Action<LocalTargetInfo>;
                if (action == null)
                    return true;

                object turretObj = turretStaticProperty.GetValue(null);
                if (turretObj == null)
                    return true;

                LocalTargetInfo target;
                switch (KeyboardTargeterConfirm.TryResolve(
                    targetParamsField.GetValue(__instance) as TargetingParameters,
                    out target, out _))
                {
                    case KeyboardTargeterConfirm.Outcome.NotOurs:
                        return true;
                    case KeyboardTargeterConfirm.Outcome.InvalidPosition:
                        return false;
                }

                // Vehicle B: TargetingHelper.TargetMeetsRequirements is the exact gate
                // TurretTargeter's own mouse-confirm branch calls (range, angle restriction,
                // line of sight).
                object[] requirementsArgs = { turretObj, target, null };
                bool meetsRequirements = (bool)targetMeetsRequirementsMethod.Invoke(null, requirementsArgs);

                if (!meetsRequirements)
                {
                    TolkHelper.Speak("RimWorldAccess.Compat.Vf.TurretTargetInvalid".Loc(), SpeechPriority.High);
                    Event.current.Use();
                    return false; // Keep-open-on-failure: session stays live for retry.
                }

                // Mirrors TurretTargeter.ProcessInputEvents' own mouse-confirm branch.
                action(target);
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
                stopTargetingBoolMethod.Invoke(__instance, new object[] { false });

                string targetLabel = target.HasThing
                    ? target.Thing.LabelShort
                    : (string)"RimWorldAccess.Combat.Target.GenericLocationLabel".Translate();
                TolkHelper.Speak("RimWorldAccess.Compat.Vf.TurretTargetConfirmed".Loc(targetLabel));
                Event.current.Use();
                return false;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfTurretTargeterCompat.ProcessInputEventsPrefix failed: {ex.Message}");
                return true;
            }
        }

        /// <summary>
        /// Postfix on the static BeginTargeting: announces the same information the
        /// drawn range ring/angle lines show (TurretTargeter.DrawTargeter /
        /// VehicleTurret.DrawTargeter), since a screen reader user can't see them.
        /// </summary>
        public static void BeginTargetingPostfix(object turret)
        {
            if (!ready || turret == null)
                return;

            try
            {
                object defObj = turretDefField.GetValue(turret);
                string turretLabel = (defObj as Def)?.label ?? "";

                float maxRange = (float)turretMaxRangeProperty.GetValue(turret);
                float minRange = 0f;
                if (turretMinRangeProperty != null && turretMinRangeProperty.GetValue(turret) is float minRangeValue)
                    minRange = minRangeValue;

                // The raw-unset condition behind VF's own "unlimited" gate (see Register).
                float rawMaxRange = (float)defRawMaxRangeField.GetValue(defObj);

                string rangeClause;
                if (rawMaxRange <= 0f)
                    rangeClause = "RimWorldAccess.Compat.Vf.TurretRangeUnlimited".Translate();
                else if (minRange > 0f)
                    rangeClause = "RimWorldAccess.Compat.Vf.TurretRangeMinMax".Translate(minRange.ToString("F1"), maxRange.ToString("F1"));
                else
                    rangeClause = "RimWorldAccess.Compat.Vf.TurretRangeMax".Translate(maxRange.ToString("F1"));

                TolkHelper.Speak("RimWorldAccess.Compat.Vf.TurretTargetingStarted".Loc(turretLabel, rangeClause));
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfTurretTargeterCompat.BeginTargetingPostfix failed: {ex.Message}");
            }
        }

        /// <summary>Postfix on StopTargeting(bool canceled): announces a cancel (Escape/right-click). The success path already announced from the prefix above.</summary>
        public static void StopTargetingPostfix(bool canceled)
        {
            if (canceled)
                TolkHelper.Speak("RimWorldAccess.Compat.Vf.TurretTargetingCancelled".Loc());
        }
    }
}
