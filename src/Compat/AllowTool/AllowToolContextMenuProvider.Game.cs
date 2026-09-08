using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Opens Allow Tool's own merged context menu for a designator -- its enabled entries
    /// followed by the designator's vanilla RightClickFloatMenuOptions, in the mod's order.
    /// Riding ContextMenuProvider.OpenContextMenu rather than rebuilding the list keeps the
    /// mod's per-entry settings handles authoritative and avoids double-listing the vanilla
    /// options the mod already concatenates.
    /// </summary>
    internal sealed class AllowToolContextMenuProvider : IDesignatorContextMenuProvider
    {
        public bool HasOptions(Designator designator)
        {
            object provider = ResolveProvider(designator);
            return provider != null
                && (bool)AllowToolCompat.HasCustomEnabledEntriesProperty.GetValue(provider, null);
        }

        public bool TryOpen(Designator designator)
        {
            object provider = ResolveProvider(designator);
            if (provider == null
                || !(bool)AllowToolCompat.HasCustomEnabledEntriesProperty.GetValue(provider, null))
                return false;

            // OpenContextMenu ends in Find.WindowStack.Add(new FloatMenu(...)); without the
            // guard raised, DialogInterceptionPatch leaves it a real FloatMenu anchored at the
            // physical mouse, which self-dismisses before a keyboard player can reach it.
            ScopeDelegateGuard.Run(delegate
            {
                AllowToolCompat.OpenContextMenuMethod.Invoke(provider, new object[] { designator });
            });
            return true;
        }

        /// <summary>
        /// The mod's provider for this designator, boxed. Never null for a live designator:
        /// an unmatched one gets the mod's fallback provider, whose entries array is empty
        /// and whose HasCustomEnabledEntries is therefore false. Invoking a public instance
        /// method on the boxed struct copy is safe -- entries is a reference and nothing
        /// reached from here mutates the struct.
        /// </summary>
        private static object ResolveProvider(Designator designator)
        {
            if (designator == null || !AllowToolCompat.ContextMenuGate.Ensure())
                return null;

            return AllowToolCompat.GetMenuProviderMethod.Invoke(null, new object[] { designator });
        }
    }
}
