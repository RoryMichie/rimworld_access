using System;
using System.Reflection;
using HarmonyLib;
using RimWorldAccess.Shell;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Wires the mod's three bespoke dialogs to the generic window reader. The two
    /// card dialogs set absorbInputAroundWindow=false, so no scope attaches without
    /// an explicit registration while closeOnClickedOutside still deafens the map
    /// beneath. All three also run a HandleCloseInput that closes on ANY Return
    /// KeyDown inside their own GUI pass, which can run before the shell dispatcher
    /// and shares Event state — so Enter meant to activate a focused row would
    /// close the whole dialog. Per shell doctrine the prefix masks the keyCode
    /// around the vanilla body whenever a scope is attached and the postfix
    /// restores it; Escape is left alone (their close-on-Escape matches vanilla
    /// closeOnCancel).
    /// </summary>
    internal static class RjwDialogCompat
    {
        private static readonly string[] dialogTypeNames =
        {
            "rjw.Dialog_SexcardNG",
            "rjw.Dialog_PartcardNG",
            "rjw.MainTab.Dialog_DesignateHero",
        };

        public static void Register(Harmony harmony)
        {
            int readers = 0;
            if (ScopeForWindow.TryRegisterGenericReaderForWindow("rjw.Dialog_SexcardNG"))
            {
                readers++;
            }
            if (ScopeForWindow.TryRegisterGenericReaderForWindow("rjw.Dialog_PartcardNG"))
            {
                readers++;
            }

            int masked = 0;
            foreach (string typeName in dialogTypeNames)
            {
                Type dialogType = AccessTools.TypeByName(typeName);
                MethodInfo body = dialogType != null
                    ? AccessTools.Method(dialogType, "DoWindowContents")
                    : null;
                if (body == null)
                {
                    continue;
                }
                harmony.Patch(body,
                    prefix: new HarmonyMethod(typeof(RjwDialogCompat), nameof(MaskAcceptPrefix)),
                    postfix: new HarmonyMethod(typeof(RjwDialogCompat), nameof(RestoreAcceptPostfix)));
                masked++;
            }

            if (readers > 0 || masked > 0)
            {
                ModLogger.Msg($"Rjw compat: registered {readers} dialog reader(s), masked Enter on {masked} dialog(s)");
            }
        }

        public static void MaskAcceptPrefix(Window __instance, out KeyCode? __state)
        {
            __state = null;
            try
            {
                if (ScopeForWindow.HasAttachedScope(__instance)
                    && Event.current.type == EventType.KeyDown
                    && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter))
                {
                    __state = Event.current.keyCode;
                    Event.current.keyCode = KeyCode.None;
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Rjw dialog draw pass error", ex);
            }
        }

        public static void RestoreAcceptPostfix(KeyCode? __state)
        {
            try
            {
                if (__state.HasValue)
                {
                    Event.current.keyCode = __state.Value;
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Rjw dialog draw pass error", ex);
            }
        }
    }
}
