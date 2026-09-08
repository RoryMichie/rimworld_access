using HarmonyLib;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    // Lifecycle patches for the Archonexus relocation selection screen
    // (Dialog_ChooseThingsForNewColony). Keyboard input and Escape/Enter ownership belong to
    // RimWorldAccess.Shell.ArchonexusColonyScope; its class remarks carry the Escape posture and
    // the empty-sections/pawn-info gating. A dispatcher-driven scope needs no window focus
    // reclaim, and the info-card/modal yields are ordinary modal-stack masking, both children
    // being ScopeForWindow-registered.

    /// <summary>
    /// Opens ArchonexusColonyState for the shell scope. Patched on the dialog's OWN PostOpen, not
    /// Window.PostOpen: Dialog_ChooseThingsForNewColony overrides PostOpen and does NOT call base,
    /// so a declaring-type patch never fires for it. EnsureOpen is idempotent per instance.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_ChooseThingsForNewColony), "PostOpen")]
    public static class ArchonexusColonyPatch_PostOpen
    {
        [HarmonyPostfix]
        static void Postfix(Dialog_ChooseThingsForNewColony __instance)
        {
            ArchonexusColonyState.EnsureOpen(__instance);
        }
    }

    [HarmonyPatch(typeof(Window), "PostClose")]
    public static class ArchonexusColonyPatch_PostClose
    {
        [HarmonyPostfix]
        static void Postfix(Window __instance)
        {
            if (__instance is Dialog_ChooseThingsForNewColony)
                ArchonexusColonyState.Close();
        }
    }

    /// <summary>
    /// The declaring-type trap, instantiated for <see cref="Dialog_ChooseThingsForNewColony"/>
    /// (QA R6): it overrides <c>OnCancelKeyPressed</c> as
    /// <c>base.OnCancelKeyPressed(); if (cancel != null) cancel();</c>
    /// (Dialog_ChooseThingsForNewColony.cs:400-407). <see cref="WindowCancelKeyRouterPatch"/> does
    /// fire on the <c>base</c> call — a direct, non-virtual call into the patched method — but a
    /// false return there skips only Window's own Close() body, while the override's
    /// <c>cancel()</c> tail lives in a different, unpatched method and runs regardless, firing the
    /// questline's cancel callback on a search-active Escape. This twin patches the override
    /// directly and delegates to the same rule, so a false return skips the WHOLE override. On an
    /// unblocked frame it returns true and the override runs in full, its base call harmlessly
    /// re-checked by the router itself.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_ChooseThingsForNewColony), "OnCancelKeyPressed")]
    public static class ArchonexusColonyPatch_OnCancelKeyPressed
    {
        [HarmonyPrefix]
        public static bool Prefix(Dialog_ChooseThingsForNewColony __instance)
        {
            return WindowCancelKeyRouterPatch.Prefix(__instance);
        }
    }
}
