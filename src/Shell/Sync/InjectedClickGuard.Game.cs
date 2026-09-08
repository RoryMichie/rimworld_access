using HarmonyLib;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Shared bookkeeping for keyboard-injected widget clicks. When an
    /// injection engine forces a widget's click result, vanilla's inline
    /// handling runs synchronously inside that window's draw — and any
    /// FloatMenu it constructs is keyboard-born: it anchors to wherever the
    /// REAL mouse happens to be and ships vanillaIfMouseDistant semantics
    /// that self-dismiss on the first frame if the mouse is far. Engines
    /// set <see cref="InFlight"/> the moment they force a
    /// result and clear it when their capture pass ends; the single
    /// WindowStack.Add postfix below de-fuses every menu born in between.
    /// </summary>
    public static class InjectedClickGuard
    {
        internal static bool InFlight;
    }

    /// <summary>
    /// Clears <see cref="FloatMenu.vanishIfMouseDistant"/> on any FloatMenu
    /// added to the stack while an injected click is in flight, regardless of
    /// which injection engine forced the click.
    /// </summary>
    [HarmonyPatch(typeof(WindowStack), "Add")]
    public static class InjectedClickFloatMenuVanishPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Window window)
        {
            if (!InjectedClickGuard.InFlight)
            {
                return;
            }
            FloatMenu menu = window as FloatMenu;
            if (menu != null)
            {
                menu.vanishIfMouseDistant = false;
            }
        }
    }
}
