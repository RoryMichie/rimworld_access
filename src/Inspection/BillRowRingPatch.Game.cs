using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorldAccess.Shell;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// A scope that can name the bill the keyboard is on, for the real bills tab's ring and
    /// auto-scroll. The windowless bills menu never hides vanilla's <see cref="ITab_Bills"/>, so
    /// the list only lacked a mark for the cursor's row.
    /// </summary>
    internal interface IBillRowFocusSource
    {
        Bill FocusedBill { get; }
    }

    /// <summary>
    /// A scope whose bill list carries a trailing "add a bill" row, for the ring over vanilla's
    /// Add Bill button. Separate from <see cref="IBillRowFocusSource"/> because that button is not
    /// a bill: a scope can focus it while <see cref="IBillRowFocusSource.FocusedBill"/> is null.
    /// </summary>
    internal interface IBillAddRowFocusSource
    {
        bool FocusedOnAddBillRow { get; }
    }

    /// <summary>
    /// Paints the shared keyboard focus ring over vanilla's own bill row and scrolls the real
    /// bill list to keep that row visible.
    ///
    /// Row geometry is vanilla's own: <see cref="Bill.DoInterface"/> returns the rect it just
    /// drew, so nothing here computes a row height or position. That rect is in the view space of
    /// the single scroll view <see cref="BillStack.DoListing"/> opens, which is the space
    /// <see cref="ScrollPositioner"/> wants, and <c>DoListing</c> draws EVERY bill with no culling,
    /// so a rect for the focused row always arrives within one frame.
    ///
    /// The scroll write rides <see cref="ScrollPositioner"/> through the <c>ref Vector2</c>
    /// <c>DoListing</c> forwards from <see cref="ITab_Bills"/>'s real scroll field. Its
    /// Layout-only, self-disarming <c>Scroll</c> is the change gate; the only state kept here is
    /// which bill the positioner was last armed for.
    /// </summary>
    internal static class BillRowFocusRing
    {
        private static readonly ScrollPositioner positioner = new ScrollPositioner();

        // Every drawn row, for pointer routing — the ring below needs only the focused one.
        private static readonly RowGeometryCache rowGeometry = new RowGeometryCache();

        // The focused row's last drawn rect. Only the focused row is recorded, so nothing
        // accumulates.
        private static Bill recordedBill;
        private static Rect recordedRect;

        private static Bill armedFor;
        private static bool insideBillListing;

        internal static void HandleDoInterfacePostfix(Bill instance, Rect result)
        {
            try
            {
                IBillRowFocusSource source = FocusStackLookup.TopmostOfType<IBillRowFocusSource>();
                if (source == null)
                {
                    return;
                }
                rowGeometry.Record(instance, result);
                if (!ReferenceEquals(instance, source.FocusedBill))
                {
                    return;
                }
                recordedBill = instance;
                recordedRect = result;
                if (Event.current.type != EventType.Repaint)
                {
                    return;
                }
                FocusRing.Draw(result.ContractedBy(1f));
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Bill row focus ring error", ex);
            }
        }

        /// <summary>
        /// The window the bill list draws in: vanilla runs an ITab's whole content in its own
        /// <see cref="ImmediateWindow"/>, so this is the tab panel, not the pane behind it.
        /// </summary>
        internal static Window HostWindow
        {
            get { return rowGeometry.HostWindow; }
        }

        internal static void AddRouteCandidate(Bill bill, int region, int index,
            List<PointerHitCandidate> candidates, List<ScreenScope.RouteTarget> targets)
        {
            rowGeometry.AddCandidate(bill, region, index, candidates, targets);
        }

        internal static void HandleDoListingPrefix()
        {
            insideBillListing = true;
        }

        internal static void HandleDoListingFinalizer()
        {
            insideBillListing = false;
        }

        /// <summary>
        /// Rings vanilla's own Add Bill button, the only ButtonText
        /// <see cref="BillStack.DoListing"/> draws and drawn before the scroll view opens, so the
        /// bracket flag still stands and the rect is vanilla's own argument inside its own group.
        /// Vanilla omits the button entirely at fifteen bills, leaving the row unringed.
        /// </summary>
        internal static void HandleAddBillButtonPostfix(Rect rect)
        {
            if (!insideBillListing)
            {
                return;
            }
            try
            {
                if (Event.current.type != EventType.Repaint)
                {
                    return;
                }
                IBillAddRowFocusSource source = FocusStackLookup.TopmostOfType<IBillAddRowFocusSource>();
                if (source == null || !source.FocusedOnAddBillRow)
                {
                    return;
                }
                FocusRing.Draw(rect.ContractedBy(1f));
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Bill add row focus ring error", ex);
            }
        }

        internal static void HandleBeginScrollViewPrefix(Rect outRect, ref Vector2 scrollPosition)
        {
            if (!insideBillListing)
            {
                return;
            }
            insideBillListing = false;
            try
            {
                IBillRowFocusSource source = FocusStackLookup.TopmostOfType<IBillRowFocusSource>();
                Bill focused = source == null ? null : source.FocusedBill;
                if (focused == null)
                {
                    armedFor = null;
                    recordedBill = null;
                    return;
                }
                if (!ReferenceEquals(focused, armedFor))
                {
                    armedFor = focused;
                    positioner.Arm();
                }
                if (!ReferenceEquals(focused, recordedBill))
                {
                    return;
                }
                positioner.ClearInterestRects();
                positioner.RegisterInterestRect(recordedRect);
                positioner.ScrollVertically(ref scrollPosition, outRect.size);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Bill row focus scroll error", ex);
            }
        }
    }

    /// <summary>
    /// Ring half of <see cref="BillRowFocusRing"/>. Bound by name:
    /// <see cref="Bill.DoInterface"/> is declared once, is not virtual, and no subclass shadows
    /// it, so the declaring-type rule is satisfied by the plain binding.
    /// </summary>
    [HarmonyPatch(typeof(Bill), nameof(Bill.DoInterface))]
    internal static class BillRowFocusRingPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Bill __instance, Rect __result)
        {
            BillRowFocusRing.HandleDoInterfacePostfix(__instance, __result);
        }
    }

    /// <summary>
    /// Brackets the one bill listing so the scroll tap below can recognize its scroll view. A
    /// finalizer rather than a postfix: it still runs when a bill's own draw throws, so the flag
    /// can never latch on.
    /// </summary>
    [HarmonyPatch(typeof(BillStack), nameof(BillStack.DoListing))]
    internal static class BillListingBracketPatch
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            BillRowFocusRing.HandleDoListingPrefix();
        }

        [HarmonyFinalizer]
        public static void Finalizer()
        {
            BillRowFocusRing.HandleDoListingFinalizer();
        }
    }

    /// <summary>
    /// Add-row half of <see cref="BillRowFocusRing"/>. This tap fires for every text button in
    /// the game and its first statement is the bracket-flag check. Both public ButtonText
    /// overloads funnel through this one.
    /// </summary>
    [HarmonyPatch(typeof(Widgets), "ButtonText",
        new Type[] { typeof(Rect), typeof(string), typeof(bool), typeof(bool), typeof(Color), typeof(bool), typeof(TextAnchor?) })]
    internal static class BillAddRowFocusRingPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Rect rect)
        {
            BillRowFocusRing.HandleAddBillButtonPostfix(rect);
        }
    }

    /// <summary>
    /// Scroll half of <see cref="BillRowFocusRing"/>. This tap fires for every scroll view in the
    /// game and cheaply no-ops for all but the bill listing's own; the bracket flag is consumed
    /// one-shot so only the first scroll view inside <see cref="BillStack.DoListing"/> is served.
    /// TargetMethod because the by-ref Vector2 parameter type is not a compile-time constant
    /// (CS0182).
    /// </summary>
    [HarmonyPatch]
    internal static class BillListingScrollFollowPatch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Widgets), "BeginScrollView",
                new Type[] { typeof(Rect), typeof(Vector2).MakeByRefType(), typeof(Rect), typeof(bool) });
        }

        [HarmonyPrefix]
        public static void Prefix(Rect outRect, ref Vector2 scrollPosition)
        {
            BillRowFocusRing.HandleBeginScrollViewPrefix(outRect, ref scrollPosition);
        }
    }
}
