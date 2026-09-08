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
    /// Keyboard support for Vehicle Framework's Vehicles.HaulTargeter — a BaseTargeter singleton
    /// parallel to vanilla's Verse.Targeter that vanilla targeting code never touches (the
    /// vehicle's "Load Pawn" gizmo calls HaulTargeter.BeginTargeting directly, not
    /// Find.Targeter.BeginTargeting). Enter-confirm contract: virtual-cursor target resolution,
    /// the GenUI.TargetsAt MouseCell-fallback GOTCHA (force thingsOnly:true), and
    /// keep-open-on-failure (speak + Event.current.Use() + return false WITHOUT stopping).
    /// Unlike the turret targeter, HaulTargeter is a deliberate multi-pick session — a successful
    /// Enter confirms one target and leaves the session running; only Escape/right-click (owned
    /// entirely by VF's own body) ends it. Registers a liveness probe with ExternalMapTargeting so
    /// shell code that stands down for "any map targeting session" also stands down for this one.
    /// Harmony patching is manual and applied only when every member resolves; a missing member
    /// declines the whole compat rather than patching partially.
    /// </summary>
    internal static class VfHaulTargeterCompat
    {
        private static Type haulTargeterType;
        private static PropertyInfo instanceProperty;             // static HaulTargeter Instance
        private static PropertyInfo isTargetingProperty;          // instance bool IsTargeting
        private static FieldInfo actionField;                     // private Action<LocalTargetInfo> action
        private static FieldInfo targetParamsField;                // private TargetingParameters targetParams
        private static FieldInfo vehicleField;                     // protected VehiclePawn vehicle (declared on BaseTargeter)
        private static MethodInfo targetMeetsRequirementsMethod;   // instance bool TargetMeetsRequirements(LocalTargetInfo)
        private static MethodInfo confirmStillValidMethod;         // private instance void ConfirmStillValid()
        private static MethodInfo stopTargetingMethod;             // instance void StopTargeting()
        private static MethodInfo processInputEventsMethod;
        private static MethodInfo beginTargetingMethod;

        private static bool ready;

        public static void Register(Harmony harmony)
        {
            try
            {
                var surface = new ReflectionSurface("VfHaulTargeterCompat");

                haulTargeterType = surface.Type("Vehicles.HaulTargeter");

                instanceProperty = surface.Property(haulTargeterType, "Instance");
                isTargetingProperty = surface.Property(haulTargeterType, "IsTargeting");
                actionField = surface.Field(haulTargeterType, "action");
                targetParamsField = surface.Field(haulTargeterType, "targetParams");
                targetMeetsRequirementsMethod = surface.Method(haulTargeterType, "TargetMeetsRequirements", new[] { typeof(LocalTargetInfo) });
                confirmStillValidMethod = surface.Method(haulTargeterType, "ConfirmStillValid");
                stopTargetingMethod = surface.Method(haulTargeterType, "StopTargeting", Type.EmptyTypes);
                processInputEventsMethod = surface.Method(haulTargeterType, "ProcessInputEvents");

                // "vehicle" is declared on the BaseTargeter base class; AccessTools.Field resolves
                // inherited fields, but fall back to an explicit base-type lookup just in case.
                vehicleField = AccessTools.Field(haulTargeterType, "vehicle")
                    ?? surface.Field(surface.Type("Vehicles.BaseTargeter"), "vehicle");

                Type vehiclePawnType = surface.Supplied("Vehicles.VehiclePawn", VfVehiclePawn.VehiclePawnType);
                beginTargetingMethod = vehiclePawnType != null
                    ? surface.Method(haulTargeterType, "BeginTargeting",
                        new[] { typeof(TargetingParameters), typeof(Action<LocalTargetInfo>), vehiclePawnType, typeof(Action), typeof(Texture2D) })
                    : null;

                ready = surface.Ready;
                if (!ready)
                    return;

                ExternalMapTargeting.Register(IsHaulTargeterActive);

                harmony.Patch(processInputEventsMethod,
                    prefix: new HarmonyMethod(typeof(VfHaulTargeterCompat), nameof(ProcessInputEventsPrefix)));
                harmony.Patch(beginTargetingMethod,
                    postfix: new HarmonyMethod(typeof(VfHaulTargeterCompat), nameof(BeginTargetingPostfix)));
                harmony.Patch(stopTargetingMethod,
                    prefix: new HarmonyMethod(typeof(VfHaulTargeterCompat), nameof(StopTargetingPrefix)),
                    postfix: new HarmonyMethod(typeof(VfHaulTargeterCompat), nameof(StopTargetingPostfix)));

                Log.Message("[RimWorld Access] VF compat: registered haul targeter keyboard support");
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorld Access] VF haul targeter compat registration failed: {ex.Message}");
                ready = false;
            }
        }

        private static bool IsHaulTargeterActive()
        {
            if (!ready)
                return false;
            object instance = instanceProperty.GetValue(null);
            return instance != null && (bool)isTargetingProperty.GetValue(instance);
        }

        /// <summary>
        /// Enter at the virtual map cursor confirms a target; every other event (mouse,
        /// right-click cancel, Escape) returns true so VF's own body — which owns those — keeps
        /// running unclaimed. A confirmed target does NOT stop the session.
        /// </summary>
        public static bool ProcessInputEventsPrefix(object __instance)
        {
            if (!ready || __instance == null)
                return true;

            try
            {
                // Vehicle B: HaulTargeter validates its own session lazily at the top of this same
                // method (stopping stale sessions), so run that gate before trusting IsTargeting.
                confirmStillValidMethod.Invoke(__instance, null);

                if (!(bool)isTargetingProperty.GetValue(__instance))
                    return true;

                var action = actionField.GetValue(__instance) as Action<LocalTargetInfo>;
                if (action == null)
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

                // HaulTargeter only ever accepts Things (TargetMeetsRequirements requires
                // HasThing), so the helper's cell fallback can never be a pick here.
                if (!target.HasThing)
                {
                    TolkHelper.Speak("RimWorldAccess.Compat.Vf.HaulTargetInvalid".Loc(), SpeechPriority.High);
                    Event.current.Use();
                    return false;
                }

                // Vehicle B: the exact gate HaulTargeter's own mouse-confirm branch calls
                // (pawn or haulable, spawned, not destroyed).
                bool meetsRequirements = (bool)targetMeetsRequirementsMethod.Invoke(__instance, new object[] { target });
                if (!meetsRequirements)
                {
                    TolkHelper.Speak("RimWorldAccess.Compat.Vf.HaulTargetInvalid".Loc(), SpeechPriority.High);
                    Event.current.Use();
                    return false; // Keep-open-on-failure: session stays live for retry.
                }

                // Mirrors HaulTargeter.ProcessInputEvents' own mouse-confirm branch. Deliberately
                // does NOT stop targeting -- multiple picks are the point of this targeter.
                action(target);
                SoundDefOf.Tick_High.PlayOneShotOnCamera();

                TolkHelper.Speak("RimWorldAccess.Compat.Vf.HaulTargetConfirmed".Loc(target.Thing.LabelShort));
                Event.current.Use();
                return false;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfHaulTargeterCompat.ProcessInputEventsPrefix failed: {ex.Message}");
                return true;
            }
        }

        /// <summary>
        /// Announces what the drawn mouse attachment shows sighted players. The parameter name
        /// must match VF's own BeginTargeting parameter ("vehicle") -- Harmony binds by name.
        /// </summary>
        public static void BeginTargetingPostfix(object vehicle)
        {
            if (!ready)
                return;

            try
            {
                string vehicleLabel = (vehicle as Pawn)?.LabelShortCap ?? "";
                TolkHelper.Speak("RimWorldAccess.Compat.Vf.HaulTargetingStarted".Loc(vehicleLabel));
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfHaulTargeterCompat.BeginTargetingPostfix failed: {ex.Message}");
            }
        }

        /// <summary>
        /// StopTargeting() runs both for real session ends and for already-dead sessions
        /// (action already null, including ConfirmStillValid's own auto-stop calling it),
        /// and it takes no bool to distinguish success from cancel. The prefix captures
        /// whether the session was actually live before stopping; the postfix announces
        /// exactly once, only when it was.
        /// </summary>
        public static void StopTargetingPrefix(object __instance, ref bool __state)
        {
            __state = false;
            if (!ready || __instance == null)
                return;

            try
            {
                __state = (bool)isTargetingProperty.GetValue(__instance);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfHaulTargeterCompat.StopTargetingPrefix failed: {ex.Message}");
            }
        }

        /// <summary>No canceled/confirmed distinction exists here -- one neutral "ended" line is correct, since every successful pick already announced itself from the prefix above.</summary>
        public static void StopTargetingPostfix(bool __state)
        {
            if (__state)
                TolkHelper.Speak("RimWorldAccess.Compat.Vf.HaulTargetingEnded".Loc());
        }
    }
}
