using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Reflection-only compatibility surface for Vehicle Framework's
    /// <c>Vehicles.World.Dialog_AssignSeats</c> (assigns colonists to vehicle role handlers during
    /// caravan formation). <see cref="VfAssignSeatsScope"/> is the sole consumer and never touches
    /// reflection directly -- every VF-typed value stays boxed as <c>object</c> here.
    ///
    /// Handler identity: <c>VehicleRoleHandler</c>'s constructor always clones its role
    /// (<c>role = new VehicleRole(role)</c>), so no two handlers on the same vehicle ever share a
    /// <c>VehicleRole</c> instance, making reference equality on the HANDLER and on its ROLE
    /// interchangeable in practice. VF's own dialog uses handler-reference equality for
    /// <c>PreAssignedCount</c> and role-reference equality for the "Add to role" button's
    /// <c>MatchingHandler</c> helper; this facade uses handler-reference equality uniformly, which
    /// reproduces both call sites identically given that guarantee.
    ///
    /// DEVIATION: VF's own <c>VehicleRoleHandler.AreSlotsAvailable</c> gates on
    /// <c>thingOwner.Count</c> -- the vehicle's actually-boarded pawns -- which does not change
    /// while this dialog assigns seats (nothing writes back to <c>thingOwner</c> until
    /// <c>FinalizeSeats</c> commits), so using it would freeze the "does this role still have room"
    /// gate at whatever it was when the dialog opened. VF's own <c>DrawAssignees</c> instead
    /// recomputes <c>slotsAvailable</c> every frame from <c>PreAssignedCount</c>, and
    /// <see cref="AssignedCount"/>/<see cref="HandlerHasOpenSlot"/> reproduce that live counting.
    /// </summary>
    internal static class VfAssignSeatsCompat
    {
        /// <summary>Result of an assignment attempt, mirroring the caution/reject branch of VF's own inline gate.</summary>
        internal enum AssignOutcome
        {
            /// <summary>The pawn can operate the role; assigned cleanly.</summary>
            Assigned,
            /// <summary>The pawn cannot operate the role but it has no Movement handling type, so VF assigns anyway with a caution message.</summary>
            CautionAssigned,
            /// <summary>The pawn cannot operate a Movement-handling role; VF refuses the assignment.</summary>
            Rejected,
        }

        private static readonly Type dialogType;
        private static readonly Type assignmentType;
        private static readonly Type assignedSeatType;
        private static readonly Type roleHandlerType;
        private static readonly Type roleType;
        private static readonly Type vehiclePawnType;
        private static readonly Type handlingTypeEnum;

        private static readonly FieldInfo vehicleField;
        private static readonly FieldInfo pawnsField;
        private static readonly FieldInfo assignerField;
        private static readonly FieldInfo insideVehicleField;
        private static readonly MethodInfo autoAssignMethod;
        private static readonly MethodInfo finalizeSeatsMethod;

        private static readonly MethodInfo getAssignmentsMethod;
        private static readonly MethodInfo setAssignmentMethod;
        private static readonly MethodInfo removeAssignmentMethod;
        private static readonly MethodInfo removeAllMethod;

        private static readonly ConstructorInfo assignedSeatCtor;
        private static readonly FieldInfo seatPawnField;
        private static readonly FieldInfo seatHandlerField;

        private static readonly FieldInfo handlerRoleField;
        private static readonly MethodInfo canOperateRoleMethod;

        private static readonly FieldInfo roleLabelField;
        private static readonly PropertyInfo roleSlotsProperty;
        private static readonly PropertyInfo roleSlotsToOperateProperty;
        private static readonly PropertyInfo roleRequiredForCaravanProperty;
        private static readonly PropertyInfo roleHandlingTypesProperty;

        private static readonly FieldInfo vehicleHandlersField;

        private static readonly int handlingTypeMovementValue;

        private static readonly bool ready;

        public static bool Ready => ready;
        public static Type DialogType => dialogType;

        static VfAssignSeatsCompat()
        {
            var surface = new ReflectionSurface("VfAssignSeatsCompat");

            dialogType = surface.Type("Vehicles.World.Dialog_AssignSeats");
            assignmentType = surface.Type("Vehicles.VehicleAssignment");
            assignedSeatType = surface.Type("Vehicles.AssignedSeat");
            roleHandlerType = surface.Type("Vehicles.VehicleRoleHandler");
            roleType = surface.Type("Vehicles.VehicleRole");
            vehiclePawnType = surface.Supplied("Vehicles.VehiclePawn", VfVehiclePawn.VehiclePawnType);
            handlingTypeEnum = surface.Type("Vehicles.HandlingType");

            vehicleField = surface.Field(dialogType, "vehicle");
            pawnsField = surface.Field(dialogType, "pawns");
            assignerField = surface.Field(dialogType, "assigner");
            insideVehicleField = surface.Field(dialogType, "insideVehicle");
            autoAssignMethod = surface.Method(dialogType, "AutoAssign");
            finalizeSeatsMethod = surface.Method(dialogType, "FinalizeSeats", new[] { typeof(string).MakeByRefType() });

            getAssignmentsMethod = vehiclePawnType != null
                ? surface.Method(assignmentType, "GetAssignments", new[] { vehiclePawnType })
                : null;
            removeAssignmentMethod = surface.Method(assignmentType, "RemoveAssignment", new[] { typeof(Pawn) });
            removeAllMethod = surface.Method(assignmentType, "RemoveAll", new[] { typeof(Predicate<Pawn>) });
            setAssignmentMethod = assignedSeatType != null
                ? surface.Method(assignmentType, "SetAssignment", new[] { assignedSeatType })
                : null;

            assignedSeatCtor = assignedSeatType != null && roleHandlerType != null
                ? AccessTools.Constructor(assignedSeatType, new[] { typeof(Pawn), roleHandlerType })
                : null;
            seatPawnField = surface.Field(assignedSeatType, "pawn");
            seatHandlerField = surface.Field(assignedSeatType, "handler");

            handlerRoleField = surface.Field(roleHandlerType, "role");
            canOperateRoleMethod = surface.Method(roleHandlerType, "CanOperateRole", new[] { typeof(Pawn) });

            roleLabelField = surface.Field(roleType, "label");
            roleSlotsProperty = surface.Property(roleType, "Slots");
            roleSlotsToOperateProperty = surface.Property(roleType, "SlotsToOperate");
            roleRequiredForCaravanProperty = surface.Property(roleType, "RequiredForCaravan");
            roleHandlingTypesProperty = surface.Property(roleType, "HandlingTypes");

            vehicleHandlersField = surface.Field(vehiclePawnType, "handlers");

            if (handlingTypeEnum != null)
            {
                try
                {
                    handlingTypeMovementValue = Convert.ToInt32(Enum.Parse(handlingTypeEnum, "Movement"));
                }
                catch (Exception ex)
                {
                    ModLogger.Error($"VfAssignSeatsCompat: could not resolve HandlingType.Movement: {ex.Message}");
                }
            }

            ready = surface.Ready && VfVehiclePawn.Ready && assignedSeatCtor != null;
        }

        // ------------------------------------------------------------------
        // Dialog-level reads.
        // ------------------------------------------------------------------

        /// <summary>The vehicle being assigned, as a Pawn (VehiclePawn extends Pawn).</summary>
        public static Pawn GetVehicle(Window w)
        {
            try
            {
                return vehicleField.GetValue(w) as Pawn;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfAssignSeatsCompat.GetVehicle failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>The dialog's ordered candidate-pawn list (Dialog_AssignSeats.pawns).</summary>
        public static List<Pawn> GetPawns(Window w)
        {
            try
            {
                return pawnsField.GetValue(w) as List<Pawn> ?? new List<Pawn>();
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfAssignSeatsCompat.GetPawns failed: {ex.Message}");
                return new List<Pawn>();
            }
        }

        /// <summary>Handler objects in the vehicle's own <c>handlers</c> order -- the sighted group order.</summary>
        public static List<object> GetHandlers(Window w)
        {
            var result = new List<object>();
            try
            {
                Pawn vehicle = GetVehicle(w);
                if (vehicle == null)
                    return result;
                if (vehicleHandlersField.GetValue(vehicle) is System.Collections.IEnumerable handlers)
                {
                    foreach (object handler in handlers)
                        result.Add(handler);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfAssignSeatsCompat.GetHandlers failed: {ex.Message}");
            }
            return result;
        }

        private static object GetAssigner(Window w)
        {
            return assignerField.GetValue(w);
        }

        private static object GetRole(object handler)
        {
            return handler != null ? handlerRoleField.GetValue(handler) : null;
        }

        // ------------------------------------------------------------------
        // Per-handler reads.
        // ------------------------------------------------------------------

        public static string RoleLabel(object handler)
        {
            try
            {
                return roleLabelField.GetValue(GetRole(handler)) as string ?? "";
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfAssignSeatsCompat.RoleLabel failed: {ex.Message}");
                return "";
            }
        }

        public static int Slots(object handler)
        {
            try
            {
                return (int)roleSlotsProperty.GetValue(GetRole(handler));
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfAssignSeatsCompat.Slots failed: {ex.Message}");
                return 0;
            }
        }

        public static int SlotsToOperate(object handler)
        {
            try
            {
                return (int)roleSlotsToOperateProperty.GetValue(GetRole(handler));
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfAssignSeatsCompat.SlotsToOperate failed: {ex.Message}");
                return 0;
            }
        }

        public static bool RequiredForCaravan(object handler)
        {
            try
            {
                return (bool)roleRequiredForCaravanProperty.GetValue(GetRole(handler));
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfAssignSeatsCompat.RequiredForCaravan failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>Whether the role's HandlingTypes include Movement (VF's own RequiredForMovement test, reproduced by flag).</summary>
        public static bool RoleHasMovement(object handler)
        {
            try
            {
                int value = Convert.ToInt32(roleHandlingTypesProperty.GetValue(GetRole(handler)));
                return (value & handlingTypeMovementValue) != 0;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfAssignSeatsCompat.RoleHasMovement failed: {ex.Message}");
                return false;
            }
        }

        public static bool CanOperateRole(object handler, Pawn p)
        {
            try
            {
                return (bool)canOperateRoleMethod.Invoke(handler, new object[] { p });
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfAssignSeatsCompat.CanOperateRole failed: {ex.Message}");
                return false;
            }
        }

        // ------------------------------------------------------------------
        // Assignment reads.
        // ------------------------------------------------------------------

        /// <summary>Whether the pawn holds a seat assignment in this dialog session (VehicleAssignment.pawnAssignment membership, reproduced by scanning Assignments).</summary>
        public static bool IsAssigned(Window w, Pawn p)
        {
            try
            {
                foreach (var pair in Assignments(w))
                {
                    if (ReferenceEquals(pair.pawn, p))
                        return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfAssignSeatsCompat.IsAssigned failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>The dialog's own <c>insideVehicle</c> set: pawns already physically boarded when the dialog opened (their rows draw no remove button).</summary>
        public static bool IsInsideVehicle(Window w, Pawn p)
        {
            try
            {
                var set = insideVehicleField.GetValue(w) as HashSet<Pawn>;
                return set != null && set.Contains(p);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfAssignSeatsCompat.IsInsideVehicle failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>assigner.GetAssignments(vehicle), projected through AssignedSeat.pawn/.handler.</summary>
        public static List<(Pawn pawn, object handler)> Assignments(Window w)
        {
            var result = new List<(Pawn, object)>();
            try
            {
                Pawn vehicle = GetVehicle(w);
                object assigner = GetAssigner(w);
                if (vehicle == null || assigner == null)
                    return result;
                if (getAssignmentsMethod.Invoke(assigner, new object[] { vehicle }) is System.Collections.IEnumerable seats)
                {
                    foreach (object seat in seats)
                    {
                        Pawn pawn = seatPawnField.GetValue(seat) as Pawn;
                        object handler = seatHandlerField.GetValue(seat);
                        if (pawn != null && handler != null)
                            result.Add((pawn, handler));
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfAssignSeatsCompat.Assignments failed: {ex.Message}");
            }
            return result;
        }

        /// <summary>Live in-dialog seat count for this handler (handler-reference equality; mirrors Dialog_AssignSeats.PreAssignedCount).</summary>
        public static int AssignedCount(Window w, object handler)
        {
            int count = 0;
            foreach (var pair in Assignments(w))
            {
                if (ReferenceEquals(pair.handler, handler))
                    count++;
            }
            return count;
        }

        /// <summary>Live "does this handler still have a free seat" gate, mirroring DrawAssignees' own <c>slotsAvailable</c> recompute rather than VF's thingOwner-based property (see class remarks).</summary>
        public static bool HandlerHasOpenSlot(Window w, object handler)
        {
            return AssignedCount(w, handler) < Slots(handler);
        }

        // ------------------------------------------------------------------
        // Mutators.
        // ------------------------------------------------------------------

        /// <summary>
        /// Mirrors the drag-drop assignment branch in DrawAssignees: a Movement-handling role
        /// the pawn cannot operate rejects outright; any other role the pawn cannot operate
        /// still assigns, with a caution message. MUTATION-C: mirrors
        /// Dialog_AssignSeats.DrawAssignees' drop-branch accept/reject/caution decision; that
        /// decision is inline IMGUI with no invocable vehicle of its own (VF's CanOperateRole
        /// only answers "can operate", not "should this drop be allowed").
        /// </summary>
        public static AssignOutcome TryAssign(Window w, Pawn p, object handler)
        {
            try
            {
                bool canOperate = CanOperateRole(handler, p);
                AssignOutcome outcome;
                if (!canOperate)
                {
                    bool hasMovement = RoleHasMovement(handler);
                    // MUTATION-C: mirrors Dialog_AssignSeats.DrawAssignees' drag-drop branch; the
                    // accept/reject/caution decision is inline IMGUI, no invocable vehicle exists.
                    Messages.Message(
                        "RimWorldAccess.Compat.Vf.SeatsIncapableForRole".Translate(p.LabelShortCap),
                        hasMovement ? MessageTypeDefOf.RejectInput : MessageTypeDefOf.CautionInput);
                    if (hasMovement)
                        return AssignOutcome.Rejected;
                    outcome = AssignOutcome.CautionAssigned;
                }
                else
                {
                    outcome = AssignOutcome.Assigned;
                }

                object seat = assignedSeatCtor.Invoke(new object[] { p, handler });
                setAssignmentMethod.Invoke(GetAssigner(w), new object[] { seat });
                return outcome;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfAssignSeatsCompat.TryAssign failed: {ex.Message}");
                return AssignOutcome.Rejected;
            }
        }

        /// <summary>
        /// Mirrors the "Add to role" button block in DrawPawns, including the MatchingHandler
        /// helper (first handler the pawn can operate with an open slot; fallback: first
        /// non-Movement handler with an open slot). MUTATION-C: mirrors Dialog_AssignSeats.DrawPawns'
        /// "Add to role" button body; the handler-pick and accept/caution decision is inline IMGUI,
        /// no invocable vehicle exists. The reject branch can never actually fire given the
        /// fallback's own !RoleHasMovement filter -- kept verbatim, matching VF's own dead branch,
        /// so a future VF change to that filter cannot silently reopen a hole here.
        /// </summary>
        public static bool TryAutoAssign(Window w, Pawn p, out AssignOutcome outcome, out object assignedHandler)
        {
            outcome = AssignOutcome.Rejected;
            assignedHandler = null;
            try
            {
                List<object> handlers = GetHandlers(w);
                object firstHandler = null;
                foreach (object h in handlers)
                {
                    if (CanOperateRole(h, p) && HandlerHasOpenSlot(w, h))
                    {
                        firstHandler = h;
                        break;
                    }
                }
                if (firstHandler == null)
                {
                    foreach (object h in handlers)
                    {
                        if (!RoleHasMovement(h) && HandlerHasOpenSlot(w, h))
                        {
                            firstHandler = h;
                            break;
                        }
                    }
                }
                if (firstHandler == null)
                    return false;

                bool canAssign = true;
                bool operable = CanOperateRole(firstHandler, p);
                if (!operable)
                {
                    canAssign = !RoleHasMovement(firstHandler);
                    Messages.Message(
                        "RimWorldAccess.Compat.Vf.SeatsIncapableForRole".Translate(p.LabelShortCap),
                        canAssign ? MessageTypeDefOf.CautionInput : MessageTypeDefOf.RejectInput);
                }
                if (!canAssign)
                    return false;

                object seat = assignedSeatCtor.Invoke(new object[] { p, firstHandler });
                setAssignmentMethod.Invoke(GetAssigner(w), new object[] { seat });
                outcome = operable ? AssignOutcome.Assigned : AssignOutcome.CautionAssigned;
                assignedHandler = firstHandler;
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfAssignSeatsCompat.TryAutoAssign failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>Vehicle A/B: the real, ungated VehicleAssignment.RemoveAssignment -- VF's own row-remove button calls this directly with no gate.</summary>
        public static void RemoveAssignment(Window w, Pawn p)
        {
            try
            {
                removeAssignmentMethod.Invoke(GetAssigner(w), new object[] { p });
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfAssignSeatsCompat.RemoveAssignment failed: {ex.Message}");
            }
        }

        /// <summary>Vehicle A: the Clear Seats button's own assigner.RemoveAll(pawn => !vehicle.AllPawnsAboard.Contains(pawn)) call -- the predicate is plain data, not a hand-copied gate.</summary>
        public static void ClearSeats(Window w)
        {
            try
            {
                Pawn vehicle = GetVehicle(w);
                List<Pawn> aboard = VfVehiclePawn.AllPawnsAboard(vehicle) ?? new List<Pawn>();
                Predicate<Pawn> keepAboardOnly = pawn => !aboard.Contains(pawn);
                removeAllMethod.Invoke(GetAssigner(w), new object[] { keepAboardOnly });
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfAssignSeatsCompat.ClearSeats failed: {ex.Message}");
            }
        }

        /// <summary>Vehicle A: invokes the dialog's own private AutoAssign() -- exactly what the Auto-Assign button calls.</summary>
        public static void AutoAssign(Window w)
        {
            try
            {
                autoAssignMethod.Invoke(w, null);
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfAssignSeatsCompat.AutoAssign failed: {ex.Message}");
            }
        }

        /// <summary>Vehicle B: invokes the dialog's own gated FinalizeSeats(out failReason) and honors its answer, mirroring the Confirm button's own reject message on failure.</summary>
        public static bool TryConfirm(Window w, out string failReason)
        {
            failReason = "";
            try
            {
                object[] args = { null };
                bool ok = (bool)finalizeSeatsMethod.Invoke(w, args);
                failReason = args[0] as string ?? "";
                if (!ok)
                {
                    Messages.Message(
                        "RimWorldAccess.Compat.Vf.SeatsAssignFailureMessage".Translate(failReason),
                        MessageTypeDefOf.RejectInput);
                }
                return ok;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfAssignSeatsCompat.TryConfirm failed: {ex.Message}");
                failReason = ex.Message;
                return false;
            }
        }
    }
}
