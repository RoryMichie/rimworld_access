using RimWorld;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The keyboard focus scope for the windowless storage settings menu (priority cycler,
    /// quick actions, hit-points/quality ranges, and the thing filter category tree for a
    /// stockpile zone or shelf): a screen-reader representation of vanilla's storage tab.
    ///
    /// This scope is now a
    /// <see cref="FilterTreeScopeBase"/> subclass riding <see cref="TreeModel{T}"/> directly
    /// instead of the old bare <see cref="FocusScope"/>+<see cref="ICharSink"/> router over
    /// <see cref="RimWorldAccess.TreeNavigationHelper"/>. The mod still owns no window here — the
    /// surface is the windowless <see cref="RimWorldAccess.StorageSettingsMenuState"/> facade — so
    /// the scope rides the focus stack through <see cref="StorageSettingsScopeMirror"/> instead of
    /// a WindowStack mirror, the same shape as <see cref="BillsScope"/>/<see cref="BillConfigScope"/>.
    /// Row content, in flat order: the Priority stepper (this screen's own leading row, only when
    /// <see cref="RimWorldAccess.StorageSettingsMenuState.ShowPriority"/>), ClearAll/AllowAll
    /// buttons, HitPoints/Quality range openers (both from the shared base), then the category
    /// tree. See <see cref="FilterTreeScopeBase"/>'s header for why these are prefix ROWS
    /// rather than synthetic tree nodes — a genuine, audible sibling-position delta for the
    /// top-level category nodes.
    ///
    /// DELIBERATE MICRO-DEVIATION (float menu), preserved from the pre-migration scope: the
    /// retired StorageSettingsMenuPatch had no yield to WindowlessFloatMenuState at any priority
    /// — storage never opens a windowless float menu itself; only a mouse-spawned gizmo redirect
    /// could trigger one while this menu is open. The shell's blanket
    /// LegacyKeyboardOverlayActive stand-down lets the float menu win, the opposite of
    /// <see cref="ThingFilterMenuScope"/>/<see cref="BillsScope"/>/<see cref="BillConfigScope"/>,
    /// which all deliberately reproduce a genuine legacy loss to the float menu.
    /// </summary>
    public sealed class StorageSettingsScope : FilterTreeScopeBase
    {
        public override string Name
        {
            get { return "storage-settings"; }
        }

        protected override string TreeRegionLabel
        {
            get { return "RimWorldAccess.Shell.FilterTree.StorageRegionName".Translate(); }
        }

        protected override ThingFilterSessionCore.FilterContext BuildFilterContext()
        {
            return RimWorldAccess.StorageSettingsMenuState.BuildFilterContext();
        }

        protected override bool ShowHitPointsRange
        {
            get { return RimWorldAccess.StorageSettingsMenuState.HitPointsConfigurable; }
        }

        protected override bool ShowQualityRange
        {
            get { return RimWorldAccess.StorageSettingsMenuState.QualityConfigurable; }
        }

        // ------------------------------------------------------------------
        // The Priority stepper — Storage's own leading row (not shared with any other screen).
        // ------------------------------------------------------------------

        protected override int SubclassLeadingRowCount
        {
            get { return RimWorldAccess.StorageSettingsMenuState.ShowPriority ? 1 : 0; }
        }

        protected override ElementDescription DescribeSubclassLeadingRow(int index)
        {
            var d = new ElementDescription();
            d.Label = "Priority".Translate();
            d.Role = ElementRole.Stepper;
            d.Value = RimWorldAccess.StorageSettingsMenuState.CurrentSettings.Priority.Label().CapitalizeFirst();
            return d;
        }

        /// <summary>Enter/Space on the Priority row cycles forward — matches the legacy ToggleCurrent's Priority case (both Enter and Space always advanced, never reversed).</summary>
        protected override void ActivateSubclassLeadingRow(int index)
        {
            RimWorldAccess.StorageSettingsMenuState.CyclePriority(true);
        }

        protected override bool CanAdjustSubclassLeadingRow(int index)
        {
            return true;
        }

        protected override void AdjustSubclassLeadingRow(int index, int direction)
        {
            RimWorldAccess.StorageSettingsMenuState.CyclePriority(direction > 0);
        }

        // ------------------------------------------------------------------
        // Escape (no active search): close the menu and return to the parent context.
        // ------------------------------------------------------------------

        protected override void OnFilterTreeClose()
        {
            RimWorldAccess.StorageSettingsMenuState.Close();
            RimWorldAccess.InspectionReturnHelper.AnnounceParentOrFallback(
                "RimWorldAccess.Inspection.Storage.MenuClosed".Translate());
        }

        // ------------------------------------------------------------------
        // Lifecycle. The tree is rebuilt from a callback fired directly by
        // StorageSettingsMenuState.Open() (see RebuildFresh's remarks) rather than from OnPush
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
        /// Called directly by StorageSettingsMenuState.Open() for every genuine fresh session
        /// (four call sites: three StorageAdapter branches, NutritionStorageAdapter, plus the two
        /// gizmo redirects ShelfLinkingPatch/ZoneGizmoPatch) — BEFORE this scope is necessarily
        /// pushed yet, which is harmless: SetTreeRoot/RefreshModel/MoveFirst don't depend on
        /// FocusStack membership. Explicitly resets the region cursor to the first row rather
        /// than leaving whatever index a PREVIOUS, differently-sized tree happened to leave
        /// behind (ScreenModel's SetRegions only clamps an existing cursor into the new count,
        /// it does not reset it). Deliberately NOT wired to OnPush (see the class remarks above).
        /// </summary>
        public void RebuildFresh()
        {
            SetTreeRoot(ThingFilterSessionCore.BuildCategoryTreeRoot(ContextForBuild(TreeRegionIndex)));
            RefreshModel();
            Model.CurrentRegion?.MoveFirst();
        }
    }

    /// <summary>
    /// Keeps <see cref="StorageSettingsScope"/> in lockstep with
    /// <see cref="RimWorldAccess.StorageSettingsMenuState.IsActive"/>, reconciled every OnGUI
    /// pass. Stands down (pops) while an info card is open over the menu, the same info-card
    /// yield every migrated inspection scope uses.
    /// </summary>
    internal static class StorageSettingsScopeMirror
    {
        private static readonly StorageSettingsScope scope = new StorageSettingsScope();

        static StorageSettingsScopeMirror()
        {
            RimWorldAccess.StorageSettingsMenuState.RebuildCallback = scope.RebuildFresh;
        }

        public static void Reconcile()
        {
            if (RimWorldAccess.StorageSettingsMenuState.IsActive && !InfoCardState.IsActive)
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
