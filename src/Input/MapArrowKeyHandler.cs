using System.Linq;
using UnityEngine;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Arrow key handling for local colony map navigation, driven from AmbientScopes'
    /// ambient arrow claims. Runs in OnGUI so OS key repeat works, unlike Update().
    /// </summary>
    public static class MapArrowKeyHandler
    {
        /// <summary>Returns true if the key was handled and should be consumed.</summary>
        public static bool HandleArrowKey(KeyCode key, bool ctrlHeld, bool shiftHeld)
        {
            // Shift+arrow configures the jump rather than performing it, so these branches
            // must never move the cursor or camera.
            if (shiftHeld)
            {
                if (key == KeyCode.UpArrow)
                {
                    MapNavigationState.CycleJumpModeForward();
                    return true;
                }
                else if (key == KeyCode.DownArrow)
                {
                    MapNavigationState.CycleJumpModeBackward();
                    return true;
                }
                else if (key == KeyCode.LeftArrow || key == KeyCode.RightArrow)
                {
                    if (MapNavigationState.CurrentJumpMode == JumpMode.PresetDistance)
                    {
                        int step = ctrlHeld ? 10 : 1;
                        if (key == KeyCode.LeftArrow)
                            MapNavigationState.DecreasePresetDistance(step);
                        else
                            MapNavigationState.IncreasePresetDistance(step);
                    }
                    else
                    {
                        // A scanning mode has no distance to adjust, so re-state it, not silence.
                        MapNavigationState.AnnounceJumpMode();
                    }
                    return true;
                }
            }

            IntVec3 moveOffset = IntVec3.Zero;
            bool keyPressed = false;

            switch (key)
            {
                case KeyCode.UpArrow:
                    moveOffset = IntVec3.North;
                    keyPressed = true;
                    break;
                case KeyCode.DownArrow:
                    moveOffset = IntVec3.South;
                    keyPressed = true;
                    break;
                case KeyCode.LeftArrow:
                    moveOffset = IntVec3.West;
                    keyPressed = true;
                    break;
                case KeyCode.RightArrow:
                    moveOffset = IntVec3.East;
                    keyPressed = true;
                    break;
            }

            if (!keyPressed)
                return false;

            bool isJump = ctrlHeld;
            bool positionChanged;
            string jumpPrefix = null;

            if (isJump)
            {
                positionChanged = MapNavigationState.Jump(moveOffset, Find.CurrentMap, out jumpPrefix);
            }
            else
            {
                positionChanged = MapNavigationState.MoveCursor(moveOffset, Find.CurrentMap);
            }

            if (positionChanged)
            {
                HandlePositionChanged(jumpPrefix);
            }
            else
            {
                HandleBoundaryReached(isJump);
            }

            return true;
        }

        /// <summary>Previews, camera, audio and announcement for a cursor that moved.</summary>
        private static void HandlePositionChanged(string prefix)
        {
            // Navigating the map ends the just-selected-a-pawn context.
            GizmoNavigationState.PawnJustSelected = false;

            IntVec3 newPosition = MapNavigationState.CurrentCursorPosition;

            Find.CameraDriver.JumpToCurrentMapLoc(newPosition);

            // Cursor mode makes the camera follow the cursor and blocks pawn following.
            MapNavigationState.CurrentCameraMode = CameraFollowMode.Cursor;

            TerrainAudioHelper.PlayCellAudio(newPosition, Find.CurrentMap, 0.5f);

            // A wall attachment turns itself to face the wall it landed next to, and that is a
            // change to what gets built, so it is spoken ahead of the cell it happened on.
            AttachmentFacingPatch.SpeakTurnAtCursor();

            AnnouncePosition(newPosition, Find.CurrentMap, prefix);
        }

        /// <summary>
        /// Speaks the tile at a position with every contextual prefix (deep ore, "in area",
        /// shape dimensions). Shared by arrow movement and Go To coordinate input.
        /// </summary>
        public static void AnnouncePosition(IntVec3 position, Map map, string prefix = null)
        {
            string tileInfo;
            string toSpeak = ComposePositionAnnouncement(position, map, prefix, allowStateWrites: true, tileInfo: out tileInfo);

            if (!string.IsNullOrEmpty(prefix) || toSpeak != MapNavigationState.LastAnnouncedInfo)
            {
                // Placement help is harvested from vanilla's draw pass, which runs after this
                // key, so a pending pass takes over the tile description and speaks it after the
                // help — the help is the part a placing player is listening for.
                if (!PlacementHelpSpeech.TryHoldTileAnnouncement(toSpeak))
                {
                    TolkHelper.SpeakData(toSpeak);
                }
                MapNavigationState.LastAnnouncedInfo = tileInfo;
            }
        }

        /// <summary>
        /// Builds the utterance for a cell without speaking it. With
        /// <paramref name="allowStateWrites"/> false the build is side-effect free, so a
        /// read-only reader (the mouse hover cursor) can describe a cell the keyboard cursor
        /// is not standing on without disturbing placement previews or dedup state.
        /// </summary>
        internal static string ComposePositionAnnouncement(IntVec3 position, Map map, string prefix, bool allowStateWrites)
        {
            string tileInfo;
            return ComposePositionAnnouncement(position, map, prefix, allowStateWrites, out tileInfo);
        }

        /// <param name="tileInfo">The description without the lead-in, which is what the
        /// spam-dedup tracks.</param>
        private static string ComposePositionAnnouncement(IntVec3 position, Map map, string prefix, bool allowStateWrites, out string tileInfo)
        {
            // A shape that grew leads the utterance with its new extent, so the size and the
            // cell the shape now ends on arrive together rather than as two racing utterances.
            string shapeExtent = null;
            if (allowStateWrites && ShapePlacementState.ShouldUpdatePreviewOnMove())
            {
                shapeExtent = ShapePlacementState.UpdatePreview(position);
            }

            tileInfo = TileInfoHelper.GetTileSummary(position, map);

            tileInfo = AddContextPrefix(tileInfo, position, allowStateWrites);

            if (!string.IsNullOrEmpty(shapeExtent))
            {
                prefix = string.IsNullOrEmpty(prefix) ? shapeExtent : $"{prefix}. {shapeExtent}";
            }

            // A prefixed announcement always speaks: it marks a deliberate jump, not incidental
            // movement. The dedup still tracks the plain tile info so arrow steps dedup normally.
            return string.IsNullOrEmpty(prefix)
                ? tileInfo
                : (string.IsNullOrEmpty(tileInfo) ? prefix : $"{prefix}. {tileInfo}");
        }

        /// <summary>Announces that the cursor cannot move further.</summary>
        private static void HandleBoundaryReached(bool isJump)
        {
            // Every jump mode names its own reason for staying put.
            if (isJump)
                return;

            TolkHelper.Speak("RimWorldAccess.Input.Map.Boundary".Loc());
        }

        /// <summary>Prefixes tile info with whatever the active mode adds to a cell.</summary>
        private static string AddContextPrefix(string tileInfo, IntVec3 position, bool allowStateWrites)
        {
            if (JumpTargetingState.IsActive)
            {
                return JumpTargetingState.GetJumpValidityPrefix(position) + tileInfo;
            }

            if (PlantTargetingState.IsActive)
            {
                return PlantTargetingState.GetPlantValidityPrefix(position) + tileInfo;
            }

            if (ArchitectState.IsInPlacementMode)
            {
                if (TileInfoHelper.ShouldShowDeepOreForCurrentDesignator())
                {
                    string deepOreInfo = TileInfoHelper.GetDeepOreInfo(position, Find.CurrentMap);
                    if (!string.IsNullOrEmpty(deepOreInfo))
                    {
                        tileInfo = deepOreInfo + ", " + tileInfo;
                    }
                }

                if (ShapePlacementState.IsActive && ShapePlacementState.PreviewCells.Contains(position))
                {
                    // Only endpoints are labelled, and the second one only once confirmed
                    // (Previewing), not while it is still being dragged (SettingSecondCorner).
                    if (ShapePlacementState.FirstPoint.HasValue && position == ShapePlacementState.FirstPoint.Value)
                    {
                        return "RimWorldAccess.Input.Map.PrefixFirstPoint".Translate(tileInfo);
                    }
                    else if (ShapePlacementState.CurrentPhase == PlacementPhase.Previewing &&
                             ShapePlacementState.SecondPoint.HasValue && position == ShapePlacementState.SecondPoint.Value)
                    {
                        return "RimWorldAccess.Input.Map.PrefixSecondPoint".Translate(tileInfo);
                    }
                }
                else if (ArchitectState.SelectedCells.Contains(position))
                {
                    return "RimWorldAccess.Input.Map.PrefixSelected".Translate(tileInfo);
                }
            }

            if (ShelfLinkingState.IsActive && ShelfLinkingState.IsStorageSelectedAt(position))
            {
                return "RimWorldAccess.Input.Map.PrefixSelected".Translate(tileInfo);
            }

            // Area membership tells the player which cells are already in the area while
            // expanding or shrinking it, for both allowed and built-in areas.
            if (ShapePlacementState.IsActive)
            {
                Designator activeDesignator = ShapePlacementState.ActiveDesignator;
                if (activeDesignator != null)
                {
                    Area targetArea = null;

                    if (ShapeHelper.IsAreaDesignator(activeDesignator))
                    {
                        targetArea = Designator_AreaAllowed.selectedArea;
                    }
                    else if (ShapeHelper.IsBuiltInAreaDesignator(activeDesignator))
                    {
                        targetArea = ShapeHelper.GetBuiltInAreaForDesignator(activeDesignator, Find.CurrentMap);
                    }

                    if (targetArea != null && targetArea.Map != null &&
                        position.InBounds(targetArea.Map) && targetArea[position])
                    {
                        return "RimWorldAccess.Input.Map.PrefixInArea".Translate(tileInfo);
                    }
                }
            }
            else if (ViewingModeState.IsActive && (ViewingModeState.IsAreaDesignator || ViewingModeState.IsBuiltInAreaDesignator))
            {
                Area targetArea = ViewingModeState.TargetArea;
                if (targetArea != null && targetArea.Map != null &&
                    position.InBounds(targetArea.Map) && targetArea[position])
                {
                    return "RimWorldAccess.Input.Map.PrefixInArea".Translate(tileInfo);
                }
            }

            // The refresh only builds or drops the scanner's temporary category; the prefix
            // below reads the engine directly, so a read-only caller keeps it without refreshing.
            if (allowStateWrites)
            {
                SubstructureOverlayState.CheckOverlayState();
            }
            if (SubstructureOverlayState.IsOverlayActive(Find.CurrentMap))
            {
                if (SubstructureOverlayState.IsDisconnectedAt(position, Find.CurrentMap))
                {
                    tileInfo = "RimWorldAccess.Input.Map.PrefixDisconnected".Translate(tileInfo).ToString();
                }
            }

            return tileInfo;
        }
    }
}
