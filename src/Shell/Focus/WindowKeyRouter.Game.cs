using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Per-frame "Escape/Enter was already handled" flags, stamped by a live scope's own Cancel/Accept
    /// claim handler (<see cref="MarkCancelConsumed"/> / <see cref="MarkAcceptConsumed"/>) and read by
    /// <see cref="WindowCancelKeyRouterPatch"/> / <see cref="WindowAcceptKeyRouterPatch"/> to block
    /// vanilla's independent <see cref="Window.OnCancelKeyPressed"/> /
    /// <see cref="Window.OnAcceptKeyPressed"/> pass for the rest of that frame.
    /// <c>Event.current.Use()</c> does NOT stop RimWorld's cancel/accept handling:
    /// <see cref="WindowStack.HandleEventsHighPriority"/> and each window's <c>InnerWindowOnGUI</c>
    /// both re-test the key binding independently of whatever consumed the event, so one Escape can
    /// call <see cref="Window.OnCancelKeyPressed"/> twice in a frame. Stamping the frame once,
    /// centrally, blocks every such re-entry without per-dialog frame guards.
    /// </summary>
    public static class ShellFrameStamps
    {
        private static int cancelFrame = -1;
        private static int acceptFrame = -1;

        /// <summary>Record that a scope's Cancel claim handled Escape on the current frame.</summary>
        public static void MarkCancelConsumed()
        {
            cancelFrame = Time.frameCount;
        }

        /// <summary>Record that a scope's Accept claim handled Enter on the current frame.</summary>
        public static void MarkAcceptConsumed()
        {
            acceptFrame = Time.frameCount;
        }

        /// <summary>True during the frame a scope's Cancel claim last fired.</summary>
        public static bool CancelConsumedThisFrame
        {
            get { return Time.frameCount == cancelFrame; }
        }

        /// <summary>True during the frame a scope's Accept claim last fired.</summary>
        public static bool AcceptConsumedThisFrame
        {
            get { return Time.frameCount == acceptFrame; }
        }
    }

    /// <summary>
    /// Marks a scope's own deliberate call into a window's OnCancelKeyPressed /
    /// OnAcceptKeyPressed, which the ownership prefixes would otherwise judge as vanilla's
    /// pass and block. The flag spans exactly the wrapped call, so the window's own re-tests
    /// later in the frame stay governed by the stamps.
    /// </summary>
    public static class DeliberateWindowKeyCall
    {
        private static bool active;

        public static bool Active
        {
            get { return active; }
        }

        public static void Run(System.Action call)
        {
            active = true;
            try
            {
                call();
            }
            finally
            {
                active = false;
            }
        }
    }

    /// <summary>
    /// Resolves which scope answers for Enter/Escape, walking the stack the way
    /// <see cref="FocusStackCore.Dispatch"/> walks it for chords.
    /// A live, non-modal, window-less scope that does not own the key is TRANSPARENT: unclaimed chords
    /// fall past it to the scope beneath, so ownership must fall the same way. Reading the top scope
    /// alone left the quest reward-choice menu's Enter unowned, and vanilla's <c>closeOnAccept</c>
    /// consumed the KeyDown before the dispatcher saw it. Every non-modal overlay inherits
    /// <c>OwnsAccept == false</c>, so the shape is general.
    /// </summary>
    internal static class ShellKeyOwnership
    {
        /// <summary>
        /// The scope answering for accept (Enter) or cancel (Escape), or null
        /// when none does and vanilla's own handling should run.
        /// </summary>
        public static FocusScope OwnerOf(bool accept)
        {
            IReadOnlyList<FocusScope> scopes = FocusStack.ScopesBottomUp;
            for (int i = scopes.Count - 1; i >= 0; i--)
            {
                FocusScope scope = scopes[i];
                if (!scope.IsLive)
                {
                    return null;
                }
                if (accept ? scope.OwnsAccept : scope.OwnsCancel)
                {
                    return scope;
                }
                // A modal scope masks everything beneath it, and a window-attached
                // scope answers for its own window: neither is transparent.
                if (scope.IsModal || ScopeForWindow.WindowOf(scope) != null)
                {
                    return null;
                }
            }
            return null;
        }
    }

    /// <summary>
    /// The consolidated Escape-ownership gate, replacing scattered per-dialog
    /// <c>Window.OnCancelKeyPressed</c> blocker patches with one rule driven by the focus stack. With
    /// no live scope on the stack it always defers to vanilla, a byte-identical pass-through; it acts
    /// only once a scope overrides <see cref="FocusScope.IsLive"/> and
    /// <see cref="FocusScope.OwnsCancel"/>.
    /// Harmony trap: this patches the DECLARING type <see cref="Window"/>, so a subclass that
    /// overrides <c>OnCancelKeyPressed</c> without calling base is NOT intercepted — virtual dispatch
    /// never reaches the rewritten base method. Such windows keep their own blocker patch, and
    /// retiring one requires confirming first that the type does not override the method.
    /// </summary>
    [HarmonyPatch(typeof(Window), "OnCancelKeyPressed")]
    public static class WindowCancelKeyRouterPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Window __instance)
        {
            if (DeliberateWindowKeyCall.Active)
            {
                return true;
            }
            if (ShellFrameStamps.CancelConsumedThisFrame)
            {
                return false;
            }

            FocusScope owner = ShellKeyOwnership.OwnerOf(accept: false);
            if (owner == null)
            {
                // With only shadow scopes (IsLive false) on the stack, vanilla runs untouched.
                return true;
            }

            Window attached = ScopeForWindow.WindowOf(owner);
            if (attached == null || ReferenceEquals(attached, __instance))
            {
                // An overlay scope (no attached window) owns cancel outright; a window-attached scope
                // owns it only for its own window.
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// The Enter/Accept counterpart to <see cref="WindowCancelKeyRouterPatch"/>: same semantics, same
    /// pass-through, same declaring-type Harmony trap.
    /// </summary>
    [HarmonyPatch(typeof(Window), "OnAcceptKeyPressed")]
    public static class WindowAcceptKeyRouterPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Window __instance)
        {
            if (DeliberateWindowKeyCall.Active)
            {
                return true;
            }
            if (ShellFrameStamps.AcceptConsumedThisFrame)
            {
                return false;
            }

            FocusScope owner = ShellKeyOwnership.OwnerOf(accept: true);
            if (owner == null)
            {
                // See WindowCancelKeyRouterPatch.
                return true;
            }

            Window attached = ScopeForWindow.WindowOf(owner);
            if (attached == null || ReferenceEquals(attached, __instance))
            {
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// The declaring-type trap, instantiated: <see cref="Dialog_MessageBox"/> overrides
    /// OnCancelKeyPressed/OnAcceptKeyPressed to run its cancelAction/acceptAction WITHOUT calling base
    /// when the action is set, so the Window-level routers never see those paths and vanilla's
    /// deferred re-test would resolve a scope-driven box behind the scope's back. These twins apply
    /// the identical rule to the overrides; every migrated dialog type needs the same check.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_MessageBox), "OnCancelKeyPressed")]
    public static class MessageBoxCancelKeyRouterPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Dialog_MessageBox __instance)
        {
            return WindowCancelKeyRouterPatch.Prefix(__instance);
        }
    }

    /// <summary>See <see cref="MessageBoxCancelKeyRouterPatch"/>.</summary>
    [HarmonyPatch(typeof(Dialog_MessageBox), "OnAcceptKeyPressed")]
    public static class MessageBoxAcceptKeyRouterPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Dialog_MessageBox __instance)
        {
            return WindowAcceptKeyRouterPatch.Prefix(__instance);
        }
    }

    /// <summary>
    /// The declaring-type trap, instantiated for <see cref="Dialog_ChooseMemes"/>: it overrides
    /// <c>OnAcceptKeyPressed</c> to call <c>TryAccept</c> directly without calling base, so
    /// <see cref="WindowAcceptKeyRouterPatch"/> never sees it. No Cancel-side twin is needed —
    /// the dialog does not override <c>OnCancelKeyPressed</c>.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_ChooseMemes), "OnAcceptKeyPressed")]
    public static class MemeSelectionAcceptKeyRouterPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Dialog_ChooseMemes __instance)
        {
            return WindowAcceptKeyRouterPatch.Prefix(__instance);
        }
    }

    /// <summary>
    /// The override-proof twin of <see cref="WindowAcceptKeyRouterPatch"/> and
    /// <see cref="WindowCancelKeyRouterPatch"/>, applying the same ownership rule one level up.
    /// Those two patch the DECLARING type <see cref="Window"/>, so a subclass doing its real work
    /// after (or instead of) the base call escapes them: suppressing the base body suppresses only
    /// vanilla's <c>Close()</c>, never the override's own commit — Character Editor's browser dialogs
    /// run <c>DoAndClose()</c> after base. Rather than a hand-written twin per offending type, which
    /// cannot cover types named only at runtime, this gates vanilla's own entry point into them.
    /// <see cref="WindowStack.Notify_PressedAccept"/> and
    /// <see cref="WindowStack.Notify_PressedCancel"/> are the sole callers in the game. A scope's own
    /// deliberate <c>window.OnAcceptKeyPressed()</c> call — vehicle A for every dialog whose OK we
    /// drive — bypasses them and is never blocked by this gate.
    /// </summary>
    public static class WindowStackKeyRouter
    {
        /// <summary>
        /// The window vanilla's loop is about to notify: topmost first, the first that both opts into
        /// the key and currently gets input. Read off the live stack with vanilla's own two predicates,
        /// so the decision concerns exactly the window vanilla would have called.
        /// </summary>
        private static Window RoutedWindow(WindowStack stack, bool accept)
        {
            IList<Window> windows = stack.Windows;
            for (int i = windows.Count - 1; i >= 0; i--)
            {
                Window window = windows[i];
                if (window == null)
                    continue;
                bool catches = (accept ? window.closeOnAccept : window.closeOnCancel)
                    || window.forceCatchAcceptAndCancelEventEvenIfUnfocused;
                if (catches && stack.GetsInput(window))
                    return window;
            }
            return null;
        }

        [HarmonyPatch(typeof(WindowStack), "Notify_PressedAccept")]
        public static class AcceptPatch
        {
            [HarmonyPrefix]
            public static bool Prefix(WindowStack __instance)
            {
                Window routed = RoutedWindow(__instance, accept: true);
                return routed == null || WindowAcceptKeyRouterPatch.Prefix(routed);
            }
        }

        [HarmonyPatch(typeof(WindowStack), "Notify_PressedCancel")]
        public static class CancelPatch
        {
            [HarmonyPrefix]
            public static bool Prefix(WindowStack __instance)
            {
                Window routed = RoutedWindow(__instance, accept: false);
                return routed == null || WindowCancelKeyRouterPatch.Prefix(routed);
            }
        }
    }
}
