using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patches for Dialog_ModSettings to announce when mod settings dialogs open and close.
    /// This is the FALLBACK tier: when the GenericWindowScope reader attaches to the dialog, the
    /// scope owns the open/close announcements and full navigation, and both patches here stand
    /// down via ScopeForWindow.HasAttachedScope. When the scope skips the dialog (not eligible),
    /// this announce-only tier keeps the dialog from opening silently.
    /// </summary>
    [HarmonyPatch]
    public static class ModSettingsDialogPatch
    {
        // Cache reflection for the mod field
        private static FieldInfo modField;

        // Track which dialogs have been announced to avoid repeating
        private static HashSet<int> announcedDialogs = new HashSet<int>();

        static ModSettingsDialogPatch()
        {
            modField = typeof(Dialog_ModSettings).GetField("mod", BindingFlags.NonPublic | BindingFlags.Instance);
        }

        /// <summary>
        /// Patch DoWindowContents to announce on first render and track dialog lifecycle.
        /// Dialog_ModSettings doesn't override PostOpen/PostClose.
        /// </summary>
        [HarmonyPatch(typeof(Dialog_ModSettings), "DoWindowContents")]
        [HarmonyPostfix]
        public static void DoWindowContents_Postfix(Dialog_ModSettings __instance, Rect inRect)
        {
            int instanceId = __instance.GetHashCode();

            // The generic reader scope, when it owns this dialog, does its own richer open
            // announcement and full navigation. Ask ELIGIBILITY, not the attach state: the
            // dialog draws a teardown pass after its scope detaches, and a check on the
            // attach state speaks the open line into that gap.
            if (Shell.ScopeForWindow.GenericReaderEligible(__instance))
                return;

            // Announce only on first render
            if (!announcedDialogs.Contains(instanceId))
            {
                announcedDialogs.Add(instanceId);

                var mod = modField?.GetValue(__instance) as Mod;
                if (mod != null)
                {
                    string modName = mod.SettingsCategory();
                    if (modName.NullOrEmpty())
                    {
                        modName = mod.Content?.Name ?? "RimWorldAccess.ModSettings.UnknownMod".Translate().ToString();
                    }
                    TolkHelper.Speak("RimWorldAccess.ModSettings.OpenedFor".Loc(modName));
                }
                else
                {
                    TolkHelper.Speak("RimWorldAccess.ModSettings.OpenedGeneric".Loc());
                }
            }
        }

        /// <summary>
        /// Patch the base Window.PostClose to detect when Dialog_ModSettings closes.
        /// </summary>
        [HarmonyPatch(typeof(Window), "PostClose")]
        [HarmonyPostfix]
        public static void Window_PostClose_Postfix(Window __instance)
        {
            if (__instance is Dialog_ModSettings)
            {
                int instanceId = __instance.GetHashCode();
                bool announcedHere = announcedDialogs.Remove(instanceId);
                // If the generic reader scope owned this dialog, its pop/refocus is the close
                // signal — a second "closed" line here would double-speak.
                if (announcedHere)
                    TolkHelper.Speak("RimWorldAccess.ModSettings.Closed".Loc());
            }
        }
    }
}
