using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Brackets WidgetCapture/TooltipCapture to the owning window's own draw
    /// via Window.InnerWindowOnGUI — see GenericWindowScope's class remarks
    /// for why that private, non-virtual method is the reliable universal
    /// hook (DoWindowContents is abstract and cannot be patched generically;
    /// WindowOnGUI is virtual and occasionally overridden).
    /// </summary>
    [HarmonyPatch]
    internal static class GenericWindowDrawPatch
    {
        // The window whose Prefix opened the current capture pass — set only
        // once BeginDrawPass() has actually succeeded, cleared by the Postfix
        // that closes the pass it opened. Pairs Prefix/Postfix explicitly
        // (fixed capture-pass leak) instead of each independently re-deriving
        // "is there a scope for this window right now": a keypress can close
        // this very window during its own GUI pass (e.g. Enter on its Close
        // button through the live activation channel), popping the scope
        // synchronously before Postfix runs, so For(__instance) alone can no
        // longer tell Postfix it owes this pass a close.
        private static Window openWindow;

        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Window), "InnerWindowOnGUI", new Type[] { typeof(int) });
        }

        /// <summary>
        /// A Layout event never opens a capture pass. IMGUI sends every window
        /// a Layout pass before each real event, and vanilla widgets are free
        /// to skip it outright — PawnTable.PawnTableOnGUI returns immediately
        /// on Layout, so a window hosting one (Colony Manager Redux's manager
        /// tab, live 2026-08-03) captured its full surface on Repaint and a
        /// starved remnant on Layout, EVERY frame. Each starved pass rebuilt
        /// the model with the tiny count, and ListModel.SetCount clamped the
        /// cursor to it: Down re-announced the same row forever and the region
        /// entry announced "1 of 2" for a 14-row page. The event type is fixed
        /// for the whole InnerWindowOnGUI call, so Prefix and Postfix skip in
        /// lockstep and the pass pairing cannot come apart.
        /// </summary>
        private static bool IsLayoutPass()
        {
            return Event.current != null && Event.current.type == EventType.Layout;
        }

        [HarmonyPrefix]
        public static void Prefix(Window __instance)
        {
            try
            {
                if (IsLayoutPass())
                {
                    return;
                }
                GenericWindowScope scope = GenericWindowScope.For(__instance);
                if (scope != null)
                {
                    scope.BeginDrawPass();
                    if (HoverSpeech.Enabled)
                    {
                        LiveTooltips.BeginBracket();
                    }
                    openWindow = __instance;
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Generic window draw pass error", ex);
            }
        }

        [HarmonyPostfix]
        public static void Postfix(Window __instance)
        {
            try
            {
                if (IsLayoutPass())
                {
                    return;
                }
                if (!ReferenceEquals(openWindow, __instance))
                {
                    return;
                }
                openWindow = null;
                GenericWindowScope scope = GenericWindowScope.For(__instance);
                if (scope != null)
                {
                    // Before OnGuiPass, which closes the capture pass: a generic
                    // window's bracket lives here, not in ScreenScopeDrawPatch
                    // (GenericWindowScope is not a ScreenScope), so this is the
                    // one place hover can read its stream.
                    HoverSpeech.EvaluateWidgetPass(detachedTips: false);
                    scope.OnGuiPass();
                }
                else
                {
                    // The scope popped mid-pass (window closed from inside
                    // its own GUI pass) — OnGuiPass never ran to close the
                    // bracket, so close it directly rather than leaving
                    // WidgetCapture's pass open indefinitely.
                    WidgetCapture.EndPass();
                    TooltipCapture.EndPass();
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Generic window draw pass error", ex);
            }
        }
    }
}
