using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    // Lifecycle patches for the structure/normal meme picker (Dialog_ChooseMemes). Keyboard
    // input and Escape/Enter ownership are now driven by RimWorldAccess.Shell.IdeoMemeScreenScope
    // (registered on Dialog_ChooseMemes in ShellBootstrap.Game.cs)
    // plus the WindowKeyRouter.Game.cs MemeSelectionAcceptKeyRouterPatch subtype twin — see that
    // scope's class remarks for the Escape/Enter posture.
    //
    // RETIRED (slice IB-2): the DoWindowContents prefix, verbatim:
    //
    //   [HarmonyPatch(typeof(Dialog_ChooseMemes), "DoWindowContents")]
    //   public static class IdeoMemeSelectionPatch
    //   {
    //       static bool Prefix(Dialog_ChooseMemes __instance, Rect rect)
    //       {
    //           if (WindowlessFloatMenuState.IsActive) return false;
    //           if (TextInputManager.Active != null) return false;
    //           if (RimWorldAccess.Shell.FocusStack.AnyLiveModal) return true;
    //           IdeoMemeSelectionState.EnsureOpen(__instance);
    //           if (Event.current.type == EventType.KeyDown)
    //               if (IdeoMemeSelectionState.HandleInput(Event.current)) Event.current.Use();
    //           return true;
    //       }
    //   }
    //
    // A dispatcher-driven scope needs no per-frame prefix: EnsureOpen moves to the PostOpen
    // postfix below (ReferenceEquals-idempotent, so calling it once on open is equivalent), and the
    // AnyLiveModal yield is now ordinary FocusStack modal-stack masking — the "changing memes/
    // structure randomizes precepts" confirmation boxes are Dialog_MessageBox, itself a real
    // ScopeForWindow-registered ScreenScope since the windowless-dialog-system removal, so no
    // bespoke ForeignWindowAbove fold is needed on this scope any more (IdeoMemeSelectionScope, the
    // retired IB-2 shim, predates that removal and carried one).
    //
    // RETIRED (slice IB-2): the Accept blocker, verbatim:
    //
    //   [HarmonyPatch(typeof(Dialog_ChooseMemes), "OnAcceptKeyPressed")]
    //   public static class IdeoMemeSelectionPatch_OnAccept
    //   {
    //       [HarmonyPrefix]
    //       static bool Prefix() { return !IdeoMemeSelectionState.IsActive; }
    //   }
    //
    // Replaced by MemeSelectionAcceptKeyRouterPatch (WindowKeyRouter.Game.cs) — the same
    // declaring-type-trap twin shape as MessageBoxAcceptKeyRouterPatch — consulting
    // IdeoMemeScreenScope.OwnsAccept = true (the base ScreenScope default).
    //
    // RETIRED (slice IB-2): the Cancel blocker, verbatim:
    //
    //   [HarmonyPatch(typeof(Window), "OnCancelKeyPressed")]
    //   public static class IdeoMemeSelectionPatch_OnCancel
    //   {
    //       [HarmonyPrefix]
    //       static bool Prefix(Window __instance)
    //       {
    //           if (__instance is Dialog_ChooseMemes && IdeoMemeSelectionState.IsActive) return false;
    //           return true;
    //       }
    //   }
    //
    // Dialog_ChooseMemes does not override OnCancelKeyPressed (decompiled-verified), so no
    // subtype twin is needed — WindowCancelKeyRouterPatch reaches it directly, consulting
    // IdeoMemeScreenScope.OwnsCancel = true (claimed unconditionally).

    /// <summary>
    /// Opens IdeoMemeSelectionState for the shell scope. The manual IMGUI focus grab this
    /// postfix used to also perform (Find.WindowStack.Notify_ManuallySetFocus) is dropped: a
    /// dispatcher-driven scope does not need Unity's IMGUI focus (ShellDispatcherPatch runs once
    /// per frame from UIRoot.UIRootOnGUI regardless of which window holds it). Patches
    /// Window.PostOpen (the declaring type): decompiled-verified that
    /// Dialog_ChooseMemes does not override PostOpen, so no declaring-type trap applies here.
    /// </summary>
    [HarmonyPatch(typeof(Window), "PostOpen")]
    public static class IdeoMemeSelectionPatch_PostOpen
    {
        [HarmonyPostfix]
        static void Postfix(Window __instance)
        {
            if (__instance is Dialog_ChooseMemes memeDialog)
                IdeoMemeSelectionState.EnsureOpen(memeDialog);
        }
    }

    /// <summary>
    /// Resets the meme-picker state when the dialog closes.
    /// </summary>
    [HarmonyPatch(typeof(Window), "PostClose")]
    public static class IdeoMemeSelectionPatch_PostClose
    {
        [HarmonyPostfix]
        static void Postfix(Window __instance)
        {
            if (__instance is Dialog_ChooseMemes)
                IdeoMemeSelectionState.Close();
        }
    }
}
