using System;
using System.Collections.Generic;
using HarmonyLib;

namespace RimWorldAccess
{
    /// <summary>
    /// Discovers and activates every <see cref="CompatModule"/> in this assembly, in a
    /// deterministic (alphabetical) order. One module failing never blocks the others.
    /// Modules whose registrations must run in a fixed relative order belong to the same
    /// module, which sequences them in its own Activate body.
    /// </summary>
    public static class CompatBootstrap
    {
        public static void ActivateAll(Harmony harmony)
        {
            var modules = new List<CompatModule>();
            foreach (Type type in typeof(CompatBootstrap).Assembly.GetTypes())
            {
                if (type.IsAbstract || !typeof(CompatModule).IsAssignableFrom(type))
                {
                    continue;
                }
                try
                {
                    modules.Add((CompatModule)Activator.CreateInstance(type));
                }
                catch (Exception ex)
                {
                    ModLogger.Error("Compat module " + type.Name + " failed to construct: " + ex);
                }
            }

            modules.Sort((a, b) => string.CompareOrdinal(a.GetType().FullName, b.GetType().FullName));

            foreach (CompatModule module in modules)
            {
                try
                {
                    if (!module.ShouldActivate)
                    {
                        continue;
                    }
                    module.Activate(harmony);
                }
                catch (Exception ex)
                {
                    ModLogger.Error("Compat module " + module.GetType().Name + " activation failed: " + ex);
                }
            }
        }
    }
}
