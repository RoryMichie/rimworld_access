#if DEBUG
using System;
using System.IO;
using System.Reflection;
using Verse;

namespace RimWorldAccess.DevBridge
{
    /// <summary>
    /// Loads the Roslyn scripting runtime from the mod's DevBridgeLibs folder. Those DLLs
    /// must stay out of Assemblies/: RimWorld GetTypes()-sweeps every DLL there at boot,
    /// and Microsoft.CodeAnalysis.Scripting references System.Runtime.Loader types Mono
    /// does not ship, which turns the sweep into a red boot error. Called lazily from
    /// RoslynEvaluator's init; load timing is free to be lazy because the evaluator holds
    /// no static typeref to any Roslyn type (see its header) and finds everything by name
    /// among the assemblies loaded here.
    /// </summary>
    internal static class RoslynRuntimeLoader
    {
        private static bool loadAttempted;

        internal static void EnsureLoaded()
        {
            if (loadAttempted) return;
            loadAttempted = true;

            string assembliesDir = Path.GetDirectoryName(typeof(RoslynRuntimeLoader).Assembly.Location);
            string libDir = Path.Combine(Path.GetDirectoryName(assembliesDir), "DevBridgeLibs");
            if (!Directory.Exists(libDir))
            {
                Log.Warning($"[RimWorld Access] Dev bridge: DevBridgeLibs not found at {libDir}; /eval will be unavailable.");
                return;
            }

            foreach (string dll in Directory.GetFiles(libDir, "*.dll"))
            {
                try
                {
                    Assembly.LoadFrom(dll);
                }
                catch (Exception e)
                {
                    Log.Warning($"[RimWorld Access] Dev bridge: failed to load {Path.GetFileName(dll)}: {e.Message}");
                }
            }
        }
    }
}
#endif
