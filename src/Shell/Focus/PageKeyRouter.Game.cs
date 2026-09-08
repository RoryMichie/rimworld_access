using HarmonyLib;
using RimWorld;
using UnityEngine;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The Page-level counterpart of WindowKeyRouter's router twins, plus the deferred-pass raw-poll
    /// guard.
    ///
    /// Pages need their own twins because <see cref="Page"/> DECLARES its own
    /// <c>OnCancelKeyPressed</c>/<c>OnAcceptKeyPressed</c> overrides, so the shell's
    /// Window-declaring-type routers never fire for any Page — the declaring-type Harmony trap. Each
    /// twin is a thin prefix delegating to the Window-level router's rule.
    ///
    /// The routers alone are not enough: both Page overrides are gated on
    /// <c>closeOnCancel</c>/<c>closeOnAccept</c>, which the Page ctor sets FALSE, so for an ordinary
    /// Page they are no-ops and the real Escape/Enter path is the raw KeyBindingDef poll inside
    /// <c>Page.DoBottomButtons</c>. Those polls run in the page's own WINDOW pass, and that pass can
    /// run BEFORE the dispatcher's main pass while SHARING Event.current state with it, in either
    /// order depending on which window last held IMGUI focus (QA R6).
    /// <see cref="PageBottomButtonsStampGuardPatch"/> closes the main-pass-first ordering: on exactly
    /// the frames a live scope's claim already consumed the chord, the deferred KeyDown is Use()d
    /// before the poll reads it. Consuming a KeyDown pass never affects Repaint, so the buttons still
    /// paint.
    ///
    /// Under window-pass-first ordering that stamp arrives too late, having been written after
    /// DoBottomButtons already read the poll. A page whose scope claims Cancel or Accept for
    /// something OTHER than leaving the page therefore needs its own state-based
    /// <c>CanDoNext</c>/<c>CanDoBack</c> twin — on the page, or on the declaring
    /// <see cref="Page"/> type with an instance guard — returning the same answer the scope's
    /// <c>when:</c> predicate would. WorldParamsPatch's pair is the reference shape, each
    /// independently instance-gated so several pages' twins coexist on one declaring-type method.
    ///
    /// StartingSitePatch's subtype accept prefix stays by design: <c>Page_SelectStartingSite</c>
    /// overrides <c>OnAcceptKeyPressed</c> without calling base, so neither these Page-level twins
    /// nor the Window-level ones can ever see it, and unconditional suppression rather than a stamp
    /// check is correct there.
    /// </summary>
    [HarmonyPatch(typeof(Page), "OnCancelKeyPressed")]
    public static class PageCancelKeyRouterPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Page __instance)
        {
            return WindowCancelKeyRouterPatch.Prefix(__instance);
        }
    }

    /// <summary>See <see cref="PageCancelKeyRouterPatch"/>.</summary>
    [HarmonyPatch(typeof(Page), "OnAcceptKeyPressed")]
    public static class PageAcceptKeyRouterPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Page __instance)
        {
            return WindowAcceptKeyRouterPatch.Prefix(__instance);
        }
    }

    /// <summary>
    /// The deferred-pass raw-poll guard, for every Page at once: when the shell already consumed
    /// this frame's Cancel/Accept chord, the fresh deferred-pass KeyDown is used up before
    /// DoBottomButtons can read it, so the same physical Escape or Enter can never ALSO trigger
    /// DoBack/DoNext behind the scope's back. <c>KeyBindingDefOf.*.KeyDownEvent</c> is exactly the
    /// test the vanilla poll runs, so this fires precisely when the poll would have — its own
    /// search-widget and meta-key suppression included — and never wider.
    /// </summary>
    [HarmonyPatch(typeof(Page), "DoBottomButtons")]
    public static class PageBottomButtonsStampGuardPatch
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            Event e = Event.current;
            if (e == null || e.type != EventType.KeyDown)
            {
                return;
            }
            if ((ShellFrameStamps.CancelConsumedThisFrame && KeyBindingDefOf.Cancel.KeyDownEvent)
                || (ShellFrameStamps.AcceptConsumedThisFrame && KeyBindingDefOf.Accept.KeyDownEvent))
            {
                e.Use();
            }
        }
    }
}
