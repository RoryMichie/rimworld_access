using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Blocks RimWorld's <c>Window.OnAcceptKeyPressed</c> and <c>Window.OnCancelKeyPressed</c>
    /// while a modal text edit is live (or was live during the current frame).
    ///
    /// Why: the game checks <c>KeyBindingDefOf.Accept.KeyDownEvent</c> from multiple code
    /// paths (<c>WindowStack.HandleEventsHighPriority</c>, each window's own <c>WindowOnGUI</c>,
    /// <c>Page.DoBottomButtons</c>). <c>Event.current.Use()</c> should suppress those, but in
    /// practice some paths still call <c>OnAcceptKeyPressed</c> — often fire-and-forget —
    /// resulting in the page advancing even though the modal controller already confirmed.
    /// Patching the virtual hook is the reliable way to stop it, matching the pattern already
    /// used in <see cref="CaravanFormationPatch"/> and <see cref="TradeNavigationPatch"/>.
    ///
    /// The <c>HandledEventThisFrame</c> check is what catches the post-close race: the
    /// controller's <c>HandleEnter</c> clears <see cref="TextInputManager.IsActive"/> BEFORE
    /// the window's Accept hook fires, so an <c>IsActive</c>-only check leaks. Tagging the
    /// frame on every handled event closes that window.
    ///
    /// This Window-level patch is LIVE for hosts that don't override OnAcceptKeyPressed at all
    /// (e.g. Dialog_BillConfig, Dialog_CreateXenotype/Dialog_CreateXenogerm — decompiled-verified,
    /// no override) but unreachable for any <c>Page</c> subclass: <c>Page.OnAcceptKeyPressed</c>
    /// (decompiled/RimWorld/Page.cs) never calls <c>base.OnAcceptKeyPressed()</c>, so virtual
    /// dispatch never enters Window's patched body — the same declaring-type Harmony trap
    /// documented on RitualPatch's Dialog_BeginLordJob_OnAcceptKeyPressed_Patch.
    ///
    /// For the Page-hosted modal text sessions (Page_ScenarioEditor's scenario-metadata rename,
    /// Page_CreateWorldParams' seed edit) that unreachability turns out to be VACUOUS in vanilla:
    /// Page's own constructor sets <c>closeOnAccept = false</c> (no vanilla Page_* re-enables it),
    /// and Page.OnAcceptKeyPressed's entire body is gated on <c>closeOnAccept</c> — verified live,
    /// WindowStack.Notify_PressedAccept reaches the override (via
    /// <c>forceCatchAcceptAndCancelEventEvenIfUnfocused = true</c>) and then no-ops. The REAL
    /// Enter-advance path for pages is Page.DoBottomButtons (Page.cs:69):
    /// <c>doNextOnKeypress &amp;&amp; KeyBindingDefOf.Accept.KeyDownEvent</c> checked INSIDE the
    /// page's own GUI pass — a path no OnAcceptKeyPressed patch (this one or any other) can block.
    /// Real-keyboard QA (2026-08-18) answered the long-open question about those in-pass polls:
    /// they are NOT consumed by a live modal session — the Cancel poll fired during a starting-pawn
    /// name edit and backed the page out — so
    /// <see cref="Page_DoBottomButtons_TextInputMask"/> below now masks both of them.
    /// <see cref="Page_OnAcceptKeyPressed_TextInputBlock"/> below is
    /// kept as defence-in-depth for modded/future pages that re-enable closeOnAccept. (Cancel
    /// needs no Page-level twin for THIS hook: <c>Page.OnCancelKeyPressed</c> DOES call
    /// <c>base.OnCancelKeyPressed()</c> — but note the analogous in-pass Cancel path at
    /// Page.cs:58, <c>KeyBindingDefOf.Cancel.KeyDownEvent</c> in DoBottomButtons, has the same
    /// unblockable-by-hook property and is masked by that same patch.)
    /// </summary>
    [HarmonyPatch(typeof(Window), "OnAcceptKeyPressed")]
    public static class Window_OnAcceptKeyPressed_TextInputBlock
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            if (TextInputManager.IsActive || TextInputManager.HandledEventThisFrame)
                return false;
            return true;
        }
    }

    /// <summary>
    /// The Page-hosted twin of <see cref="Window_OnAcceptKeyPressed_TextInputBlock"/> — see that
    /// class's remarks: for vanilla pages this guards a body that already no-ops
    /// (closeOnAccept=false), so it is DEFENCE-IN-DEPTH only, kept for modded or future Page
    /// subclasses that re-enable closeOnAccept. It does NOT protect the real in-pass
    /// Enter-advance path (Page.DoBottomButtons, doNextOnKeypress) — nothing hook-based can.
    /// Neither confirmed live host (Page_ScenarioEditor, Page_CreateWorldParams) overrides
    /// OnAcceptKeyPressed further, so patching Page itself covers the family via virtual dispatch.
    /// </summary>
    [HarmonyPatch(typeof(Page), "OnAcceptKeyPressed")]
    public static class Page_OnAcceptKeyPressed_TextInputBlock
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            if (TextInputManager.IsActive || TextInputManager.HandledEventThisFrame)
                return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(Window), "OnCancelKeyPressed")]
    public static class Window_OnCancelKeyPressed_TextInputBlock
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            if (TextInputManager.IsActive || TextInputManager.HandledEventThisFrame)
                return false;
            return true;
        }
    }

    /// <summary>
    /// Closes the one path the three hook patches above cannot reach: the raw in-pass polls in
    /// <c>Page.DoBottomButtons</c> (decompiled Page.cs:58 Cancel -&gt; CanDoBack -&gt; DoBack,
    /// :69 Accept -&gt; CanDoNext -&gt; DoNext), which read <c>KeyBindingDefOf.*.KeyDownEvent</c>
    /// inside the page's own GUI pass — BEFORE the dispatcher under window-pass-first ordering
    /// (QA R6), so no scope claim, frame stamp, or OnCancel/OnAccept hook can precede them. The
    /// per-page <c>CanDoBack</c>/<c>CanDoNext</c> guard twins all key on <c>FocusStack.Top</c> and
    /// miss the modal text session, whose scope sits ABOVE the page scope (one
    /// Escape during a starting-pawn name edit backed out both the pawn page and the ideo page).
    ///
    /// While a session is live (or handled an event this frame — the post-confirm race the hook
    /// patches also cover), the event's keyCode is masked to <see cref="KeyCode.None"/> around the
    /// body and restored after, so both polls read <c>KeyDownEvent</c> false. Masking only a
    /// Cancel/Accept KeyDown keeps every other event — mouse clicks on the Back/Next buttons
    /// included — untouched, and the postfix restore keeps later passes pristine (the
    /// <see cref="RimWorldAccess.Shell.TextFieldRawPollGuard"/> mask-and-restore shape;
    /// DoBottomButtons is non-virtual, so this single site covers every Page subclass, present
    /// and future).
    /// </summary>
    [HarmonyPatch(typeof(Page), "DoBottomButtons")]
    public static class Page_DoBottomButtons_TextInputMask
    {
        [HarmonyPrefix]
        public static void Prefix(out KeyCode __state)
        {
            __state = KeyCode.None;
            if (!TextInputManager.IsActive && !TextInputManager.HandledEventThisFrame)
                return;
            Event e = Event.current;
            if (e == null)
                return;
            if (!KeyBindingDefOf.Cancel.KeyDownEvent && !KeyBindingDefOf.Accept.KeyDownEvent)
                return;
            __state = e.keyCode;
            e.keyCode = KeyCode.None;
        }

        [HarmonyPostfix]
        public static void Postfix(KeyCode __state)
        {
            if (__state == KeyCode.None)
                return;
            Event e = Event.current;
            if (e != null)
                e.keyCode = __state;
        }
    }
}
