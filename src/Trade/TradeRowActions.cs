using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using RimWorldAccess.Shell;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// The per-row trade operations both trade views share: vanilla's adjust gates, count changes
    /// through Transferable.CanAdjustTo/AdjustTo, the shared quantity chooser, the spoken forms of
    /// a pending deal, and the toolbar both views declare. Direction always derives from the
    /// game's own sign semantics, since gift mode flips PositiveCountDirection.
    /// </summary>
    public static class TradeRowActions
    {
        /// <summary>
        /// Vanilla's two per-row adjust gates: the trader-will-not-trade text and Ideology's
        /// negotiator-refuses-to-sell-slaves rule. Gift mode bypasses TraderWillTrade exactly as
        /// vanilla does.
        /// </summary>
        public static bool CanAdjust(Tradeable t, bool announceReason)
        {
            if (!t.TraderWillTrade && !TradeSession.giftMode)
            {
                if (announceReason)
                {
                    TolkHelper.Speak("RimWorldAccess.Trade.Reject.WillNotTrade".Loc());
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                }
                return false;
            }
            if (SlaveTradeBlocked(t))
            {
                if (announceReason)
                {
                    TolkHelper.SpeakData((string)"NegotiatorWillNotTradeSlaves".Translate(TradeSession.playerNegotiator));
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                }
                return false;
            }
            return true;
        }

        public static bool SlaveTradeBlocked(Tradeable t)
        {
            return ModsConfig.IdeologyActive
                && TradeSession.trader != null
                && TradeSession.playerNegotiator != null
                && TransferableUIUtility.TradeIsPlayerSellingToSlavery(t, TradeSession.trader.Faction)
                && !new HistoryEvent(HistoryEventDefOf.SoldSlave,
                    TradeSession.playerNegotiator.Named(HistoryEventArgsNames.Doer)).DoerWillingToDo();
        }

        /// <summary>
        /// The row's primary direction: for goods only the colony holds, outside gift mode, "more"
        /// means selling more; everywhere else it is the positive raw direction.
        /// </summary>
        public static bool SellingPrimary(Tradeable t)
        {
            return !TradeSession.giftMode
                && t.CountHeldBy(Transactor.Colony) > 0
                && t.CountHeldBy(Transactor.Trader) == 0;
        }

        /// <summary>
        /// Steps the count by <paramref name="steps"/> in the row's primary direction, clamped to
        /// the game's own range. The announcement derives from ActionToDo, so the player hears the
        /// real direction either way.
        /// </summary>
        public static void AdjustBy(Tradeable t, int steps)
        {
            if (t == null)
            {
                TolkHelper.Speak("RimWorldAccess.Guard.NoItemSelected".Loc());
                return;
            }
            if (t.IsCurrency && !t.Interactive)
            {
                TradeNavigationState.AnnounceTradeBalance();
                return;
            }
            if (!CanAdjust(t, announceReason: true))
            {
                return;
            }
            int delta = SellingPrimary(t) ? -steps : steps;
            int newAmount = Mathf.Clamp(t.CountToTransfer + delta, t.GetMinimumToTransfer(), t.GetMaximumToTransfer());
            if (t.CanAdjustTo(newAmount))
            {
                t.AdjustTo(newAmount);
                TradeNavigationState.NotifyTradeChanged();
                AnnounceQuantityChange(t);
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Trade.Reject.CannotAdjustAmount".Loc(), SpeechPriority.High);
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
            }
        }

        /// <summary>Jumps to a range end: GetMaximumToTransfer (most buying or gifting) or GetMinimumToTransfer (most selling).</summary>
        public static void SetExtreme(Tradeable t, bool max)
        {
            if (t == null)
            {
                TolkHelper.Speak("RimWorldAccess.Guard.NoItemSelected".Loc());
                return;
            }
            if (t.IsCurrency && !t.Interactive)
            {
                TradeNavigationState.AnnounceTradeBalance();
                return;
            }
            if (!CanAdjust(t, announceReason: true))
            {
                return;
            }
            int target = max ? t.GetMaximumToTransfer() : t.GetMinimumToTransfer();
            if (!t.CanAdjustTo(target))
            {
                TolkHelper.Speak((max
                    ? "RimWorldAccess.Trade.Reject.CannotAdjustMax"
                    : "RimWorldAccess.Trade.Reject.CannotAdjustMin").Loc());
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }
            t.AdjustTo(target);
            TradeNavigationState.NotifyTradeChanged();
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            string label = TradeNavigationState.GetCleanLabel(t);
            if (target == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Trade.Max.Reset".Loc(label));
                return;
            }
            string key = t.ActionToDo == TradeAction.PlayerBuys
                ? "RimWorldAccess.Trade.Max.Buying"
                : (TradeSession.giftMode ? "RimWorldAccess.Trade.Max.Gifting" : "RimWorldAccess.Trade.Max.Selling");
            float value = Math.Abs(t.CurTotalCurrencyCostForDestination);
            TolkHelper.Speak(key.Loc(Math.Abs(target), label, TradeNavigationState.FormatPrice(value)));
        }

        /// <summary>Delete / Alt+R: the row back to zero.</summary>
        public static void ResetItem(Tradeable t)
        {
            if (t == null)
            {
                TolkHelper.Speak("RimWorldAccess.Guard.NoItemSelected".Loc());
                return;
            }
            if (t.CountToTransfer == 0)
            {
                AnnounceQuantityChange(t);
                return;
            }
            if (!t.CanAdjustTo(0))
            {
                TolkHelper.Speak("RimWorldAccess.Trade.Reject.CannotAdjustAmount".Loc(), SpeechPriority.High);
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }
            t.AdjustTo(0);
            TradeNavigationState.NotifyTradeChanged();
            SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();
            TolkHelper.Speak("RimWorldAccess.Trade.Reset.SingleItem".Loc(TradeNavigationState.GetCleanLabel(t)));
        }

        /// <summary>
        /// Enter: the shared quantity chooser over the SIGNED range
        /// [GetMinimumToTransfer, GetMaximumToTransfer], stepping by one in the row's primary
        /// direction, with buy/sell phrasing and the typed-sign prompts.
        /// </summary>
        public static void OpenQuantityMenu(Tradeable t)
        {
            QuantityMenuState.OpenRange(t,
                t.GetMinimumToTransfer,
                t.GetMaximumToTransfer,
                q => DescribeTradeQuantity(t, q),
                delegate (int newQuantity)
                {
                    if (t.CanAdjustTo(newQuantity))
                    {
                        t.AdjustTo(newQuantity);
                        TradeNavigationState.NotifyTradeChanged();
                    }
                },
                TradeNavigationState.GetCleanLabel(t),
                SellingPrimary(t) ? -1 : 1,
                SignPrompt);
        }

        /// <summary>The prompt after a typed sign: "-" starts a sell (a gift in gift mode), "+" a buy.</summary>
        private static string SignPrompt(int sign)
        {
            string key = sign < 0
                ? (TradeSession.giftMode ? "RimWorldAccess.Trade.Numeric.PromptGifting" : "RimWorldAccess.Trade.Numeric.PromptSelling")
                : "RimWorldAccess.Trade.Numeric.PromptBuying";
            return key.Translate().ToString();
        }

        /// <summary>
        /// Buy/sell phrasing for a candidate quantity: a transfer to the colony is a buy, and gift
        /// mode's flipped PositiveCountDirection is honored automatically.
        /// </summary>
        private static string DescribeTradeQuantity(Tradeable t, int quantity)
        {
            string label = TradeNavigationState.GetCleanLabel(t);
            if (quantity == 0)
            {
                return "RimWorldAccess.Trade.Adjust.NoTrade".Translate(label);
            }
            if (t.IsCurrency)
            {
                string noun = (TradeSession.trader?.TradeCurrency == TradeCurrency.Favor
                    ? "RimWorldAccess.Trade.Currency.FavorNoun"
                    : "RimWorldAccess.Trade.Currency.SilverNoun").Translate();
                return quantity.ToStringWithSign() + " " + noun;
            }
            int toColony = t.PositiveCountDirection == TransferablePositiveCountDirection.Source ? quantity : -quantity;
            bool buying = toColony > 0;
            string key = buying
                ? "RimWorldAccess.Trade.Adjust.Buying"
                : (TradeSession.giftMode ? "RimWorldAccess.Trade.Adjust.Gifting" : "RimWorldAccess.Trade.Adjust.Selling");
            float cost = Math.Abs(quantity) * t.GetPriceFor(buying ? TradeAction.PlayerBuys : TradeAction.PlayerSells);
            return key.Translate(Math.Abs(quantity), label, TradeNavigationState.FormatPrice(cost));
        }

        public static void AnnounceQuantityChange(Tradeable t)
        {
            string label = TradeNavigationState.GetCleanLabel(t);
            if (t.ActionToDo == TradeAction.None)
            {
                TolkHelper.Speak("RimWorldAccess.Trade.Adjust.NoTrade".Loc(label));
                return;
            }
            int count = Math.Abs(t.CountToTransfer);
            float totalCost = Math.Abs(t.CurTotalCurrencyCostForDestination);
            string key = t.ActionToDo == TradeAction.PlayerBuys
                ? "RimWorldAccess.Trade.Adjust.Buying"
                : (TradeSession.giftMode ? "RimWorldAccess.Trade.Adjust.Gifting" : "RimWorldAccess.Trade.Adjust.Selling");
            TolkHelper.Speak(key.Loc(count, label, TradeNavigationState.FormatPrice(totalCost)));
        }

        /// <summary>"Buying 5" / "Selling 3" / "Gifting 2": action word plus count.</summary>
        public static string PendingPhrase(Tradeable t)
        {
            string action = (t.ActionToDo == TradeAction.PlayerBuys
                ? "RimWorldAccess.Trade.Pending.ActionBuying"
                : (TradeSession.giftMode
                    ? "RimWorldAccess.Trade.Pending.ActionGifting"
                    : "RimWorldAccess.Trade.Pending.ActionSelling")).Translate();
            return "RimWorldAccess.Trade.Pending.ActionWithCount"
                .Translate(action, Math.Abs(t.CountToTransfer)).ToString();
        }

        /// <summary>"Total: $40", or "Value: $40" in gift mode, for a row with a pending count.</summary>
        public static string PendingTotalLine(Tradeable t)
        {
            string priceLabel = (TradeSession.giftMode
                ? "RimWorldAccess.Trade.Pending.PriceLabelValue"
                : "RimWorldAccess.Trade.Pending.PriceLabelTotal").Translate();
            float totalCost = Math.Abs(t.CurTotalCurrencyCostForDestination);
            return "RimWorldAccess.Trade.Pending.PriceLine"
                .Translate(priceLabel, TradeNavigationState.FormatPrice(totalCost)).ToString();
        }

        /// <summary>
        /// The row's identity: clean label, then the pending action, since vanilla shows the count
        /// field on every row, then the will-not-trade gate a sighted player sees greyed out.
        /// </summary>
        public static string RowLabel(Tradeable t)
        {
            if (t == null)
            {
                return "";
            }
            var parts = new List<string> { TradeNavigationState.GetCleanLabel(t) };
            if (t.CountToTransfer != 0 && !t.IsCurrency)
            {
                parts.Add(PendingPhrase(t));
            }
            if (!t.TraderWillTrade)
            {
                parts.Add("TraderWillNotTrade".Translate());
            }
            else if (SlaveTradeBlocked(t))
            {
                parts.Add("NegotiatorWillNotTradeSlaves".Translate(TradeSession.playerNegotiator));
            }
            return string.Join(", ", parts);
        }

        /// <summary>Alt+I: the same info card the row's own info button opens.</summary>
        public static void InspectItem(Tradeable t)
        {
            if (t == null)
            {
                TolkHelper.Speak("RimWorldAccess.Guard.NoItemSelected".Loc());
                return;
            }
            Thing thing = t.AnyThing;
            if (thing != null)
            {
                Find.WindowStack.Add(new Dialog_InfoCard(thing));
            }
            else if (!InfoCardState.TryOpenInfoCardForDef(t.ThingDef))
            {
                InfoCardState.SpeakNoInfoCardAvailable();
            }
        }

        public static void ShowSellableItems()
        {
            if (TradeSession.trader != null)
            {
                Find.WindowStack.Add(new Dialog_SellableItems(TradeSession.trader));
            }
        }

        /// <summary>The screen's open announcement: trader and kind, then vanilla's own negotiator line (Dialog_Trade.DoWindowContents).</summary>
        public static string OpeningHeading()
        {
            ITrader trader = TradeSession.trader;
            string traderName = trader?.TraderName
                ?? "RimWorldAccess.Trade.Open.UnknownTrader".Translate().ToString();
            string traderKind = trader?.TraderKind?.label
                ?? "RimWorldAccess.Trade.Open.TraderKindFallback".Translate().ToString();
            string heading = "RimWorldAccess.Trade.Open.Heading".Translate(traderName, traderKind);
            Pawn negotiator = TradeSession.playerNegotiator;
            if (negotiator != null)
            {
                string negotiatorLine = "NegotiatorTradeDialogInfo".Translate(
                    negotiator.LabelShort,
                    negotiator.GetStatValue(StatDefOf.TradePriceImprovement).ToStringPercent());
                heading = heading + ". " + negotiatorLine;
            }
            return heading;
        }

        /// <summary>Vanilla's own accept-button label: "Offer gifts (+N)" with the live goodwill change in gift mode, "Accept" otherwise.</summary>
        public static string AcceptLabel()
        {
            if (!TradeSession.giftMode)
            {
                return "AcceptButton".Translate().ToString();
            }
            string goodwill = FactionGiftUtility
                .GetGoodwillChange(TradeSession.deal.AllTradeables, TradeSession.trader.Faction)
                .ToStringWithSign();
            return "OfferGifts".Translate() + " (" + goodwill + ")";
        }

        /// <summary>The gift/trade toggle exists exactly when vanilla draws it (Dialog_Trade.DoWindowContents).</summary>
        public static bool GiftToggleAvailable(Dialog_Trade dialog, bool giftsOnly)
        {
            Faction faction = TradeSession.trader?.Faction;
            return faction != null
                && dialog != null
                && !giftsOnly
                && !faction.def.permanentEnemy
                && TradeSession.trader.TradeCurrency != TradeCurrency.Favor;
        }

        /// <summary>
        /// The toolbar both views declare, in vanilla's draw order: accept, reset, cancel, the
        /// sellable-items browser, the gift/trade toggle when vanilla draws it, then the view swap.
        /// </summary>
        public static void AddToolbarActions(List<ScreenAction> actions, Dialog_Trade dialog, bool giftsOnly,
            Action resetAll, Action toggleGift, TradeView currentView)
        {
            actions.Add(new ScreenAction(AcceptLabel(), TradeNavigationState.AcceptTrade, "trade.accept"));
            actions.Add(new ScreenAction("ResetButton".Translate().ToString(), resetAll, "trade.resetAll"));
            actions.Add(new ScreenAction("CancelButton".Translate().ToString(),
                TradeNavigationState.CloseAndAnnounceCancel, SharedMenuGrammar.Cancel));
            actions.Add(new ScreenAction("SellableItemsTitle".Translate().ToString(),
                ShowSellableItems, "trade.showSellableItems"));
            if (GiftToggleAvailable(dialog, giftsOnly))
            {
                string targetMode = (TradeSession.giftMode
                    ? "RimWorldAccess.Trade.Gift.ModeTrade"
                    : "RimWorldAccess.Trade.Gift.ModeGift").Translate();
                actions.Add(new ScreenAction(targetMode, toggleGift, "trade.giftMode.toggle"));
            }
            actions.Add(new ScreenAction(TradeViewOpener.SwapLabel(currentView),
                delegate { TradeViewOpener.SwapView(dialog); }, "trade.swapView"));
        }
    }
}
