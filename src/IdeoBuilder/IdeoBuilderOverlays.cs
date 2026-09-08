namespace RimWorldAccess
{
    /// <summary>
    /// Lifecycle/liveness utility for the builder's three windowless overlay editors (precept
    /// selection, typed precepts, deities). Before slice IB-5 this class was also the
    /// shared router the three host screens (the Custom-creation hub, the in-game reform dialog,
    /// the Archonexus reform screen) funnelled input and float-menu-return refresh bookkeeping
    /// through; that role is retired now that each overlay has its own scope with its own claims
    /// and its own <c>OnFocus</c>-driven refresh — precept selection and typed precepts as full
    /// <c>ScreenScope</c> subclasses (<c>IdeoPreceptScreenScope</c>,
    /// <c>IdeoTypedPreceptScreenScope</c>, <c>src/Shell/Screens/</c>), deities promoted the same
    /// way (<c>IdeoDeityScreenScope</c>,
    /// same folder; the appearance editor is gone entirely — the focus-ring census's wave-3 slice
    /// W3-9 replaced it with vanilla's own <c>Dialog_EditIdeoStyleItems</c>, whose lifetime the
    /// WindowStack owns). What remains here has nothing
    /// to do with keyboard routing: <see cref="AnyActive"/> is still consulted by the hub's
    /// DoBack/DoNext guards and its residual prefix's announce gate (none of which route input
    /// through it, they just need to know whether an overlay is currently up), and
    /// <see cref="CloseAllOverlayEditors"/> is the IB-0 lifecycle sweeper called from every host's
    /// close path plus the two session-boundary resets.
    /// </summary>
    public static class IdeoBuilderOverlays
    {
        public static bool AnyActive =>
            IdeoPreceptSelectionState.IsActive
            || IdeoTypedPreceptState.IsActive
            || IdeoDeityListState.IsActive;

        /// <summary>
        /// Closes all three overlay editors unconditionally. Every host that can display one of
        /// these overlays (the Custom-creation hub, the in-game reform dialog, the Archonexus
        /// reform screen) must call this from its own close path, or a host closing while an
        /// overlay is still open leaves the overlay's IsActive stuck true underneath a screen
        /// that no longer exists -- the stale overlay then misroutes the
        /// host that opens next and, for IdeoTypedPreceptState specifically, hijacks every
        /// FloatMenu in the game via DialogInterceptionPatch. Also called from the two
        /// session-boundary reset points (GameStartPatch, MainMenuAccessibilityPatch) as a
        /// backstop for the transitions no host close path covers (mid-flow save-load, quit to
        /// menu). All three Close() bodies are silent and side-effect-free, so calling them
        /// unconditionally here is safe even when none of them are active. Vanilla's own appearance
        /// window needs no term here: a scene transition builds a fresh <c>UIRoot</c> with a fresh
        /// <c>WindowStack</c> (decompiled Verse/Root.cs:49-58, reached by both
        /// <c>GenScene.GoToMainMenu</c> and <c>GameDataSaveLoader.LoadGame</c>), so every open
        /// window is discarded by the game itself.
        /// </summary>
        public static void CloseAllOverlayEditors()
        {
            IdeoPreceptSelectionState.Close();
            IdeoTypedPreceptState.Close();
            IdeoDeityListState.Close();
        }
    }
}
