using System;
using System.Collections.Generic;
using RimWorld.Planet;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Registry seam for mods that run their own world-map targeting session
    /// parallel to vanilla's <see cref="RimWorld.Planet.WorldTargeter"/> — the
    /// world-flavor analog of <see cref="ExternalMapTargeting"/>. Vehicle
    /// Framework's SmashTools.Targeting.WorldTargeter (aerial launch/retarget)
    /// is the first consumer; see <see cref="VfWorldTargeterCompat"/>.
    ///
    /// A provider registers once at startup via <see cref="Register"/>. Shell
    /// code that must stand down, or change behavior, while ANY external
    /// world-targeting session is live should consult <see cref="Active"/> /
    /// <see cref="ActiveProvider"/> rather than reaching into a specific mod's
    /// state directly. A provider whose <see cref="IExternalWorldTargeter.IsActive"/>
    /// throws (its host mod misbehaving mid-session, which should never
    /// happen but must never crash the shell) is logged and treated as
    /// inactive rather than propagating.
    /// </summary>
    internal interface IExternalWorldTargeter
    {
        /// <summary>Liveness probe; must be cheap and must never throw uncaught.</summary>
        bool IsActive();

        /// <summary>Enter at the world cursor: sets a waypoint, or commits on a repeat at the same target.</summary>
        void Confirm();

        /// <summary>Escape: cancels the whole targeting session.</summary>
        void Cancel();

        /// <summary>Backspace: pops the last waypoint. Returns false when there is nothing to pop.</summary>
        bool PopWaypoint();

        /// <summary>Spoken validity/fuel clause for the given tile, for AnnounceTile and the status key. Null or empty means nothing to add.</summary>
        string DestinationInfo(PlanetTile tile);
    }

    internal static class ExternalWorldTargeting
    {
        private static readonly List<IExternalWorldTargeter> providers = new List<IExternalWorldTargeter>();

        /// <summary>Registers a provider. Null providers are ignored.</summary>
        public static void Register(IExternalWorldTargeter provider)
        {
            if (provider != null)
                providers.Add(provider);
        }

        /// <summary>The first registered provider whose IsActive() is true, or null.</summary>
        public static IExternalWorldTargeter ActiveProvider
        {
            get
            {
                for (int i = 0; i < providers.Count; i++)
                {
                    try
                    {
                        if (providers[i].IsActive())
                            return providers[i];
                    }
                    catch (Exception ex)
                    {
                        ModLogger.Error($"ExternalWorldTargeting probe threw: {ex.Message}");
                    }
                }
                return null;
            }
        }

        /// <summary>Whether any registered external world targeter is currently active.</summary>
        public static bool Active => ActiveProvider != null;

        /// <summary>
        /// Any world-level targeting session, vanilla or external. Nothing
        /// consumes this yet in this slice, but it completes the seam
        /// contract symmetrically with <see cref="ExternalMapTargeting.MapTargetingActive"/>.
        /// </summary>
        public static bool WorldTargetingActive =>
            (Find.WorldTargeter != null && Find.WorldTargeter.IsTargeting) || Active;

        /// <summary>
        /// Armed by a provider around a <see cref="IExternalWorldTargeter.Confirm"/>
        /// call that may synchronously raise a vanilla FloatMenu (an
        /// arrival-options menu). Consumed and cleared by
        /// <see cref="DialogInterceptionPatch"/> after redirecting that menu
        /// into the windowless one; providers must ALSO clear it in a finally
        /// around the staged invoke as a defensive backstop.
        /// </summary>
        internal static bool IsConfirming { get; set; }
    }
}
