using System;
using System.Collections.Generic;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Registry letting a compat module append rows to any scope's Buttons region without that
    /// scope knowing the mod exists — the keyboard twin of a mod Harmony-patching an extra button
    /// onto a vanilla screen. Keyed by <see cref="FocusScope.Name"/>; providers run on every read
    /// so labels and disabled state stay live. A provider returning null contributes no row that
    /// pass (e.g. the mod's button is only drawn in some states).
    /// </summary>
    public static class CompatScreenActions
    {
        private static readonly Dictionary<string, List<Func<ScreenAction>>> providers =
            new Dictionary<string, List<Func<ScreenAction>>>();

        public static void Register(string scopeName, Func<ScreenAction> provider)
        {
            if (string.IsNullOrEmpty(scopeName) || provider == null)
            {
                return;
            }
            List<Func<ScreenAction>> list;
            if (!providers.TryGetValue(scopeName, out list))
            {
                list = new List<Func<ScreenAction>>();
                providers[scopeName] = list;
            }
            list.Add(provider);
        }

        /// <summary>Rows registered for <paramref name="scopeName"/>, or null when none resolve.</summary>
        internal static List<ScreenAction> CollectFor(string scopeName)
        {
            List<Func<ScreenAction>> list;
            if (scopeName == null || !providers.TryGetValue(scopeName, out list))
            {
                return null;
            }
            List<ScreenAction> actions = null;
            for (int i = 0; i < list.Count; i++)
            {
                ScreenAction action;
                try
                {
                    action = list[i]();
                }
                catch (Exception ex)
                {
                    ModLogger.LimitedError("Compat screen action provider error", ex);
                    continue;
                }
                if (action == null)
                {
                    continue;
                }
                if (actions == null)
                {
                    actions = new List<ScreenAction>();
                }
                actions.Add(action);
            }
            return actions;
        }
    }
}
