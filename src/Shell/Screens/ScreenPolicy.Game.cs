using RimWorld;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Mod-wide behavior defaults for every ScreenScope-based screen
    /// (the screen-model doctrine — see the ScreenScope header). This is the
    /// ONE place a behavior changes for all screens at once; a screen that
    /// must deviate overrides the matching virtual on its own scope. A screen
    /// may never change these behaviors any other way — hand-rolled cursor or
    /// tab math in a screen file is the defect this layer exists to end.
    /// </summary>
    public static class ScreenPolicy
    {
        /// <summary>Region cycling (Tab past the last region) wraps to the first — vanilla's own tab-strip behavior.</summary>
        public static bool WrapTabs
        {
            get { return true; }
        }

        /// <summary>Each region keeps its item cursor across region switches.</summary>
        public static bool RememberTabPositions
        {
            get { return true; }
        }

        /// <summary>Item-cursor wrap inside a region follows the player's WrapNavigation setting.</summary>
        public static bool WrapItems
        {
            get
            {
                return RimWorldAccessMod_Settings.Settings != null
                    && RimWorldAccessMod_Settings.Settings.WrapNavigation;
            }
        }

        /// <summary>The sound a region switch plays: vanilla's own tab-click sound (TabDrawer.DrawTabs).</summary>
        public static SoundDef TabSwitchSound
        {
            get { return SoundDefOf.RowTabSelect; }
        }
    }
}
