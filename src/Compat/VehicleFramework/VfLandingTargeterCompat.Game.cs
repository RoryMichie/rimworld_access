using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorldAccess.Shell;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Keyboard support for Vehicle Framework's Vehicles.LandingTargeter — the map-level
    /// landing-spot picker VF opens as the continuation of an aerial arrival at an existing map,
    /// and for forced landings when a flight runs out of fuel. A BaseTargeter singleton exactly
    /// like Vehicles.TurretTargeter, so this compat has the same shape: manual Harmony patching
    /// only when every member resolves, an ExternalMapTargeting probe registration, and an
    /// Enter-confirm prefix with the keep-open-on-failure contract.
    ///
    /// Escape needs no scope claim: TargetingScope does not claim Escape for map-external
    /// targeters, the shell stands down via ExternalMapTargeting.MapTargetingActive, and VF's own
    /// ProcessInputEvents KeyBindingDefOf.Cancel branch (via its private TryCancelTargeter,
    /// including the ForcedTargeting confirmation dialog the windowless dialog interception
    /// already reads) handles Escape and right-click cancel unmodified.
    ///
    /// Rotation (Q/E, when the landing spot allows it) works the same way: VF's own
    /// HandleRotationShortcuts, called unconditionally at the top of ProcessInputEvents, already
    /// rotates on the real key event, so this compat only observes — the prefix stashes the
    /// pre-call rotation in __state and the postfix announces a changed facing.
    /// </summary>
    internal static class VfLandingTargeterCompat
    {
        private static Type landingTargeterType;
        private static PropertyInfo instanceProperty;     // static LandingTargeter Instance
        private static PropertyInfo isTargetingProperty;  // instance bool IsTargeting
        private static FieldInfo actionField;               // private Action<LocalTargetInfo, Rot4> action
        private static FieldInfo landingRotationField;       // private Rot4 landingRotation
        private static FieldInfo allowRotatingField;         // private bool allowRotating
        private static PropertyInfo forcedTargetingProperty; // public bool ForcedTargeting; OPTIONAL, absence must not clear ready
        private static PropertyInfo pausedProperty;         // private bool Paused
        private static MethodInfo getPosStateMethod;         // public PositionState GetPosState(LocalTargetInfo, bool)
        private static MethodInfo stopTargetingMethod;       // public override void StopTargeting()
        private static MethodInfo tryCancelTargeterMethod;   // private void TryCancelTargeter()
        private static MethodInfo processInputEventsMethod;  // public override void ProcessInputEvents()
        private static MethodInfo beginTargetingMethod;      // public void BeginTargeting(...)

        private static bool ready;

        /// <summary>
        /// Armed around our own action+StopTargeting invoke on Enter-confirm so the StopTargeting
        /// postfix doesn't double-announce a stop we already spoke for. A stop that fires OUTSIDE
        /// that window (Escape, right-click cancel, the ForcedTargeting confirmation dialog) is a
        /// genuinely separate event and still gets announced.
        /// </summary>
        private static bool suppressStopAnnouncement;

        public static void Register(Harmony harmony)
        {
            try
            {
                var surface = new ReflectionSurface("VfLandingTargeterCompat");

                landingTargeterType = surface.Type("Vehicles.LandingTargeter");

                instanceProperty = surface.Property(landingTargeterType, "Instance");
                isTargetingProperty = surface.Property(landingTargeterType, "IsTargeting");
                actionField = surface.Field(landingTargeterType, "action");
                landingRotationField = surface.Field(landingTargeterType, "landingRotation");
                allowRotatingField = surface.Field(landingTargeterType, "allowRotating");
                pausedProperty = surface.Property(landingTargeterType, "Paused");
                getPosStateMethod = surface.Method(landingTargeterType, "GetPosState",
                    new[] { typeof(LocalTargetInfo), typeof(bool) });
                stopTargetingMethod = surface.Method(landingTargeterType, "StopTargeting", Type.EmptyTypes);
                tryCancelTargeterMethod = surface.Method(landingTargeterType, "TryCancelTargeter", Type.EmptyTypes);
                processInputEventsMethod = surface.Method(landingTargeterType, "ProcessInputEvents");
                beginTargetingMethod = surface.Method(landingTargeterType, "BeginTargeting");

                forcedTargetingProperty = landingTargeterType != null
                    ? AccessTools.Property(landingTargeterType, "ForcedTargeting")
                    : null;

                ready = surface.Ready;
                if (!ready)
                    return;

                ExternalMapTargeting.Register(IsLandingTargeterActive);

                harmony.Patch(processInputEventsMethod,
                    prefix: new HarmonyMethod(typeof(VfLandingTargeterCompat), nameof(ProcessInputEventsPrefix)),
                    postfix: new HarmonyMethod(typeof(VfLandingTargeterCompat), nameof(ProcessInputEventsPostfix)));
                harmony.Patch(beginTargetingMethod,
                    postfix: new HarmonyMethod(typeof(VfLandingTargeterCompat), nameof(BeginTargetingPostfix)));
                harmony.Patch(stopTargetingMethod,
                    postfix: new HarmonyMethod(typeof(VfLandingTargeterCompat), nameof(StopTargetingPostfix)));

            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorld Access] VF landing targeter compat registration failed: {ex.Message}");
                ready = false;
            }
        }

        private static bool IsLandingTargeterActive()
        {
            if (!ready)
                return false;
            object instance = instanceProperty.GetValue(null);
            return instance != null && (bool)isTargetingProperty.GetValue(instance);
        }

        /// <summary>
        /// Enter at the virtual map cursor becomes a landing confirm; rotation keys and everything
        /// else return true so VF's own body — which owns those, including HandleRotationShortcuts
        /// and TryCancelTargeter's ForcedTargeting confirmation — keeps running unclaimed. __state
        /// always stashes the pre-call rotation so the postfix can announce a change regardless of
        /// which branch ran.
        /// </summary>
        public static bool ProcessInputEventsPrefix(object __instance, ref Rot4 __state)
        {
            __state = Rot4.Invalid;

            if (!ready || __instance == null)
                return true;

            try
            {
                __state = (Rot4)landingRotationField.GetValue(__instance);

                // The forced-cancel confirmation dialog owns input while paused.
                if ((bool)pausedProperty.GetValue(__instance))
                    return true;

                if (!(bool)isTargetingProperty.GetValue(__instance))
                    return true;

                if (Event.current == null)
                    return true;

                bool allowRotating = (bool)allowRotatingField.GetValue(__instance);
                if (allowRotating && Event.current.type == EventType.KeyDown
                    && (VanillaBindings.IsBoundTo(KeyBindingDefOf.Designator_RotateRight, Event.current.keyCode)
                        || VanillaBindings.IsBoundTo(KeyBindingDefOf.Designator_RotateLeft, Event.current.keyCode)))
                {
                    // Vanilla's own HandleRotationShortcuts rotates on this event; we only observe via __state/postfix.
                    return true;
                }

                // A landing spot is a position, never a Thing standing on it, so this targeter
                // confirms the cursor cell itself and never resolves a Thing target.
                IntVec3 cursorPosition;
                switch (KeyboardTargeterConfirm.TryResolveCell(out cursorPosition, SpeechPriority.High))
                {
                    case KeyboardTargeterConfirm.Outcome.NotOurs:
                        return true;
                    case KeyboardTargeterConfirm.Outcome.InvalidPosition:
                        return false;
                }

                var target = new LocalTargetInfo(cursorPosition);

                // Vehicle B: the exact placement gate LandingTargeter's own mouse-confirm branch
                // calls (validator, impassable/vehicle-block, launch restriction, roof punch).
                object posStateObj = getPosStateMethod.Invoke(__instance, new object[] { target, false });
                int state = Convert.ToInt32(posStateObj); // Invalid = 0, Obstructed = 1, Valid = 2

                if (state == 0)
                {
                    TolkHelper.Speak("RimWorldAccess.Compat.Vf.LandingSpotInvalid".Loc(), SpeechPriority.High);
                    Event.current.Use();
                    return false; // Keep-open-on-failure: session stays live for retry.
                }

                var action = actionField.GetValue(__instance) as Action<LocalTargetInfo, Rot4>;
                if (action == null)
                    return true;

                Rot4 rotation = (Rot4)landingRotationField.GetValue(__instance);

                // Mirrors LandingTargeter.ProcessInputEvents' own mouse-confirm branch.
                SoundDefOf.Tick_High.PlayOneShotOnCamera(null);
                suppressStopAnnouncement = true;
                try
                {
                    action(target, rotation);
                    stopTargetingMethod.Invoke(__instance, null);
                }
                finally
                {
                    suppressStopAnnouncement = false;
                }

                string cellLabel = $"{cursorPosition.x}, {cursorPosition.z}";
                TolkHelper.Speak(state == 1
                    ? "RimWorldAccess.Compat.Vf.LandingConfirmedObstructed".Loc(cellLabel)
                    : "RimWorldAccess.Compat.Vf.LandingConfirmed".Loc(cellLabel));
                Event.current.Use();
                return false;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfLandingTargeterCompat.ProcessInputEventsPrefix failed: {ex.Message}");
                return true;
            }
        }

        /// <summary>Announces a rotation VF's own HandleRotationShortcuts just applied, since a screen reader user can't see the ghost preview turn.</summary>
        public static void ProcessInputEventsPostfix(object __instance, Rot4 __state)
        {
            if (!ready || __instance == null || __state == Rot4.Invalid)
                return;

            try
            {
                var current = (Rot4)landingRotationField.GetValue(__instance);
                if (current != __state)
                    TolkHelper.Speak("RimWorldAccess.Compat.Vf.LandingRotationChanged".Loc(current.ToStringHuman()));
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfLandingTargeterCompat.ProcessInputEventsPostfix failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Announces what the drawn ghost/rotation window shows. By the time this runs, VF's own
        /// targeterQueue has already applied the enqueued field assignments synchronously (the
        /// common case of no prior queued session), so Instance's allowRotating already reflects
        /// this call.
        /// </summary>
        public static void BeginTargetingPostfix(object __0)
        {
            if (!ready)
                return;

            try
            {
                string vehicleLabel = (__0 as Pawn)?.LabelShort ?? string.Empty;

                object instance = instanceProperty.GetValue(null);
                bool rotatable = instance != null && (bool)allowRotatingField.GetValue(instance);

                TolkHelper.Speak(rotatable
                    ? "RimWorldAccess.Compat.Vf.LandingTargetingStartedRotatable".Loc(vehicleLabel)
                    : "RimWorldAccess.Compat.Vf.LandingTargetingStarted".Loc(vehicleLabel));
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfLandingTargeterCompat.BeginTargetingPostfix failed: {ex.Message}");
            }
        }

        /// <summary>Announces a stop our own Enter-confirm didn't already speak for (Escape, right-click cancel, or the ForcedTargeting confirmation dialog).</summary>
        public static void StopTargetingPostfix()
        {
            if (!ready || suppressStopAnnouncement)
                return;

            try
            {
                TolkHelper.Speak("RimWorldAccess.Compat.Vf.LandingTargetingEnded".Loc());
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfLandingTargeterCompat.StopTargetingPostfix failed: {ex.Message}");
            }
        }
    }
}
