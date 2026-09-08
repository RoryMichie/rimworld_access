using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Facade for the windowless storage settings surface (stockpile zones / shelves,
    /// vanilla surface: ITab_Storage). Holds the live <see cref="StorageSettings"/>, the parent
    /// filter, priority-row visibility, and the category tree root; builds the shared filter
    /// session context.
    ///
    /// All cursor, announcement, and keyboard-claim logic
    /// that used to live here (TreeNavigationHelper-driven BuildTree/FormatItemAnnouncement/
    /// NavigatePrevious/ExpandCurrent/ToggleCurrent/HandleCancel/typeahead routing, and the
    /// Priority/ClearAll/AllowAll/HitPointsRange/QualityRange nodes that used to live IN the
    /// InspectionTreeItem tree as synthetic root children) has moved to
    /// <see cref="RimWorldAccess.Shell.StorageSettingsScope"/>, a
    /// <see cref="RimWorldAccess.Shell.FilterTreeScopeBase"/> subclass riding
    /// <see cref="RimWorldAccess.Shell.TreeModel{T}"/> directly. This class is now a thin
    /// data/lifecycle facade the scope reads from — the migration family's D1 ruling ("states
    /// remain facades") applies verbatim. The scope builds its own category/thing-def tree from
    /// <see cref="BuildFilterContext"/> through
    /// <see cref="RimWorldAccess.Shell.FilterTreeScopeBase.ContextForBuild"/>;
    /// Priority/ClearAll/AllowAll/range rows are the scope's own prefix rows, not tree nodes
    /// (see FilterTreeScopeBase's header for why).
    ///
    /// EXTERNAL CONTRACT (do not rename without updating the listed callers, several of which
    /// belong to other packets/files this migration must not touch):
    /// <see cref="Open"/> — four call sites (StorageAdapter x3, NutritionStorageAdapter).
    /// <see cref="IsActive"/> — read by ShellGuards, MapScope.CursorTools, InspectComponentScopes
    /// doc comments, MapNavigationPatch.
    /// </summary>
    public static class StorageSettingsMenuState
    {
        private static bool isActive = false;
        private static StorageSettings currentSettings = null;
        private static ThingFilter parentFilter = null;
        private static TreeNode_ThingCategory rootNode = null;
        private static bool showPriority = true;

        public static bool IsActive
        {
            get { return isActive; }
        }

        public static bool ShowPriority
        {
            get { return showPriority; }
        }

        public static StorageSettings CurrentSettings
        {
            get { return currentSettings; }
        }

        /// <summary>Storage filters always show both HP/quality rows (no recipe-scoping) — defaults true when there's no parent filter, matching vanilla ThingFilterUI.</summary>
        public static bool HitPointsConfigurable
        {
            get { return parentFilter == null || parentFilter.allowedHitPointsConfigurable; }
        }

        public static bool QualityConfigurable
        {
            get { return parentFilter == null || parentFilter.allowedQualitiesConfigurable; }
        }

        /// <summary>
        /// Wired once by StorageSettingsScopeMirror to the live scope's own tree-rebuild method,
        /// called at the end of every <see cref="Open"/> (a genuine fresh session). Deliberately
        /// NOT triggered by the scope's own OnPush: StorageSettingsScopeMirror stands down (pops)
        /// while an info card is open over this menu and re-pushes on close, so OnPush ALSO fires
        /// on that round trip — rebuilding there would silently discard the player's cursor
        /// position and the tree's expand/collapse state on every info-card look-up (the same
        /// fix applied here to this scope as well). Firing this from Open() instead
        /// means the tree only rebuilds when the underlying zone/shelf's filter
        /// actually changes.
        /// </summary>
        public static Action RebuildCallback;

        public static void Open(StorageSettings settings, bool showPriority = true)
        {
            if (settings == null)
            {
                Log.Error("Cannot open storage settings menu: settings is null");
                return;
            }

            StorageSettingsMenuState.showPriority = showPriority;
            currentSettings = settings;
            parentFilter = settings.owner?.GetParentStoreSettings()?.filter;
            // Use the parent filter's stable display root when available so expansion
            // state isn't perturbed by toggling individual items.
            if (parentFilter != null)
            {
                rootNode = parentFilter.DisplayRootCategory;
            }
            else
            {
                rootNode = settings.filter.RootNode ?? ThingCategoryNodeDatabase.RootNode;
            }

            isActive = true;

            RebuildCallback?.Invoke();
        }

        public static void Close()
        {
            isActive = false;
            currentSettings = null;
            parentFilter = null;
            rootNode = null;
            showPriority = true;
        }

        private static List<SpecialThingFilterDef> hiddenSpecialFilters;

        /// <summary>
        /// MUTATION-free mirror of ITab_Storage.HiddenSpecialThingFilters (decompiled
        /// RimWorld/ITab_Storage.cs:178-187; private instance method, shared by its ITab_Shells
        /// and ITab_BiosculpterNutritionStorage subclasses, so one list serves every storage
        /// surface).
        /// </summary>
        private static List<SpecialThingFilterDef> HiddenSpecialThingFilters()
        {
            if (hiddenSpecialFilters == null)
            {
                hiddenSpecialFilters = new List<SpecialThingFilterDef>();
                if (ModsConfig.IdeologyActive)
                {
                    hiddenSpecialFilters.Add(SpecialThingFilterDefOf.AllowVegetarian);
                    hiddenSpecialFilters.Add(SpecialThingFilterDefOf.AllowCarnivore);
                    hiddenSpecialFilters.Add(SpecialThingFilterDefOf.AllowCannibal);
                    hiddenSpecialFilters.Add(SpecialThingFilterDefOf.AllowInsectMeat);
                }
            }
            return hiddenSpecialFilters;
        }

        /// <summary>
        /// Builds the shared per-session context threaded through
        /// <see cref="ThingFilterSessionCore"/>'s tree-build/allowance/toggle/announce logic.
        /// ForceHiddenFilters mirrors vanilla's ITab_Storage.HiddenSpecialThingFilters (the four
        /// Ideology diet filters storage tabs hide) — StorageSettings previously passed null here,
        /// which was a bug: storage screens showed four checkboxes vanilla hides.
        /// </summary>
        public static ThingFilterSessionCore.FilterContext BuildFilterContext()
        {
            return new ThingFilterSessionCore.FilterContext
            {
                CurrentFilter = currentSettings.filter,
                ParentFilter = parentFilter,
                ForceHiddenFilters = HiddenSpecialThingFilters(),
                DisplayRoot = rootNode
            };
        }

        // ===== Priority cycling (Storage's own prefix row; not shared with any other screen) =====

        private static readonly StoragePriority[] PrioritiesAsc = new[]
        {
            StoragePriority.Low,
            StoragePriority.Normal,
            StoragePriority.Preferred,
            StoragePriority.Important,
            StoragePriority.Critical
        };

        public static void CyclePriority(bool forward)
        {
            int currentIndex = Array.IndexOf(PrioritiesAsc, currentSettings.Priority);
            if (currentIndex < 0) currentIndex = 1;

            int len = PrioritiesAsc.Length;
            int delta = forward ? 1 : -1;
            currentIndex = ((currentIndex + delta) % len + len) % len;

            currentSettings.Priority = PrioritiesAsc[currentIndex];
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            // Standard stepper consequence (the changed value only); the priority label IS the
            // whole state, so speak it directly rather than re-reading the whole row.
            TolkHelper.SpeakData(currentSettings.Priority.Label().CapitalizeFirst());
        }
    }
}
