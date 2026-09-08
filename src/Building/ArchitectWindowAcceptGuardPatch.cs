using System;
using HarmonyLib;
using RimWorld;
using UnityEngine;

namespace RimWorldAccess
{
    /// <summary>
    /// Keeps Enter usable while the architect tree drives the real architect
    /// window. <c>MainTabWindow_Architect.DoWindowContents</c> closes itself and
    /// consumes the event on <c>KeyBindingDefOf.Accept.KeyDownEvent</c>
    /// (decompiled RimWorld/MainTabWindow_Architect.cs:148-160), and that GUI pass
    /// can run before the dispatcher on the very Event the shell is about to
    /// resolve, so every Enter meant for a category or a designator would instead
    /// close the panel and take the tree down with it.
    ///
    /// The guard masks and restores one Event field around vanilla's own body:
    /// <c>KeyBindingDef.KeyDownEvent</c> requires
    /// <c>Event.current.keyCode != KeyCode.None</c> (decompiled
    /// Verse/KeyBindingDef.cs:43-47), so blanking the keyCode is exactly enough and
    /// nothing weaker will do. No <c>Use()</c>, no frame stamp: the key still
    /// reaches the dispatcher intact, and the scope that claims it is unchanged.
    /// The finalizer restores as well, so a throw inside vanilla's body cannot
    /// leave the event masked for the rest of the frame.
    ///
    /// Masking also suppresses the other branch of that block, the quick-search
    /// unique-match force-activation at :150-153. That is intended: while the tree
    /// drives, the keyboard shell owns Enter and the quick search is not the
    /// surface the player is on.
    /// </summary>
    [HarmonyPatch(typeof(MainTabWindow_Architect), "DoWindowContents")]
    internal static class ArchitectWindowAcceptGuardPatch
    {
        private static bool masked;
        private static KeyCode stashedKeyCode;

        [HarmonyPrefix]
        internal static void Prefix()
        {
            try
            {
                if (masked || !ArchitectTreeState.IsActive || Event.current == null
                    || Event.current.type != EventType.KeyDown)
                {
                    return;
                }
                stashedKeyCode = Event.current.keyCode;
                Event.current.keyCode = KeyCode.None;
                masked = true;
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Architect accept guard error", ex);
            }
        }

        [HarmonyPostfix]
        internal static void Postfix()
        {
            Restore();
        }

        [HarmonyFinalizer]
        internal static void Finalizer()
        {
            Restore();
        }

        private static void Restore()
        {
            if (!masked)
            {
                return;
            }
            masked = false;
            if (Event.current != null)
            {
                Event.current.keyCode = stashedKeyCode;
            }
        }
    }
}
