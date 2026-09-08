using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    // Lifecycle patches for the saved-ideoligion load picker (Dialog_IdeoList_Load). Keyboard
    // input and Escape/Enter ownership are now driven by RimWorldAccess.Shell.IdeoLoadScope
    // (registered on Dialog_IdeoList_Load in ShellBootstrap.Game.cs, IdeoBuilder shell
    // migration slice IB-1) — see that scope's class remarks for the Escape posture and the
    // empty-list gating this file used to do per-frame.
    //
    // RETIRED (slice IB-1): the DoWindowContents prefix, verbatim:
    //
    //   [HarmonyPatch(typeof(Dialog_FileList), "DoWindowContents")]
    //   public static class IdeoLoadPatch
    //   {
    //       static bool Prefix(Window __instance)
    //       {
    //           if (!(__instance is Dialog_IdeoList_Load loadDialog)) return true;
    //           IdeoLoadState.EnsureOpen(loadDialog);
    //           if (RimWorldAccess.Shell.FocusStack.AnyLiveModal) return true;
    //           if (Event.current.type == EventType.KeyDown)
    //               if (IdeoLoadState.HandleInput(Event.current)) Event.current.Use();
    //           return true;
    //       }
    //   }
    //
    // A dispatcher-driven scope needs no per-frame prefix: EnsureOpen moves to the PostOpen
    // postfix below (it is ReferenceEquals-idempotent, so calling it once on open instead of
    // every DoWindowContents pass is equivalent), and the AnyLiveModal yield is now ordinary
    // modal-stack masking via IdeoLoadScope's ForeignWindowAbove fold (the only real child,
    // the delete-confirmation Dialog_MessageBox, is already ScopeForWindow-registered — see
    // that scope's class remarks).
    //
    // RETIRED (slice IB-1): the conditional Window.OnCancelKeyPressed blocker, verbatim:
    //
    //   [HarmonyPatch(typeof(Window), "OnCancelKeyPressed")]
    //   public static class IdeoLoadPatch_OnCancel
    //   {
    //       [HarmonyPrefix]
    //       static bool Prefix(Window __instance)
    //       {
    //           if (__instance is Dialog_IdeoList_Load && IdeoLoadState.IsActive && IdeoLoadState.HasActiveSearch)
    //               return false;
    //           return true;
    //       }
    //   }
    //
    // Replaced by IdeoLoadScope.OwnsCancel = false (the J1 shape) plus the scope's own Cancel
    // claim (search-clear only) stamping ShellFrameStamps.MarkCancelConsumed — an idle Escape
    // is left UNCLAIMED so WindowCancelKeyRouterPatch defers and vanilla's real closeOnCancel
    // body runs, exactly as this blocker's own fallthrough used to preserve.

    /// <summary>
    /// Opens IdeoLoadState for the shell scope. The manual IMGUI focus grab this postfix used
    /// to also perform (Find.WindowStack.Notify_ManuallySetFocus) is dropped: a
    /// dispatcher-driven scope does not need Unity's IMGUI focus (ShellDispatcherPatch runs
    /// once per frame from UIRoot.UIRootOnGUI regardless of which window holds it). Patches
    /// Window.PostOpen (the declaring type): decompiled-verified that
    /// nothing in the Dialog_FileList/Dialog_IdeoList/Dialog_IdeoList_Load chain overrides
    /// PostOpen, so no declaring-type trap applies here.
    /// </summary>
    [HarmonyPatch(typeof(Window), "PostOpen")]
    public static class IdeoLoadPatch_PostOpen
    {
        [HarmonyPostfix]
        static void Postfix(Window __instance)
        {
            if (__instance is Dialog_IdeoList_Load loadDialog)
                IdeoLoadState.EnsureOpen(loadDialog);
        }
    }

    [HarmonyPatch(typeof(Window), "PostClose")]
    public static class IdeoLoadPatch_PostClose
    {
        [HarmonyPostfix]
        static void Postfix(Window __instance)
        {
            if (__instance is Dialog_IdeoList_Load)
                IdeoLoadState.Close();
        }
    }
}
