using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The keyboard focus scope for the windowless thing-filter submenu (the allow/disallow
    /// category tree shared by bill ingredient filters, pen animal/auto-cut filters, and the
    /// shells tab): a screen-reader representation of vanilla's ThingFilterUI panel.
    ///
    /// This scope is now a <see cref="FilterTreeScopeBase"/>
    /// subclass riding <see cref="TreeModel{T}"/> directly instead of the old bare
    /// <see cref="FocusScope"/>+<see cref="ICharSink"/> router over
    /// <see cref="RimWorldAccess.TreeNavigationHelper"/> — the SAME base
    /// <see cref="StorageSettingsScope"/> rides. The mod still owns no
    /// window here — the surface is the windowless <see cref="RimWorldAccess.ThingFilterMenuState"/>
    /// facade — so the scope rides the focus stack through
    /// <see cref="ThingFilterMenuScopeMirror"/> instead of a WindowStack mirror, the same shape
    /// as <see cref="BillsScope"/>/<see cref="BillConfigScope"/>. Row content, in flat order:
    /// ClearAll/AllowAll buttons, HitPoints/Quality range openers (both from the shared base,
    /// per-caller hide-flag threading unchanged), then the category tree — no screen-specific
    /// leading row (unlike Storage's Priority stepper).
    ///
    /// This menu opens as the ingredient filter ON TOP of <see cref="BillConfigState"/> (which
    /// itself opens on top of <see cref="BillsMenuState"/>) without closing either: the bills →
    /// bill config → ingredient filter chain leaves all three states active simultaneously, and
    /// all three scopes stay pushed. Modal stack masking (this mirror reconciles after
    /// <see cref="BillConfigScopeMirror"/>, so it lands topmost) gives the filter the keyboard,
    /// matching the retired ladder's branch order.
    ///
    /// The retired handler's own top guard returned before ever reaching the ThingFilterMenuState
    /// branch whenever a windowless float menu was open, so this scope loses to the float menu —
    /// the shell's blanket LegacyKeyboardOverlayActive stand-down reproduces that exactly, the
    /// same as <see cref="BillsScope"/>/<see cref="BillConfigScope"/> (and the opposite of
    /// StorageSettingsScope's deliberate deviation — see its header).
    ///
    /// Alt+I now arrives via the shared base (<c>filterTree.infoCard</c>) — this CLOSES the
    /// documented pre-existing gap (the retired handler had no info-card action for this menu at
    /// all), a changelog-worthy fix, not merely a preserved behavior.
    /// </summary>
    public sealed class ThingFilterMenuScope : FilterTreeScopeBase
    {
        public override string Name
        {
            get { return "thing-filter-menu"; }
        }

        /// <summary>The vanilla dialog title threaded through from the call site ("Pen Animals", the shells tab title, the ingredient-filter title, the auto-cut title) — previously stored but never spoken; now the region's own name.</summary>
        protected override string TreeRegionLabel
        {
            get { return RimWorldAccess.ThingFilterMenuState.MenuTitle; }
        }

        protected override ThingFilterSessionCore.FilterContext BuildFilterContext()
        {
            return RimWorldAccess.ThingFilterMenuState.BuildFilterContext();
        }

        protected override bool ShowHitPointsRange
        {
            get { return RimWorldAccess.ThingFilterMenuState.HitPointsConfigurable; }
        }

        protected override bool ShowQualityRange
        {
            get { return RimWorldAccess.ThingFilterMenuState.QualityConfigurable; }
        }

        // ------------------------------------------------------------------
        // Escape (no active search): close the menu and return to the parent context.
        // ------------------------------------------------------------------

        protected override void OnFilterTreeClose()
        {
            RimWorldAccess.ThingFilterMenuState.Close();
            RimWorldAccess.InspectionReturnHelper.AnnounceParentOrFallback(
                "RimWorldAccess.Inspection.Patch.ClosedThingFilterMenu".Translate());
        }

        // ------------------------------------------------------------------
        // Lifecycle. The tree is rebuilt from a callback fired directly by
        // ThingFilterMenuState.Open() (see RebuildFresh's remarks) rather than from OnPush
        // itself: this scope's own mirror stands down (pops) while an info card is open over
        // the menu and re-pushes on close, so OnPush ALSO fires on that round trip — if the
        // rebuild lived there, every info-card look-up would silently discard the player's
        // cursor position (and the tree's own expand/collapse state) back to the first row.
        // Since this is a persistent singleton scope, its Tree/Model simply survive an
        // info-card pop/push untouched when OnPush does nothing.
        // ------------------------------------------------------------------

        public override void OnFocus()
        {
            base.OnFocus();
            AnnounceCurrentItem();
        }

        /// <summary>
        /// Called directly by ThingFilterMenuState.Open() for every genuine fresh session (four
        /// call sites, a different filter/tree each time) — BEFORE this scope is necessarily
        /// pushed yet, which is harmless: SetTreeRoot/RefreshModel/MoveFirst don't depend on
        /// FocusStack membership. Explicitly resets the region cursor to the first row rather
        /// than leaving whatever index a PREVIOUS, differently-sized tree happened to leave
        /// behind (ScreenModel's SetRegions only clamps an existing cursor into the new count,
        /// it does not reset it) — matches the legacy tree's own "always start at index 0 on
        /// Open()" behavior. Deliberately NOT wired to OnPush (see the class remarks above).
        /// </summary>
        public void RebuildFresh()
        {
            SetTreeRoot(ThingFilterSessionCore.BuildCategoryTreeRoot(ContextForBuild(TreeRegionIndex)));
            RefreshModel();
            Model.CurrentRegion?.MoveFirst();
        }
    }

    /// <summary>
    /// Keeps <see cref="ThingFilterMenuScope"/> in lockstep with
    /// <see cref="RimWorldAccess.ThingFilterMenuState.IsActive"/>, reconciled every OnGUI pass
    /// AFTER <see cref="BillConfigScopeMirror"/> so it lands above both the bills and bill-config
    /// scopes on the stack when all three are active (opening ThingFilterMenuState closes
    /// neither of them) — matching the retired BuildingInspectPatch ladder, which checked
    /// ThingFilterMenuState before BillConfigState/BillsMenuState.
    ///
    /// Stands down (pops) while an info card is open over the menu, the same info-card yield
    /// every migrated inspection scope uses.
    /// </summary>
    internal static class ThingFilterMenuScopeMirror
    {
        private static readonly ThingFilterMenuScope scope = new ThingFilterMenuScope();

        static ThingFilterMenuScopeMirror()
        {
            RimWorldAccess.ThingFilterMenuState.RebuildCallback = scope.RebuildFresh;
        }

        public static void Reconcile()
        {
            if (RimWorldAccess.ThingFilterMenuState.IsActive && !InfoCardState.IsActive)
            {
                FocusStack.Push(scope);
            }
            else
            {
                FocusStack.Pop(scope);
            }
        }
    }
}
