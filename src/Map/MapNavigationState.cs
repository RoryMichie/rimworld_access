using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>What Ctrl+Arrow jumps to; Impassable stops before the blocker, the rest land on the change.</summary>
    public enum JumpMode
    {
        PresetDistance,
        Impassable,
        Terrain,
        Structure
    }

    /// <summary>Whether the camera follows the cursor or a selected pawn; the two are exclusive.</summary>
    public enum CameraFollowMode
    {
        Cursor,  // Camera stays at cursor position, blocks RimWorld's pawn following
        Pawn     // Camera follows selected pawn (enabled by comma/period cycling)
    }

    /// <summary>
    /// The map cursor's position and navigation modes, stored per map so switching maps preserves
    /// each cursor.
    /// </summary>
    public static class MapNavigationState
    {
        // Per-map cursor positions, keyed by map.uniqueID
        private static Dictionary<int, IntVec3> cursorPositionsByMap = new Dictionary<int, IntVec3>();

        private static string lastAnnouncedInfo = "";
        private static bool isInitialized = false;
        private static int initializedForMapId = -1; // Track which map we're initialized for
        private static bool suppressMapNavigation = false;
        private static JumpMode currentJumpMode = JumpMode.PresetDistance;
        private static int presetJumpDistance = 5;
        private static CameraFollowMode cameraFollowMode = CameraFollowMode.Cursor;

        // When set, Initialize uses this instead of the camera position — for returning from a
        // dialog such as trade.
        private static IntVec3 pendingRestorePosition = IntVec3.Invalid;

        private static HashSet<int> knownMapIds = new HashSet<int>();
        private static bool hasAnnouncedMultiMapHint = false;

        /// <summary>The cursor position on the current map, stored and retrieved per map.uniqueID.</summary>
        public static IntVec3 CurrentCursorPosition
        {
            get
            {
                if (Find.CurrentMap == null)
                    return IntVec3.Invalid;

                if (cursorPositionsByMap.TryGetValue(Find.CurrentMap.uniqueID, out IntVec3 pos))
                    return pos;

                return IntVec3.Invalid;
            }
            set
            {
                if (Find.CurrentMap != null)
                {
                    cursorPositionsByMap[Find.CurrentMap.uniqueID] = value;
                    // Any external cursor write invalidates the scanner session, so the next Page
                    // Up/Down re-sorts from the new cursor. Scanner-driven jumps guard with a flag
                    // so they do not self-invalidate.
                    ScannerState.NotifyCursorWritten();
                }
            }
        }

        /// <summary>The stored cursor position for a map, or (0,0,0) when none is stored.</summary>
        public static IntVec3 GetCursorPositionForMap(Map map)
        {
            if (map == null)
                return IntVec3.Invalid;

            if (cursorPositionsByMap.TryGetValue(map.uniqueID, out IntVec3 pos))
                return pos;

            return new IntVec3(0, 0, 0);
        }

        /// <summary>Restores the cursor to the current map's last known position and moves the camera there.</summary>
        public static void RestoreCursorForCurrentMap()
        {
            if (Find.CurrentMap == null)
                return;

            IntVec3 restorePosition = GetCursorPositionForMap(Find.CurrentMap);

            if (!restorePosition.InBounds(Find.CurrentMap))
                restorePosition = new IntVec3(0, 0, 0);

            CurrentCursorPosition = restorePosition;
            Find.CameraDriver?.JumpToCurrentMapLoc(restorePosition);
            isInitialized = true;
        }

        /// <summary>The last announced tile information, so it is not repeated.</summary>
        public static string LastAnnouncedInfo
        {
            get => lastAnnouncedInfo;
            set => lastAnnouncedInfo = value;
        }

        /// <summary>False when nothing is initialized, or when the current map is not the one initialized for.</summary>
        public static bool IsInitialized
        {
            get
            {
                if (!isInitialized)
                    return false;

                if (Find.CurrentMap == null)
                    return false;

                if (Find.CurrentMap.uniqueID != initializedForMapId)
                    return false;

                return true;
            }
            set => isInitialized = value;
        }

        /// <summary>
        /// When true, arrow keys do not move the map cursor. True automatically while the trade menu
        /// or gizmo navigation is active.
        /// </summary>
        public static bool SuppressMapNavigation
        {
            get
            {
                if (TradeNavigationState.IsActive)
                    return true;

                if (GizmoNavigationState.IsActive)
                    return true;
                return suppressMapNavigation;
            }
            set => suppressMapNavigation = value;
        }

        /// <summary>The current jump mode.</summary>
        public static JumpMode CurrentJumpMode => currentJumpMode;

        /// <summary>The current preset jump distance, in tiles.</summary>
        public static int PresetJumpDistance => presetJumpDistance;

        /// <summary>
        /// The camera follow mode. Cursor mode keeps the camera at the cursor and blocks pawn
        /// following; pawn mode follows the selected pawn.
        /// </summary>
        public static CameraFollowMode CurrentCameraMode
        {
            get => cameraFollowMode;
            set => cameraFollowMode = value;
        }

        /// <summary>Cycles to the next jump mode and announces it.</summary>
        public static void CycleJumpModeForward()
        {
            int modeCount = Enum.GetValues(typeof(JumpMode)).Length;
            currentJumpMode = (JumpMode)(((int)currentJumpMode + 1) % modeCount);
            AnnounceJumpMode();
        }

        /// <summary>Cycles to the previous jump mode and announces it.</summary>
        public static void CycleJumpModeBackward()
        {
            int modeCount = Enum.GetValues(typeof(JumpMode)).Length;
            currentJumpMode = (JumpMode)(((int)currentJumpMode + modeCount - 1) % modeCount);
            AnnounceJumpMode();
        }

        /// <summary>Increases the preset jump distance and announces the new value.</summary>
        public static void IncreasePresetDistance(int amount = 1)
        {
            presetJumpDistance += amount;
            TolkHelper.Speak("RimWorldAccess.Map.Jump.Distance".Loc(presetJumpDistance));
        }

        /// <summary>Decreases the preset jump distance, minimum 1, and announces the new value.</summary>
        public static void DecreasePresetDistance(int amount = 1)
        {
            if (presetJumpDistance > 1)
            {
                presetJumpDistance = System.Math.Max(1, presetJumpDistance - amount);
                TolkHelper.Speak("RimWorldAccess.Map.Jump.Distance".Loc(presetJumpDistance));
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Map.Jump.MinimumDistance".Loc());
            }
        }

        /// <summary>Announces the current jump mode.</summary>
        public static void AnnounceJumpMode()
        {
            string modeText;
            switch (currentJumpMode)
            {
                case JumpMode.PresetDistance:
                    modeText = "RimWorldAccess.Map.Jump.Mode.PresetDistance".Translate(presetJumpDistance);
                    break;
                case JumpMode.Impassable:
                    modeText = "RimWorldAccess.Map.Jump.Mode.Impassable".Translate();
                    break;
                case JumpMode.Terrain:
                    modeText = "RimWorldAccess.Map.Jump.Mode.Terrain".Translate();
                    break;
                case JumpMode.Structure:
                    modeText = "RimWorldAccess.Map.Jump.Mode.Structure".Translate();
                    break;
                default:
                    modeText = "RimWorldAccess.Map.Jump.Mode.Unknown".Translate();
                    break;
            }
            TolkHelper.SpeakData(modeText);
        }

        /// <summary>Sets the position to restore the next time navigation initializes, for returning from a dialog.</summary>
        public static void SetPendingRestorePosition(IntVec3 position)
        {
            pendingRestorePosition = position;
        }

        /// <summary>
        /// Initializes a map's cursor position, preferring in order: a pending restore position, the
        /// stored per-map position, the camera position, then (0,0,0).
        /// </summary>
        public static void Initialize(Map map)
        {
            if (map == null)
            {
                isInitialized = false;
                return;
            }

            IntVec3 newPosition;

            if (pendingRestorePosition.IsValid && pendingRestorePosition.InBounds(map))
            {
                newPosition = pendingRestorePosition;
                pendingRestorePosition = IntVec3.Invalid;
            }
            else if (cursorPositionsByMap.TryGetValue(map.uniqueID, out IntVec3 storedPos) && storedPos.InBounds(map))
            {
                newPosition = storedPos;
            }
            else if (Find.CameraDriver != null)
            {
                newPosition = Find.CameraDriver.MapPosition;
            }
            else
            {
                newPosition = new IntVec3(0, 0, 0);
            }

            cursorPositionsByMap[map.uniqueID] = newPosition;

            lastAnnouncedInfo = "";
            isInitialized = true;
            initializedForMapId = map.uniqueID;
        }

        /// <summary>Moves the cursor by an offset, clamped to map bounds; true when the position changed.</summary>
        public static bool MoveCursor(IntVec3 offset, Map map)
        {
            if (map == null || !isInitialized)
                return false;

            IntVec3 newPosition = CurrentCursorPosition + offset;

            newPosition.x = UnityEngine.Mathf.Clamp(newPosition.x, 0, map.Size.x - 1);
            newPosition.z = UnityEngine.Mathf.Clamp(newPosition.z, 0, map.Size.z - 1);

            if (newPosition != CurrentCursorPosition)
            {
                CurrentCursorPosition = newPosition;
                DocsTeacher.NotifyCursorMoved();        // tile-by-tile counter (jump-modes lesson)
                DocsTeacher.NotifyCursorLanded(newPosition, map);
                return true;
            }

            return false;
        }

        /// <summary>Resets all navigation state, including every stored per-map cursor position.</summary>
        public static void Reset()
        {
            cursorPositionsByMap.Clear();
            lastAnnouncedInfo = "";
            isInitialized = false;
            initializedForMapId = -1;
            knownMapIds.Clear();
            hasAnnouncedMultiMapHint = false;
            cameraFollowMode = CameraFollowMode.Cursor;

            // The map data behind the scanner is gone.
            ScannerState.Invalidate();
        }

        /// <summary>Announces map additions and removals; call periodically.</summary>
        public static void CheckForMapChanges()
        {
            if (Find.Maps == null)
                return;

            var currentMapIds = new HashSet<int>();
            foreach (var map in Find.Maps)
            {
                currentMapIds.Add(map.uniqueID);
            }

            foreach (int mapId in currentMapIds)
            {
                if (!knownMapIds.Contains(mapId))
                {
                    Map newMap = Find.Maps.FirstOrDefault(m => m.uniqueID == mapId);
                    string mapName = GetMapDisplayName(newMap);

                    int totalMaps = Find.Maps.Count;
                    if (totalMaps == 2 && !hasAnnouncedMultiMapHint)
                    {
                        TolkHelper.Speak("RimWorldAccess.Map.NewMap.WithHint".Loc(mapName, totalMaps));
                        hasAnnouncedMultiMapHint = true;
                    }
                    else if (totalMaps > 1)
                    {
                        TolkHelper.Speak("RimWorldAccess.Map.NewMap.Total".Loc(mapName, totalMaps));
                    }
                }
            }

            foreach (int mapId in knownMapIds)
            {
                if (!currentMapIds.Contains(mapId))
                {
                    // The map object is gone, so the removal is announced without a name.
                    int remainingMaps = Find.Maps.Count;
                    if (remainingMaps == 1)
                    {
                        TolkHelper.Speak("RimWorldAccess.Map.MapClosed.One".Loc());
                        hasAnnouncedMultiMapHint = false; // Reset hint for next time
                    }
                    else if (remainingMaps > 1)
                    {
                        TolkHelper.Speak("RimWorldAccess.Map.MapClosed.Many".Loc(remainingMaps));
                    }

                    cursorPositionsByMap.Remove(mapId);
                }
            }

            knownMapIds = currentMapIds;
        }

        /// <summary>A display name for a map.</summary>
        private static string GetMapDisplayName(Map map)
        {
            if (map == null)
                return "RimWorldAccess.Map.Label.Unknown".Translate();

            if (map.Parent != null)
            {
                if (!string.IsNullOrEmpty(map.Parent.Label))
                    return map.Parent.Label;

                if (map.Parent.def != null && !string.IsNullOrEmpty(map.Parent.def.label))
                    return map.Parent.def.label;
            }

            return "RimWorldAccess.Map.Display.Numbered".Translate(map.uniqueID);
        }

        /// <summary>Moves the cursor by the current jump mode; true when it moved. A scanning mode
        /// hands back a distance prefix for the landing announcement, and speaks its own failure.</summary>
        public static bool Jump(IntVec3 direction, Map map, out string announcementPrefix)
        {
            announcementPrefix = null;

            if (map == null || !isInitialized)
                return false;

            if (currentJumpMode == JumpMode.PresetDistance)
                return JumpPresetDistance(direction, map);

            if (!MapJumpScanner.TryScan(currentJumpMode, CurrentCursorPosition, direction, map,
                    out IntVec3 destination, out int distance, out JumpScanFailure failure))
            {
                TolkHelper.Speak(ScanFailureText(failure));
                return false;
            }

            CurrentCursorPosition = destination;
            announcementPrefix = distance == 1
                ? "RimWorldAccess.Map.Jump.Moved.One".Translate().ToString()
                : "RimWorldAccess.Map.Jump.Moved.Many".Translate(distance).ToString();
            return true;
        }

        /// <summary>Jumps the preset number of tiles in a direction; true when the position changed.</summary>
        private static bool JumpPresetDistance(IntVec3 direction, Map map)
        {
            IntVec3 newPosition = CurrentCursorPosition + (direction * presetJumpDistance);

            newPosition.x = UnityEngine.Mathf.Clamp(newPosition.x, 0, map.Size.x - 1);
            newPosition.z = UnityEngine.Mathf.Clamp(newPosition.z, 0, map.Size.z - 1);

            if (newPosition == CurrentCursorPosition)
            {
                TolkHelper.Speak("RimWorldAccess.Map.Jump.Boundary".Loc());
                return false;
            }

            CurrentCursorPosition = newPosition;
            return true;
        }

        private static Localized ScanFailureText(JumpScanFailure failure)
        {
            if (failure == JumpScanFailure.MapBoundary)
                return "RimWorldAccess.Map.Jump.Boundary".Loc();

            if (failure == JumpScanFailure.NoOpenTile)
                return "RimWorldAccess.Map.Jump.Blocked".Loc();

            return currentJumpMode == JumpMode.Terrain
                ? "RimWorldAccess.Map.Jump.NoTerrainChange".Loc()
                : "RimWorldAccess.Map.Jump.NoStructureChange".Loc();
        }

        // ===== JUMP / SELECTION ANNOUNCEMENTS =====

        /// <summary>Announces "Jumped to {target}"; a null or empty label announces "Jumped to target".</summary>
        public static void SpeakJumpedTo(string targetLabel, SpeechPriority priority = SpeechPriority.Normal)
        {
            string phrase = string.IsNullOrEmpty(targetLabel)
                ? "RimWorldAccess.Map.JumpedToTarget".Translate().ToString()
                : "RimWorldAccess.Map.JumpedTo".Translate(targetLabel).ToString();
            TolkHelper.SpeakData(phrase, priority);
        }

        /// <summary>Formats the "Selected, {x}, {z}" announcement for picking a single map tile; the caller speaks it.</summary>
        public static string FormatSelectedCell(IntVec3 cell)
        {
            return "RimWorldAccess.Map.SelectedCell".Translate(cell.x, cell.z).ToString();
        }
    }
}
