using RimWorld.Planet;
using Verse;
using RimWorldAccess.Shell;

namespace RimWorldAccess
{
    /// <summary>
    /// Two halves of "stay with this pawn": the commentary behind
    /// <see cref="RimWorldAccessSettings.AnnounceSelectedPawnActivity"/>, and the cursor tether
    /// Alt+C arms. The tether is deliberately NOT a setting — always-on, it takes the cursor from
    /// a player who never asked. Polled per frame so the tether lands even while paused; the
    /// reports are paced in ticks, where a job or a room can actually change.
    /// </summary>
    public static class FollowedPawnAnnouncer
    {
        private const int ReportPollTicks = 15;

        private static Pawn trackedPawn;
        private static int lastReportTick;
        private static string lastJobReport;
        private static string lastLocation;

        private static Pawn tetheredPawn;
        private static IntVec3 tetheredCell;

        /// <summary>Arms the tether. Call AFTER writing the cursor, so it arms on the landing cell.</summary>
        public static void ArmTether(Pawn pawn)
        {
            tetheredPawn = pawn;
            tetheredCell = MapNavigationState.CurrentCursorPosition;
        }

        /// <summary>Once-per-frame poll. Cheap and self-gating with no tether and the setting off.</summary>
        public static void Poll()
        {
            // Held rather than released: something else owns the cursor and the screen for now.
            if (CursorIsSpokenFor())
                return;

            UpdateTether();

            RimWorldAccessSettings settings = RimWorldAccessMod_Settings.Settings;
            if (settings == null || !settings.AnnounceSelectedPawnActivity)
            {
                trackedPawn = null;
                return;
            }

            Pawn pawn = FollowedPawn();
            if (pawn == null)
            {
                trackedPawn = null;
                return;
            }

            // Only baselined: the selection announcement already spoke its job and location.
            if (pawn != trackedPawn)
            {
                Rebase(pawn);
                return;
            }

            int tick = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            if (tick >= lastReportTick && tick - lastReportTick < ReportPollTicks)
            {
                return;
            }
            lastReportTick = tick;

            AnnounceChanges(pawn);
        }

        /// <summary>Releases the tether and the tracked pawn, for session boundaries.</summary>
        public static void Forget()
        {
            trackedPawn = null;
            tetheredPawn = null;
            tetheredCell = IntVec3.Invalid;
            lastReportTick = 0;
            lastJobReport = null;
            lastLocation = null;
        }

        /// <summary>
        /// Carries the cursor and camera to the pawn, without the audio, announcement and teaching
        /// counters a real cursor move carries. A cursor no longer where this left it is the player
        /// taking it back, by any means, not just the arrow keys.
        /// </summary>
        private static void UpdateTether()
        {
            if (tetheredPawn == null)
                return;

            if (MapNavigationState.CurrentCursorPosition != tetheredCell
                || !tetheredPawn.Spawned
                || tetheredPawn.Map != Find.CurrentMap
                || Find.Selector == null
                || !Find.Selector.IsSelected(tetheredPawn))
            {
                tetheredPawn = null;
                tetheredCell = IntVec3.Invalid;
                return;
            }

            IntVec3 position = tetheredPawn.Position;
            if (position == tetheredCell)
                return;

            MapNavigationState.CurrentCursorPosition = position;
            tetheredCell = position;
            // The tiles crossed were never spoken, so nothing may be deduped against them.
            MapNavigationState.LastAnnouncedInfo = "";
            if (Find.CameraDriver != null)
                Find.CameraDriver.JumpToCurrentMapLoc(position);
        }

        /// <summary>
        /// True while anything but plain map navigation owns the cursor: a menu, a placement or
        /// viewing mode, or a targeter. Tethering then would drag a cursor the player is aiming.
        /// </summary>
        private static bool CursorIsSpokenFor()
        {
            return MapNavigationState.SuppressMapNavigation
                || ShellGuards.MenuOwnsInput()
                || ExternalMapTargeting.MapTargetingActive;
        }

        /// <summary>The tethered pawn, else the single selected pawn while the camera is on it.</summary>
        private static Pawn FollowedPawn()
        {
            Map map = Find.CurrentMap;
            if (map == null || !WorldRendererUtility.DrawingMap)
                return null;

            if (tetheredPawn != null)
                return tetheredPawn;

            if (MapNavigationState.CurrentCameraMode != CameraFollowMode.Pawn)
                return null;

            Pawn pawn = Find.Selector != null ? Find.Selector.SingleSelectedThing as Pawn : null;
            if (pawn == null || !pawn.Spawned || pawn.Map != map)
                return null;

            return pawn;
        }

        private static void Rebase(Pawn pawn)
        {
            trackedPawn = pawn;
            lastReportTick = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            lastJobReport = pawn.GetJobReport();
            lastLocation = TileInfoHelper.GetLocationContextPlain(pawn.Position, pawn.Map);
        }

        private static void AnnounceChanges(Pawn pawn)
        {
            // A null report is the gap between two jobs, not an activity of its own.
            string job = pawn.GetJobReport();
            bool jobChanged = !string.IsNullOrEmpty(job) && job != lastJobReport;
            if (jobChanged)
                lastJobReport = job;

            // Outdoors and unnamed rooms answer null: recorded so re-entry speaks again, never spoken.
            string location = TileInfoHelper.GetLocationContextPlain(pawn.Position, pawn.Map);
            bool locationChanged = location != lastLocation;
            if (locationChanged)
                lastLocation = location;
            bool speakLocation = locationChanged && !string.IsNullOrEmpty(location);

            if (jobChanged && speakLocation)
            {
                TolkHelper.SpeakData(
                    "RimWorldAccess.Map.Pawn.Follow.ActivityAndLocation".Translate(job, location).ToString(),
                    SpeechPriority.Low);
            }
            else if (jobChanged)
            {
                TolkHelper.SpeakData(job, SpeechPriority.Low);
            }
            else if (speakLocation)
            {
                TolkHelper.SpeakData(location, SpeechPriority.Low);
            }
        }
    }
}
