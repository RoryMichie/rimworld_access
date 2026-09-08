using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// State for "Go To" coordinate input feature (Ctrl+G).
    /// Allows typing X,Z coordinates to jump the map cursor.
    /// Supports absolute (10,20), partial (10 or ,20), and relative (+10,-20) coordinates.
    /// </summary>
    public static class GoToState
    {
        private static bool isActive = false;

        private static string xBuffer = "";
        private static string zBuffer = "";

        // false = editing X, true = editing Z.
        private static bool isInZField = false;

        /// <summary>Whether coordinate input mode is active.</summary>
        public static bool IsActive => isActive;

        /// <summary>Opens coordinate input mode and announces the current position.</summary>
        public static void Activate()
        {
            xBuffer = "";
            zBuffer = "";
            isInZField = false;
            isActive = true;

            IntVec3 current = MapNavigationState.CurrentCursorPosition;
            TolkHelper.Speak("RimWorldAccess.Map.GoTo.Open".Loc(current.x, current.z), SpeechPriority.Normal);
        }

        /// <summary>
        /// Whether an overlay menu is showing that should receive Enter/Escape instead.
        /// Go To yields to menu UI, never to placement mode — placement and Go To coexist.
        /// </summary>
        public static bool ShouldYieldToOverlayMenu()
        {
            if (!isActive) return false;

            // The float-menu term is redundant with FloatMenuOverlayScope's modal masking; kept
            // so this predicate reads standalone.
            if (ArchitectTreeState.IsActive || WindowlessFloatMenuState.IsActive || ShapeSelectionMenuState.IsActive)
                return true;

            return false;
        }

        /// <summary>Appends a digit to the current field; +/- only at the start of a buffer.</summary>
        public static void HandleCharacter(char c)
        {
            if (c == '+' || c == '-')
            {
                string currentBuffer = isInZField ? zBuffer : xBuffer;
                if (!string.IsNullOrEmpty(currentBuffer))
                {
                    return;
                }
            }
            else if (c < '0' || c > '9')
            {
                return;
            }

            if (isInZField)
            {
                zBuffer += c;
            }
            else
            {
                xBuffer += c;
            }

            TolkHelper.SpeakData(c.ToString(), SpeechPriority.Low);
        }

        /// <summary>
        /// Comma or space: moves from the X field to the Z field.
        /// </summary>
        public static void HandleFieldSeparator()
        {
            if (!isInZField)
            {
                isInZField = true;
                TolkHelper.Speak("RimWorldAccess.Map.GoTo.FieldZ".Loc(), SpeechPriority.Normal);
            }
        }

        /// <summary>
        /// Removes the last character from the current field buffer.
        /// If Z field is empty, switches back to X field.
        /// </summary>
        public static void HandleBackspace()
        {
            if (isInZField)
            {
                if (string.IsNullOrEmpty(zBuffer))
                {
                    isInZField = false;
                    TolkHelper.Speak("RimWorldAccess.Map.GoTo.FieldX".Loc(), SpeechPriority.Normal);
                }
                else
                {
                    char deleted = zBuffer[zBuffer.Length - 1];
                    zBuffer = zBuffer.Substring(0, zBuffer.Length - 1);
                    TolkHelper.Speak("RimWorldAccess.Map.Input.Deleted".Loc(deleted), SpeechPriority.Low);
                }
            }
            else
            {
                if (string.IsNullOrEmpty(xBuffer))
                {
                    Cancel();
                }
                else
                {
                    char deleted = xBuffer[xBuffer.Length - 1];
                    xBuffer = xBuffer.Substring(0, xBuffer.Length - 1);
                    TolkHelper.Speak("RimWorldAccess.Map.Input.Deleted".Loc(deleted), SpeechPriority.Low);
                }
            }
        }

        /// <summary>
        /// Moves the cursor to the parsed coordinates, clamped to the map. Stays open on a
        /// parse failure so the player can fix the input.
        /// </summary>
        public static void ConfirmGoTo()
        {
            if (!isActive) return;

            Map map = Find.CurrentMap;
            if (map == null)
            {
                Cancel();
                return;
            }

            IntVec3 current = MapNavigationState.CurrentCursorPosition;

            if (!ParseCoordinate(xBuffer, current.x, out int targetX))
            {
                TolkHelper.Speak("RimWorldAccess.Map.GoTo.InvalidX".Loc(), SpeechPriority.Normal);
                return;
            }

            if (!ParseCoordinate(zBuffer, current.z, out int targetZ))
            {
                TolkHelper.Speak("RimWorldAccess.Map.GoTo.InvalidZ".Loc(), SpeechPriority.Normal);
                return;
            }

            targetX = Mathf.Clamp(targetX, 0, map.Size.x - 1);
            targetZ = Mathf.Clamp(targetZ, 0, map.Size.z - 1);

            IntVec3 targetPos = new IntVec3(targetX, 0, targetZ);

            MapNavigationState.CurrentCursorPosition = targetPos;
            Find.CameraDriver.JumpToCurrentMapLoc(targetPos);

            // Cursor mode: the camera follows the cursor and stops following a pawn.
            MapNavigationState.CurrentCameraMode = CameraFollowMode.Cursor;
            GizmoNavigationState.PawnJustSelected = false;

            TerrainAudioHelper.PlayCellAudio(targetPos, map, 0.5f);
            MapArrowKeyHandler.AnnouncePosition(targetPos, map);

            Close();
        }

        /// <summary>Cancels coordinate input mode and says so.</summary>
        public static void Cancel()
        {
            Close();
            TolkHelper.Speak("RimWorldAccess.Map.GoTo.Cancelled".Loc(), SpeechPriority.Normal);
        }

        /// <summary>
        /// Closes the coordinate input mode silently. Internal so
        /// <see cref="StateResetRegistry"/> can run it at session boundaries.
        /// </summary>
        internal static void Close()
        {
            xBuffer = "";
            zBuffer = "";
            isInZField = false;
            isActive = false;
        }

        /// <summary>
        /// Parses one coordinate field: absolute, empty (keeps <paramref name="currentValue"/>),
        /// or relative (+/-offset). Returns false on a malformed buffer.
        /// </summary>
        private static bool ParseCoordinate(string buffer, int currentValue, out int result)
        {
            result = currentValue;

            if (string.IsNullOrEmpty(buffer) || string.IsNullOrWhiteSpace(buffer))
            {
                return true;
            }

            buffer = buffer.Trim();

            if (buffer.StartsWith("+") || buffer.StartsWith("-"))
            {
                if (int.TryParse(buffer, out int offset))
                {
                    result = currentValue + offset;
                    return true;
                }
                return false;
            }

            if (int.TryParse(buffer, out int absolute))
            {
                result = absolute;
                return true;
            }

            return false;
        }
    }
}
