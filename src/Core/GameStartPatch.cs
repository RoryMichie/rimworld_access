using HarmonyLib;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Ensures the view switches from world map to local colony map after game initialization.
    /// During chargen, the world renderer's wantedMode remains WorldRenderMode.Planet from the
    /// starting site selection screen. Nothing in Game.InitNewGame explicitly hides the world view,
    /// so without this patch the player starts the game staring at the world map.
    /// Also fires on game load (which calls FinalizeInit), safely guarded by CurrentMap check.
    /// </summary>
    [HarmonyPatch(typeof(Game), "FinalizeInit")]
    public static class GameStartPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            // The full ordered checklist of static per-screen resets lives in
            // StateResetRegistry.OnGameLoad — see that file for the manifest and the
            // per-entry rationale this used to carry inline.
            StateResetRegistry.RunOnGameLoad();

            LongEventHandler.ExecuteWhenFinished(() =>
            {
                if (Find.CurrentMap != null)
                {
                    // A brand-new game starts at Normal speed; pause it so the player can get
                    // their bearings before the drop pods land. Loads keep their saved speed.
                    if (Find.TickManager.TicksGame == 0)
                    {
                        Find.TickManager.Pause();
                    }

                    CameraJumper.TryHideWorld();

                    if (WorldNavigationState.IsActive)
                    {
                        WorldNavigationState.Close();
                    }

                    // Selecting a colonist is the prerequisite for every order, but at game start the
                    // starting colonists are still descending in drop pods — the map has no spawned
                    // colonists yet. Arm the lesson to fire the moment one actually lands.
                    DocsTeacher.RequestSelectingColonistsWhenPresent();

                    // Controlling time (speed up, pause, read the clock) is one of the first things a
                    // new player needs — they must speed up time just to let the drop pods land. The
                    // vanilla concept is normally surfaced by the desire engine, but our onboarding
                    // lessons crowd out its slot while paused, so teach it explicitly here.
                    DocsTeacher.Teach("TimeControls");

                    // The scanner and forbid/allow lessons both wait until every starting colonist
                    // has landed (all pod cargo down). The scanner surveys the whole map, so it is
                    // most useful once everything is on the ground; forbidding likewise lets the
                    // player free the map after the forbidden items arrive, not before. Firing in the
                    // same poll frame coalesces them into one announcement, matching the time lesson's
                    // promise that a few new lessons appear once everyone has landed.
                    DocsTeacher.RequestMapBasicsWhenAllColonistsPresent();
                }
            });
        }
    }
}
