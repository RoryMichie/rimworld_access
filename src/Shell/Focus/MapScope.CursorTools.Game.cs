using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Ambient map claims with no mode of their own: bookmarks and the self-contained
    /// cursor hotkeys (coordinates, forbid toggle, unforbid-all, info card, pointer
    /// bridge), living directly on <see cref="MapScope"/> beside the arrow claims.
    /// </summary>
    public sealed partial class MapScope
    {
        /// <summary>
        /// Ctrl/Ctrl+Shift/Ctrl+Alt + digit map bookmarks. Slot comes from the digit key
        /// and the operation from which action matched, so no modifier branching is needed.
        /// </summary>
        private void RegisterBookmarkClaims()
        {
            Claim("map.bookmark.set",
                delegate (KeyEventSnapshot e) { BookmarkHelper.SetBookmark(e.Key - KeyCode.Alpha0); }, when: BookmarksLive);
            Claim("map.bookmark.jump",
                delegate (KeyEventSnapshot e) { BookmarkHelper.JumpToBookmark(e.Key - KeyCode.Alpha0); }, when: BookmarksLive);
            Claim("map.bookmark.peekOrJump",
                delegate (KeyEventSnapshot e) { BookmarkHelper.PeekOrJumpToBookmark(e.Key - KeyCode.Alpha0); }, when: BookmarksLive);
        }

        /// <summary>
        /// Self-contained cursor hotkeys: coordinates, forbid toggle at the cursor tile,
        /// unforbid-all, info card / drill-in picker, the two pointer-bridge directions,
        /// and the mouse-tracking toggles. The bare-Enter inspection opener
        /// (map.inspect.open) is deliberately NOT claimed here: it has no menu-active gate
        /// of its own, so an ambient claim would steal Enter from every unmigrated menu.
        /// </summary>
        private void RegisterCursorHotkeyClaims()
        {
            Claim("map.info.cursorCoordinates",
                delegate (KeyEventSnapshot e) { CursorActionHelper.AnnounceCursorCoordinates(); }, when: CursorCoordsLive);
            Claim("map.item.forbidToggle",
                delegate (KeyEventSnapshot e) { CursorActionHelper.ToggleForbidAtCursor(); }, when: ForbidToggleLive);
            Claim("map.item.unforbidAll",
                delegate (KeyEventSnapshot e) { CursorActionHelper.UnforbidAllItems(); }, when: UnforbidAllLive);
            Claim("map.inspect.infoCardAtCursor",
                delegate (KeyEventSnapshot e) { CursorActionHelper.OpenInfoCardAtCursor(); }, when: InfoCardAtCursorLive);
            Claim("map.cursor.warpPointer",
                delegate (KeyEventSnapshot e) { PointerWarpState.WarpPointerToCursor(); }, when: PointerBridgeLive);
            Claim("map.cursor.pullFromPointer",
                delegate (KeyEventSnapshot e) { PointerWarpState.PullCursorToPointer(); }, when: PointerBridgeLive);
            // A settings flip needs no valid cursor, hence ArrowsLive rather than PointerBridgeLive.
            Claim("map.cursor.toggleMouseTracking",
                delegate (KeyEventSnapshot e) { PointerWarpState.ToggleMouseTracking(); }, when: ArrowsLive);
            Claim("map.cursor.toggleFollowKeyboard",
                delegate (KeyEventSnapshot e) { PointerWarpState.ToggleFollowKeyboard(); }, when: PointerBridgeLive);
        }

        /// <summary>Gate for the bookmark claims.</summary>
        private static bool BookmarksLive()
        {
            return Current.ProgramState == ProgramState.Playing
                && Find.CurrentMap != null
                && WorldRendererUtility.DrawingMap
                && (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion)
                && MapNavigationState.IsInitialized;
        }

        /// <summary>Gate for the coordinate announcement. The pos-valid term is part of the
        /// guard so an invalid-position key falls through untouched.</summary>
        private static bool CursorCoordsLive()
        {
            return Current.ProgramState == ProgramState.Playing
                && Find.CurrentMap != null
                && WorldRendererUtility.DrawingMap
                && MapNavigationState.IsInitialized
                && (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion)
                && !ShellGuards.MenuOwnsInput()
                && !ScannerSearchState.IsActive
                && MapNavigationState.CurrentCursorPosition.IsValid;
        }

        /// <summary>
        /// Live wherever the keyboard cursor is. Deliberately carries no
        /// <see cref="ShellGuards.MenuOwnsInput"/> term: keyboard placement owns input, and that
        /// is exactly the mode where handing the cell to the mouse matters most. Every menu that
        /// owns input runs a MODAL scope whose masking already stops the walk before it reaches
        /// an ambient MapScope claim.
        /// </summary>
        private static bool PointerBridgeLive()
        {
            return ArrowsLive() && MapNavigationState.CurrentCursorPosition.IsValid;
        }

        /// <summary>Gate for the forbid toggle.</summary>
        private static bool ForbidToggleLive()
        {
            return Current.ProgramState == ProgramState.Playing
                && Find.CurrentMap != null
                && MapNavigationState.IsInitialized
                && (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion)
                && !ShellGuards.MenuOwnsInput()
                && !ScannerSearchState.IsActive;
        }

        /// <summary>Gate for unforbid-all.</summary>
        private static bool UnforbidAllLive()
        {
            return Current.ProgramState == ProgramState.Playing
                && Find.CurrentMap != null
                && (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion);
        }

        /// <summary>
        /// Gate for the info card at the cursor. Menus that run their own modal scope need no
        /// term here: their masking already stops the dispatch walk before it reaches an ambient
        /// MapScope claim. Only states without such a scope are listed below.
        /// </summary>
        private static bool InfoCardAtCursorLive()
        {
            return Current.ProgramState == ProgramState.Playing
                && Find.CurrentMap != null
                && (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion)
                && MapNavigationState.IsInitialized
                && !WindowlessInventoryState.IsActive
                && !GizmoNavigationState.IsActive
                && !WindowlessInspectionState.IsActive
                && !WindowlessFloatMenuState.IsActive
                && !PlantSelectionMenuState.IsActive
                && !MechControlGroupState.IsActive
                && !StorageSettingsMenuState.IsActive
                && !BillsMenuState.IsActive
                && !BillConfigState.IsActive;
        }

    }
}
