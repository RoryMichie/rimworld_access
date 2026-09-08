namespace RimWorldAccess
{
    /// <summary>
    /// Lifecycle flag backend for the in-game storyteller/difficulty page
    /// (<see cref="RimWorld.Page_SelectStorytellerInGame"/>). Every bit of
    /// navigation, typeahead, announcement, and mutation lives on
    /// <see cref="RimWorldAccess.Shell.StorytellerInGameScope"/> (via the
    /// shared <see cref="RimWorldAccess.Shell.StorytellerScopeBase"/>), the
    /// same modern <c>ScreenScope</c> shape the pre-game twin
    /// (<see cref="RimWorldAccess.Shell.StorytellerScreenScope"/>) already
    /// used. This class keeps only what D1 of the migration blueprint calls
    /// load-bearing outside the scope: the <see cref="IsActive"/> flag
    /// (<c>ShellGuards.Game.cs</c> lists it by name in a debug-dump OR-list)
    /// and the <see cref="Open"/>/<see cref="Close"/> entry points
    /// <c>StorytellerSelectionPatch</c>'s PreOpen/PreClose Harmony postfixes
    /// call on the page's own real window lifecycle.
    ///
    /// RETIRED with this migration (moved into StorytellerInGameScope/
    /// StorytellerScopeBase, or dissolved because ScreenScope+ScreenModel now
    /// own the cursor): the four-level state machine
    /// (<c>StorytellerSelectionLevel</c>: StorytellerList/DifficultyList/
    /// CustomSectionList/CustomSettingsList), the storyteller/difficulty
    /// index fields and lists (now <c>StorytellerScopeBase.Storytellers</c>/
    /// <c>Difficulties</c>, rebuilt by <c>RefreshContent</c> every focus cycle
    /// instead of once in <c>Open()</c>), the
    /// <c>CustomDifficultySectionNavigator</c> composition (deleted outright
    /// — its only consumer was this class, grep-proved), the bespoke
    /// <c>TypeaheadSearchHelper</c> wiring (replaced by the shared
    /// <c>ScreenScope.Typeahead</c> engine), and every navigation/adjustment/
    /// announcement method (<c>SelectNext</c>/<c>Previous</c>,
    /// <c>SwitchLevel</c>, <c>ExecuteOrEnter</c>, <c>GoBack</c>,
    /// <c>JumpToFirst</c>/<c>Last</c>, <c>Adjust*</c>/<c>Toggle*</c>,
    /// <c>OpenResetToPresetMenu</c>, <c>ProcessBackspace</c>,
    /// <c>SelectNextMatch</c>/<c>PreviousMatch</c>, <c>Confirm</c>,
    /// <c>ApplyStorytellerSelection</c>, <c>ApplyDifficultySelection</c>,
    /// <c>BuildCustomDifficultySections</c>, <c>AnnounceCurrentState</c> and
    /// friends).
    ///
    /// The legacy "Enter on a Storyteller/Difficulty row confirms and closes
    /// the whole page" shortcut (the old <c>Confirm()</c>) is GONE — see
    /// <see cref="RimWorldAccess.Shell.StorytellerInGameScope"/>'s class
    /// remarks for the audible/functional delta this is.
    /// </summary>
    public static class StorytellerSelectionState
    {
        private static bool isActive;

        public static bool IsActive
        {
            get { return isActive; }
        }

        public static void Open()
        {
            isActive = true;
        }

        public static void Close()
        {
            isActive = false;
        }
    }
}
