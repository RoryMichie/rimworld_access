using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages keyboard navigation for door controls (Building_Door).
    /// Allows toggling hold-open setting via keyboard shortcuts.
    /// </summary>
    public static class DoorControlState
    {
        private static Building_Door door = null;
        private static bool isActive = false;

        public static bool IsActive => isActive;

        /// <summary>Live hold-open flag for DoorControlScope's checkbox row (read fresh each announce, never cached).</summary>
        public static bool HoldOpen => door != null && door.HoldOpen;

        public static void Open(Building targetBuilding)
        {
            if (!GuardHelper.RequireBuilding(targetBuilding)) return;

            Building_Door doorBuilding = targetBuilding as Building_Door;
            if (doorBuilding == null)
            {
                TolkHelper.Speak("RimWorldAccess.Building.Door.NotADoor".Loc());
                return;
            }

            door = doorBuilding;
            isActive = true;
            MapNavigationState.SuppressMapNavigation = true;

            AnnounceCurrentStatus();
        }

        public static void Close()
        {
            door = null;
            isActive = false;
            MapNavigationState.SuppressMapNavigation = false;
        }

        /// <summary>
        /// Flips the hold-open flag only (map-controls migration: the sound cue and re-announce now
        /// live in DoorControlScope, which speaks the toggle-doctrine state-change fragment for its
        /// checkbox row instead of this method's old full-status re-announce).
        /// </summary>
        public static bool ToggleHoldOpen()
        {
            if (door == null)
                return false;

            var holdOpenField = typeof(Building_Door).GetField("holdOpenInt",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (holdOpenField != null)
            {
                bool currentValue = (bool)holdOpenField.GetValue(door);
                // MUTATION-C: mirrors Building_Door.GetGizmos' hold-open
                // Command_Toggle verbatim (toggleAction is a raw
                // `holdOpenInt = !holdOpenInt`; no gated setter exists).
                holdOpenField.SetValue(door, !currentValue);
                return true;
            }

            TolkHelper.Speak("RimWorldAccess.Building.Door.HoldOpenAccessError".Loc(), SpeechPriority.High);
            return false;
        }

        public static void AnnounceCurrentStatus()
        {
            if (door == null)
                return;

            string holdOpenLabel = (string)"CommandToggleDoorHoldOpen".Translate();
            string onOff = door.HoldOpen ? (string)"On".Translate() : (string)"Off".Translate();
            string holdOpen = "RimWorldAccess.Building.Door.HoldOpenWithValue".Translate(holdOpenLabel, onOff);

            string openness = door.Open
                ? "RimWorldAccess.Building.Door.CurrentlyOpen".Translate()
                : "RimWorldAccess.Building.Door.CurrentlyClosed".Translate();

            TolkHelper.Speak("RimWorldAccess.Building.Door.LabelStatusOpenness".Loc(door.LabelCap, holdOpen, openness));
        }

        public static void AnnounceDetailedStatus()
        {
            if (door == null)
                return;

            string holdOpenLabel = (string)"CommandToggleDoorHoldOpen".Translate();
            string onOff = door.HoldOpen ? (string)"On".Translate() : (string)"Off".Translate();

            var b = new AnnouncementBuilder();
            b.Add(door.LabelCap);
            b.Add("RimWorldAccess.Building.Door.HoldOpenWithValue".Translate(holdOpenLabel, onOff));
            b.Add(door.Open
                ? "RimWorldAccess.Building.Door.DetailStateOpen".Translate()
                : "RimWorldAccess.Building.Door.DetailStateClosed".Translate());

            if (door.powerComp != null)
            {
                b.Add(door.powerComp.PowerOn
                    ? "RimWorldAccess.Building.Door.DetailPoweredFast".Translate()
                    : "RimWorldAccess.Building.Door.DetailNoPowerSlow".Translate());
            }
            else
            {
                b.Add("RimWorldAccess.Building.Door.DetailManual".Translate());
            }

            TolkHelper.SpeakData(b.Build());
        }
    }
}
