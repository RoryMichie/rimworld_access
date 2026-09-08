using System;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patches for Dialog_BeginLordJob and its subclasses (ritual, psychic ritual, gravship
    /// launch): keeps LordJobDialogState synchronized with the dialog lifecycle and blocks vanilla's
    /// Accept/Cancel handling while our keyboard navigation is active.
    /// </summary>
    public static class RitualPatch
    {
        /// <summary>Dialog_BeginRitual finishes FillPawns and the per-comp notify loop inside PostOpen,
        /// so a postfix sees completed state. Gravship launch inherits this PostOpen.</summary>
        [HarmonyPatch(typeof(Dialog_BeginRitual), "PostOpen")]
        public static class Dialog_BeginRitual_PostOpen_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Dialog_BeginRitual __instance)
            {
                try
                {
                    LordJobDialogState.Open(__instance);
                }
                catch (Exception ex)
                {
                    Log.Error($"[RitualPatch] Error in Dialog_BeginRitual.PostOpen: {ex.Message}");
                }
            }
        }

        /// <summary>Dialog_BeginPsychicRitual does not override PostOpen, so patch base Window.PostOpen
        /// and filter. Its constructor fills assignments before PostOpen fires.</summary>
        [HarmonyPatch(typeof(Window), "PostOpen")]
        public static class Window_PostOpen_PsychicRitual_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Window __instance)
            {
                if (!(__instance is Dialog_BeginPsychicRitual psychic)) return;
                try
                {
                    LordJobDialogState.Open(psychic);
                }
                catch (Exception ex)
                {
                    Log.Error($"[RitualPatch] Error in Window.PostOpen psychic: {ex.Message}");
                }
            }
        }

        /// <summary>Fires however the dialog closes (Cancel, Start, click outside): our state must
        /// follow. The closing phrase comes from the adapter, one wording per dialog type.</summary>
        [HarmonyPatch(typeof(Window), "PostClose")]
        public static class Window_PostClose_LordJob_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Window __instance)
            {
                if (!(__instance is Dialog_BeginLordJob)) return;
                try
                {
                    if (LordJobDialogState.IsActive)
                    {
                        string announcement = LordJobDialogState.ClosingAnnouncement;
                        LordJobDialogState.Close();
                        if (!string.IsNullOrEmpty(announcement))
                            TolkHelper.SpeakData(announcement);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[RitualPatch] Error in PostClose: {ex.Message}");
                }
            }
        }

        // Start() is overridden by each subclass and Harmony only intercepts the declaring type, so
        // each concrete subclass is patched separately. The shared prefix skips the original while
        // LordJobDialogState is active so Enter never begins the job behind the player; Alt+S clears
        // IsActive first so its own Start gets through.

        public static bool LordJobStartPrefix()
        {
            if (LordJobDialogState.IsActive) return false;
            return true;
        }

        [HarmonyPatch(typeof(Dialog_BeginRitual), "Start")]
        public static class Dialog_BeginRitual_Start_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix() => LordJobStartPrefix();
        }

        // Dialog_BeginPsychicRitual.Start and Dialog_BeginGravshipLaunch.Start are patched manually in
        // Core/rimworld_access.cs via AccessTools.TypeByName, keeping the DLC typerefs lazy.

        /// <summary>
        /// Catches Enter before it reaches Start(): KeyBindingDefOf.Accept.KeyDownEvent triggers
        /// OnAcceptKeyPressed independently of our handling, and Event.current.Use() does not stop it.
        /// Patched on Dialog_BeginLordJob, NOT Window: the dialog overrides OnAcceptKeyPressed with no
        /// base call, so virtual dispatch never enters a Window-declared patch body.
        /// </summary>
        [HarmonyPatch(typeof(Dialog_BeginLordJob), "OnAcceptKeyPressed")]
        public static class Dialog_BeginLordJob_OnAcceptKeyPressed_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix()
            {
                if (LordJobDialogState.IsActive) return false;
                // Deliberately the only term: StatBreakdown stamps MarkAcceptConsumed (honored by
                // WindowAcceptKeyRouterPatch) and inspection stands down beneath a window-attached
                // dialog, so adding either back kills Enter for the opened-from-inspection flow.
                return true;
            }
        }

        /// <summary>Blocks vanilla Cancel while a submenu (pawn selection, quality stats) owns Escape.</summary>
        [HarmonyPatch(typeof(Window), "OnCancelKeyPressed")]
        public static class Window_OnCancelKeyPressed_LordJob_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(Window __instance)
            {
                if (!(__instance is Dialog_BeginLordJob)) return true;

                // StatBreakdown and inspection terms are deliberately absent: StatBreakdown stamps
                // MarkCancelConsumed for WindowCancelKeyRouterPatch and inspection stands down beneath
                // this dialog. Either term leaves Escape dead in the Roles region.

                if (LordJobDialogState.IsActive)
                {
                    var mode = LordJobDialogState.CurrentNavigationMode;
                    if (mode != LordJobDialogState.NavigationMode.RoleList) return false;
                    if (LordJobDialogState.HasActiveTypeahead) return false;
                }
                return true;
            }
        }

        /// <summary>Draws the keyboard-mode badge while our state drives the dialog; patched on
        /// Dialog_BeginLordJob.DoWindowContents so every subclass shares it.</summary>
        [HarmonyPatch(typeof(Dialog_BeginLordJob), "DoWindowContents")]
        public static class Dialog_BeginLordJob_DoWindowContents_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Dialog_BeginLordJob __instance, Rect inRect)
            {
                if (!LordJobDialogState.IsActive) return;
                try
                {
                    DrawKeyboardModeIndicator(inRect);
                }
                catch
                {
                    // tolerate drawing errors
                }
            }

            private static void DrawKeyboardModeIndicator(Rect inRect)
            {
                float w = 250f;
                float h = 30f;
                Rect r = new Rect(inRect.x + 10f, inRect.y + 10f, w, h);

                Widgets.DrawBoxSolid(r, new Color(0.2f, 0.4f, 0.6f, 0.85f));
                Widgets.DrawBox(r, 1);

                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(r, (string)"RimWorldAccess.Rituals.Dialog.KeyboardModeIndicator".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;
            }
        }
    }
}
