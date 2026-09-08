using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Gives hover a capture of any window nobody else brackets, on the same
    /// universal Window.InnerWindowOnGUI hook the two capture brackets use (see
    /// GenericWindowScope's remarks for why that private method is the reliable
    /// one). Pure observation: it opens a detached pass and closes it, and
    /// never touches the event.
    /// </summary>
    [HarmonyPatch]
    internal static class HoverWindowPassPatch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Window), "InnerWindowOnGUI", new Type[] { typeof(int) });
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        public static void Prefix(Window __instance, out bool __state)
        {
            __state = false;
            try
            {
                // An existing bracket already feeds hover this window's stream;
                // the check is explicit so correctness does not depend on how
                // Harmony orders the prefixes. Only the WIDGET bracket
                // evaluates hover, so a scope with button capture alone still
                // needs this pass — its concurrent ButtonTextCapture pass is
                // an independent tap that a detached WidgetCapture pass leaves
                // untouched.
                if (__instance == null
                    || GenericWindowScope.For(__instance) != null)
                {
                    return;
                }
                ScreenScope scope = ScreenScopeDrawPatch.For(__instance);
                if (scope != null && scope.WantsWidgetCapture)
                {
                    return;
                }
                // A window above this one holds the pointer: reading this one
                // through from underneath would speak the obscured surface.
                if (ShellGuards.NonImmediateWindowUnderPointer(__instance))
                {
                    return;
                }
                __state = HoverCapturePass.TryBegin(__instance.windowRect);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Hover window pass error", ex);
            }
        }

        /// <summary>
        /// A finalizer, not a postfix: a postfix does not run when the window
        /// body throws, and the leaked detached pass would then throw out of
        /// the next BeginDetachedPass, the inspect-tab harness's included. The
        /// game's exception is returned unchanged.
        /// </summary>
        [HarmonyFinalizer]
        public static Exception Finalizer(Exception __exception, bool __state)
        {
            if (__state)
            {
                HoverCapturePass.End(evaluate: __exception == null);
            }
            return __exception;
        }
    }

    /// <summary>
    /// The Entry main menu has no window at all — UIRoot_Entry draws it
    /// straight through MainMenuDrawer — so it needs its own bracket. Its
    /// options go through ListableOption.DrawOption's Widgets.ButtonText,
    /// which WidgetCapture already records.
    /// </summary>
    [HarmonyPatch(typeof(MainMenuDrawer), nameof(MainMenuDrawer.MainMenuOnGUI))]
    internal static class HoverMainMenuPassPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        public static void Prefix(out bool __state)
        {
            __state = false;
            try
            {
                // A dialog over the menu must not be read through from underneath.
                if (ShellGuards.NonImmediateWindowUnderPointer())
                {
                    return;
                }
                __state = HoverCapturePass.TryBegin(
                    new Rect(0f, 0f, UI.screenWidth, UI.screenHeight));
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Hover main menu pass error", ex);
            }
        }

        [HarmonyFinalizer]
        public static Exception Finalizer(Exception __exception, bool __state)
        {
            if (__state)
            {
                HoverCapturePass.End(evaluate: __exception == null);
            }
            return __exception;
        }
    }

    /// <summary>
    /// The inspect pane's tab strip draws from Window.ExtraOnGUI, which the
    /// WindowStack calls OUTSIDE InnerWindowOnGUI (decompiled
    /// Verse/WindowStack.cs:211), so the pane's own bracket never sees it. The
    /// static utility is the patch target rather than either pane's override:
    /// one patch then covers the map pane and the world pane both, and the tabs
    /// themselves are Widgets.ButtonText calls (RimWorld/InspectPaneUtility.cs:311)
    /// that WidgetCapture already records with their own labels.
    /// </summary>
    [HarmonyPatch(typeof(InspectPaneUtility), nameof(InspectPaneUtility.ExtraOnGUI))]
    internal static class HoverInspectTabsPassPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        public static void Prefix(IInspectPane pane, out bool __state)
        {
            __state = false;
            try
            {
                if (pane == null || ShellGuards.NonImmediateWindowUnderPointer(pane as Window))
                {
                    return;
                }
                // The strip yields to the designator's rotation and draw-style
                // panels, which vanilla paints over its left end while a
                // designator is selected and which their own window pass reads
                // per element later in the same frame (see the guard's remarks).
                // Two channels over one rect would each speak.
                if (ShellGuards.OpaqueImmediatePanelUnderPointer())
                {
                    return;
                }
                // The strip's own band, not a full-screen rect: TryBegin gates on
                // surfaceRect.Contains(mouse), so a wider rect would open and
                // evaluate a pass on every pointer move over the map. The tabs
                // fill it leftward from its right edge, one 72-wide tab each
                // (InspectPaneUtility.cs:277-323).
                __state = HoverCapturePass.TryBegin(new Rect(
                    0f, pane.PaneTopY - InspectPaneUtility.TabHeight,
                    InspectPaneUtility.PaneWidthFor(pane), InspectPaneUtility.TabHeight));
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Hover inspect tabs pass error", ex);
            }
        }

        [HarmonyFinalizer]
        public static Exception Finalizer(Exception __exception, bool __state)
        {
            if (__state)
            {
                HoverCapturePass.End(evaluate: __exception == null);
            }
            return __exception;
        }
    }
}
