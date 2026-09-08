using System.Reflection;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Port of the original GetGizmoLabel ladder's "GuardianShipGizmo" branch
    /// (static Type.GuardianShip label). The status facet is new relative to the
    /// legacy ladder: the gizmo's entire visual content is a single payment
    /// countdown line (decompiled RimWorld.GuardianShipGizmo.GizmoOnGUI), and
    /// TicksLeft is pure quest-part state (delayTicks - ticksPassed while
    /// enabled), so it is computable without any render-time state. Execution
    /// has no branch here — the gizmo is display-only (GizmoState.Clear, no
    /// click handling in vanilla).
    /// </summary>
    internal sealed class GuardianShipGizmoHandler : GizmoHandlerBase
    {
        /// <summary>
        /// GuardianShipGizmo keeps its QuestPart_GuardianShipDelay in a private
        /// instance field; cache the FieldInfo once rather than re-reflecting per
        /// announcement.
        /// </summary>
        private static readonly FieldInfo DelayField = typeof(GuardianShipGizmo)
            .GetField("delay", BindingFlags.Instance | BindingFlags.NonPublic);

        public override bool TryGetLabel(Gizmo gizmo, out string label)
        {
            label = null;
            if (!(gizmo is GuardianShipGizmo))
                return false;

            label = "RimWorldAccess.Inspection.Gizmo.Type.GuardianShip".Translate();
            return true;
        }

        public override bool TryGetStatus(Gizmo gizmo, out string status)
        {
            status = null;
            if (!(gizmo is GuardianShipGizmo))
                return false;

            try
            {
                var delay = DelayField?.GetValue(gizmo) as QuestPart_GuardianShipDelay;
                if (delay == null)
                    return false;

                // Vanilla draws "GuardianShipPayment".Translate(period), but that
                // key exists in no game Keyed XML (live-verified CanTranslate() ==
                // false — sighted players see the raw key). Speak our own key with
                // the same period argument instead.
                status = "RimWorldAccess.Inspection.Gizmo.Status.GuardianShipPayment"
                    .Translate(delay.TicksLeft.ToStringTicksToPeriod())
                    .ToString().StripTags();
                return !string.IsNullOrEmpty(status);
            }
            catch (System.Exception ex)
            {
                ModLogger.Error($"Exception reading GuardianShipGizmo: {ex.Message}");
                return false;
            }
        }
    }
}
