using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Marks a gene-dialog scope dirty when ANY path — vanilla load callbacks, vanilla buttons,
    /// our facades — changes the gene set. Consumed (deferred) by
    /// <c>GeneDialogScopeBase.RefreshContent</c>; deferred because vanilla's Load custom callback
    /// assigns <c>ignoreRestrictions</c> AFTER calling OnGenesChanged, so a synchronous rebuild
    /// would build the Library tree against the stale flag.
    ///
    /// Patched on the DECLARING type: Dialog_CreateXenotype's override calls base, and
    /// Dialog_CreateXenogerm has no override, so both dialogs are covered by this one patch.
    /// </summary>
    [HarmonyPatch(typeof(GeneCreationDialogBase), "OnGenesChanged")]
    public static class GeneDialogGenesChangedPatch
    {
        /// <summary>The dialog whose gene set changed since its scope last rebuilt; weak so a
        /// closed dialog is not pinned by the signal.</summary>
        private static System.WeakReference<Window> pending;

        [HarmonyPostfix]
        public static void Postfix(GeneCreationDialogBase __instance)
        {
            pending = __instance == null ? null : new System.WeakReference<Window>(__instance);
        }

        /// <summary>True when <paramref name="window"/> has an unconsumed change; consumes it.</summary>
        public static bool ConsumeIfPending(Window window)
        {
            if (!IsPendingFor(window))
            {
                return false;
            }
            pending = null;
            return true;
        }

        /// <summary>Clears a pending signal the caller just satisfied with its own rebuild.</summary>
        public static void ClearFor(Window window)
        {
            if (IsPendingFor(window))
            {
                pending = null;
            }
        }

        private static bool IsPendingFor(Window window)
        {
            Window target;
            return window != null
                && pending != null
                && pending.TryGetTarget(out target)
                && ReferenceEquals(target, window);
        }
    }
}
