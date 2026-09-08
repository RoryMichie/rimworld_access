using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Adapter for the mortar Shells tab, which opens the thing-filter menu on the turret gun's
    /// changeable-projectile settings.
    /// </summary>
    internal sealed class ShellsAdapter : InspectNodeAdapter
    {
        public override string CategoryKey => "Shells";

        public override TabHandlerType Handler => TabHandlerType.Action;

        public override void ExecuteAction(object obj)
        {
            if (!(obj is Building_TurretGun turretGun))
                return;

            var shellComp = turretGun.gun?.TryGetComp<CompChangeableProjectile>();
            if (shellComp != null)
            {
                var settings = shellComp.GetStoreSettings();
                var parentSettings = shellComp.GetParentStoreSettings();
                if (settings != null)
                {
                    ThingFilterMenuState.Open(settings.filter, parentSettings?.filter, "TabShells".Translate(),
                        forceHideHitPointsConfig: true, forceHideQualityConfig: true);
                }
            }
        }
    }
}
