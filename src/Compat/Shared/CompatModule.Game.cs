using HarmonyLib;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// One mod family's activation entry point. <see cref="CompatBootstrap"/> discovers every
    /// concrete subclass at startup and calls <see cref="Activate"/> only when
    /// <see cref="ShouldActivate"/> finds the target mod present, so an absent mod costs no
    /// Harmony patches, no reflection resolution, and no handler registrations. Adding support
    /// for a mod means one new folder under src/Compat/ containing one subclass of this.
    /// </summary>
    public abstract class CompatModule
    {
        /// <summary>Package id of the supported mod, as ModsConfig lists it.</summary>
        public abstract string TargetPackageId { get; }

        public virtual bool ShouldActivate => ModsConfig.IsActive(TargetPackageId);

        public abstract void Activate(Harmony harmony);
    }
}
