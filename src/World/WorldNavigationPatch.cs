using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patch for WorldInterface.HandleLowPriorityInput — now LIFECYCLE-ONLY. This
    /// method used to be a genuine input side door (arrows, the world scanner, caravan
    /// keys, C/']'/Enter/
    /// Escape); the execution-order proof (see the class doc comment on
    /// WorldScope.Nav.Game.cs) is that vanilla calls
    /// <c>WorldInterface.HandleLowPriorityInput</c> AFTER
    /// <c>UIRoot.UIRootOnGUI</c> returns (decompiled UIRoot_Play.cs:26/54) — the
    /// same method the shell dispatcher patches, at a HIGHER Harmony priority
    /// than this file's own prefix. Every
    /// branch this Prefix used to run therefore only ever saw whatever nothing
    /// upstream had already consumed: arrows and the C/']' handoffs were the
    /// only ones actually still live (verified branch by branch),
    /// and all three are WorldScope claims now (arrows: world.cursor.*;
    /// C: world.formCaravan; ']':
    /// world.caravanOrders — see WorldScope.Nav.Game.cs). Every other branch
    /// (world scanner PageUp/Down/Home/End/Alt+J, I, digits, Enter, Escape)
    /// was already dead code, shadowed by an earlier-running UKP rung, and is
    /// deleted rather than migrated.
    ///
    /// What remains is the SOLE lifecycle trigger for
    /// <see cref="WorldNavigationState"/>.Open()/Close() and
    /// <see cref="WorldScannerState"/>.Reset() — the isWorldView transition
    /// edge, unchanged byte-for-byte. Nothing else currently opens or closes
    /// world navigation state, so this Prefix cannot be deleted even though
    /// its input-handling role is gone.
    ///
    /// The visual half (a Postfix drawing the selected tile) is retired. Vanilla already draws the
    /// keyboard selection: <see cref="WorldNavigationState"/>.SyncSelectionWithGame writes
    /// <c>Find.WorldSelector.SelectedTile</c> on every arrow press (WorldNavigationState.cs:317-321),
    /// and
    /// <c>WorldDrawLayer_SelectedTile.Tile</c> reads exactly that property
    /// (decompiled RimWorld.Planet/WorldDrawLayer_SelectedTile.cs:8) while its
    /// base <c>WorldDrawLayer_SingleTile.ShouldRegenerate</c> regenerates the
    /// hex mesh whenever <c>Tile != lastDrawnPlanetTile</c> (decompiled
    /// RimWorld.Planet/WorldDrawLayer_SingleTile.cs:18-28) — so the game's own
    /// highlight follows the keyboard cursor with no help from this patch. The
    /// deleted Postfix was a caption panel: it printed
    /// <see cref="WorldInfoHelper"/>.GetTileSummary, the screen reader's own
    /// sentence, on screen, which this mod's caption-panel ban forbids outright.
    /// </summary>
    [HarmonyPatch(typeof(WorldInterface))]
    [HarmonyPatch("HandleLowPriorityInput")]
    public static class WorldNavigationPatch
    {
        private static bool lastFrameWasWorldView = false;

        /// <summary>
        /// Prefix patch: detects the isWorldView open/close transition and
        /// drives WorldNavigationState/WorldScannerState's lifecycle. No
        /// longer touches Event.current — see the class doc comment.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPriority(Priority.High)]
        public static void Prefix()
        {
            // Detect if we're in world view - MUST happen before any early returns
            // to properly track state transitions
            bool isWorldView = Current.ProgramState == ProgramState.Playing &&
                              Find.World != null &&
                              Find.World.renderer != null &&
                              Find.World.renderer.wantedMode == WorldRenderMode.Planet;

            // Handle state transitions - MUST happen before accessibility menu check
            // Otherwise we might miss the transition if a menu is active during map generation
            if (isWorldView && !lastFrameWasWorldView)
            {
                // Just entered world view
                WorldNavigationState.Open();
            }
            else if (!isWorldView && lastFrameWasWorldView)
            {
                // Just left world view
                WorldNavigationState.Close();
                WorldScannerState.Reset();
            }

            lastFrameWasWorldView = isWorldView;
        }
    }
}
