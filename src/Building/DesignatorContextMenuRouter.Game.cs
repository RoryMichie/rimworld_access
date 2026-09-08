using System;
using System.Collections.Generic;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// A source of context-menu options for a designator beyond its own
    /// RightClickFloatMenuOptions. Vanilla has none; Allow Tool has fourteen, and builds
    /// its menu by concatenating its own entries with the designator's vanilla ones, so a
    /// provider OPENS the merged menu rather than handing options back to be merged again.
    /// </summary>
    public interface IDesignatorContextMenuProvider
    {
        /// <summary>True when this provider would put at least one option of its own in the menu.</summary>
        bool HasOptions(Designator designator);

        /// <summary>
        /// Opens the merged menu. Returns false to decline, leaving the caller to fall
        /// back to the vanilla-only path. Must not throw.
        /// </summary>
        bool TryOpen(Designator designator);
    }

    public static class DesignatorContextMenuRouter
    {
        private static readonly List<IDesignatorContextMenuProvider> providers =
            new List<IDesignatorContextMenuProvider>();

        /// <summary>
        /// Registers a provider for the process lifetime. Provider registration is not
        /// per-game state, so it deliberately has no <see cref="StateResetRegistry"/> hook:
        /// a save-load must not unregister it.
        /// </summary>
        public static void Register(IDesignatorContextMenuProvider provider)
        {
            if (provider == null || providers.Contains(provider))
                return;

            providers.Add(provider);
        }

        public static bool HasOptions(Designator designator)
        {
            return designator != null && FindProvider(designator) != null;
        }

        public static bool TryOpen(Designator designator)
        {
            IDesignatorContextMenuProvider provider = designator == null ? null : FindProvider(designator);
            if (provider == null)
                return false;

            try
            {
                return provider.TryOpen(designator);
            }
            catch (Exception ex)
            {
                // A mod's internal failure must never take down the key handler that got here.
                ModLogger.Error($"[DesignatorContextMenuRouter] {provider.GetType().Name}.TryOpen threw: {ex}");
                return false;
            }
        }

        private static IDesignatorContextMenuProvider FindProvider(Designator designator)
        {
            for (int i = 0; i < providers.Count; i++)
            {
                try
                {
                    if (providers[i].HasOptions(designator))
                        return providers[i];
                }
                catch (Exception ex)
                {
                    ModLogger.Error($"[DesignatorContextMenuRouter] {providers[i].GetType().Name}.HasOptions threw: {ex}");
                }
            }

            return null;
        }
    }
}
