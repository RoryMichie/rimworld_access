using System;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// The trade session's lifecycle backend. Navigation, announcements and the table itself belong
    /// to TradeScope; this static bridge is what the Harmony patches and cross-cutting guards read,
    /// plus the dialog-level operations shared between the scope's claims and its toolbar:
    /// accept/gift execution, gift-mode toggle, reset, balance and price-breakdown speech, and the
    /// label and price formatting helpers.
    /// </summary>
    public static class TradeNavigationState
    {
        private static bool isActive = false;

        private static Dialog_Trade currentDialog = null;

        private static TradeDeal cachedDeal = null;
        private static ITrader cachedTrader = null;
        private static Pawn cachedNegotiator = null;

        // Restored after the trade closes.
        private static IntVec3 savedCursorPosition = IntVec3.Invalid;
        private static bool savedWasOnWorldMap = false;
        private static int savedWorldTile = -1;
        private static bool viewStateSavedByPrefix = false;

        // Speech-only: vanilla's TryExecute performs the gift itself.
        private static int pendingGiftGoodwill;

        public static bool IsActive => isActive;

        /// <summary>The current trade deal, preferring TradeSession.deal for freshness.</summary>
        private static TradeDeal CurrentDeal => TradeSession.deal ?? cachedDeal;

        /// <summary>
        /// Saves the view state before a trade dialog opens. Must run before the game's own
        /// CameraJumper.TryJumpAndSelect switches views.
        /// </summary>
        public static void SaveViewStateBeforeTrade()
        {
            savedWasOnWorldMap = Find.World?.renderer?.wantedMode == WorldRenderMode.Planet;

            if (savedWasOnWorldMap)
            {
                savedWorldTile = Find.WorldSelector?.SelectedTile ?? -1;
                savedCursorPosition = IntVec3.Invalid;
            }
            else
            {
                savedWorldTile = -1;
                savedCursorPosition = MapNavigationState.IsInitialized
                    ? MapNavigationState.CurrentCursorPosition
                    : IntVec3.Invalid;
            }

            viewStateSavedByPrefix = true;
        }

        /// <summary>
        /// Records the trade session when Dialog_Trade opens. TradeScope announces the screen and owns
        /// navigation; this only prepares the shared session state.
        /// </summary>
        public static void Open(Dialog_Trade dialog)
        {
            if (dialog == null)
            {
                TolkHelper.Speak("RimWorldAccess.Trade.Open.NoDialog".Loc());
                return;
            }
            if (!TradeSession.Active)
            {
                TolkHelper.Speak("RimWorldAccess.Trade.Open.NoSession".Loc());
                return;
            }

            currentDialog = dialog;

            if (!viewStateSavedByPrefix)
            {
                savedWasOnWorldMap = Find.World?.renderer?.wantedMode == WorldRenderMode.Planet;
                if (savedWasOnWorldMap)
                {
                    savedWorldTile = Find.WorldSelector?.SelectedTile ?? -1;
                    savedCursorPosition = IntVec3.Invalid;
                }
                else
                {
                    savedWorldTile = -1;
                    savedCursorPosition = MapNavigationState.IsInitialized
                        ? MapNavigationState.CurrentCursorPosition
                        : IntVec3.Invalid;
                }
            }
            viewStateSavedByPrefix = false;

            cachedDeal = TradeSession.deal;
            cachedTrader = TradeSession.trader;
            cachedNegotiator = TradeSession.playerNegotiator;

            if (cachedDeal == null || cachedTrader == null)
            {
                TolkHelper.Speak("RimWorldAccess.Trade.Open.SessionNotInitialized".Loc());
                return;
            }

            // Never write CurTimeSpeed here: Dialog_Trade's constructor sets forcePause, which the
            // tick manager honors without touching curTimeSpeed, so the persisted speed survives and
            // resumes on close. Writing it directly clobbers that speed with nothing to restore it.

            isActive = true;
            SoundDefOf.TabOpen.PlayOneShotOnCamera();
        }

        /// <summary>Closes the trade screen, and the underlying Dialog_Trade, without executing the trade.</summary>
        public static void Close()
        {
            var dialogToClose = currentDialog;

            isActive = false;
            currentDialog = null;
            cachedDeal = null;
            cachedTrader = null;
            cachedNegotiator = null;

            RestoreViewState();

            // Let the dialog's own close sound play, matching what a sighted player hears.
            if (dialogToClose != null && Find.WindowStack != null)
            {
                Find.WindowStack.TryRemove(dialogToClose, doCloseSound: true);
            }

            // Never call TradeSession.Close(): vanilla's Dialog_Trade does not either, pending
            // tooltip lambdas would NRE on TradeSession.TradeCurrency, and the next SetupWith
            // overwrites the session anyway.
        }

        /// <summary>Escape: close with the cancellation announcement.</summary>
        public static void CloseAndAnnounceCancel()
        {
            Close();
            TolkHelper.Speak("RimWorldAccess.Trade.Cancelled".Loc());
            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        /// <summary>
        /// Cleans up when the dialog closes from outside, without closing it again. Silent: the close
        /// may be a completed trade.
        /// </summary>
        public static void OnDialogClosing()
        {
            if (!isActive)
                return;

            isActive = false;
            currentDialog = null;
            cachedDeal = null;
            cachedTrader = null;
            cachedNegotiator = null;

            RestoreViewState();
        }

        /// <summary>Restores the view (world vs colony map) and cursor to where the user was before trading.</summary>
        private static void RestoreViewState()
        {
            if (!savedWasOnWorldMap)
            {
                CameraJumper.TryHideWorld();
                if (savedCursorPosition.IsValid)
                {
                    MapNavigationState.SetPendingRestorePosition(savedCursorPosition);
                    if (MapNavigationState.IsInitialized)
                    {
                        MapNavigationState.CurrentCursorPosition = savedCursorPosition;
                    }
                }
            }
            else
            {
                CameraJumper.TryShowWorld();
                if (savedWorldTile >= 0 && Find.WorldSelector != null)
                {
                    // MUTATION-C: WorldSelector.SelectedTile's private backing field
                    // forces the property as the only vehicle (RimWorld.Planet/
                    // WorldSelector.cs:36); the setter is an unconditional,
                    // self-consistent field-set with no Can*/Try* gate to bypass --
                    // its own layer-sync (line 44-47) is exactly what a cross-layer
                    // restore needs.
                    Find.WorldSelector.SelectedTile = savedWorldTile;
                    WorldNavigationState.CurrentSelectedTile = savedWorldTile;
                }
            }

            savedCursorPosition = IntVec3.Invalid;
            savedWasOnWorldMap = false;
            savedWorldTile = -1;
        }

        /// <summary>
        /// Notifies the trade system that quantities changed: recomputes the currency row and pokes
        /// the dialog's CountToTransferChanged so caravan mass and food caches invalidate, exactly as
        /// vanilla's count widgets do.
        /// </summary>
        public static void NotifyTradeChanged()
        {
            CurrentDeal?.UpdateCurrencyCount();
            InvokeDialogMethod("CountToTransferChanged");
        }

        /// <summary>Vanilla's Reset button verbatim: deal.Reset + CacheTradeables + CountToTransferChanged.</summary>
        public static void ResetDeal()
        {
            TradeDeal deal = CurrentDeal;
            if (deal == null)
                return;
            deal.Reset();
            InvokeDialogMethod("CacheTradeables");
            InvokeDialogMethod("CountToTransferChanged");
        }

        private static void InvokeDialogMethod(string name)
        {
            if (currentDialog == null)
                return;
            try
            {
                MethodInfo method = AccessTools.Method(typeof(Dialog_Trade), name);
                if (method == null)
                {
                    Log.Warning("RimWorld Access: Could not find Dialog_Trade." + name + " via reflection");
                    return;
                }
                method.Invoke(currentDialog, null);
            }
            catch (Exception ex)
            {
                Log.Warning("RimWorld Access: Could not invoke Dialog_Trade." + name + ": " + ex.Message);
            }
        }

        /// <summary>
        /// Toggles gift mode with vanilla's own guards and side effects, as its mode button does;
        /// false when gifting is unavailable.
        /// </summary>
        public static bool ToggleGiftMode()
        {
            if (cachedTrader == null || cachedTrader.Faction == null)
            {
                TolkHelper.Speak("RimWorldAccess.Trade.Gift.CannotGiftTrader".Loc(), SpeechPriority.High);
                return false;
            }
            if (cachedTrader.Faction.HostileTo(Faction.OfPlayer))
            {
                TolkHelper.Speak("RimWorldAccess.Trade.Gift.CannotGiftHostile".Loc(), SpeechPriority.High);
                return false;
            }
            if (cachedTrader.TradeCurrency == TradeCurrency.Favor)
            {
                TolkHelper.Speak("RimWorldAccess.Trade.Gift.CannotGiftFavor".Loc(), SpeechPriority.High);
                return false;
            }

            TradeSession.giftMode = !TradeSession.giftMode;
            ResetDeal();

            string mode = (TradeSession.giftMode
                ? "RimWorldAccess.Trade.Gift.ModeGift"
                : "RimWorldAccess.Trade.Gift.ModeTrade").Translate();
            TolkHelper.Speak("RimWorldAccess.Trade.Gift.SwitchedTo".Loc(mode));
            SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
            return true;
        }

        /// <summary>
        /// Executes the trade through vanilla's own accept path: TryExecute handles both trade and
        /// gift mode, and the trader-short-funds confirmation mirrors the real Accept button.
        /// </summary>
        public static void AcceptTrade()
        {
            TradeDeal deal = CurrentDeal;
            if (deal == null)
            {
                TolkHelper.Speak("RimWorldAccess.Trade.Accept.NoDeal".Loc());
                return;
            }

            if (TradeSession.giftMode)
            {
                try
                {
                    pendingGiftGoodwill = cachedTrader?.Faction != null
                        ? FactionGiftUtility.GetGoodwillChange(deal.AllTradeables, cachedTrader.Faction)
                        : 0;
                }
                catch (Exception ex)
                {
                    Log.Warning("RimWorld Access: Error calculating gift goodwill: " + ex.Message);
                    pendingGiftGoodwill = 0;
                }
                ExecuteTradeAction();
                return;
            }

            if (deal.DoesTraderHaveEnoughSilver())
            {
                ExecuteTradeAction();
            }
            else
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("RimWorldAccess.Trade.Accept.WarnTraderShortFunds".Loc(), SpeechPriority.High);
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "ConfirmTraderShortFunds".Translate(),
                    ExecuteTradeAction));
            }
        }

        private static void ExecuteTradeAction()
        {
            TradeDeal deal = CurrentDeal;
            if (deal == null)
                return;

            bool wasGift = TradeSession.giftMode;
            bool actuallyTraded = false;
            AcceptanceReport result = deal.TryExecute(out actuallyTraded);

            if (!result.Accepted)
            {
                if (!string.IsNullOrEmpty(result.Reason))
                {
                    TolkHelper.Speak("RimWorldAccess.Trade.Accept.CannotComplete".Loc(result.Reason), SpeechPriority.High);
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                }
                return;
            }

            if (actuallyTraded)
            {
                SoundDefOf.ExecuteTrade.PlayOneShotOnCamera();
                if (wasGift)
                {
                    TolkHelper.Speak("RimWorldAccess.Trade.Gift.Offered".Loc(pendingGiftGoodwill));
                }
                else
                {
                    TolkHelper.Speak("RimWorldAccess.Trade.Accept.Completed".Loc());
                }
                Close();
            }
            else
            {
                TolkHelper.Speak(wasGift
                    ? "RimWorldAccess.Trade.Gift.NoGifts".Loc()
                    : "RimWorldAccess.Trade.Accept.NoItemsToTrade".Loc());
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
            }
        }

        /// <summary>Announces both parties' post-deal currency holdings.</summary>
        public static void AnnounceTradeBalance()
        {
            if (CurrentDeal == null)
                return;
            string line = HoldingsLine();
            if (line == null)
            {
                TolkHelper.Speak("RimWorldAccess.Trade.Currency.None".Loc());
                return;
            }
            TolkHelper.SpeakData(line);
        }

        public static string HoldingsLine()
        {
            string player = FormattedHolding(Transactor.Colony);
            string trader = FormattedHolding(Transactor.Trader);
            if (player == null || trader == null)
                return null;
            return "RimWorldAccess.Trade.Currency.Both".Translate(player, trader).ToString();
        }

        /// <summary>One side's projected currency holding after the pending deal, formatted; null without a currency row.</summary>
        public static string FormattedHolding(Transactor side)
        {
            TradeDeal deal = CurrentDeal;
            if (deal == null)
                return null;
            deal.UpdateCurrencyCount();
            Tradeable currency = deal.CurrencyTradeable;
            if (currency == null)
                return null;
            return FormatCurrencyHolding(currency.CountPostDealFor(side), cachedTrader?.TradeCurrency == TradeCurrency.Favor);
        }

        /// <summary>
        /// Opens the price-multiplier breakdown a sighted player gets by hovering a price: the game's
        /// own GetPriceTooltip, in a navigable StatBreakdownState.
        /// </summary>
        public static void ShowPriceBreakdown(Tradeable tradeable, TradeAction action)
        {
            if (!GuardHelper.RequireItem(tradeable)) return;

            if (TradeSession.giftMode)
            {
                TolkHelper.Speak("RimWorldAccess.Trade.Detail.NotInGiftMode".Loc());
                return;
            }
            if (!tradeable.TraderWillTrade)
            {
                TolkHelper.Speak("RimWorldAccess.Trade.Reject.WillNotTrade".Loc());
                return;
            }

            string title = (action == TradeAction.PlayerBuys
                ? "RimWorldAccess.Trade.Detail.PriceTitleBuy"
                : "RimWorldAccess.Trade.Detail.PriceTitleSell").Translate(GetCleanLabel(tradeable));

            string tooltip = tradeable.GetPriceTooltip(action);
            if (string.IsNullOrEmpty(tooltip))
            {
                TolkHelper.Speak("RimWorldAccess.Trade.Detail.NoPriceInfo".Loc());
                return;
            }

            // The title already carries the description header, which ends at the blank line.
            int breakdownStart = tooltip.IndexOf("\n\n");
            if (breakdownStart >= 0)
            {
                tooltip = tooltip.Substring(breakdownStart + 2);
            }
            StatBreakdownState.Open(title, tooltip);
        }

        private static readonly Regex RichTextTagRegex = new Regex(@"<[^>]+>", RegexOptions.Compiled);

        private static string StripRichText(string text)
        {
            return string.IsNullOrEmpty(text) ? text : RichTextTagRegex.Replace(text, "");
        }

        /// <summary>A tradeable's label with rich-text tags stripped; grouped pawns read with gender and life stage.</summary>
        public static string GetCleanLabel(Tradeable tradeable)
        {
            if (tradeable == null)
                return "";
            if (tradeable.AnyThing is Pawn pawn)
            {
                int totalCount = tradeable.CountHeldBy(Transactor.Colony) + tradeable.CountHeldBy(Transactor.Trader);
                if (totalCount > 1)
                {
                    return PawnLabelHelper.BuildGroupedPawnLabel(pawn, totalCount);
                }
            }
            return StripRichText(tradeable.Label ?? "");
        }

        /// <summary>A price-quality descriptor from the game's own PriceType, making its colouring audible; empty for a normal price.</summary>
        public static string GetPriceQuality(Tradeable tradeable, TradeAction action)
        {
            if (!tradeable.TraderWillTrade)
                return "";

            // Keys are spelled out literally, never concatenated, so the l10n checker sees them all.
            PriceType priceType = tradeable.PriceTypeFor(action);
            if (action == TradeAction.PlayerBuys)
            {
                switch (priceType)
                {
                    case PriceType.VeryCheap: return "RimWorldAccess.Trade.Quality.Buy.VeryCheap".Translate();
                    case PriceType.Cheap: return "RimWorldAccess.Trade.Quality.Buy.Cheap".Translate();
                    case PriceType.Expensive: return "RimWorldAccess.Trade.Quality.Buy.Expensive".Translate();
                    case PriceType.Exorbitant: return "RimWorldAccess.Trade.Quality.Buy.Exorbitant".Translate();
                    default: return "";
                }
            }
            switch (priceType)
            {
                case PriceType.VeryCheap: return "RimWorldAccess.Trade.Quality.Sell.VeryCheap".Translate();
                case PriceType.Cheap: return "RimWorldAccess.Trade.Quality.Sell.Cheap".Translate();
                case PriceType.Expensive: return "RimWorldAccess.Trade.Quality.Sell.Expensive".Translate();
                case PriceType.Exorbitant: return "RimWorldAccess.Trade.Quality.Sell.Exorbitant".Translate();
                default: return "";
            }
        }

        /// <summary>
        /// Formats a price in the session's currency, mirroring vanilla's trade-row label logic:
        /// silver through ToStringMoney, which owns the precision decision, and favor as a plain
        /// ToString. Calling ToStringMoney rather than hand-copying its precision ternary is what
        /// keeps this from drifting if that threshold changes upstream.
        /// </summary>
        public static string FormatPrice(float price)
        {
            bool isSilver = cachedTrader?.TradeCurrency != TradeCurrency.Favor;
            return isSilver
                ? price.ToStringMoney()
                : "RimWorldAccess.Trade.Price.Favor".Translate(price.ToString()).ToString();
        }

        private static string FormatCurrencyHolding(int amount, bool isFavorTrade)
        {
            return isFavorTrade
                ? "RimWorldAccess.Trade.Currency.FavorCount".Translate(amount).ToString()
                : ((float)amount).ToStringMoney();
        }
    }
}
