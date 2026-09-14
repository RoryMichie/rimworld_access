using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorldAccess.Shell;
using UnityEngine;

namespace RimWorldAccess
{
    /// <summary>
    /// Keeps the mod's map-wide hotkeys (KeyHandler.MapComponentOnGUI: Allow on Home, Forbid on
    /// End by default) off the scanner's keys. The dispatcher already consumes every chord the
    /// scanner claims, but KeyBindingDef.KeyDownEvent matches on key code alone, so a modifier
    /// chord the scanner leaves unclaimed would still fire a map-wide allow. Scanner keys belong
    /// to the scanner in every modifier combination; the mod's other bindings pass untouched,
    /// and an Allow key the player rebinds elsewhere works as set.
    /// </summary>
    internal static class KauScannerKeyGuard
    {
        private const string ScannerActionPrefix = "map.scanner.";

        public static void Register(Harmony harmony)
        {
            MethodInfo target = AccessTools.Method("KeyzAllowUtilities.KeyHandler:MapComponentOnGUI");
            if (target == null)
            {
                ModLogger.Error("Keyz' Allow Utilities compat: KeyHandler.MapComponentOnGUI not found; Home/End guard inactive");
                return;
            }

            harmony.Patch(target, prefix: new HarmonyMethod(typeof(KauScannerKeyGuard), nameof(Prefix)));
        }

        public static bool Prefix()
        {
            Event e = Event.current;
            if (e == null || e.type != EventType.KeyDown || e.keyCode == KeyCode.None)
                return true;
            return !IsScannerKey(e.keyCode);
        }

        private static bool IsScannerKey(KeyCode key)
        {
            IReadOnlyList<InputAction> actions = ActionRegistry.All;
            for (int i = 0; i < actions.Count; i++)
            {
                InputAction action = actions[i];
                if (!action.Id.StartsWith(ScannerActionPrefix, StringComparison.Ordinal))
                    continue;
                IReadOnlyList<KeyChord> chords = action.Bindings;
                for (int j = 0; j < chords.Count; j++)
                {
                    if (chords[j].Key == key)
                        return true;
                }
            }
            return false;
        }
    }
}
