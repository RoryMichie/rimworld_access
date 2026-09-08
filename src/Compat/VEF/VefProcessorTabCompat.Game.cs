using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Registers the inspection-tree tab adapter for Vanilla Expanded Framework's PipeSystem
    /// processor tab (PipeSystem.ITab_Processor).
    /// </summary>
    internal static class VefProcessorTabCompat
    {
        public static void RegisterTabAdapters()
        {
            CompatRegistration.TabAdapter("PipeSystem.ITab_Processor",
                t =>
                {
                    var adapter = new VefProcessorTabAdapter();
                    adapter.sharedTab = InspectTabManager.GetSharedInstance(t);
                    return adapter;
                },
                "VEF processor tab compat");
        }
    }
}
