using HarmonyLib;
using LudeonTK;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Audio feedback for the vanilla dev-mode key toggles that
    /// <see cref="DebugWindowsOpener"/> handles natively (Semicolon god mode,
    /// BackQuote log window, Quote inspector, and the tweakvalues toolbar
    /// button). The shell passes these keys straight through to vanilla, so
    /// without these postfixes a screen-reader user gets no confirmation the
    /// toggle happened.
    ///
    /// God mode announces unconditionally from
    /// <see cref="DebugSettings.godMode"/>'s new value — the sole announcement
    /// site now that <see cref="AmbientScopes"/>' F12 Development entry rides a
    /// bare opener invoke (no double-speak).
    ///
    /// The three EditWindow togglers announce only when the toggle left the
    /// window open WITHOUT an attached focus scope — i.e. it was opened by the
    /// vanilla hotkey/toolbar rather than the F12 &gt; Development path, which
    /// arms the window's own scope (<see cref="DevLogScope"/> /
    /// <see cref="DevTweakValuesScope"/> / <see cref="DevInspectorScope"/>) so
    /// that scope's own open announcement already covers it. The
    /// <see cref="ScopeForWindow.Attach"/> hook runs inside the window's
    /// <c>WindowStack.Add</c> call, before these postfixes, so
    /// <see cref="ScopeForWindow.HasAttachedScope"/> is already accurate here.
    /// A toggle that removed the window announces "closed". Dialog_Debug
    /// (always scoped) and Dialog_DevPalette (visual-only chrome) need no
    /// announcement, so their togglers are not patched.
    /// </summary>
    [HarmonyPatch(typeof(DebugWindowsOpener), "ToggleGodMode")]
    public static class DebugWindowsOpenerToggleGodModePatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            TolkHelper.SpeakData((DebugSettings.godMode
                ? "RimWorldAccess.Dev.GodModeOn"
                : "RimWorldAccess.Dev.GodModeOff").Translate().ToString());
        }
    }

    [HarmonyPatch(typeof(DebugWindowsOpener), "ToggleLogWindow")]
    public static class DebugWindowsOpenerToggleLogWindowPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            DevToggleAnnounce.AnnounceWindowToggle(
                Find.WindowStack?.WindowOfType<EditWindow_Log>(), "Debug log");
        }
    }

    [HarmonyPatch(typeof(DebugWindowsOpener), "ToggleTweakValuesMenu")]
    public static class DebugWindowsOpenerToggleTweakValuesMenuPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            DevToggleAnnounce.AnnounceWindowToggle(
                Find.WindowStack?.WindowOfType<EditWindow_TweakValues>(), "TweakValues");
        }
    }

    [HarmonyPatch(typeof(DebugWindowsOpener), "ToggleDebugInspector")]
    public static class DebugWindowsOpenerToggleDebugInspectorPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            DevToggleAnnounce.AnnounceWindowToggle(
                Find.WindowStack?.WindowOfType<EditWindow_DebugInspector>(), "Debug inspector");
        }
    }

    internal static class DevToggleAnnounce
    {
        /// <summary>
        /// Announces the post-toggle state of one dev EditWindow.
        /// <paramref name="window"/> is the live instance after the toggle
        /// (null if the toggle closed it); <paramref name="title"/> is the
        /// window's own <c>optionalTitle</c> literal. Stays silent when the
        /// window is open with a scope attached (the F12 path already spoke).
        /// </summary>
        public static void AnnounceWindowToggle(Window window, string title)
        {
            if (window == null)
            {
                TolkHelper.SpeakData("RimWorldAccess.Dev.WindowClosed".Translate(title).ToString());
            }
            else if (!ScopeForWindow.HasAttachedScope(window))
            {
                TolkHelper.SpeakData("RimWorldAccess.Dev.WindowOpenedUnfocused".Translate(title).ToString());
            }
        }
    }
}
