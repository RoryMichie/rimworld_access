using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Port of the original announcement ladders' "Gizmo_ProjectileInterceptorHitPoints"
    /// branches, upgraded from reflection to typed access — the gizmo's `interceptor`
    /// field and CompProjectileInterceptor's ChargingTicksLeft / currentHitPoints /
    /// HitPointsMax members are all public. Unlike the legacy branch (which always
    /// spoke a static "Shield Hit Points" label, and whose status lookup silently
    /// failed because it read the `currentHitPoints` field via GetProperty), this
    /// handler reproduces vanilla's dual state (Gizmo_ProjectileInterceptorHitPoints
    /// .GizmoOnGUI): while charging it shows the ShieldTimeToRecovery label with the
    /// remaining recharge period, otherwise the ShieldEnergy label with "HP / max".
    /// The description facet surfaces the Props.gizmoTipKey tooltip the gizmo draws.
    /// Execution has no branch here — the original ladder had no execute-side
    /// counterpart for this type.
    /// </summary>
    internal sealed class ProjectileInterceptorGizmoHandler : GizmoHandlerBase
    {
        public override bool TryGetLabel(Gizmo gizmo, out string label)
        {
            label = null;

            if (!(gizmo is Gizmo_ProjectileInterceptorHitPoints interceptorGizmo))
                return false;

            CompProjectileInterceptor interceptor = interceptorGizmo.interceptor;
            if (interceptor == null)
            {
                // Legacy fallback: interceptor component missing → generic type name.
                label = "RimWorldAccess.Inspection.Gizmo.Type.ShieldHitPoints".Translate();
                return true;
            }

            // Vanilla's dual-state label (Gizmo_ProjectileInterceptorHitPoints.GizmoOnGUI);
            // both keys verified in Core Keyed/Misc_Gameplay.xml.
            label = (interceptor.ChargingTicksLeft > 0
                ? "ShieldTimeToRecovery"
                : "ShieldEnergy").Translate();
            return true;
        }

        public override bool TryGetStatus(Gizmo gizmo, out string status)
        {
            status = null;

            if (!(gizmo is Gizmo_ProjectileInterceptorHitPoints interceptorGizmo))
                return false;

            CompProjectileInterceptor interceptor = interceptorGizmo.interceptor;
            if (interceptor == null)
                return false;

            if (interceptor.ChargingTicksLeft > 0)
            {
                // Vanilla's charging-state bar text is the bare remaining period
                // (ToStringTicksToPeriod is itself localized); the label already
                // provides the "Shield cooldown" context.
                status = interceptor.ChargingTicksLeft.ToStringTicksToPeriod();
            }
            else
            {
                status = "RimWorldAccess.Inspection.Gizmo.Status.HitPoints".Translate(
                    interceptor.currentHitPoints,
                    interceptor.HitPointsMax);
            }
            return true;
        }

        public override bool TryGetDescription(Gizmo gizmo, out string description)
        {
            description = null;

            if (!(gizmo is Gizmo_ProjectileInterceptorHitPoints interceptorGizmo))
                return false;

            // Vanilla tooltip: Props.gizmoTipKey is an optional per-def translation
            // key (Gizmo_ProjectileInterceptorHitPoints.GizmoOnGUI only shows a tip
            // when the def provides one).
            string tipKey = interceptorGizmo.interceptor?.Props?.gizmoTipKey;
            if (tipKey.NullOrEmpty())
                return false;

            description = tipKey.Translate().ToString().StripTags();
            return true;
        }
    }
}
