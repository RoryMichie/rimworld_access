using RimWorldAccess.Shell;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    public static partial class FishingZoneMenuState
    {
        #region Composer Descriptions

        /// <summary>
        /// One row's live description: its identity label plus the control role a sighted
        /// player sees on vanilla's own fishing tab and that control's current value. Read
        /// fresh on every refresh, so nothing here caches a composed label and no edit path
        /// has to write one back. Level and sibling position are left unset —
        /// <see cref="TreeRegionScope"/> fills both.
        /// </summary>
        internal static ElementDescription DescribeItem(InspectionTreeItem item)
        {
            switch (RowOf(item)?.Kind)
            {
                case MenuItemType.ZoneStatus: return DescribeZoneStatus();
                case MenuItemType.AllowToggle: return DescribeAllowToggle();
                case MenuItemType.RepeatMode: return DescribeRepeatMode();
                case MenuItemType.RepeatCount: return DescribeRepeatCount();
                case MenuItemType.TargetCount: return DescribeTargetCount();
                case MenuItemType.CurrentlyHave: return DescribeCurrentlyHave();
                case MenuItemType.PauseWhenSatisfied: return DescribePauseWhenSatisfied();
                case MenuItemType.UnpauseAt: return DescribeUnpauseAt();
                case MenuItemType.MinimumPopulation: return DescribeMinPopulation();
                case MenuItemType.FishSpecies: return DescribeFishSpecies(item);
                case MenuItemType.FishSpeciesItem: return DescribeFishSpeciesItem(item);
                default: return new ElementDescription();
            }
        }

        private static ElementDescription DescribeZoneStatus()
        {
            string detail = GetZoneStatusLabel();
            string tooltip = "FishPopulationDesc".Translate().ToString().TrimEnd('.', ' ');
            return new ElementDescription
            {
                Label = "RimWorldAccess.Inspection.Fishing.SearchLabel.ZoneStatus".Translate(),
                ReadOnly = true,
                Extras = string.IsNullOrEmpty(tooltip) ? detail : detail + ". " + tooltip,
            };
        }

        private static ElementDescription DescribeAllowToggle()
        {
            bool allowed = allowedProp != null && (bool)allowedProp.GetValue(fishingZone);
            return new ElementDescription
            {
                Label = "RimWorldAccess.Inspection.Fishing.SearchLabel.AllowFishing".Translate(),
                Role = ElementRole.Checkbox,
                Check = allowed ? CheckState.Checked : CheckState.Unchecked,
            };
        }

        private static ElementDescription DescribeRepeatMode()
        {
            return new ElementDescription
            {
                Label = "RimWorldAccess.Inspection.Fishing.SearchLabel.RepeatMode".Translate(),
                Role = ElementRole.ComboBox,
                Value = RepeatModeName(repeatModeField?.GetValue(fishingZone)),
            };
        }

        private static ElementDescription DescribeRepeatCount()
        {
            int count = repeatCountField != null ? (int)repeatCountField.GetValue(fishingZone) : 0;
            return DescribeCount("RimWorldAccess.Inspection.Fishing.SearchLabel.RepeatCount", count);
        }

        private static ElementDescription DescribeTargetCount()
        {
            int count = targetCountField != null ? (int)targetCountField.GetValue(fishingZone) : 0;
            return DescribeCount("RimWorldAccess.Inspection.Fishing.SearchLabel.TargetCount", count);
        }

        private static ElementDescription DescribeUnpauseAt()
        {
            int count = unpauseAtCountField != null ? (int)unpauseAtCountField.GetValue(fishingZone) : 0;
            return DescribeCount("RimWorldAccess.Inspection.Fishing.SearchLabel.UnpauseAt", count);
        }

        /// <summary>
        /// One of the three whole-number counts. EntersEditOnAccept marks Enter as this row's
        /// own: it opens exact numeric entry rather than offering the screen's proceed button.
        /// </summary>
        private static ElementDescription DescribeCount(string labelKey, int count)
        {
            return new ElementDescription
            {
                Label = labelKey.Translate(),
                Role = ElementRole.Stepper,
                Value = count.ToString(),
                EntersEditOnAccept = true,
            };
        }

        private static ElementDescription DescribeCurrentlyHave()
        {
            int count = ownedFishCountProp != null ? (int)ownedFishCountProp.GetValue(fishingZone) : 0;
            return new ElementDescription
            {
                Label = "RimWorldAccess.Inspection.Fishing.SearchLabel.CurrentlyHave".Translate(),
                ReadOnly = true,
                Value = count.ToString(),
            };
        }

        private static ElementDescription DescribePauseWhenSatisfied()
        {
            bool pause = pauseWhenSatisfiedField != null && (bool)pauseWhenSatisfiedField.GetValue(fishingZone);
            return new ElementDescription
            {
                Label = "RimWorldAccess.Inspection.Fishing.SearchLabel.PauseWhenSatisfied".Translate(),
                Role = ElementRole.Checkbox,
                Check = pause ? CheckState.Checked : CheckState.Unchecked,
            };
        }

        private static ElementDescription DescribeMinPopulation()
        {
            float pct = targetPopulationPctField != null ? (float)targetPopulationPctField.GetValue(fishingZone) : 0.5f;
            int pctInt = Mathf.RoundToInt(pct * 100f);
            int fishAtThreshold = Mathf.RoundToInt(pct * MaxPopulation());

            string tooltip = "MinimumPopulationDesc".Translate().ToString().TrimEnd('.', ' ');
            return new ElementDescription
            {
                Label = "RimWorldAccess.Inspection.Fishing.SearchLabel.MinimumPopulation".Translate(),
                Role = ElementRole.Slider,
                Value = FormatMinPopulationValue(pctInt, fishAtThreshold),
                Extras = tooltip,
            };
        }

        private static ElementDescription DescribeFishSpecies(InspectionTreeItem item)
        {
            return new ElementDescription
            {
                Label = "RimWorldAccess.Inspection.Fishing.SearchLabel.FishSpecies".Translate(),
                Role = ElementRole.TreeItem,
                Expanded = item.IsExpanded,
                Extras = "RimWorldAccess.Inspection.Fishing.Label.FishSpeciesCount".Translate(GetFishSpeciesCount()),
            };
        }

        private static ElementDescription DescribeFishSpeciesItem(InspectionTreeItem item)
        {
            ThingDef fish = RowOf(item).Fish;
            bool uncommon = fish != null && IsUncommonFish(fish);
            return new ElementDescription
            {
                Label = fish != null ? fish.LabelCap.ToString() : item.Label,
                Role = ElementRole.TreeItem,
                Extras = uncommon
                    ? "RimWorldAccess.Inspection.Fishing.Label.UncommonSuffix".Translate().ToString().Trim()
                    : null,
            };
        }

        #endregion
    }
}
