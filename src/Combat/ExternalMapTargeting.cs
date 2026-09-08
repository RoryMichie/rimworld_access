using System;
using System.Collections.Generic;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Registry for mods that run their own map-level targeting session parallel to
    /// vanilla's <see cref="Verse.Targeter"/> (e.g. Vehicle Framework's TurretTargeter,
    /// a separate singleton <see cref="Verse.Targeter.ProcessInputEvents"/> never
    /// touches). Shell code that must stand down, or change behavior, while ANY map
    /// targeting session is live — vanilla or modded — should consult
    /// <see cref="MapTargetingActive"/> instead of reading <see cref="Verse.Find.Targeter"/>
    /// directly. A compat class for such a mod registers a liveness probe once at
    /// startup via <see cref="Register"/>; a probe throwing (e.g. the mod's assembly
    /// unloaded mid-session, which should never happen but must never crash the shell)
    /// is treated as "not active" rather than propagating.
    /// </summary>
    internal static class ExternalMapTargeting
    {
        private static readonly List<Func<bool>> probes = new List<Func<bool>>();

        /// <summary>Registers a liveness predicate for an external map targeter. Null probes are ignored.</summary>
        public static void Register(Func<bool> probe)
        {
            if (probe != null)
                probes.Add(probe);
        }

        /// <summary>Whether any registered external map targeter is currently active.</summary>
        public static bool Active
        {
            get
            {
                for (int i = 0; i < probes.Count; i++)
                {
                    try
                    {
                        if (probes[i]())
                            return true;
                    }
                    catch (Exception ex)
                    {
                        ModLogger.Error($"ExternalMapTargeting probe threw: {ex.Message}");
                    }
                }
                return false;
            }
        }

        /// <summary>Any map-level targeting session, vanilla or external.</summary>
        public static bool MapTargetingActive =>
            (Find.Targeter != null && Find.Targeter.IsTargeting) || Active;
    }
}
