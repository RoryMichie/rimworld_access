using System;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Registers inspection-tree tab adapters for Vanilla Psycasts Expanded.
    /// </summary>
    internal static class VpeTabCompat
    {
        public static void RegisterTabAdapters()
        {
            InspectNodeAdapter adapter = CompatRegistration.TabAdapter(
                "VanillaPsycastsExpanded.UI.ITab_Pawn_Psycasts",
                t => new VpePsycastsTabAdapter(), "VPE psycasts tab compat");

            if (adapter != null && VpePsysetCompat.Ready)
            {
                try
                {
                    ScopeForWindow.RegisterHierarchy(VpePsysetCompat.DialogPsysetType, delegate (Window w)
                    {
                        return new VpePsysetEditorScope(w);
                    });
                }
                catch (Exception ex)
                {
                    Log.Error($"[RimWorld Access] VPE tab compat registration failed: {ex.Message}");
                }
            }
        }
    }
}
