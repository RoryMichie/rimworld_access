using HarmonyLib;
using RimWorld;
using UnityEngine;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patches for Page_ModsConfig lifecycle. Keyboard input and Escape ownership belong to
    /// <see cref="RimWorldAccess.Shell.ModListScreenScope"/>; this file hosts only the lifecycle
    /// hooks and the window-pass twin guard below.
    /// </summary>
    [HarmonyPatch]
    public static class ModListPatch
    {
        /// <summary>Initializes the mod-list state when the page opens.</summary>
        [HarmonyPatch(typeof(Page_ModsConfig), "PreOpen")]
        [HarmonyPostfix]
        public static void PreOpen_Postfix(Page_ModsConfig __instance)
        {
            ModListState.Open(__instance);
        }

        /// <summary>
        /// Page_ModsConfig declares its own <c>PostClose</c>, which runs exactly once on the real
        /// close path, after the unsaved-changes dialog's accept or discard resolves it.
        /// <see cref="ModListState.Close"/> is idempotent regardless.
        /// </summary>
        [HarmonyPatch(typeof(Page_ModsConfig), "PostClose")]
        [HarmonyPostfix]
        public static void PostClose_Postfix()
        {
            ModListState.Close();
        }

        // Same-frame dedupe for the unsaved-changes confirmation. Page.OnCancelKeyPressed runs its
        // own closeOnCancel body and THEN calls base.OnCancelKeyPressed, which closes again when
        // closeOnCancel is set, so one Escape on a dirty mods page issues two Close() calls in one
        // frame and OnCloseRequest, which has no re-entry guard, spawns two identical confirmations.
        // The frame stamp is language-independent and otherwise behavior-neutral: the first refusal
        // per frame runs vanilla's real body, and repeats that frame report "refused" without
        // spawning.
        private static int closeRefusedFrame = -1;

        [HarmonyPatch(typeof(Page_ModsConfig), "OnCloseRequest")]
        [HarmonyPrefix]
        public static bool OnCloseRequest_DedupePrefix(ref bool __result)
        {
            if (closeRefusedFrame == Time.frameCount)
            {
                __result = false;
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(Page_ModsConfig), "OnCloseRequest")]
        [HarmonyPostfix]
        public static void OnCloseRequest_DedupePostfix(bool __result)
        {
            if (!__result)
            {
                closeRefusedFrame = Time.frameCount;
            }
        }

        /// <summary>
        /// The window-pass twin guard. Vanilla handles Up/Down inside
        /// <c>Page_ModsConfig.DoWindowContents</c> itself, and the FOCUSED WINDOW's pass runs BEFORE
        /// the dispatcher's main pass (QA R4), so this guard must NOT <c>Use()</c> the event — doing
        /// so kills every real Up/Down before the dispatcher can claim it, a regression injected
        /// chords cannot reproduce because they skip window passes. Instead the keyCode is masked to
        /// None for exactly the page's own body and restored by the postfix twin: vanilla's handler
        /// sees no arrow, while every other window's pass and the dispatcher's main pass see the
        /// pristine KeyDown. The mask being invisible outside the page's body, no window-above stand
        /// down is needed. A draw exception would skip the restore and deaden that one event only.
        /// </summary>
        [HarmonyPatch(typeof(Page_ModsConfig), "DoWindowContents")]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.High)]
        public static void ModListWindowKeyGuardPatch_Prefix(Rect rect)
        {
            maskedArrowKey = KeyCode.None;
            Event e = Event.current;
            if (e == null || e.type != EventType.KeyDown)
            {
                return;
            }
            if (e.keyCode != KeyCode.UpArrow && e.keyCode != KeyCode.DownArrow)
            {
                return;
            }
            if (!ModListState.IsActive)
            {
                return;
            }
            maskedArrowKey = e.keyCode;
            e.keyCode = KeyCode.None;
        }

        /// <summary>Restores the arrow keyCode masked by the prefix twin above.</summary>
        [HarmonyPatch(typeof(Page_ModsConfig), "DoWindowContents")]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Low)]
        public static void ModListWindowKeyGuardPatch_Postfix()
        {
            if (maskedArrowKey == KeyCode.None)
            {
                return;
            }
            Event e = Event.current;
            if (e != null)
            {
                e.keyCode = maskedArrowKey;
            }
            maskedArrowKey = KeyCode.None;
        }

        private static KeyCode maskedArrowKey = KeyCode.None;
    }

    // No Page.OnCancelKeyPressed blocker lives here: ModListScreenScope needs no OwnsCancel
    // override, the base ScreenScope's state-based OwnsCancel covering the one mod-handled case,
    // a live typeahead search.
}
