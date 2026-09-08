using System;
using HarmonyLib;
using RimWorld;
using RimWorldAccess.Shell;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// The quantity menu has no window of its own and needs none — it edits one row of a vanilla
    /// transfer table that is already on screen (<see cref="TransferableOneWayWidget"/>.DoRow,
    /// decompiled :360, for caravan/pods/split; <see cref="TradeUI"/>.DrawTradeableRow, decompiled :64,
    /// for trade), both funnelling through this one public static method. The ring says where the
    /// keyboard is; the number itself stays vanilla's, because the menu only applies its value on
    /// Confirm and making the in-flight value visible would change when the mutation lands.
    ///
    /// One `Transferable` instance is shared between the menu and this postfix's
    /// call, so trap 4 (the identity trap) does not arise here — matched by
    /// reference below, never by ordinal.
    /// </summary>
    [HarmonyPatch(typeof(TransferableUIUtility), "DoCountAdjustInterface")]
    internal static class QuantityMenuRingPatch
    {
        [HarmonyPostfix]
        internal static void Postfix([HarmonyArgument(0)] Rect rect, [HarmonyArgument(1)] Transferable trad)
        {
            try
            {
                if (trad == null)
                {
                    return;
                }

                // Vanilla applies this rounding first (decompiled
                // RimWorld/TransferableUIUtility.cs:142) before taking the centre;
                // do the same here or the ring sits half a pixel off.
                rect = rect.Rounded();

                // Vanilla's own count rect, decompiled RimWorld/TransferableUIUtility.cs:143.
                Rect countRect = new Rect(rect.center.x - 45f, rect.center.y - 12.5f, 90f, 25f).Rounded();

                if (QuantityMenuState.IsActive && ReferenceEquals(trad, QuantityMenuState.CurrentTransferable))
                {
                    FocusRing.Draw(countRect);
                    return;
                }

                // No quantity menu open: ring the live scope's own focused row instead: one ring per
                // element, never both at once.
                Func<Transferable> provider = TransferableRingRequest.CurrentProvider;
                Transferable current = provider != null ? provider() : null;
                if (current == null || !ReferenceEquals(trad, current))
                {
                    return;
                }
                FocusRing.Draw(countRect);
                UiPointerFollow.NotifyFocusedRect(GuiSpace.VisibleScreenRect(countRect));
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Quantity menu ring draw error", ex);
            }
        }
    }

    /// <summary>
    /// Each of TradeScope, SplitCaravanScope, TransportPodLoadingScope, and CaravanFormationScope
    /// registers its own current-transferable accessor here in OnPush and clears it in OnPop, so
    /// <see cref="QuantityMenuRingPatch"/> can ring the row the keyboard sits on even when no
    /// quantity menu is open. Exactly one scope drives a transfer table at a time (each is a
    /// modal ScreenScope over its own window), so a single static slot is enough.
    /// </summary>
    internal static class TransferableRingRequest
    {
        internal static Func<Transferable> CurrentProvider;
    }
}
