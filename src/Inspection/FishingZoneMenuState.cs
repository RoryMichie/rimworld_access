using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Verse;
using RimWorld;
using RimWorldAccess.Shell;
using UnityEngine;

namespace RimWorldAccess
{
    /// <summary>
    /// The windowless fishing zone menu's DATA and MUTATION half (Zone_Fishing, Odyssey): the zone
    /// identity, the open/close lifecycle, the row tree it presents, each row's live description, and
    /// every write back into the zone. Reflection throughout, because Zone_Fishing is an Odyssey type
    /// absent from our compile references.
    /// Navigation, typeahead, announcements and numeric entry live on
    /// <see cref="FishingZoneScope"/>, a <see cref="TreeRegionScope"/> that owns the tree; this class
    /// only builds a root, populates the fish-species branch on first expand, and answers per-row
    /// questions. Rows carry no composed label: every announced word is derived live in
    /// <see cref="DescribeItem"/>, so a value change needs no label refresh.
    /// Numeric stepper value-change speech keeps using <see cref="NumericStepperHelper"/> and plain
    /// <see cref="TolkHelper"/> rather than
    /// <see cref="AnnouncementComposer.ComposeStateChange"/> — not a second composer-based echo.
    /// </summary>
    public static partial class FishingZoneMenuState
    {
        private static Zone fishingZone = null;
        private static bool isActive = false;

        // Cached reflection info for Zone_Fishing.
        private static Type zoneFishingType = null;
        private static PropertyInfo allowedProp = null;
        private static FieldInfo allowedField = null; // Private field for setting
        private static FieldInfo repeatModeField = null;
        private static FieldInfo repeatCountField = null;
        private static FieldInfo targetCountField = null;
        private static PropertyInfo ownedFishCountProp = null;
        private static FieldInfo pauseWhenSatisfiedField = null;
        private static FieldInfo unpauseAtCountField = null;
        private static FieldInfo targetPopulationPctField = null;
        private static PropertyInfo fishTypeProp = null;
        private static MethodInfo recheckPausedMethod = null;

        // Cached reflection for FishRepeatMode enum
        private static Type fishRepeatModeType = null;
        private static object doForeverValue = null;
        private static object repeatCountValue = null;
        private static object targetCountValue = null;

        private enum MenuItemType
        {
            ZoneStatus,
            AllowToggle,
            RepeatMode,
            RepeatCount,
            TargetCount,
            CurrentlyHave,
            PauseWhenSatisfied,
            UnpauseAt,
            MinimumPopulation,
            FishSpecies,
            FishSpeciesItem
        }

        /// <summary>
        /// Domain tag on each <see cref="InspectionTreeItem.Data"/>: which zone setting the row
        /// presents, and for FishSpeciesItem rows which fish. Never leaves this class —
        /// <see cref="FishingZoneScope"/> asks through the capability predicates instead.
        /// </summary>
        private class FishRowData
        {
            public MenuItemType Kind;
            public ThingDef Fish;
        }

        public static bool IsActive => isActive;

        /// <summary>The row's domain tag, or null for a row this menu did not build.</summary>
        private static FishRowData RowOf(InspectionTreeItem item)
        {
            return item == null ? null : item.Data as FishRowData;
        }

        /// <summary>Initializes reflection info for Zone_Fishing.</summary>
        private static bool InitializeReflection()
        {
            if (zoneFishingType != null)
                return true;

            zoneFishingType = AccessTools.TypeByName("RimWorld.Zone_Fishing");
            if (zoneFishingType == null)
            {
                Log.Warning("[RimWorld Access] Could not find Zone_Fishing type - Odyssey DLC may not be installed");
                return false;
            }

            allowedProp = zoneFishingType.GetProperty("Allowed"); // Public property for reading
            allowedField = zoneFishingType.GetField("allowed", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance); // Private field for setting
            repeatModeField = zoneFishingType.GetField("repeatMode");
            repeatCountField = zoneFishingType.GetField("repeatCount");
            targetCountField = zoneFishingType.GetField("targetCount");
            ownedFishCountProp = zoneFishingType.GetProperty("OwnedFishCount");
            pauseWhenSatisfiedField = zoneFishingType.GetField("pauseWhenSatisfied");
            unpauseAtCountField = zoneFishingType.GetField("unpauseAtCount");
            targetPopulationPctField = zoneFishingType.GetField("targetPopulationPct");
            fishTypeProp = zoneFishingType.GetProperty("FishType");
            recheckPausedMethod = zoneFishingType.GetMethod("RecheckPausedDueToResourceCount");

            fishRepeatModeType = AccessTools.TypeByName("RimWorld.FishRepeatMode");
            if (fishRepeatModeType != null)
            {
                doForeverValue = Enum.Parse(fishRepeatModeType, "DoForever");
                repeatCountValue = Enum.Parse(fishRepeatModeType, "RepeatCount");
                targetCountValue = Enum.Parse(fishRepeatModeType, "TargetCount");
            }

            // WaterBody is reached directly via map.waterBodyTracker; no reflection needed.

            return true;
        }

        /// <summary>
        /// Single funnel for every Zone_Fishing settings write here. There is no gated vanilla setter
        /// to ride: ITab_Fishing writes these public fields raw from its own widgets, so callers
        /// mirror the ITab's clamping before calling.
        /// </summary>
        private static void SetZoneField(System.Reflection.FieldInfo field, object value)
        {
            // MUTATION-C: mirrors ITab_Fishing's direct writes to Zone_Fishing's
            // repeat/count/population fields (no gated setter exists in vanilla).
            field.SetValue(fishingZone, value);
        }

        /// <summary>
        /// Invokes Zone_Fishing's own public <c>RecheckPausedDueToResourceCount()</c> (vehicle B),
        /// mirroring ITab_Fishing.FillTab's call sites: after a repeat-mode change, a target-count
        /// change, and a pause-when-satisfied toggle.
        /// </summary>
        private static void RecheckPausedDueToResourceCount()
        {
            recheckPausedMethod?.Invoke(fishingZone, null);
        }

        /// <summary>
        /// Mirrors ITab_Fishing.FillTab's TargetCount branch: every targetCount change shifts
        /// unpauseAtCount by the same delta, floored at zero, whether or not pauseWhenSatisfied is
        /// set, so the gap between stop and restart amounts survives. Callers still recheck the pause
        /// state afterwards.
        /// </summary>
        private static void ShiftUnpauseAtWithTargetCount(int oldTargetCount, int newTargetCount)
        {
            if (unpauseAtCountField == null || oldTargetCount == newTargetCount)
                return;

            int unpauseAt = (int)unpauseAtCountField.GetValue(fishingZone);
            int shifted = Mathf.Max(0, unpauseAt + (newTargetCount - oldTargetCount));
            if (shifted == unpauseAt)
                return;

            // MUTATION-C: mirrors ITab_Fishing.FillTab TargetCount delta-shift
            // (unpauseAtCount = Max(0, unpauseAt + delta)); no vanilla Try* twin
            // exists for keyboard adjustment.
            SetZoneField(unpauseAtCountField, shifted);
        }

        /// <summary>Opens the fishing zone configuration menu.</summary>
        public static void Open(Zone zone)
        {
            if (zone == null || zone.GetType().Name != "Zone_Fishing")
            {
                Log.Error("[RimWorld Access] Cannot open fishing zone menu: invalid zone");
                return;
            }

            if (!InitializeReflection())
            {
                TolkHelper.Speak("RimWorldAccess.Inspection.Fishing.NotAvailable".Loc());
                return;
            }

            fishingZone = zone;
            isActive = true;

            // Present and announce synchronously, before the mirror's next reconcile pass can push.
            FishingZoneScope.Live?.OpenTree(BuildTree());

            Log.Message($"[RimWorld Access] Opened fishing zone menu for {zone.label}");
        }

        /// <summary>Closes the fishing zone configuration menu.</summary>
        public static void Close()
        {
            fishingZone = null;
            isActive = false;
            FishingZoneScope.Live?.ClearTree();
        }

        /// <summary>
        /// Rebuilds the row tree after a change that adds or removes rows (a repeat-mode change hides
        /// one count row and reveals another; the pause toggle reveals the unpause threshold), keeping
        /// the cursor on the same row NUMBER and re-announcing it.
        /// </summary>
        private static void RebuildRows()
        {
            FishingZoneScope.Live?.RebuildTreePreservingIndex(BuildTree());
        }

        #region Tree Construction

        /// <summary>
        /// Builds one row. The label is the field's own IDENTITY text, never its value, so a row never
        /// needs refreshing after an edit — <see cref="DescribeItem"/> reads the zone live.
        /// </summary>
        private static InspectionTreeItem NewRow(MenuItemType kind, string label,
            int indent = 0, ThingDef fish = null)
        {
            return new InspectionTreeItem
            {
                Label = label,
                IndentLevel = indent,
                Data = new FishRowData { Kind = kind, Fish = fish }
            };
        }

        private static void AddChild(InspectionTreeItem parent, InspectionTreeItem child)
        {
            child.Parent = parent;
            parent.Children.Add(child);
        }

        /// <summary>The row set this menu presents, in vanilla ITab_Fishing's own order.</summary>
        internal static InspectionTreeItem BuildTree()
        {
            var root = new InspectionTreeItem();

            AddChild(root, NewRow(MenuItemType.ZoneStatus,
                "RimWorldAccess.Inspection.Fishing.SearchLabel.ZoneStatus".Translate()));

            AddChild(root, NewRow(MenuItemType.AllowToggle,
                "RimWorldAccess.Inspection.Fishing.SearchLabel.AllowFishing".Translate()));

            AddChild(root, NewRow(MenuItemType.RepeatMode,
                "RimWorldAccess.Inspection.Fishing.SearchLabel.RepeatMode".Translate()));

            object repeatMode = repeatModeField?.GetValue(fishingZone);

            if (repeatMode != null && repeatMode.Equals(repeatCountValue))
            {
                AddChild(root, NewRow(MenuItemType.RepeatCount,
                    "RimWorldAccess.Inspection.Fishing.SearchLabel.RepeatCount".Translate()));
            }

            if (repeatMode != null && repeatMode.Equals(targetCountValue))
            {
                AddChild(root, NewRow(MenuItemType.TargetCount,
                    "RimWorldAccess.Inspection.Fishing.SearchLabel.TargetCount".Translate()));

                AddChild(root, NewRow(MenuItemType.CurrentlyHave,
                    "RimWorldAccess.Inspection.Fishing.SearchLabel.CurrentlyHave".Translate()));

                AddChild(root, NewRow(MenuItemType.PauseWhenSatisfied,
                    "RimWorldAccess.Inspection.Fishing.SearchLabel.PauseWhenSatisfied".Translate()));

                bool pauseWhenSatisfied = pauseWhenSatisfiedField != null && (bool)pauseWhenSatisfiedField.GetValue(fishingZone);
                if (pauseWhenSatisfied)
                {
                    AddChild(root, NewRow(MenuItemType.UnpauseAt,
                        "RimWorldAccess.Inspection.Fishing.SearchLabel.UnpauseAt".Translate()));
                }
            }

            AddChild(root, NewRow(MenuItemType.MinimumPopulation,
                "RimWorldAccess.Inspection.Fishing.SearchLabel.MinimumPopulation".Translate()));

            // Fish species: the one genuinely expandable branch. Children populate on first expand,
            // so a menu that is never expanded never sweeps the water body's fish lists.
            var fishSpeciesNode = NewRow(MenuItemType.FishSpecies,
                "RimWorldAccess.Inspection.Fishing.SearchLabel.FishSpecies".Translate());
            fishSpeciesNode.IsExpandable = true;
            AddChild(root, fishSpeciesNode);

            return root;
        }

        /// <summary>Populates the fish-species branch on first expand: common fish (extras included) then uncommon, the two lists ITab_Fishing itself lists.</summary>
        internal static void LoadFishChildren(InspectionTreeItem node)
        {
            WaterBody waterBody = GetWaterBody();
            if (waterBody == null)
                return;

            foreach (ThingDef fish in FishSpecies(waterBody))
            {
                AddChild(node, NewRow(MenuItemType.FishSpeciesItem, fish.LabelCap, indent: 1, fish: fish));
            }
        }

        private static IEnumerable<ThingDef> FishSpecies(WaterBody waterBody)
        {
            IEnumerable<ThingDef> common = waterBody.CommonFishIncludingExtras;
            if (common != null)
            {
                foreach (ThingDef fish in common)
                    yield return fish;
            }

            IEnumerable<ThingDef> uncommon = waterBody.UncommonFish;
            if (uncommon != null)
            {
                foreach (ThingDef fish in uncommon)
                    yield return fish;
            }
        }

        #endregion

        #region Label Generators

        private static string GetZoneStatusLabel()
        {
            try
            {
                var waterBody = GetWaterBody();
                if (waterBody == null)
                    return "RimWorldAccess.Inspection.Fishing.Status.NoWaterBody".Translate();

                int population = Mathf.RoundToInt(waterBody.Population);
                int maxPop = Mathf.RoundToInt(waterBody.MaxPopulation);

                object fishTypeValue = fishTypeProp?.GetValue(fishingZone);
                string fishType = fishTypeValue?.ToString()
                    ?? "RimWorldAccess.Inspection.Fishing.Status.UnknownFishType".Translate().ToString();

                // Vanilla's own absolute-population bucketing (ITab_Fishing.FillTab calls
                // FishingUtility.FishPopulationLabel the same way), never a mod-invented ratio bucket.
                string populationLabel = FishingUtility.FishPopulationLabel(population);

                return "RimWorldAccess.Inspection.Fishing.Status.Detail".Translate(
                    fishType, population, maxPop, populationLabel);
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimWorld Access] Error getting zone status: {ex.Message}");
                return "RimWorldAccess.Inspection.Fishing.Status.UnableToRead".Translate();
            }
        }

        /// <summary>One FishRepeatMode value's spoken name -- the row's value word and the picker's own option labels.</summary>
        private static string RepeatModeName(object repeatMode)
        {
            string modeName = repeatMode?.ToString();

            string nameKey;
            if (modeName == "DoForever") nameKey = "RimWorldAccess.Inspection.Fishing.Label.RepeatModeDoForever";
            else if (modeName == "RepeatCount") nameKey = "RimWorldAccess.Inspection.Fishing.Label.RepeatModeRepeatCount";
            else if (modeName == "TargetCount") nameKey = "RimWorldAccess.Inspection.Fishing.Label.RepeatModeTargetCount";
            else nameKey = "RimWorldAccess.Inspection.Fishing.Label.RepeatModeUnknown";

            return nameKey.Translate().ToString();
        }

        /// <summary>The bare "{0}% ({1} fish)" value used by the minimum-population row and its stepper boundary announcements.</summary>
        private static string FormatMinPopulationValue(int pctInt, int fishAtThreshold)
        {
            return "RimWorldAccess.Inspection.Fishing.Label.MinimumPopulationValue".Translate(pctInt, fishAtThreshold);
        }

        #endregion

        #region Focus Ring

        /// <summary>
        /// Which row of vanilla's own tab the focused menu row corresponds to, for
        /// <see cref="FishingTabRingPatch"/>'s pass. Rows vanilla's FillTab has no counterpart for —
        /// the allow/forbid toggle (a zone gizmo) and the fish-species parent — ring nothing. Called
        /// from inside a vanilla DRAW patch, so it reads the cursor as-is and never refreshes.
        /// </summary>
        internal static ListingRingFocus CurrentListingFocus()
        {
            FishRowData row = isActive ? RowOf(FishingZoneScope.Live?.FocusedRow) : null;
            if (row == null)
                return ListingRingFocus.None;

            switch (row.Kind)
            {
                case MenuItemType.FishSpeciesItem:
                    return row.Fish == null
                        ? ListingRingFocus.None
                        : new ListingRingFocus { RowObject = row.Fish };

                case MenuItemType.RepeatMode:
                    return new ListingRingFocus
                    {
                        RowKey = FishingRowKeys.RepeatModeSite,
                        LabelTripwire = VanillaRepeatModeLabel(),
                    };

                case MenuItemType.RepeatCount:
                    return new ListingRingFocus
                    {
                        RowKey = FishingRowKeys.RepeatCountSite,
                        LabelTripwire = VanillaRepeatCountLabel(),
                    };

                // Many-to-one: vanilla folds the owned count and the target count into one label
                // (ITab_Fishing.cs:70).
                case MenuItemType.TargetCount:
                case MenuItemType.CurrentlyHave:
                    return new ListingRingFocus
                    {
                        RowKey = FishingRowKeys.CurrentlyHaveSite,
                        LabelTripwire = VanillaCurrentlyHaveLabel(),
                    };

                case MenuItemType.PauseWhenSatisfied:
                    return new ListingRingFocus
                    {
                        RowKey = FishingRowKeys.PauseWhenSatisfied,
                        LabelTripwire = (string)"PauseWhenSatisfied".Translate(),
                    };

                case MenuItemType.UnpauseAt:
                    return new ListingRingFocus
                    {
                        RowKey = FishingRowKeys.UnpauseAtSite,
                        LabelTripwire = VanillaUnpauseAtLabel(),
                    };

                // No tripwire: vanilla appends a live count to the label (ITab_Fishing.cs:107, :109),
                // so it never matches ours.
                case MenuItemType.ZoneStatus:
                    return new ListingRingFocus { RowKey = FishingRowKeys.FishPopulation };

                case MenuItemType.MinimumPopulation:
                    return new ListingRingFocus { RowKey = FishingRowKeys.MinimumPopulation };

                default:
                    return ListingRingFocus.None;
            }
        }

        /// <summary>Mirrors ITab_Fishing.RepeatModeLabel (decompiled :139-148).</summary>
        private static string VanillaRepeatModeLabel()
        {
            switch (repeatModeField?.GetValue(fishingZone)?.ToString())
            {
                case "RepeatCount": return "FishRepeatMode_RepeatCount".Translate().CapitalizeFirst();
                case "TargetCount": return "FishRepeatMode_TargetCount".Translate().CapitalizeFirst();
                case "DoForever": return "FishRepeatMode_Forever".Translate().CapitalizeFirst();
                default: return "Unknown";
            }
        }

        /// <summary>Mirrors the label vanilla draws at ITab_Fishing.cs:65.</summary>
        private static string VanillaRepeatCountLabel()
        {
            int count = repeatCountField != null ? (int)repeatCountField.GetValue(fishingZone) : 0;
            return string.Concat("RepeatCount".Translate() + " ", count.ToString());
        }

        /// <summary>Mirrors the label vanilla draws at ITab_Fishing.cs:70.</summary>
        private static string VanillaCurrentlyHaveLabel()
        {
            int owned = ownedFishCountProp != null ? (int)ownedFishCountProp.GetValue(fishingZone) : 0;
            int target = targetCountField != null ? (int)targetCountField.GetValue(fishingZone) : 0;
            return string.Concat("CurrentlyHave".Translate() + ": ", owned.ToString(), " / ", target.ToString());
        }

        /// <summary>Mirrors the label vanilla draws at ITab_Fishing.cs:88.</summary>
        private static string VanillaUnpauseAtLabel()
        {
            int count = unpauseAtCountField != null ? (int)unpauseAtCountField.GetValue(fishingZone) : 0;
            return ("UnpauseWhenYouHave".Translate() + ": " + count.ToString("F0")).Resolve();
        }

        #endregion

        #region Helper Methods

        /// <summary>The WaterBody for the fishing zone, via map.waterBodyTracker.</summary>
        private static WaterBody GetWaterBody()
        {
            try
            {
                if (fishingZone == null || fishingZone.Cells == null || !fishingZone.Cells.Any())
                    return null;

                var map = fishingZone.Map;
                if (map == null || map.waterBodyTracker == null)
                    return null;

                var firstCell = fishingZone.Cells.First();
                if (map.waterBodyTracker.TryGetWaterBodyAt(firstCell, out var waterBody))
                {
                    return waterBody;
                }

                return null;
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimWorld Access] Error getting water body: {ex.Message}");
                return null;
            }
        }

        private static int GetFishSpeciesCount()
        {
            var waterBody = GetWaterBody();
            return waterBody == null ? 0 : FishSpecies(waterBody).Count();
        }

        private static bool IsUncommonFish(ThingDef fish)
        {
            var waterBody = GetWaterBody();
            if (waterBody == null) return false;

            var uncommonFish = waterBody.UncommonFish;
            return uncommonFish != null && uncommonFish.Contains(fish);
        }

        #endregion

    }
}
