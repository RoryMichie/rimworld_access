using System;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Picks which scope drives an opening <see cref="Dialog_Trade"/> from the DefaultTradeView
    /// setting, and swaps the other view onto the open dialog when either scope's Ctrl+Tab claim
    /// fires. The swap persists the choice, so the next trade opens in the view last used.
    /// </summary>
    public static class TradeViewOpener
    {
        public static TradeView CurrentView
        {
            get
            {
                RimWorldAccessSettings settings = RimWorldAccessMod_Settings.Settings;
                return settings != null ? settings.DefaultTradeView : TradeView.Classic;
            }
        }

        public static FocusScope CreateScope(Dialog_Trade dialog)
        {
            return CreateScope(dialog, CurrentView, swappedIn: false);
        }

        private static FocusScope CreateScope(Dialog_Trade dialog, TradeView view, bool swappedIn)
        {
            if (view == TradeView.Table)
            {
                return new TradeScope(dialog, swappedIn);
            }
            return new TradeClassicScope(dialog, swappedIn);
        }

        /// <summary>The other view takes over the same open dialog; the deal and the window are untouched.</summary>
        public static void SwapView(Dialog_Trade dialog)
        {
            TradeView target = CurrentView == TradeView.Table ? TradeView.Classic : TradeView.Table;
            RememberView(target);
            ScopeForWindow.Swap(dialog, w => CreateScope((Dialog_Trade)w, target, swappedIn: true));
        }

        private static void RememberView(TradeView view)
        {
            RimWorldAccessSettings settings = RimWorldAccessMod_Settings.Settings;
            if (settings == null || settings.DefaultTradeView == view)
            {
                return;
            }
            settings.DefaultTradeView = view;
            LoadedModManager.GetMod<RimWorldAccessMod_Settings>()?.WriteSettings();
        }

        public static string ViewName(TradeView view)
        {
            return (view == TradeView.Table
                ? "RimWorldAccess.Trade.View.Table"
                : "RimWorldAccess.Trade.View.Classic").Translate();
        }

        /// <summary>The toolbar label of the swap action while <paramref name="current"/> is showing.</summary>
        public static string SwapLabel(TradeView current)
        {
            return (current == TradeView.Table
                ? "RimWorldAccess.Trade.View.SwitchToClassic"
                : "RimWorldAccess.Trade.View.SwitchToTable").Translate();
        }
    }
}
