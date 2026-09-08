using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System.Text;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    [HarmonyPatch(typeof(Page_SelectStartingSite))]
    [HarmonyPatch("DoWindowContents")]
    public class StartingSitePatch
    {
        private static bool advancingToNextPage = false;
        private static PlanetTile savedTileForReturn = PlanetTile.Invalid;

        // Prefix: initialize the world-navigation session. Keyboard input and
        // the one-shot opening announcement are StartingSiteScreenScope's
        // (src/Shell/Screens/StartingSiteScreenScope.Game.cs) — the latter
        // through its ComposeOpenAnnouncement override.
        static void Prefix(Page_SelectStartingSite __instance, Rect rect)
        {
            try
            {
                // Don't initialize if pawn selection screen is active on top of us
                if (StartingPawnState.IsActive)
                    return;

                // Initialize shared world navigation state on first frame
                if (!WorldNavigationState.IsActive)
                {
                    if (savedTileForReturn.Valid)
                    {
                        WorldNavigationState.Open(WorldNavContext.WorldGen, savedTileForReturn);
                        savedTileForReturn = PlanetTile.Invalid;
                    }
                    else
                    {
                        WorldNavigationState.Open(WorldNavContext.WorldGen);
                    }
                    StartingSiteContext.Open();
                }

                // This patch is lifecycle-only: every key for this screen is
                // claimed by StartingSiteScreenScope and the widened
                // ScannerSearchScope — the scanner-search text fallback,
                // arrows/Ctrl+arrows, R/Space/F, Z / Ctrl+Z, and the 1-5
                // tile-info digits. Handling them out of window, through the
                // dispatcher, is what survives Entry's IMGUI focus loss.
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimWorld Access] Error in StartingSitePatch Prefix: {ex}");
            }
        }

        /// <summary>
        /// The Enter path, absorbed from the retired OnAcceptKeyPressed
        /// prefix: validate the current tile,
        /// sync the selection into the game, announce, persist the R10
        /// return-trip state, and run the page's own DoNext (which handles
        /// CheckConfirmSettle's proximity confirmation internally). Called by
        /// StartingSiteScreenScope's map-item activation and its Next row; the
        /// search-confirm branch that preceded this logic in the retired prefix
        /// belongs to ScannerSearchScope now, and the I-menu-read branch is gone
        /// with the menu itself.
        /// </summary>
        internal static void ConfirmSiteSelection(Page_SelectStartingSite instance)
        {
            // Use shared navigation state's tile
            PlanetTile tile = WorldNavigationState.CurrentSelectedTile;
            if (!tile.Valid)
            {
                // MUTATION-C: mirrors Page_SelectStartingSite.CanDoNext's dev-mode
                // auto-select branch (Page_SelectStartingSite.cs:191-196) so Enter and
                // the Buttons-toolbar Next row behave like the mouse Next button (which
                // calls the real CanDoNext()) in debug mode -- this branch was missing
                // here, so pressing Enter/Next with no
                // tile selected in dev mode silently did nothing instead of
                // auto-selecting a tile the way the mouse path does.
                if (Prefs.DevMode && !Find.WorldInterface.selector.AnyObjectOrTileSelected)
                {
                    tile = TileFinder.RandomStartingTile();
                    Find.WorldInterface.SelectedTile = tile;
                    WorldNavigationState.CurrentSelectedTile = tile;
                    string tileInfo = WorldInfoHelper.GetTileSummary(tile, includeRouteInfo: false);
                    TolkHelper.Speak("RimWorldAccess.StartingSite.DevRandomTileSelected".Loc(tileInfo));
                }
                else
                {
                    TolkHelper.Speak("RimWorldAccess.StartingSite.NoTileSelected".Loc());
                    return;
                }
            }

            // Check if tile is valid for settlement
            StringBuilder reason = new StringBuilder();
            bool isValid = TileFinder.IsValidTileForNewSettlement(tile, reason, forGravship: false);

            if (!isValid)
            {
                Localized errorMessage = "RimWorldAccess.StartingSite.CannotSettleHere".Loc(reason.ToString());
                TolkHelper.Speak(errorMessage, SpeechPriority.High);
                return;
            }

            // Mirrors Page_SelectStartingSite.CanDoNext's tutor gate (Page_SelectStartingSite.cs:206):
            // GetActionStringForChoosingTile is private, so it's harvested via reflection rather than
            // hand-copied, and AllowAction itself is called (Category B) so tutorial-mode behaves
            // exactly like vanilla's silent no-op when it blocks.
            string tileActionString = (string)AccessTools.Method(typeof(Page_SelectStartingSite), "GetActionStringForChoosingTile")
                .Invoke(null, new object[] { tile.Tile });
            if (!TutorSystem.AllowAction(tileActionString))
            {
                return;
            }

            // Sync game selection state so the game's DoNext picks up our tile
            WorldNavigationState.SyncSelectionWithGame();

            // Announce confirmation before advancing
            TolkHelper.Speak("RimWorldAccess.StartingSite.Selected".Loc());

            // Save tile so we can restore it if the user comes back from chargen
            savedTileForReturn = WorldNavigationState.CurrentSelectedTile;

            // Prevent PostClose from resetting state, which causes the DoWindowContents
            // Prefix to re-initialize and re-announce during the page transition.
            advancingToNextPage = true;

            // Call the game's DoNext directly to advance to the next page.
            // This handles CheckConfirmSettle (proximity warnings) internally.
            AccessTools.Method(typeof(Page_SelectStartingSite), "DoNext").Invoke(instance, null);
        }

        // Reset state when page is opened
        [HarmonyPatch(typeof(Page_SelectStartingSite), "PreOpen")]
        [HarmonyPostfix]
        static void PreOpen_Postfix()
        {
            // Ensure clean state in case PostClose didn't fire (e.g., page re-entered without closing)
            if (WorldNavigationState.IsActive)
            {
                WorldNavigationState.Close();
                StartingSiteContext.Close();
                WorldScannerState.Reset();
            }

            // Choosing a landing site is the player's first time on the world map, so teach the
            // world-map chapter (our corrected WorldCameraMovement) here. It already covers using
            // the scanner to find a spot, so the dedicated scanner lesson is deferred to game start
            // (see GameStartPatch) to avoid doubling up. Going through DocsTeacher (rather than
            // relying on the game's own PostOpen teach) also clears a veteran player's stale
            // completion of the vanilla concept so our re-authored version actually surfaces.
            DocsTeacher.Teach("WorldCameraMovement");
        }

        // Clean up when page is closed
        [HarmonyPatch(typeof(Page_SelectStartingSite), "PostClose")]
        [HarmonyPostfix]
        static void PostClose_Postfix()
        {
            // When advancing to the next page, keep world navigation state alive
            // so the DoWindowContents Prefix doesn't re-initialize and re-announce
            // the tile details during the page transition.
            if (!advancingToNextPage)
            {
                WorldNavigationState.Close();
                StartingSiteContext.Close();
                WorldScannerState.Reset();
            }
            advancingToNextPage = false;
        }

        /// <summary>
        /// The subtype accept twin, unconditional.
        /// Page_SelectStartingSite.OnAcceptKeyPressed overrides Page's own
        /// declaration WITHOUT calling base (vanilla body:
        /// <c>if (CanDoNext()) DoNext();</c>, no Use(), no route-planner
        /// guard), so neither the Window-level router twins nor the I1
        /// Page-level twins ever see it — a subtype-level patch is the only
        /// interception point (the MessageBox override-twin precedent).
        ///
        /// It returns false UNCONDITIONALLY because the retired prefix here
        /// returned false on EVERY branch — LearningHelperState yield,
        /// AnyLiveModal/AcceptConsumedThisFrame guard, search-confirm,
        /// I-menu read, and the validate/announce/DoNext tail (now
        /// <see cref="ConfirmSiteSelection"/>, invoked by
        /// StartingSiteScreenScope) — meaning the vanilla body has been fully dead
        /// under the mod for as long as this screen has been accessible.
        /// Total suppression is therefore byte parity AND the safety net for
        /// every frame the scope's claims stand down (learning helper owns
        /// the keyboard, a live modal masks the page): the deferred
        /// per-window re-test of Accept gets a FRESH event that main-pass
        /// consumption cannot reach, and without this blocker it would run
        /// CanDoNext→DoNext behind the overlay's back. The mouse Next button
        /// calls CanDoNext/DoNext directly and is unaffected.
        /// </summary>
        [HarmonyPatch(typeof(Page_SelectStartingSite), "OnAcceptKeyPressed")]
        [HarmonyPrefix]
        static bool OnAcceptKeyPressed_Prefix()
        {
            return false;
        }
    }
}
