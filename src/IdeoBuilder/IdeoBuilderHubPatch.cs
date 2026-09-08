using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Residual lifecycle patches for Page_ConfigureIdeo and its Fluid subclass.
    /// <see cref="RimWorldAccess.Shell.IdeoBuilderScreenScope"/> owns all keyboard routing and
    /// section presentation; what stays here is the DoNext/DoBack reflection helpers and their
    /// discard/randomize confirmation flows, the two Page.DoBack/DoNext guards below (which read
    /// <see cref="RimWorldAccess.Shell.IdeoBuilderScreenScope.AdvanceRequested"/>/
    /// <see cref="RimWorldAccess.Shell.IdeoBuilderScreenScope.BackRequested"/>), and the slim
    /// per-frame prefix that supplies what those guards need: EnsureOpen bookkeeping, ritual-preview
    /// upkeep, and the two draw-skips a stamp cannot replace.
    /// </summary>
    [HarmonyPatch(typeof(Page_ConfigureIdeo), "DoWindowContents")]
    public static class IdeoBuilderHubPatch
    {
        private static MethodInfo doNextMethod;
        private static MethodInfo doBackMethod;
        private static MethodInfo canDoNextMethod;
        private static MethodInfo canDoBackMethod;

        private static void EnsureReflectionCached()
        {
            if (doNextMethod != null) return;
            doNextMethod = AccessTools.Method(typeof(Page), "DoNext");
            doBackMethod = AccessTools.Method(typeof(Page), "DoBack");
            canDoNextMethod = AccessTools.Method(typeof(Page), "CanDoNext");
            canDoBackMethod = AccessTools.Method(typeof(Page), "CanDoBack");
        }

        /// <summary>
        /// Slim per-frame residual. Order matters: the TextInputManager draw-skip runs FIRST, then
        /// reflection caching and the ideo-null check, then the EnsureOpen/ContinueAction/
        /// MaintainRitualPreview wiring, then the float-menu draw-skip, then the gated opening
        /// announcement.
        /// </summary>
        static bool Prefix(Page_ConfigureIdeo __instance, Rect rect)
        {
            try
            {
                // A DRAW-skip, not just an input yield: TextSessionScope is a claim-less shadow
                // scope that stamps nothing, so a text-commit Enter would otherwise reach the
                // deferred DoBottomButtons Accept poll and DoNext the wizard out from under the edit.
                if (TextInputManager.Active != null)
                    return false;

                EnsureReflectionCached();

                if (__instance.ideo == null)
                    return true; // Nothing to do; let the page draw its empty state.

                IdeoBuilderHubState.EnsureOpen();
                // Keep any ritual-sound preview alive even while a context menu / editor is open.
                IdeoEditorCommands.MaintainRitualPreview();

                // A bare float menu owns the keyboard (routed by the live modal
                // FloatMenuOverlayScope). Skip the page so DoBottomButtons cannot grab keys.
                if (WindowlessFloatMenuState.IsActive)
                    return false;
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorld Access] Error in IdeoBuilderHubPatch.Prefix: {ex}");
            }
            return true; // Run original DoWindowContents so visuals still render.
        }

        /// <summary>
        /// True when a real (non-Immediate) window sits above <paramref name="page"/> on the window
        /// stack, whether or not it has an attached scope. This is the guards' own business-rule
        /// question — is some OTHER window entitled to the keyboard/mouse — not the input-routing
        /// modal fold, and it replaces <c>FocusStack.AnyLiveModal</c>, which is always true while
        /// the page's own modal scope is live and would permanently block DoBack/DoNext.
        /// </summary>
        internal static bool RealWindowAbovePage(Page page)
        {
            IList<Window> windows = Find.WindowStack.Windows;
            bool above = false;
            for (int i = 0; i < windows.Count; i++)
            {
                Window window = windows[i];
                if (ReferenceEquals(window, page))
                {
                    above = true;
                    continue;
                }
                if (above && !(window is ImmediateWindow))
                {
                    return true;
                }
            }
            return false;
        }

        #region DoNext / DoBack via reflection

        internal static void TryDoNext(Page_ConfigureIdeo page)
        {
            string err = IdeoBuilderHelper.BuildValidationSummary(page.ideo);
            if (!string.IsNullOrEmpty(err))
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.SpeakData(err, SpeechPriority.High);
                return;
            }

            RimWorldAccess.Shell.IdeoBuilderScreenScope.AdvanceRequested = true;
            try
            {
                bool canNext = true;
                if (canDoNextMethod != null)
                    canNext = (bool)canDoNextMethod.Invoke(page, null);
                if (!canNext)
                {
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    return;
                }

                doNextMethod?.Invoke(page, null);
            }
            finally { RimWorldAccess.Shell.IdeoBuilderScreenScope.AdvanceRequested = false; }
        }

        internal static void TryDoBack(Page_ConfigureIdeo page)
        {
            bool canBack = true;
            if (canDoBackMethod != null)
                canBack = (bool)canDoBackMethod.Invoke(page, null);
            if (!canBack)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }

            // Leaving discards the custom ideoligion the player just built, so confirm first.
            RequestBackConfirm(page);
        }

        // Re-entrancy guard for the discard confirmation. An AnyLiveModal early-return inverts to
        // always-true once this page has its own live modal scope, stranding the player on every
        // Escape-back with no confirmation. Independent of randomizeConfirmOpen below, and cleared
        // from BOTH the confirm and cancel delegates so a dismissed dialog never leaves it stuck.
        private static bool backConfirmOpen;

        /// <summary>
        /// Shows a discard-confirmation before leaving the builder. Continue performs the real
        /// DoBack; Cancel keeps the page. No-op if the confirmation is already open.
        /// </summary>
        internal static void RequestBackConfirm(Page_ConfigureIdeo page)
        {
            if (backConfirmOpen)
                return;

            // Consume the triggering Escape so the dialog doesn't immediately catch the same press.
            if (Event.current != null && Event.current.type == EventType.KeyDown)
                Event.current.Use();

            backConfirmOpen = true;
            Action confirm = () => { backConfirmOpen = false; DoBackConfirmed(page); };
            Action cancel = () => backConfirmOpen = false;
            Find.WindowStack.Add(new Dialog_MessageBox(
                "RimWorldAccess.Ideology.Builder.DiscardConfirm".Translate(),
                buttonAText: "RimWorldAccess.Ideology.Builder.DiscardContinue".Translate(),
                buttonAAction: confirm,
                buttonBText: "RimWorldAccess.Ideology.Builder.DiscardCancel".Translate(),
                buttonBAction: cancel,
                title: null,
                buttonADestructive: true,
                acceptAction: confirm,
                cancelAction: cancel));
        }

        private static void DoBackConfirmed(Page_ConfigureIdeo page)
        {
            RimWorldAccess.Shell.IdeoBuilderScreenScope.BackRequested = true;
            try { doBackMethod?.Invoke(page, null); }
            catch (Exception ex) { Log.Error($"[RimWorld Access] Error in ideo DoBack: {ex}"); }
            finally { RimWorldAccess.Shell.IdeoBuilderScreenScope.BackRequested = false; }
        }

        /// <summary>
        /// Leaves the builder after the player abandoned an unconfigured ideo — e.g. backing out of
        /// the initial structure picker, which makes vanilla remove the empty ideo while leaving
        /// page.ideo dangling. Clears that reference and returns without the discard confirmation.
        /// </summary>
        internal static void LeaveBuilderAbandoned(Page_ConfigureIdeo page)
        {
            if (page == null) return;
            EnsureReflectionCached();
            IdeoBuilderHubState.Close();
            page.ideo = null;
            IdeoUIUtility.UnselectCurrent();
            DoBackConfirmed(page);
        }

        // Own re-entrancy flag, independent of backConfirmOpen above; see its remarks.
        private static bool randomizeConfirmOpen;

        internal static void TryRandomizeAll(Page_ConfigureIdeo page)
        {
            if (page.ideo == null) return;

            // Randomizing replaces the ENTIRE ideoligion and vanilla shows no warning, so always
            // confirm — unless a confirmation is already up, to avoid stacking dialogs.
            if (randomizeConfirmOpen)
                return;

            if (Event.current != null && Event.current.type == EventType.KeyDown)
                Event.current.Use();
            randomizeConfirmOpen = true;
            Action confirm = () => { randomizeConfirmOpen = false; DoRandomizeAll(page); };
            Action cancel = () => randomizeConfirmOpen = false;
            Find.WindowStack.Add(new Dialog_MessageBox(
                "RimWorldAccess.Ideology.Builder.RandomizeAllConfirm".Translate(),
                buttonAText: "RimWorldAccess.Ideology.Builder.RandomizeAllContinue".Translate(),
                buttonAAction: confirm,
                buttonBText: "RimWorldAccess.Ideology.Builder.RandomizeAllCancel".Translate(),
                buttonBAction: cancel,
                title: null,
                buttonADestructive: true,
                acceptAction: confirm,
                cancelAction: cancel));
        }

        private static void DoRandomizeAll(Page_ConfigureIdeo page)
        {
            if (page.ideo == null) return;
            try
            {
                if (!IdeoEditorCommands.RandomizeAll(page.ideo))
                    return;
                // Randomize replaces every field on the ideo: a structural change, so full
                // re-announce rather than a state-change fragment.
                RimWorldAccess.Shell.IdeoBuilderScreenScope.NotifyIdeoEdited();
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorld Access] Error randomizing ideoligion: {ex}");
            }
        }

        #endregion
    }

    /// <summary>
    /// Guards the custom-ideoligion builder against an accidental Escape/Back that would silently
    /// discard the whole ideoligion. Page.DoBottomButtons calls DoBack directly off
    /// KeyBindingDefOf.Cancel (bypassing closeOnCancel), so this intercepts DoBack and routes
    /// through a confirmation. A confirmed back sets explicitDoBack to pass; a sub-editor owning
    /// the keyboard blocks DoBack entirely.
    /// </summary>
    [HarmonyPatch(typeof(Page), "DoBack")]
    public static class IdeoBuilderHubPatch_DoBackGuard
    {
        [HarmonyPrefix]
        static bool Prefix(Page __instance)
        {
            if (!(__instance is Page_ConfigureIdeo page))
                return true;
            if (RimWorldAccess.Shell.IdeoBuilderScreenScope.BackRequested)
                return true;
            if (TextInputManager.Active != null || WindowlessFloatMenuState.IsActive
                || IdeoBuilderHubPatch.RealWindowAbovePage(page)
                || IdeoBuilderOverlays.AnyActive
                || Find.WindowStack.WindowOfType<Dialog_ChooseMemes>() != null) // redundant: RealWindowAbovePage already covers this
                return false;
            IdeoBuilderHubPatch.RequestBackConfirm(page);
            return false;
        }
    }

    /// <summary>
    /// Mirror of the DoBack guard for the forward direction. Page.DoBottomButtons fires DoNext on
    /// the Accept key, so a stray Enter would otherwise advance the player to the next page. Blocks
    /// DoNext for the configure page unless it is our own Alt+S advance or nothing owns input.
    /// </summary>
    [HarmonyPatch(typeof(Page), "DoNext")]
    public static class IdeoBuilderHubPatch_DoNextGuard
    {
        [HarmonyPrefix]
        static bool Prefix(Page __instance)
        {
            if (!(__instance is Page_ConfigureIdeo page))
                return true;
            if (RimWorldAccess.Shell.IdeoBuilderScreenScope.AdvanceRequested)
                return true;
            if (TextInputManager.Active != null || WindowlessFloatMenuState.IsActive
                || IdeoBuilderHubPatch.RealWindowAbovePage(page)
                || IdeoBuilderOverlays.AnyActive
                || Find.WindowStack.WindowOfType<Dialog_ChooseMemes>() != null) // redundant: RealWindowAbovePage already covers this
                return false;
            return true;
        }
    }

    /// <summary>
    /// Keyboard-Accept twin of <see cref="IdeoBuilderHubPatch_DoNextGuard"/>. Page.DoBottomButtons
    /// polls Accept inside the page's own GUI pass (Page.cs:69), which can run before the
    /// dispatcher's pass and shares Event.current with it, so a same-frame ShellFrameStamps check
    /// arrives too late; this guard keys on stable scope state instead. Gated on the keyboard event
    /// so a mouse click on Next still advances. Blocking here rather than in DoNext also suppresses
    /// vanilla's own reject Messages, which CanDoNext raises as a side effect.
    /// </summary>
    [HarmonyPatch(typeof(Page_ConfigureIdeo), "CanDoNext")]
    public static class IdeoBuilderHubPatch_CanDoNextGuard
    {
        [HarmonyPrefix]
        static bool Prefix(Page_ConfigureIdeo __instance, ref bool __result)
        {
            if (KeyBindingDefOf.Accept.KeyDownEvent
                && !RimWorldAccess.Shell.IdeoBuilderScreenScope.AdvanceRequested
                && (RimWorldAccess.Shell.FocusStack.Top is RimWorldAccess.Shell.IdeoBuilderScreenScope
                    || IdeoBuilderOverlays.AnyActive
                    || WindowlessFloatMenuState.IsActive
                    || TextInputManager.Active != null
                    || IdeoBuilderHubPatch.RealWindowAbovePage(__instance)))
            {
                __result = false;
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Closes the hub state when the configure page closes, whichever subclass was open. PostClose
    /// is declared on Window and not overridden by the page, so patch it there and filter by
    /// instance type — Page_ConfigureFluidIdeo derives from Page_ConfigureIdeo.
    /// </summary>
    [HarmonyPatch(typeof(Window), "PostClose")]
    public static class IdeoBuilderHubPatch_Close
    {
        [HarmonyPostfix]
        static void Postfix(Window __instance)
        {
            if (__instance is Page_ConfigureIdeo)
            {
                IdeoBuilderHubState.Close();
                // The page closing while an overlay editor is still open must not leave the
                // overlay's IsActive stuck true.
                IdeoBuilderOverlays.CloseAllOverlayEditors();
            }
        }
    }

    /// <summary>
    /// PostOpen hook. For Custom Fixed entry vanilla's Page_ConfigureIdeo.PostOpen only creates an
    /// ideoligion when IdeoUIUtility.selected was already set, so on first entry ideo stays null and
    /// the player is expected to click a "create new ideoligion" button. Calling
    /// SelectOrMakeNewIdeo() here gives a live ideo; vanilla then auto-opens the Dialog_ChooseMemes
    /// structure picker, which flows into the meme picker and lands the player on the hub.
    /// </summary>
    [HarmonyPatch(typeof(Page_ConfigureIdeo), "PostOpen")]
    public static class IdeoBuilderHubPatch_PostOpen
    {
        [HarmonyPostfix]
        static void Postfix(Page_ConfigureIdeo __instance)
        {
            try
            {
                if (__instance.ideo == null)
                {
                    __instance.SelectOrMakeNewIdeo();
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorld Access] Error in IdeoBuilderHubPatch_PostOpen: {ex}");
            }
        }
    }
}
