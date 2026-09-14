using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Keyboard support for Vehicle Framework's aerial world targeting.
    /// SmashTools.Targeting.WorldTargeter&lt;TPayload&gt; is the single machine behind all three
    /// aerial flows (ground takeoff via CompVehicleLauncher, VehicleCaravan world orders,
    /// in-flight AerialVehicleInFlight retargeting), driven by the static
    /// SmashTools.Targeting.TargeterDispatcher. Registers a provider with
    /// <see cref="ExternalWorldTargeting"/> so any world-cursor session treats this the same as
    /// the vanilla world-tile-pick family.
    ///
    /// Vehicle A: every keyboard action stages WorldTargeter's own private curTarget/curResult
    /// fields and then invokes its own private ProcessInput() through a fabricated mouse event, so
    /// PrimaryClick/SecondaryClick/Select/Finalize — all of VF's gates, sounds, waypoint
    /// accumulation, and the arrival-options float menu — run unmodified inside VF's compiled
    /// code. The staged curResult is never invented here: it is exactly the TargetValidation
    /// object source.CanTarget(target) itself returns, the identical check the mouse path uses.
    ///
    /// Keep-open-on-failure: an invalid target speaks and returns without stopping the session.
    /// One announcement per action: the intercepted arrival-options float menu announces itself,
    /// and Messages VF raises internally are spoken by the game's own message pipeline.
    ///
    /// Fields declared on WorldTargeter&lt;TPayload&gt; itself (curTarget, curResult, source) only
    /// resolve off the exact closed generic instance type, so those handles are cached per closed
    /// <see cref="Type"/> the first time each is seen (<see cref="handlesCache"/>). The
    /// ITargeterSource&lt;,&gt; members are resolved per concrete SOURCE type instead
    /// (<see cref="sourceHandlesCache"/>), since VF's three sources implement the interface
    /// explicitly and are never referenced by concrete type here. The Vehicles-assembly members
    /// powering the spoken fuel clause are ALL OPTIONAL: their absence silences that one clause,
    /// since this compat drives any SmashTools world targeter, not only Vehicle Framework's.
    /// </summary>
    internal static class VfWorldTargeterCompat
    {
        // ---- SmashTools.Targeting members (required; missing any = decline) ----
        private static Type targeterDispatcherType;
        private static PropertyInfo currentProperty;   // static ITargeter Current
        private static MethodInfo startMethod;          // static void Start(ITargeter)
        private static MethodInfo stopMethod;            // static void Stop(ITargeter)
        private static Type worldTargeterOpenGeneric;    // WorldTargeter`1
        private static Type targetValidationType;        // TargetValidation struct
        private static FieldInfo targetValidationIsValidField;
        private static PropertyInfo targetValidationTooltipProperty;
        private static Type itargeterSourceOpenGeneric;  // ITargeterSource`2
        private static MethodInfo sphericalDistanceMethod; // Ext_Math.SphericalDistance(Vector3, Vector3)

        private static bool ready;

        // ---- Vehicles members powering the spoken fuel clause (optional) ----
        private static Type flightPathTargetUpdaterType;
        private static Type ilauncherType;
        private static Type compVehicleLauncherType;
        private static Type vehiclePawnType;
        private static FieldInfo updaterLauncherField;    // protected ILauncher launcher
        private static FieldInfo updaterVehicleField;     // protected VehiclePawn vehicle
        private static PropertyInfo originProperty;       // ILauncher.Origin
        private static MethodInfo fuelNeededMethod;        // CompVehicleLauncher.FuelNeededToLaunchAtDist(float)
        private static PropertyInfo compVehicleLauncherProperty; // VehiclePawn.CompVehicleLauncher

        private static bool vehiclesReady;

        /// <summary>
        /// Armed around a staged ProcessInput invoke so the Start/Stop postfixes don't
        /// double-announce a stop our own Confirm/Cancel already spoke for. A Stop that fires
        /// OUTSIDE that window (the user picking an option from the intercepted arrival float menu,
        /// or TargeterValid going false) is a genuinely separate event and still gets announced.
        /// </summary>
        private static bool suppressStopAnnouncement;

        private sealed class Handles
        {
            public MethodInfo ProcessInput;
            public FieldInfo TargetData;
            public FieldInfo TargetsField;
            public FieldInfo CurTarget;
            public FieldInfo CurResult;
            public FieldInfo Source;
            public FieldInfo Updater; // optional; null tolerated
        }

        private sealed class SourceHandles
        {
            public MethodInfo CanTarget;
        }

        private static readonly Dictionary<Type, Handles> handlesCache = new Dictionary<Type, Handles>();
        private static readonly Dictionary<Type, SourceHandles> sourceHandlesCache = new Dictionary<Type, SourceHandles>();

        public static void Register(Harmony harmony)
        {
            try
            {
                var surface = new ReflectionSurface("VfWorldTargeterCompat");

                targeterDispatcherType = surface.Type("SmashTools.Targeting.TargeterDispatcher");

                currentProperty = surface.Property(targeterDispatcherType, "Current");
                startMethod = surface.Method(targeterDispatcherType, "Start");
                stopMethod = surface.Method(targeterDispatcherType, "Stop");

                worldTargeterOpenGeneric = surface.Type("SmashTools.Targeting.WorldTargeter`1");
                itargeterSourceOpenGeneric = surface.Type("SmashTools.Targeting.ITargeterSource`2");

                targetValidationType = surface.Type("SmashTools.Targeting.TargetValidation");
                targetValidationIsValidField = surface.Field(targetValidationType, "isValid");
                targetValidationTooltipProperty = surface.Property(targetValidationType, "Tooltip");

                sphericalDistanceMethod = surface.Method(surface.Type("SmashTools.Ext_Math"),
                    "SphericalDistance", new[] { typeof(Vector3), typeof(Vector3) });

                ready = surface.Ready;
                if (!ready)
                    return;

                ResolveOptionalVehiclesMembers();

                ExternalWorldTargeting.Register(new Provider());

                harmony.Patch(startMethod, postfix: new HarmonyMethod(typeof(VfWorldTargeterCompat), nameof(StartPostfix)));
                harmony.Patch(stopMethod, postfix: new HarmonyMethod(typeof(VfWorldTargeterCompat), nameof(StopPostfix)));

            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorld Access] VF world targeter compat registration failed: {ex.Message}");
                ready = false;
            }
        }

        private static void ResolveOptionalVehiclesMembers()
        {
            try
            {
                flightPathTargetUpdaterType = AccessTools.TypeByName("Vehicles.World.FlightPathTargetUpdater");
                ilauncherType = AccessTools.TypeByName("Vehicles.ILauncher");
                compVehicleLauncherType = AccessTools.TypeByName("Vehicles.CompVehicleLauncher");
                vehiclePawnType = VfVehiclePawn.VehiclePawnType;

                updaterLauncherField = flightPathTargetUpdaterType != null
                    ? AccessTools.Field(flightPathTargetUpdaterType, "launcher")
                    : null;
                updaterVehicleField = flightPathTargetUpdaterType != null
                    ? AccessTools.Field(flightPathTargetUpdaterType, "vehicle")
                    : null;
                originProperty = ilauncherType != null ? AccessTools.Property(ilauncherType, "Origin") : null;
                fuelNeededMethod = compVehicleLauncherType != null
                    ? AccessTools.Method(compVehicleLauncherType, "FuelNeededToLaunchAtDist", new[] { typeof(float) })
                    : null;
                compVehicleLauncherProperty = vehiclePawnType != null
                    ? AccessTools.Property(vehiclePawnType, "CompVehicleLauncher")
                    : null;

                vehiclesReady = flightPathTargetUpdaterType != null && updaterLauncherField != null
                    && updaterVehicleField != null && ilauncherType != null && originProperty != null
                    && compVehicleLauncherType != null && fuelNeededMethod != null
                    && vehiclePawnType != null && compVehicleLauncherProperty != null;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfWorldTargeterCompat: optional Vehicles fuel-info members failed to resolve: {ex.Message}");
                vehiclesReady = false;
            }
        }

        private sealed class Provider : IExternalWorldTargeter
        {
            public bool IsActive() => IsWorldTargeterActive();
            public void Confirm() => ConfirmDestination();
            public void Cancel() => CancelSession();
            public bool PopWaypoint() => TryPopWaypoint();
            public string DestinationInfo(PlanetTile tile) => BuildDestinationInfo(tile);
        }

        private static bool IsWorldTargeterActive()
        {
            if (!ready)
                return false;
            object current = currentProperty.GetValue(null);
            return current != null && IsWorldTargeterClosed(current.GetType());
        }

        private static bool IsWorldTargeterClosed(Type t)
        {
            while (t != null)
            {
                if (t.IsGenericType && t.GetGenericTypeDefinition() == worldTargeterOpenGeneric)
                    return true;
                t = t.BaseType;
            }
            return false;
        }

        private static Handles GetHandles(Type closedType)
        {
            if (handlesCache.TryGetValue(closedType, out Handles cached))
                return cached;

            Handles handles = null;
            try
            {
                MethodInfo processInput = AccessTools.Method(closedType, "ProcessInput");
                FieldInfo targetData = AccessTools.Field(closedType, "targetData");
                FieldInfo curTarget = AccessTools.Field(closedType, "curTarget");
                FieldInfo curResult = AccessTools.Field(closedType, "curResult");
                FieldInfo source = AccessTools.Field(closedType, "source");
                FieldInfo updater = AccessTools.Field(closedType, "updater");
                FieldInfo targetsField = targetData != null ? AccessTools.Field(targetData.FieldType, "targets") : null;

                if (processInput != null && targetData != null && targetsField != null
                    && curTarget != null && curResult != null && source != null)
                {
                    handles = new Handles
                    {
                        ProcessInput = processInput,
                        TargetData = targetData,
                        TargetsField = targetsField,
                        CurTarget = curTarget,
                        CurResult = curResult,
                        Source = source,
                        Updater = updater,
                    };
                }
                else
                {
                    ModLogger.Error($"VfWorldTargeterCompat: could not resolve required Targeter members for {closedType}; declining this targeter instance.");
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfWorldTargeterCompat: handle resolution failed for {closedType}: {ex.Message}");
            }

            handlesCache[closedType] = handles;
            return handles;
        }

        private static SourceHandles GetSourceHandles(Type sourceType)
        {
            if (sourceHandlesCache.TryGetValue(sourceType, out SourceHandles cached))
                return cached;

            SourceHandles handles = null;
            try
            {
                Type closedInterface = null;
                foreach (Type iface in sourceType.GetInterfaces())
                {
                    if (!iface.IsGenericType || iface.GetGenericTypeDefinition() != itargeterSourceOpenGeneric)
                        continue;
                    Type[] args = iface.GetGenericArguments();
                    if (args.Length > 0 && args[0] == typeof(GlobalTargetInfo))
                    {
                        closedInterface = iface;
                        break;
                    }
                }

                if (closedInterface != null)
                {
                    MethodInfo canTarget = AccessTools.Method(closedInterface, "CanTarget");
                    if (canTarget != null)
                        handles = new SourceHandles { CanTarget = canTarget };
                }

                if (handles == null)
                    ModLogger.Error($"VfWorldTargeterCompat: could not resolve ITargeterSource<GlobalTargetInfo,> for {sourceType}; declining this source.");
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfWorldTargeterCompat: source handle resolution failed for {sourceType}: {ex.Message}");
            }

            sourceHandlesCache[sourceType] = handles;
            return handles;
        }

        /// <summary>
        /// Resolves the target at the given tile the way the mouse path would: world objects at
        /// the tile first, then the bare tile, each checked through source.CanTarget until one
        /// validates. If none validate, the first candidate comes back with its own failed
        /// validation so the caller can speak the exact reason.
        /// </summary>
        private static (GlobalTargetInfo target, object validation, bool isValid) ResolveCursorTarget(
            object sourceInstance, SourceHandles sh, PlanetTile tile)
        {
            List<GlobalTargetInfo> candidates = new List<GlobalTargetInfo>();
            var worldObjects = Find.WorldObjects?.ObjectsAt(tile);
            if (worldObjects != null)
            {
                foreach (WorldObject wo in worldObjects)
                    candidates.Add(new GlobalTargetInfo(wo));
            }
            candidates.Add(new GlobalTargetInfo(tile));

            GlobalTargetInfo? fallbackTarget = null;
            object fallbackValidation = null;

            foreach (GlobalTargetInfo candidate in candidates)
            {
                object validationObj = sh.CanTarget.Invoke(sourceInstance, new object[] { candidate });
                bool isValid = (bool)targetValidationIsValidField.GetValue(validationObj);
                if (isValid)
                    return (candidate, validationObj, true);
                if (fallbackTarget == null)
                {
                    fallbackTarget = candidate;
                    fallbackValidation = validationObj;
                }
            }

            if (fallbackTarget.HasValue)
                return (fallbackTarget.Value, fallbackValidation, false);

            return (GlobalTargetInfo.Invalid, null, false);
        }

        private static string ReadTooltip(object validationObj)
        {
            if (validationObj == null)
                return null;
            return targetValidationTooltipProperty.GetValue(validationObj) as string;
        }

        /// <summary>
        /// Enter: sets a waypoint on the first press at a valid tile, commits on a repeat press at
        /// the same target — VF's own PrimaryClick/Select/Finalize decide which, via the
        /// fabricated MouseDown ProcessInput invoke below.
        /// </summary>
        private static void ConfirmDestination()
        {
            if (!ready)
                return;

            object current = currentProperty.GetValue(null);
            if (current == null || !IsWorldTargeterClosed(current.GetType()))
                return;

            Handles handles = GetHandles(current.GetType());
            if (handles == null)
                return;

            object sourceInstance = handles.Source.GetValue(current);
            SourceHandles sh = sourceInstance != null ? GetSourceHandles(sourceInstance.GetType()) : null;
            if (sourceInstance == null || sh == null)
                return;

            PlanetTile tile = WorldNavigationState.CurrentSelectedTile;
            if (!tile.Valid)
            {
                TolkHelper.Speak("RimWorldAccess.Combat.Target.InvalidPosition".Loc(), SpeechPriority.High);
                return;
            }

            (GlobalTargetInfo target, object validationObj, bool isValid) = ResolveCursorTarget(sourceInstance, sh, tile);
            if (!target.IsValid)
            {
                TolkHelper.Speak("RimWorldAccess.Combat.Target.InvalidPosition".Loc(), SpeechPriority.High);
                return;
            }

            if (!isValid)
            {
                string invalidTooltip = ReadTooltip(validationObj);
                if (!string.IsNullOrEmpty(invalidTooltip))
                    TolkHelper.SpeakData(invalidTooltip, SpeechPriority.High);
                else
                    TolkHelper.Speak("RimWorldAccess.Compat.Vf.WorldTargetInvalid".Loc(), SpeechPriority.High);
                return; // Keep-open-on-failure: session stays live for retry.
            }

            Event cur = Event.current;
            if (cur == null)
            {
                TolkHelper.Speak("RimWorldAccess.Combat.Target.InvalidPosition".Loc(), SpeechPriority.High);
                return;
            }

            object targetDataObj = handles.TargetData.GetValue(current);
            IList targetsList = handles.TargetsField.GetValue(targetDataObj) as IList;
            int beforeCount = targetsList?.Count ?? 0;

            EventType savedType = cur.type;
            int savedButton = cur.button;
            suppressStopAnnouncement = true;
            ExternalWorldTargeting.IsConfirming = true;
            try
            {
                // Vehicle A: stage the exact validation source.CanTarget already computed,
                // then let VF's own ProcessInput/PrimaryClick/Select/Finalize run unmodified.
                // MUTATION-C: primes the cursor-target state WorldTargeter's own PrimaryClick
                // reads below (the same fields its mouse Update pass writes every frame);
                // not a direct game-state write.
                handles.CurTarget.SetValue(current, target);
                handles.CurResult.SetValue(current, validationObj);

                cur.type = EventType.MouseDown;
                cur.button = 0;
                handles.ProcessInput.Invoke(current, null);
            }
            finally
            {
                cur.type = savedType;
                cur.button = savedButton;
                ExternalWorldTargeting.IsConfirming = false;
                suppressStopAnnouncement = false;
            }

            AnnounceConfirmOutcome(current, handles, targetsList, beforeCount, validationObj, tile);
        }

        private static void AnnounceConfirmOutcome(object current, Handles handles, IList targetsList,
            int beforeCount, object validationObj, PlanetTile tile)
        {
            // The intercepted arrival-options menu announces itself (see DialogInterceptionPatch).
            if (WindowlessFloatMenuState.IsActive)
                return;

            // A continuation map targeter (VF's landing-spot picker via ArrivalOption.continueWith)
            // starts synchronously inside the staged invoke and announces its own instructions; a
            // trailing "destination confirmed" after those would read out of order.
            if (ExternalMapTargeting.Active)
                return;

            object stillCurrent = currentProperty.GetValue(null);
            if (!ReferenceEquals(stillCurrent, current))
            {
                TolkHelper.Speak("RimWorldAccess.Compat.Vf.WorldTargetLaunched".Loc());
                return;
            }

            int afterCount = targetsList?.Count ?? 0;
            if (afterCount > beforeCount)
            {
                float? fuel = null;
                try
                {
                    fuel = ComputeFuelCost(current, handles, tile);
                }
                catch (Exception ex)
                {
                    ModLogger.Error($"VfWorldTargeterCompat fuel computation failed: {ex.Message}");
                }

                TolkHelper.Speak(fuel.HasValue
                    ? "RimWorldAccess.Compat.Vf.WorldWaypointSetWithFuel".Loc(fuel.Value.ToString("F0"))
                    : "RimWorldAccess.Compat.Vf.WorldWaypointSet".Loc());
                return;
            }

            // Click rejected (no arrival options) — the waypoint count didn't move.
            string tooltip = ReadTooltip(validationObj);
            if (!string.IsNullOrEmpty(tooltip))
                TolkHelper.SpeakData(tooltip, SpeechPriority.High);
            else
                TolkHelper.Speak("RimWorldAccess.Compat.Vf.WorldTargetInvalid".Loc(), SpeechPriority.High);
        }

        /// <summary>Escape: cancels the whole session via TargeterDispatcher.Stop, mirroring VF's own Cancel branch.</summary>
        private static void CancelSession()
        {
            if (!IsWorldTargeterActive())
                return;

            object current = currentProperty.GetValue(null);
            if (current == null)
                return;

            suppressStopAnnouncement = true;
            try
            {
                stopMethod.Invoke(null, new object[] { current });
            }
            finally
            {
                suppressStopAnnouncement = false;
            }

            SoundDefOf.CancelMode.PlayOneShotOnCamera();
            TolkHelper.Speak("RimWorldAccess.Compat.Vf.WorldTargetingCancelled".Loc());
        }

        /// <summary>Backspace: pops the last waypoint via a fabricated secondary click through ProcessInput.</summary>
        private static bool TryPopWaypoint()
        {
            if (!ready)
                return false;

            object current = currentProperty.GetValue(null);
            if (current == null || !IsWorldTargeterClosed(current.GetType()))
                return false;

            Handles handles = GetHandles(current.GetType());
            if (handles == null)
                return false;

            object targetDataObj = handles.TargetData.GetValue(current);
            IList targetsList = handles.TargetsField.GetValue(targetDataObj) as IList;
            if (targetsList == null || targetsList.Count == 0)
                return false;

            Event cur = Event.current;
            if (cur == null)
                return false;

            EventType savedType = cur.type;
            int savedButton = cur.button;
            try
            {
                cur.type = EventType.MouseDown;
                cur.button = 1;
                handles.ProcessInput.Invoke(current, null);
            }
            finally
            {
                cur.type = savedType;
                cur.button = savedButton;
            }

            TolkHelper.Speak("RimWorldAccess.Compat.Vf.WorldWaypointRemoved".Loc());
            return true;
        }

        /// <summary>The spoken validity/fuel clause for a tile, built without staging anything.</summary>
        private static string BuildDestinationInfo(PlanetTile tile)
        {
            if (!ready || !tile.Valid)
                return null;

            object current = currentProperty.GetValue(null);
            if (current == null || !IsWorldTargeterClosed(current.GetType()))
                return null;

            Handles handles = GetHandles(current.GetType());
            if (handles == null)
                return null;

            object sourceInstance = handles.Source.GetValue(current);
            SourceHandles sh = sourceInstance != null ? GetSourceHandles(sourceInstance.GetType()) : null;
            if (sourceInstance == null || sh == null)
                return null;

            var (target, validationObj, _) = ResolveCursorTarget(sourceInstance, sh, tile);
            if (!target.IsValid || validationObj == null)
                return null;

            string clause = ReadTooltip(validationObj);

            try
            {
                float? fuel = ComputeFuelCost(current, handles, tile);
                if (fuel.HasValue)
                {
                    string fuelClause = "RimWorldAccess.Compat.Vf.WorldFuelCost".Loc(fuel.Value.ToString("F0")).ToString();
                    clause = string.IsNullOrEmpty(clause) ? fuelClause : $"{clause}. {fuelClause}";
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfWorldTargeterCompat fuel computation failed: {ex.Message}");
            }

            return clause;
        }

        /// <summary>
        /// Sums SphericalDistance legs from the launcher's origin through every staged waypoint to
        /// the cursor tile, then asks the vehicle's own CompVehicleLauncher for the fuel cost of
        /// that distance. Null whenever an optional Vehicles member is unavailable, the source
        /// isn't fuel-driven, or the updater isn't a FlightPathTargetUpdater (an unlimited-range
        /// shuttle, say).
        /// </summary>
        private static float? ComputeFuelCost(object targeterInstance, Handles handles, PlanetTile cursorTile)
        {
            if (!vehiclesReady || handles.Updater == null)
                return null;

            object updater = handles.Updater.GetValue(targeterInstance);
            if (updater == null || !flightPathTargetUpdaterType.IsInstanceOfType(updater))
                return null;

            object launcher = updaterLauncherField.GetValue(updater);
            object vehicle = updaterVehicleField.GetValue(updater);
            if (launcher == null || vehicle == null)
                return null;

            object compLauncher = compVehicleLauncherProperty.GetValue(vehicle);
            if (compLauncher == null)
                return null;

            Vector3 origin = (Vector3)originProperty.GetValue(launcher);

            List<Vector3> points = new List<Vector3> { origin };
            object targetDataObj = handles.TargetData.GetValue(targeterInstance);
            IList targetsList = handles.TargetsField.GetValue(targetDataObj) as IList;
            if (targetsList != null)
            {
                foreach (object t in targetsList)
                    points.Add(TargetPosition((GlobalTargetInfo)t));
            }
            points.Add(TargetPosition(new GlobalTargetInfo(cursorTile)));

            float totalDistance = 0f;
            for (int i = 0; i < points.Count - 1; i++)
                totalDistance += (float)sphericalDistanceMethod.Invoke(null, new object[] { points[i], points[i + 1] });

            return (float)fuelNeededMethod.Invoke(compLauncher, new object[] { totalDistance });
        }

        private static Vector3 TargetPosition(GlobalTargetInfo target)
        {
            return target.HasWorldObject ? target.WorldObject.DrawPos : Find.WorldGrid.GetTileCenter(target.Tile);
        }

        /// <summary>
        /// Announces when the newly started targeter is a SmashTools world targeter. Deliberately
        /// does NOT force-open WorldNavigationState: the CameraJumper jump to the world view
        /// already triggers WorldNavigationPatch's auto-open of the world cursor.
        /// </summary>
        public static void StartPostfix(object __0)
        {
            if (!ready || __0 == null)
                return;

            try
            {
                if (!IsWorldTargeterClosed(__0.GetType()))
                    return;

                TolkHelper.Speak("RimWorldAccess.Compat.Vf.WorldTargetingStarted".Loc());
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfWorldTargeterCompat.StartPostfix failed: {ex.Message}");
            }
        }

        /// <summary>Announces a stop our own Confirm/Cancel didn't already speak for (the intercepted arrival float menu, or VF's own TargeterValid check ending the session).</summary>
        public static void StopPostfix(object __0)
        {
            if (!ready || __0 == null || suppressStopAnnouncement)
                return;

            try
            {
                if (!IsWorldTargeterClosed(__0.GetType()))
                    return;

                TolkHelper.Speak("RimWorldAccess.Compat.Vf.WorldTargetingEnded".Loc());
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfWorldTargeterCompat.StopPostfix failed: {ex.Message}");
            }
        }
    }
}
