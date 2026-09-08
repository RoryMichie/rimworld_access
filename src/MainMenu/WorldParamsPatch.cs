using HarmonyLib;
using RimWorld;
using Verse;
using RimWorldAccess.Shell;

namespace RimWorldAccess
{
    /// <summary>
    /// Support for the world-parameters page (<see cref="Page_CreateWorldParams"/>).
    /// All keyboard navigation and announcement now lives in
    /// <see cref="WorldParamsScreenScope"/> (src/Shell/Screens/WorldParamsScreenScope.Game.cs);
    /// this patch keeps only the minimal per-GUI-pass work the shell cannot do
    /// from outside the page's own draw, the close-side reset, and the two
    /// wizard-advance poll guards.
    ///
    /// The following no longer exist: the in-pass KeyDown block (Tab
    /// section toggle, per-section arrows/typeahead, Left/Right value edits,
    /// Enter-on-seed, R randomize, Delete faction, Alt+A add menu, the add-menu
    /// consume-all, inline typeahead chars — all now WorldParamsScreenScope +
    /// WorldParamsAddFactionScope), the one-shot title announcement (the scope's
    /// own first-focus announcement replaces it), the current-field indicator box,
    /// and the HostFocusReturn per-frame focus steal (the shell owns focus now).
    /// The Prefix survives only to drive the seed edit session's live mirror
    /// (WorldParamsScreenScope.OnPageDrawPass), which must run inside the page's own
    /// GUI pass so the vanilla seed TextField renders the buffer and Escape keeps it.
    /// </summary>
    [HarmonyPatch(typeof(Page_CreateWorldParams))]
    [HarmonyPatch("DoWindowContents")]
    public class WorldParamsPatch
    {
        static void Prefix()
        {
            try
            {
                WorldParamsScreenScope.Active?.OnPageDrawPass();
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimWorld Access] Error in WorldParamsPatch Prefix: {ex}");
            }
        }
    }

    // Reset faction-overlay state when the page opens.
    [HarmonyPatch(typeof(Page_CreateWorldParams), "PreOpen")]
    public class WorldParamsPatch_PreOpen
    {
        [HarmonyPostfix]
        static void Postfix()
        {
            FactionsNavigationState.Reset();
        }
    }

    /// <summary>
    /// Deterministic close-side reset (wave-I law 7). FactionsNavigationState.Reset
    /// resets the FactionAddMenuState overlay flags that drive the MODAL
    /// <see cref="WorldParamsAddFactionScope"/> mirror, so a flag leaking past page
    /// close (a mouse click on Next/Back advances while the windowless add menu is
    /// open, and CanDoNext's async world gen closes the page from a long event)
    /// cannot black out the next screen. We patch Window.PreClose because
    /// Page_CreateWorldParams doesn't override it (the StorytellerSelectionPatch
    /// precedent). The scope's own OnPop cancels a dangling seed edit session and
    /// unbinds the page bridge.
    /// </summary>
    [HarmonyPatch(typeof(Window), "PreClose")]
    public class WorldParamsPatch_PreClose
    {
        [HarmonyPostfix]
        static void Postfix(Window __instance)
        {
            if (__instance is Page_CreateWorldParams)
            {
                FactionsNavigationState.Reset();
            }
        }
    }

    /// <summary>
    /// Wizard-advance guard: blocks the raw keyboard Accept poll (Page.DoBottomButtons,
    /// Page.cs:69) so an Enter this scope consumed for row activation, seed editing, or
    /// a windowless picker/add-menu selection cannot ALSO fire the page's own
    /// CanDoNext — which for this page QUEUES async world generation as a side effect
    /// and returns false. The raw poll fires inside the page's GUI pass, which can run
    /// BEFORE the shell dispatcher stamps ShellFrameStamps (QA-R6 live-verified), so a
    /// same-frame stamp is set too late; this guard keys on STABLE scope state instead.
    ///
    /// State-based: blocks whenever the keyboard Accept poll fires while this page's
    /// scope is live (browse, seed edit — the scope stays FocusStack.Top), or one of
    /// its windowless overlays sits above it (the add-menu, a combo picker — the
    /// overlay is Top, so the page scope must be recognized through its explicit
    /// terms), UNLESS the Generate declared action set AdvanceRequested for its own
    /// explicit CanDoNext call. Gated on the keyboard Accept event so a mouse click on
    /// "Generate" (or any picker option) still works.
    /// </summary>
    [HarmonyPatch(typeof(Page_CreateWorldParams), "CanDoNext")]
    public class WorldParamsPatch_CanDoNext
    {
        [HarmonyPrefix]
        static bool Prefix(ref bool __result)
        {
            if (KeyBindingDefOf.Accept.KeyDownEvent
                && !WorldParamsScreenScope.AdvanceRequested
                && (FocusStack.Top is WorldParamsScreenScope
                    || FactionsNavigationState.IsAddMenuOpen
                    || WindowlessFloatMenuState.IsActive))
            {
                __result = false;
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Back-navigation guard twin. Page_CreateWorldParams does not override CanDoBack
    /// (decompiled-verified), so this patches the declaring type (RimWorld.Page) with
    /// an instance guard — the same posture as every other CanDoBack twin in this wave
    /// (each independently instance-gated so they coexist). Escape now legitimately
    /// reaches vanilla's Back EXCEPT while the scope would itself claim that Escape for
    /// something other than leaving the page: a live typeahead search-clear, or an open
    /// windowless overlay (the add menu, a combo picker) whose own Escape closes it.
    /// </summary>
    [HarmonyPatch(typeof(Page), "CanDoBack")]
    public class WorldParamsPatch_CanDoBack
    {
        [HarmonyPrefix]
        static bool Prefix(Page __instance, ref bool __result)
        {
            if (!(__instance is Page_CreateWorldParams))
            {
                return true;
            }
            // The scope claims Escape and runs the Back gate itself
            // (WorldParamsScreenScope.EscapeBack — the only caller that sets
            // BackRequested); the raw Cancel poll in the page's window pass is
            // blocked outright so pass ordering can never double-fire Back or
            // starve it (the dispatcher's modal swallow ate the key when this
            // page's window pass ran second — live-traced 2026-07-22).
            if (KeyBindingDefOf.Cancel.KeyDownEvent
                && !WorldParamsScreenScope.BackRequested
                && (FactionsNavigationState.IsAddMenuOpen
                    || WindowlessFloatMenuState.IsActive
                    || FocusStack.Top is WorldParamsScreenScope))
            {
                __result = false;
                return false;
            }
            return true;
        }
    }
}
