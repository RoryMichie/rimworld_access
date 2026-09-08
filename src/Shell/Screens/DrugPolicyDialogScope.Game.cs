using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The <see cref="PolicyDialogScope"/> for <see cref="Dialog_ManageDrugPolicies"/>, whose
    /// <c>DoContentsRect</c> draws the drug grid rather than a ThingFilterUI panel. The grid is
    /// content region 1 of the one window; region 0 stays the policy list.
    ///
    /// The region's SHAPE toggles between the drug list and one drug's settings — the same
    /// "same region toggles shape" trick <c>AnimalsScope</c> uses for its submenu, and the shape
    /// this screen already had as a windowless scope. <see cref="savedDrugIndex"/> remembers the
    /// drug row across the settings sub-mode's lifetime, because one <c>ListModel</c> cursor is
    /// serving two logically different indices.
    ///
    /// ELEMENT SEMANTICS. Every row announces as the vanilla widget it actually is, with
    /// <c>Dialog_ManageDrugPolicies.DoEntryRow</c> as the authority: "Keep in inventory" is its
    /// <c>Widgets.TextFieldNumeric</c> bounded by <c>PawnUtility.GetMaxAllowedToPickUp</c> (a Stepper
    /// here, since Left/Right step it); the three usage rows are its <c>Widgets.Checkbox</c> calls;
    /// "Frequency" is its <c>FrequencyHorizontalSlider(0.1f, 25f)</c> and the two thresholds its
    /// <c>HorizontalSlider(0.01f, 1f)</c> calls. The drug list is a real table over vanilla's own
    /// eight columns, in vanilla's own order, each carrying the tooltip vanilla attaches to it.
    ///
    /// <c>drugPolicy.settingDecrease</c>/<c>settingIncrease</c>/<c>toggleSetting</c>/<c>infoCard</c>
    /// stay their OWN action ids rather than folding into the base's generic Left/Right adjust, so an
    /// existing rebinding of these ids keeps working.
    ///
    /// ESCAPE. In the settings sub-mode Escape returns to the drug list, and this scope both
    /// claims AND owns it, the symmetry a real window's Escape needs. In list mode it does neither,
    /// so one Escape closes the window through vanilla's own path, exactly as it does from
    /// every other region of this window.
    /// </summary>
    public sealed class DrugPolicyDialogScope : PolicyDialogScope
    {
        private const int DrugsRegion = FirstContentsRegion;

        private enum Mode { DrugList, DrugSettings }

        private enum SettingType
        {
            TakeToInventory,
            AllowForAddiction,
            AllowForJoy,
            AllowScheduled,
            Frequency,
            MoodThreshold,
            JoyThreshold
        }

        private sealed class DrugSetting
        {
            public SettingType Type;
            public string Label;
            public string Tooltip;
        }

        // Harvested from the vanilla slider calls this screen stands in for.
        private const float MinFrequency = 0.1f;
        private const float MaxFrequency = 25f;
        private const float MinThreshold = 0.01f;
        private const float MaxThreshold = 1f;

        private DrugPolicy policy;
        private Mode mode = Mode.DrugList;

        /// <summary>The drug row remembered across the Settings sub-mode's lifetime — see the class remarks.</summary>
        private int savedDrugIndex = -1;

        private readonly List<DrugSetting> currentSettings = new List<DrugSetting>();

        public DrugPolicyDialogScope(Window dialog) : base(dialog)
        {
            Claim(SharedMenuGrammar.Cancel, delegate { ReturnToDrugList(); }, when: ClaimsSubModeCancel);

            Claim("drugPolicy.settingDecrease", delegate { AdjustSetting(-1); }, when: DrugSettingsMode);
            Claim("drugPolicy.settingIncrease", delegate { AdjustSetting(1); }, when: DrugSettingsMode);
            Claim("drugPolicy.toggleSetting", delegate { ToggleSetting(); }, when: DrugSettingsMode);
            Claim("drugPolicy.infoCard", delegate { OpenInfoCard(); },
                when: delegate { return Model.RegionIndex == DrugsRegion; });
        }

        private bool DrugSettingsMode()
        {
            return mode == Mode.DrugSettings;
        }

        /// <summary>Escape belongs to this scope only while the settings sub-mode is open.</summary>
        private bool ClaimsSubModeCancel()
        {
            return mode == Mode.DrugSettings && !TypeaheadHasActiveSearch;
        }

        /// <summary>
        /// The other half of the claim above: a scope that claims Escape must own it, or vanilla's
        /// own GUI pass closes the window before the dispatcher sees the key.
        /// </summary>
        public override bool OwnsCancel
        {
            get { return base.OwnsCancel || ClaimsSubModeCancel(); }
        }

        internal bool Owns(Window window)
        {
            return ReferenceEquals(window, dialog);
        }

        // ------------------------------------------------------------------
        // Focus ring. Vanilla highlights the selected POLICY but nothing in the
        // drug grid, so the grid's rows and cells are ringed from the geometry
        // DrugPolicyRowDrawPatch records off vanilla's own row method.
        // ------------------------------------------------------------------

        /// <summary>
        /// The drug row the ring belongs on: the cursor's row in list mode, and in the settings
        /// sub-mode the drug being edited, whose row rect is the base for every cell rect.
        /// -1 when the cursor is elsewhere in the window.
        /// </summary>
        internal int FocusedDrugIndex
        {
            get
            {
                if (mode == Mode.DrugSettings)
                {
                    return savedDrugIndex;
                }
                if (Model.RegionIndex != DrugsRegion)
                {
                    return -1;
                }
                ListModel drugs = Model.Region(DrugsRegion);
                return drugs != null ? drugs.Index : -1;
            }
        }

        protected internal override Rect FocusedContentRect()
        {
            if (Model.RegionIndex != DrugsRegion)
            {
                return base.FocusedContentRect();
            }
            if (mode == Mode.DrugSettings)
            {
                Rect cell = FocusedSettingCellRect();
                if (cell.width > 0f && cell.height > 0f)
                {
                    return cell;
                }
            }
            return DrugPolicyRowDrawPatch.RowRect();
        }

        /// <summary>Empty when the focused setting's widget is not drawn this frame, which sends the ring back to the whole row.</summary>
        private Rect FocusedSettingCellRect()
        {
            ListModel rows = Model.Region(DrugsRegion);
            int index = rows == null ? -1 : rows.Index;
            if (index < 0 || index >= currentSettings.Count)
            {
                return default(Rect);
            }
            return DrugPolicyRowDrawPatch.CellRect(CellFor(currentSettings[index].Type));
        }

        private static DrugRowCell CellFor(SettingType type)
        {
            switch (type)
            {
                case SettingType.AllowForAddiction:
                    return DrugRowCell.AllowForAddiction;
                case SettingType.AllowForJoy:
                    return DrugRowCell.AllowForJoy;
                case SettingType.AllowScheduled:
                    return DrugRowCell.AllowScheduled;
                case SettingType.Frequency:
                    return DrugRowCell.Frequency;
                case SettingType.MoodThreshold:
                    return DrugRowCell.MoodThreshold;
                case SettingType.JoyThreshold:
                    return DrugRowCell.JoyThreshold;
                default:
                    return DrugRowCell.TakeToInventory;
            }
        }

        // ------------------------------------------------------------------
        // Contents contract.
        // ------------------------------------------------------------------

        protected override int ContentsRegionCount
        {
            get { return 1; }
        }

        /// <summary>
        /// The region name carries the LEVEL: vanilla's own dialog title over the drug list, the
        /// drug's own label while its settings are open. No new key either way.
        /// </summary>
        protected override string ContentsRegionName(int region)
        {
            if (mode == Mode.DrugSettings)
            {
                DrugPolicyEntry entry = CurrentEntry();
                if (entry != null && entry.drug != null)
                {
                    return entry.drug.LabelCap.ToString();
                }
            }
            return "DrugPolicyTitle".Translate().ToString();
        }

        protected override string TreeRegionLabel
        {
            get { return ContentsRegionName(DrugsRegion); }
        }

        protected override void RebuildContents()
        {
            policy = Selected as DrugPolicy;
            mode = Mode.DrugList;
            savedDrugIndex = -1;
            currentSettings.Clear();
        }

        // ------------------------------------------------------------------
        // Typeahead: the drug list only, letters only.
        // ------------------------------------------------------------------

        protected override bool ContentRegionSearchable(int region)
        {
            return region == PoliciesRegion || (region == DrugsRegion && mode == Mode.DrugList);
        }

        protected override bool TypeaheadAcceptsDigits
        {
            get { return false; }
        }

        // ------------------------------------------------------------------
        // Rows.
        // ------------------------------------------------------------------

        protected override int ContentItemCount(int region)
        {
            if (region != DrugsRegion)
            {
                return base.ContentItemCount(region);
            }
            return mode == Mode.DrugList
                ? (policy == null ? 0 : policy.Count)
                : currentSettings.Count;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            if (region != DrugsRegion)
            {
                return base.DescribeContentItem(region, index);
            }

            var d = new ElementDescription();
            if (mode == Mode.DrugList)
            {
                if (policy != null && index >= 0 && index < policy.Count)
                {
                    ThingDef drug = policy[index].drug;
                    d.Label = drug != null ? drug.LabelCap.ToString() : "";
                }
                return d;
            }

            DrugPolicyEntry entry = CurrentEntry();
            if (entry == null || index < 0 || index >= currentSettings.Count)
            {
                return d;
            }

            DrugSetting setting = currentSettings[index];
            d.Label = setting.Label;
            d.Extras = setting.Tooltip;
            DescribeSetting(d, entry, setting.Type);
            return d;
        }

        /// <summary>Gives a settings row the role and live state of the vanilla widget DoEntryRow draws for it — see the class remarks for the map.</summary>
        private static void DescribeSetting(ElementDescription d, DrugPolicyEntry entry, SettingType type)
        {
            switch (type)
            {
                case SettingType.TakeToInventory:
                    d.Role = ElementRole.Stepper;
                    d.Value = entry.takeToInventory.ToString();
                    d.AtMinimum = entry.takeToInventory <= 0;
                    d.AtMaximum = entry.takeToInventory >= PawnUtility.GetMaxAllowedToPickUp(entry.drug);
                    break;

                case SettingType.AllowForAddiction:
                    SetCheckbox(d, entry.allowedForAddiction);
                    break;

                case SettingType.AllowForJoy:
                    SetCheckbox(d, entry.allowedForJoy);
                    break;

                case SettingType.AllowScheduled:
                    SetCheckbox(d, entry.allowScheduled);
                    break;

                case SettingType.Frequency:
                    d.Role = ElementRole.Slider;
                    d.Value = FormatFrequency(entry.daysFrequency);
                    d.AtMinimum = entry.daysFrequency <= MinFrequency;
                    d.AtMaximum = entry.daysFrequency >= MaxFrequency;
                    break;

                case SettingType.MoodThreshold:
                    SetThresholdSlider(d, entry.onlyIfMoodBelow);
                    break;

                case SettingType.JoyThreshold:
                    SetThresholdSlider(d, entry.onlyIfJoyBelow);
                    break;
            }
        }

        private static void SetCheckbox(ElementDescription d, bool on)
        {
            d.Role = ElementRole.Checkbox;
            d.Check = on ? CheckState.Checked : CheckState.Unchecked;
        }

        private static void SetThresholdSlider(ElementDescription d, float value)
        {
            d.Role = ElementRole.Slider;
            d.Value = FormatThreshold(value);
            d.AtMinimum = value <= MinThreshold;
            d.AtMaximum = value >= MaxThreshold;
        }

        // ------------------------------------------------------------------
        // Table contract for the drug list: vanilla's own eight columns, in vanilla's own order, each
        // with the tooltip vanilla attaches to it in DoColumnLabels.
        // ------------------------------------------------------------------

        private enum DrugColumn
        {
            Name,
            TakeToInventory,
            ForAddiction,
            ForJoy,
            Scheduled,
            Frequency,
            MoodThreshold,
            JoyThreshold,
        }

        protected override int ContentColumnCount(int region)
        {
            if (region != DrugsRegion)
            {
                return base.ContentColumnCount(region);
            }
            return mode == Mode.DrugList ? 8 : 1;
        }

        protected override TableColumnInfo ContentColumnInfo(int region, int column)
        {
            if (region != DrugsRegion)
            {
                return base.ContentColumnInfo(region, column);
            }
            string label;
            string tip;
            switch ((DrugColumn)column)
            {
                case DrugColumn.TakeToInventory:
                    label = "TakeToInventoryColumnLabel".Translate();
                    tip = "TakeToInventoryColumnDesc".Translate();
                    break;
                case DrugColumn.ForAddiction:
                    SplitUsageTip("DrugUsageTipForAddiction", out label, out tip);
                    break;
                case DrugColumn.ForJoy:
                    SplitUsageTip("DrugUsageTipForJoy", out label, out tip);
                    break;
                case DrugColumn.Scheduled:
                    SplitUsageTip("DrugUsageTipScheduled", out label, out tip);
                    break;
                case DrugColumn.Frequency:
                    label = "FrequencyColumnLabel".Translate();
                    tip = "FrequencyColumnDesc".Translate();
                    break;
                case DrugColumn.MoodThreshold:
                    label = "MoodThresholdColumnLabel".Translate();
                    tip = "MoodThresholdColumnDesc".Translate();
                    break;
                case DrugColumn.JoyThreshold:
                    label = "JoyThresholdColumnLabel".Translate();
                    tip = "JoyThresholdColumnDesc".Translate();
                    break;
                default:
                    label = "DrugColumnLabel".Translate();
                    tip = "DrugNameColumnDesc".Translate();
                    break;
            }
            // Vanilla offers no sorting on this grid.
            return new TableColumnInfo(label, tip, false);
        }

        /// <summary>
        /// Cell text mirrors what vanilla DRAWS: a column vanilla leaves blank for this row reads as
        /// empty rather than as a default value.
        /// </summary>
        protected override string ContentCellText(int region, int row, int column)
        {
            if (region != DrugsRegion)
            {
                return base.ContentCellText(region, row, column);
            }
            if (policy == null || row < 0 || row >= policy.Count)
            {
                return "";
            }
            DrugPolicyEntry entry = policy[row];
            switch ((DrugColumn)column)
            {
                case DrugColumn.Name:
                    return entry.drug != null ? entry.drug.LabelCap.ToString() : "";
                case DrugColumn.TakeToInventory:
                    return entry.takeToInventory.ToString();
                case DrugColumn.ForAddiction:
                    return entry.drug.IsAddictiveDrug ? FormatCheck(entry.allowedForAddiction) : "";
                case DrugColumn.ForJoy:
                    return entry.drug.IsPleasureDrug ? FormatCheck(entry.allowedForJoy) : "";
                case DrugColumn.Scheduled:
                    return FormatCheck(entry.allowScheduled);
                case DrugColumn.Frequency:
                    return entry.allowScheduled ? FormatFrequency(entry.daysFrequency) : "";
                case DrugColumn.MoodThreshold:
                    return entry.allowScheduled ? FormatThreshold(entry.onlyIfMoodBelow) : "";
                case DrugColumn.JoyThreshold:
                    return entry.allowScheduled ? FormatThreshold(entry.onlyIfJoyBelow) : "";
                default:
                    return "";
            }
        }

        protected override void ActivateContentItem(int region, int index)
        {
            if (region != DrugsRegion)
            {
                base.ActivateContentItem(region, index);
                return;
            }
            if (mode == Mode.DrugList)
            {
                EnterDrugSettings();
            }
            else
            {
                ToggleSetting();
            }
        }

        /// <summary>The drug currently being edited, valid in either mode — see the class remarks.</summary>
        private int CurrentDrugIndex
        {
            get
            {
                if (mode == Mode.DrugSettings)
                {
                    return savedDrugIndex;
                }
                ListModel drugs = Model.Region(DrugsRegion);
                return drugs != null ? drugs.Index : -1;
            }
        }

        /// <summary>The live entry <see cref="CurrentDrugIndex"/> points at, or null.</summary>
        private DrugPolicyEntry CurrentEntry()
        {
            int idx = CurrentDrugIndex;
            if (policy == null || idx < 0 || idx >= policy.Count)
            {
                return null;
            }
            return policy[idx];
        }

        // ------------------------------------------------------------------
        // Mode transitions. The region's NAME carries the level, so AnnounceRegion is what makes
        // entering and leaving the settings sound like a transition rather than a cursor move.
        // ------------------------------------------------------------------

        private void EnterDrugSettings()
        {
            RefreshModel();
            ListModel drugs = Model.Region(DrugsRegion);
            int drugIdx = drugs == null ? -1 : drugs.Index;
            if (policy == null || drugIdx < 0 || drugIdx >= policy.Count)
            {
                return;
            }

            BuildSettingsForCurrentDrug(drugIdx);
            if (currentSettings.Count == 0)
            {
                return;
            }

            savedDrugIndex = drugIdx;
            mode = Mode.DrugSettings;
            RefreshModel();
            Model.Region(DrugsRegion)?.MoveTo(0);
            AnnounceRegion();
        }

        private void ReturnToDrugList()
        {
            mode = Mode.DrugList;
            currentSettings.Clear();
            RefreshModel();
            if (policy != null && savedDrugIndex >= 0 && savedDrugIndex < policy.Count)
            {
                Model.Region(DrugsRegion)?.MoveTo(savedDrugIndex);
            }
            AnnounceRegion();
        }

        // ------------------------------------------------------------------
        // Settings mutation.
        // ------------------------------------------------------------------

        private void ToggleSetting()
        {
            int drugIdx = CurrentDrugIndex;
            if (policy == null || drugIdx < 0 || drugIdx >= policy.Count)
            {
                return;
            }
            RefreshModel();
            ListModel drugs = Model.Region(DrugsRegion);
            int settingIdx = drugs == null ? -1 : drugs.Index;
            if (currentSettings.Count == 0 || settingIdx < 0 || settingIdx >= currentSettings.Count)
            {
                return;
            }

            DrugPolicyEntry entry = policy[drugIdx];
            DrugSetting setting = currentSettings[settingIdx];

            // MUTATION-C: mirrors Dialog_ManageDrugPolicies.DoEntryRow's
            // Widgets.Checkbox(ref entry.allowedForAddiction/allowedForJoy/
            // allowScheduled, ...) calls (Dialog_ManageDrugPolicies.cs:192,197,200);
            // vanilla toggles these bools directly with no gated setter.
            switch (setting.Type)
            {
                case SettingType.AllowForAddiction:
                    entry.allowedForAddiction = !entry.allowedForAddiction;
                    break;
                case SettingType.AllowForJoy:
                    entry.allowedForJoy = !entry.allowedForJoy;
                    break;
                case SettingType.AllowScheduled:
                    entry.allowScheduled = !entry.allowScheduled;
                    // Frequency/threshold visibility depends on this; RefreshModel's SetCount clamps
                    // the region cursor if the list shrank.
                    BuildSettingsForCurrentDrug(drugIdx);
                    RefreshModel();
                    break;
                default:
                    return;
            }

            AnnounceCurrentItem();
        }

        /// <summary>
        /// Steps daysFrequency through the discrete values vanilla's FrequencyHorizontalSlider
        /// produces: whole "every N days" values 1..25 at or above 1, and whole "N times per day"
        /// values 2..10 below it. Direction +1 moves toward less frequent, -1 toward more frequent,
        /// bottoming out at vanilla's 0.1 minFreq floor.
        /// </summary>
        // MUTATION-C: hand-copied discrete stepping over the same range
        // vanilla's Widgets.FrequencyHorizontalSlider(minFreq: 0.1f,
        // maxFreq: 25f, roundToInt: true) covers by continuous drag
        // (Dialog_ManageDrugPolicies.cs:204); no discrete-step vehicle exists
        // in vanilla for keyboard-driven adjustment, so the bounds (0.1..25)
        // are harvested from the slider call and the step logic is
        // hand-written to match its labeled value set (see doc comment above).
        private static float AdjustDrugFrequency(float freq, int direction)
        {
            const float MinFreq = 0.1f;
            const float MaxFreq = 25f;

            if (freq >= 1f)
            {
                float newDays = Mathf.Round(freq) + direction;
                if (newDays < 1f)
                {
                    return 0.5f; // cross into "2 times a day"
                }
                return Mathf.Clamp(newDays, 1f, MaxFreq);
            }

            int timesPerDay = Mathf.RoundToInt(1f / freq);
            int newTimesPerDay = timesPerDay - direction;
            if (newTimesPerDay < 2)
            {
                return 1f; // cross into "every day"
            }
            int maxTimesPerDay = Mathf.RoundToInt(1f / MinFreq);
            newTimesPerDay = Mathf.Clamp(newTimesPerDay, 2, maxTimesPerDay);
            return 1f / newTimesPerDay;
        }

        private void AdjustSetting(int direction)
        {
            int drugIdx = CurrentDrugIndex;
            if (policy == null || drugIdx < 0 || drugIdx >= policy.Count)
            {
                return;
            }
            RefreshModel();
            ListModel drugs = Model.Region(DrugsRegion);
            int settingIdx = drugs == null ? -1 : drugs.Index;
            if (currentSettings.Count == 0 || settingIdx < 0 || settingIdx >= currentSettings.Count)
            {
                return;
            }

            DrugPolicyEntry entry = policy[drugIdx];
            DrugSetting setting = currentSettings[settingIdx];

            // MUTATION-C: MoodThreshold/JoyThreshold hand-copy discrete
            // stepping over the bounds of Dialog_ManageDrugPolicies.DoEntryRow's
            // Widgets.HorizontalSlider(..., min: 0.01f, max: 1f, ...) calls
            // (Dialog_ManageDrugPolicies.cs:206,208) — continuous drag has no
            // discrete-step vehicle, so the 0.01/1f bounds are harvested from
            // the slider call and the 0.05f step is our own keyboard
            // granularity. TakeToInventory mirrors the same file's
            // Widgets.TextFieldNumeric(ref entry.takeToInventory, ...,
            // 0f, PawnUtility.GetMaxAllowedToPickUp(entry.drug)) bounds
            // (Dialog_ManageDrugPolicies.cs:188) exactly.
            switch (setting.Type)
            {
                case SettingType.Frequency:
                    entry.daysFrequency = AdjustDrugFrequency(entry.daysFrequency, direction);
                    break;

                case SettingType.MoodThreshold:
                    entry.onlyIfMoodBelow += direction * 0.05f;
                    entry.onlyIfMoodBelow = Mathf.Clamp(entry.onlyIfMoodBelow, 0.01f, 1f);
                    break;

                case SettingType.JoyThreshold:
                    entry.onlyIfJoyBelow += direction * 0.05f;
                    entry.onlyIfJoyBelow = Mathf.Clamp(entry.onlyIfJoyBelow, 0.01f, 1f);
                    break;

                case SettingType.TakeToInventory:
                    int maxPickup = PawnUtility.GetMaxAllowedToPickUp(entry.drug);
                    entry.takeToInventory = Mathf.Clamp(entry.takeToInventory + direction, 0, maxPickup);
                    break;

                default:
                    return;
            }

            AnnounceCurrentItem();
        }

        /// <summary>
        /// Opens the info card for the selected drug, mirroring vanilla's per-row InfoCardButton.
        /// Available in both modes, since vanilla's button lives on the row itself.
        /// </summary>
        private void OpenInfoCard()
        {
            int drugIdx = CurrentDrugIndex;
            if (policy == null || drugIdx < 0 || drugIdx >= policy.Count)
            {
                return;
            }
            InfoCardState.TryOpenInfoCardForDef(policy[drugIdx].drug);
        }

        // ------------------------------------------------------------------
        // Settings list builder.
        // ------------------------------------------------------------------

        /// <summary>One of vanilla's three icon-only usage checkboxes, named from its own tip.</summary>
        private static DrugSetting UsageCheckbox(SettingType type, string tipKey)
        {
            string label;
            string description;
            SplitUsageTip(tipKey, out label, out description);
            return new DrugSetting { Type = type, Label = label, Tooltip = description };
        }

        private void BuildSettingsForCurrentDrug(int drugIdx)
        {
            currentSettings.Clear();
            if (policy == null || drugIdx < 0 || drugIdx >= policy.Count)
            {
                return;
            }
            DrugPolicyEntry entry = policy[drugIdx];

            currentSettings.Add(new DrugSetting
            {
                Type = SettingType.TakeToInventory,
                Label = "TakeToInventoryColumnLabel".Translate(),
                Tooltip = "TakeToInventoryColumnDesc".Translate()
            });

            // Addiction and joy checkboxes appear only where vanilla's DoEntryRow draws them.
            if (entry.drug.IsAddictiveDrug)
            {
                currentSettings.Add(UsageCheckbox(SettingType.AllowForAddiction, "DrugUsageTipForAddiction"));
            }

            if (entry.drug.IsPleasureDrug)
            {
                currentSettings.Add(UsageCheckbox(SettingType.AllowForJoy, "DrugUsageTipForJoy"));
            }

            currentSettings.Add(UsageCheckbox(SettingType.AllowScheduled, "DrugUsageTipScheduled"));

            // Frequency and the two thresholds show only while scheduled is on, as in DoEntryRow.
            if (entry.allowScheduled)
            {
                currentSettings.Add(new DrugSetting
                {
                    Type = SettingType.Frequency,
                    Label = "FrequencyColumnLabel".Translate(),
                    Tooltip = "FrequencyColumnDesc".Translate()
                });

                currentSettings.Add(new DrugSetting
                {
                    Type = SettingType.MoodThreshold,
                    Label = "MoodThresholdColumnLabel".Translate(),
                    Tooltip = "MoodThresholdColumnDesc".Translate()
                });

                currentSettings.Add(new DrugSetting
                {
                    Type = SettingType.JoyThreshold,
                    Label = "JoyThresholdColumnLabel".Translate(),
                    Tooltip = "JoyThresholdColumnDesc".Translate()
                });
            }
        }

        // ------------------------------------------------------------------
        // Value formatting. Every one of these is spoken by the shared composer,
        // either as a settings row's Value or as a table cell.
        // ------------------------------------------------------------------

        /// <summary>Vanilla's Widgets.FrequencyHorizontalSlider label formatting.</summary>
        private static string FormatFrequency(float freq)
        {
            if (freq == 1f)
            {
                return "EveryDay".Translate();
            }
            if (freq < 1f)
            {
                return "TimesPerDay".Translate((1f / freq).ToString("0.##"));
            }
            return "EveryDays".Translate(freq.ToString("0.##"));
        }

        /// <summary>The label vanilla passes to the two threshold sliders: its "no requirement" string at the top of the range, a percentage below it.</summary>
        private static string FormatThreshold(float value)
        {
            if (value >= MaxThreshold)
            {
                return "NoDrugUseRequirement".Translate();
            }
            return value.ToStringPercent();
        }

        /// <summary>Checkbox state for a TABLE CELL, where no role word carries it.</summary>
        private static string FormatCheck(bool on)
        {
            return TranslatedShellVocabulary.Instance.Word(
                on ? ElementStateWord.Checked : ElementStateWord.Unchecked);
        }

        /// <summary>
        /// Vanilla stores the three usage checkbox tips as "title\n\nbody" and draws only an icon,
        /// so the first line is the only name the game has for these columns. Splits on the game's
        /// own separator; matches no translated text.
        /// </summary>
        private static void SplitUsageTip(string key, out string label, out string description)
        {
            string full = key.Translate().Resolve();
            int idx = full.IndexOf('\n');
            if (idx < 0)
            {
                label = full;
                description = null;
                return;
            }
            label = full.Substring(0, idx);
            description = full.Substring(idx + 1).TrimStart('\n', ' ');
        }
    }
}
