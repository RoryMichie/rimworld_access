using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The keyboard focus scope for the entity inspect tab (ITab_Entity, Anomaly): a
    /// screen-reader representation of the vanilla tab for a held entity on a holding platform.
    /// The mod owns no window — the surface is the windowless
    /// <see cref="RimWorldAccess.EntityTabState"/> facade — so the scope rides the focus stack
    /// through <see cref="EntityTabScopeMirror"/>.
    ///
    /// ONE flat content region:
    /// <list type="number">
    /// <item>Four read-only stat rows, Label plus Extras carrying the full vanilla tooltip text,
    /// newline-sanitized by <see cref="SanitizeTooltip"/>.</item>
    /// <item>Allow medicine, an ElementRole.ComboBox: Enter/Space opens vanilla's own dropdown
    /// (MedicalCareUtility's MedicalCareSelectButton_GenerateMenu, ITab_Entity.cs:117) through
    /// <see cref="DropdownColumnHandler.OpenGeneratedMenu"/>. Left/Right do NOT change the value:
    /// a combo box is a dropdown, not a slider.</item>
    /// <item>Containment mode as FOUR RadioButton rows, mirroring vanilla's four
    /// Widgets.RadioButtonLabeled calls (ITab_Entity.cs:128-157) rather than one cycling row — a
    /// sighted player sees four distinct targets. Each row selects its own value directly; the
    /// Execute row stays navigable but disabled when <c>!Props.canBeExecuted</c>, announcing the
    /// reason on focus.</item>
    /// <item>Extract bioferrite, a Checkbox keeping both disable reasons.</item>
    /// </list>
    ///
    /// Left/Right ride the shared CanAdjustContentItem/AdjustContentItem path through the base's
    /// menus.nextHorizontal/previousHorizontal claims; subclasses must not hand-roll selection or
    /// tab arithmetic. Typeahead rides the shared scope engine
    /// (<see cref="EnableTypeahead"/>). Escape is <see cref="Close"/> plus
    /// <see cref="InspectionReturnHelper.AnnounceParentOrFallback"/>.
    ///
    /// <b>Focus ring.</b> The tab draws inside the inspect pane's ImmediateWindow, so there is no
    /// window to register a listing-ring client against; <see cref="EntityTabRingPatch"/> brackets
    /// vanilla's FillTab and reads the focused row from <see cref="Current"/>.
    /// </summary>
    public sealed class EntityTabScope : ScreenScope
    {
        private enum RowKind
        {
            ContainmentStrength,    // read-only stat
            EscapeMtb,              // read-only stat
            StudyInterval,          // read-only (only if CompStudiable.Props.frequencyTicks > 0)
            KnowledgeGain,          // read-only
            AllowMedicine,          // interactive: ComboBox, Enter opens vanilla's MedicalCareCategory dropdown
            ContainmentModeRadio,   // interactive: one of four RadioButton rows
            ExtractBioferrite,      // interactive: Checkbox
        }

        private sealed class Row
        {
            public RowKind Kind;
            public bool Interactive;
            public bool DisabledForInteraction;
            public string DisabledReason;
            public EntityContainmentMode RadioValue; // ContainmentModeRadio only
        }

        /// <summary>Vanilla's own menu generator for the allow-medicine dropdown — see <see cref="OpenMedicalCarePicker"/>.</summary>
        private static readonly MethodInfo medicalCareMenu =
            AccessTools.Method(typeof(MedicalCareUtility), "MedicalCareSelectButton_GenerateMenu");

        private readonly List<Row> rows = new List<Row>();
        private Pawn heldPawn;
        private Thing platformThing;
        private bool announcedOpen;

        public EntityTabScope()
        {
            Claim(SharedMenuGrammar.Cancel, delegate { HandleCancelKey(); });

            Claim("entityTab.infoCard", delegate { ShowInfoCard(); });
            Claim("entityTab.healthInfo", delegate { if (heldPawn != null) TolkHelper.SpeakData(PawnInfoHelper.GetHealthInfo(heldPawn)); });
            Claim("entityTab.moodInfo", delegate { if (heldPawn != null) TolkHelper.SpeakData(PawnInfoHelper.GetMoodInfo(heldPawn)); });
            Claim("entityTab.needsInfo", delegate { if (heldPawn != null) TolkHelper.SpeakData(PawnInfoHelper.GetNeedsInfo(heldPawn)); });
            Claim("entityTab.gearInfo", delegate { if (heldPawn != null) TolkHelper.SpeakData(PawnInfoHelper.GetGearInfo(heldPawn)); });
        }

        public override string Name
        {
            get { return "entity-tab"; }
        }

        /// <summary>No owning <c>Window</c> exists for this windowless tab — self-claim Escape.</summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        /// <summary>Shared cross-region typeahead.</summary>
        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        // ScreenScope content contract.

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        /// <summary>Reuses vanilla's own Entity tab label — no new translation key.</summary>
        protected override string ContentRegionName(int region)
        {
            return (string)"TabEntity".Translate();
        }

        protected override int ContentItemCount(int region)
        {
            return rows.Count;
        }

        protected override void RefreshContent()
        {
            if (rows.Count > 0)
                return;
            BuildRows();
        }

        private void BuildRows()
        {
            rows.Clear();
            if (heldPawn == null)
                return;

            var compStudiable = heldPawn.TryGetComp<CompStudiable>();
            var compHpTarget = heldPawn.TryGetComp<CompHoldingPlatformTarget>();

            rows.Add(new Row { Kind = RowKind.ContainmentStrength });
            rows.Add(new Row { Kind = RowKind.EscapeMtb });

            if (compStudiable?.Props != null && compStudiable.Props.frequencyTicks > 0)
                rows.Add(new Row { Kind = RowKind.StudyInterval });
            if (compStudiable != null)
                rows.Add(new Row { Kind = RowKind.KnowledgeGain });

            rows.Add(new Row { Kind = RowKind.AllowMedicine, Interactive = true });

            if (compHpTarget != null)
            {
                // One row per EntityContainmentMode value, in vanilla's own draw order:
                // ITab_Entity.cs:128-157 draws four independent radio buttons.
                foreach (EntityContainmentMode mode in (EntityContainmentMode[])Enum.GetValues(typeof(EntityContainmentMode)))
                {
                    bool disabled = mode == EntityContainmentMode.Execute && !compHpTarget.Props.canBeExecuted;
                    rows.Add(new Row
                    {
                        Kind = RowKind.ContainmentModeRadio,
                        Interactive = true,
                        RadioValue = mode,
                        DisabledForInteraction = disabled,
                        DisabledReason = disabled ? (string)"CantBeExecuted".Translate() : null,
                    });
                }

                // Bioferrite extraction has two disable conditions (mirrors ITab_Entity.FillTab).
                string disableReason = null;
                if (!ResearchProjectDefOf.BioferriteExtraction.IsFinished)
                    disableReason = "RequiresBioferriteExtraction".Translate();
                else
                {
                    var heldPlatform = compHpTarget.HeldPlatform;
                    if (heldPlatform != null && heldPlatform.HasAttachedBioferriteHarvester)
                        disableReason = "BioferriteHarvesterAttached".Translate();
                }

                rows.Add(new Row
                {
                    Kind = RowKind.ExtractBioferrite,
                    Interactive = true,
                    DisabledForInteraction = disableReason != null,
                    DisabledReason = disableReason,
                });
            }
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            if (index < 0 || index >= rows.Count)
                return new ElementDescription();
            Row row = rows[index];
            switch (row.Kind)
            {
                case RowKind.ContainmentStrength: return DescribeContainmentStrength();
                case RowKind.EscapeMtb: return DescribeEscapeMtb();
                case RowKind.StudyInterval: return DescribeStudyInterval();
                case RowKind.KnowledgeGain: return DescribeKnowledgeGain();
                case RowKind.AllowMedicine: return DescribeAllowMedicine();
                case RowKind.ContainmentModeRadio: return DescribeContainmentModeRadio(row);
                case RowKind.ExtractBioferrite: return DescribeExtractBioferrite(row);
                default: return new ElementDescription();
            }
        }

        protected override void ActivateContentItem(int region, int index)
        {
            if (index < 0 || index >= rows.Count)
                return;
            Row row = rows[index];
            // Selecting an item ends the search.
            TypeaheadReset();
            if (!row.Interactive)
            {
                AnnounceCurrentItem();
                return;
            }
            switch (row.Kind)
            {
                case RowKind.AllowMedicine: OpenMedicalCarePicker(); break;
                case RowKind.ContainmentModeRadio: SelectContainmentMode(row); break;
                case RowKind.ExtractBioferrite: ToggleExtractBioferrite(row); break;
            }
        }

        // No row here has a direction-sensitive adjust, so CanAdjustContentItem stays at the base
        // default: the radios and the checkbox have no arrow-adjust in vanilla either, and the
        // medicine row is a combo box whose dropdown opens on Enter/Space.

        // Lifecycle.

        /// <summary>
        /// The pushed instance, for <see cref="EntityTabRingPatch"/>: this tab owns no window and
        /// its facade state holds no cursor, so the ring bracket reaches the focused row here.
        /// </summary>
        internal static EntityTabScope Current { get; private set; }

        public override void OnPush()
        {
            base.OnPush();
            Current = this;
            heldPawn = RimWorldAccess.EntityTabState.HeldPawn;
            platformThing = RimWorldAccess.EntityTabState.PlatformThing;
            rows.Clear();
            TypeaheadReset();
            announcedOpen = false;
        }

        public override void OnPop()
        {
            base.OnPop();
            if (ReferenceEquals(Current, this))
            {
                Current = null;
            }
        }

        /// <summary>
        /// Which row of vanilla's tab the focused row corresponds to, for
        /// <see cref="EntityTabRingPatch"/>. All four containment radios ring the ONE band vanilla
        /// allocates them — it splits that band with raw Widgets.RadioButtonLabeled calls, whose
        /// rects never reach a listing — and the other rows ring their whole band too.
        /// </summary>
        internal ListingRingFocus CurrentListingFocus()
        {
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty || Model.RegionIndex != 0)
            {
                return ListingRingFocus.None;
            }
            int index = region.Index;
            if (index < 0 || index >= rows.Count)
            {
                return ListingRingFocus.None;
            }
            switch (rows[index].Kind)
            {
                case RowKind.ContainmentStrength:
                    return Ring(EntityTabRowKeys.ContainmentStrength, VanillaContainmentStrengthLabel());
                case RowKind.EscapeMtb:
                    return Ring(EntityTabRowKeys.EscapeMtb, VanillaEscapeMtbLabel());
                // The only harvestable key here and not "#"-shaped, so it matches bare.
                case RowKind.StudyInterval:
                    return new ListingRingFocus { RowKey = EntityTabRowKeys.StudyInterval };
                case RowKind.KnowledgeGain:
                    return Ring(EntityTabRowKeys.KnowledgeGain, VanillaKnowledgeGainLabel());
                case RowKind.AllowMedicine:
                    return Ring(EntityTabRowKeys.Medicine, EntityTabRowKeys.RawBand);
                case RowKind.ContainmentModeRadio:
                    return Ring(EntityTabRowKeys.Containment, EntityTabRowKeys.RawBand);
                case RowKind.ExtractBioferrite:
                    return Ring(EntityTabRowKeys.Extract, EntityTabRowKeys.RawBand);
                default:
                    return ListingRingFocus.None;
            }
        }

        private static ListingRingFocus Ring(string rowKey, string tripwire)
        {
            return tripwire == null
                ? ListingRingFocus.None
                : new ListingRingFocus { RowKey = rowKey, LabelTripwire = tripwire };
        }

        /// <summary>Mirrors the caption vanilla builds at ITab_Entity.cs:76.</summary>
        private string VanillaContainmentStrengthLabel()
        {
            var comp = platformThing?.TryGetComp<CompEntityHolder>();
            if (comp == null) return null;
            StatDef stat = StatDefOf.ContainmentStrength;
            return (string)(stat.LabelCap + ": " + stat.ValueToString(comp.ContainmentStrength));
        }

        /// <summary>Mirrors the caption vanilla builds at ITab_Entity.cs:80-92.</summary>
        private string VanillaEscapeMtbLabel()
        {
            if (heldPawn == null) return null;
            float days = ContainmentUtility.InitiateEscapeMtbDays(heldPawn, new StringBuilder());
            TaggedString label = "HoldingPlatformEscapeMTBDays".Translate() + ": ";
            if (days < 0f)
                label += "Never".Translate();
            else
                label += Mathf.FloorToInt(days * 60000f).ToStringTicksToPeriod().Colorize(ColoredText.DateTimeColor);
            return label;
        }

        /// <summary>Mirrors the caption vanilla builds at ITab_Entity.cs:233.</summary>
        private string VanillaKnowledgeGainLabel()
        {
            var comp = heldPawn?.TryGetComp<CompStudiable>();
            if (comp?.KnowledgeCategory == null) return null;
            return (string)("StudyKnowledgeGain".Translate() + ": "
                + (comp.AdjustedAnomalyKnowledgePerStudy * 5f).ToStringDecimalIfSmall()
                + " (" + comp.KnowledgeCategory.label + ")");
        }

        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen)
                return;
            announcedOpen = true;
            // Two-call open announcement: tab identity, then the first row. Bills and Storage
            // speak only the first row.
            if (heldPawn != null)
            {
                TolkHelper.SpeakData($"{(string)"TabEntity".Translate()}. {heldPawn.LabelShortCap}");
            }
            AnnounceCurrentItem();
        }

        // Escape / close.

        private void HandleCancelKey()
        {
            Close();
            InspectionReturnHelper.AnnounceParentOrFallback("RimWorldAccess.Inspection.Patch.ClosedEntityTab".Translate());
        }

        private void Close()
        {
            RimWorldAccess.EntityTabState.Close();
        }

        // Row activations. Alt+I opens a real Dialog_InfoCard directly: InfoCardState exposes only
        // def-keyed entry points, so no shared wrapper covers a live pawn card.

        private void ShowInfoCard()
        {
            if (heldPawn == null) return;
            Find.WindowStack.Add(new Dialog_InfoCard(heldPawn));
        }

        /// <summary>
        /// Opens vanilla's own dropdown for the allow-medicine control: a Widgets.Dropdown
        /// (ITab_Entity.cs:117) whose menu comes from the private
        /// MedicalCareSelectButton_GenerateMenu, so the options are vanilla's real
        /// FloatMenuOptions, click delegates and "change defaults" entry included, through the
        /// same reflection vehicle <see cref="MedicalCareColumnHandler"/> rides.
        /// </summary>
        private void OpenMedicalCarePicker()
        {
            if (heldPawn?.playerSettings == null) return;
            if (!DropdownColumnHandler.OpenGeneratedMenu(medicalCareMenu, null, heldPawn))
            {
                AnnounceCurrentItem();
            }
        }

        private void SelectContainmentMode(Row row)
        {
            var comp = heldPawn?.TryGetComp<CompHoldingPlatformTarget>();
            if (comp == null) return;
            if (row.DisabledForInteraction)
            {
                TolkHelper.SpeakData(row.DisabledReason);
                return;
            }
            // MUTATION-C: mirrors ITab_Entity.FillTab's radio rows (decompiled
            // RimWorld/ITab_Entity.cs:128-157) incl. the canBeExecuted disable;
            // Widgets.RadioButtonLabeled(disabled: true) refuses the set and
            // messages CantBeExecuted (line 149-158) rather than setting the
            // value — no gated setter exists on containmentMode itself.
            comp.containmentMode = row.RadioValue;
            var d = new ElementDescription { Selected = true };
            TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(d, TranslatedShellVocabulary.Instance));
        }

        private void ToggleExtractBioferrite(Row row)
        {
            var comp = heldPawn?.TryGetComp<CompHoldingPlatformTarget>();
            if (comp == null) return;
            if (row.DisabledForInteraction)
            {
                TolkHelper.SpeakData(row.DisabledReason);
                return;
            }
            // MUTATION-C: mirrors ITab_Entity.FillTab's Widgets.CheckboxLabeled
            // call (decompiled RimWorld/ITab_Entity.cs:171,178,184) incl. the
            // research-finished/harvester-attached disable; no gated setter
            // exists on extractBioferrite itself.
            comp.extractBioferrite = !comp.extractBioferrite;
            var d = new ElementDescription { Check = comp.extractBioferrite ? CheckState.Checked : CheckState.Unchecked };
            TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(d, TranslatedShellVocabulary.Instance));
        }

        // Row descriptions, split into Label + Extras for the shared composer.

        private ElementDescription DescribeContainmentStrength()
        {
            var stat = StatDefOf.ContainmentStrength;
            var comp = platformThing?.TryGetComp<CompEntityHolder>();
            float value = comp?.ContainmentStrength ?? 0f;
            // Vanilla's tooltip is stat.description + GetExplanationFull, the full contribution
            // breakdown; surface both so nothing a mouseover shows is lost.
            string description = stat.description ?? "";
            string explanation = "";
            if (platformThing != null)
            {
                try
                {
                    explanation = stat.Worker.GetExplanationFull(
                        StatRequest.For(platformThing), stat.toStringNumberSense, value);
                }
                catch (Exception ex)
                {
                    Log.Warning($"[EntityTabScope] ContainmentStrength explanation failed: {ex.Message}");
                }
            }
            return new ElementDescription
            {
                Label = $"{stat.LabelCap}: {stat.ValueToString(value)}",
                Extras = CombineTooltip(description, explanation),
            };
        }

        private ElementDescription DescribeEscapeMtb()
        {
            var sb = new StringBuilder();
            float days = ContainmentUtility.InitiateEscapeMtbDays(heldPawn, sb);
            string label = "HoldingPlatformEscapeMTBDays".Translate();
            string headline = days < 0f
                ? $"{label}: {"Never".Translate()}"
                : $"{label}: {Mathf.FloorToInt(days * 60000f).ToStringTicksToPeriod()}";
            string description = "HoldingPlatformEscapeMTBDaysDesc".Translate();
            string reasons = sb.Length > 0 ? sb.ToString() : "";
            return new ElementDescription { Label = headline, Extras = CombineTooltip(description, reasons) };
        }

        private ElementDescription DescribeStudyInterval()
        {
            var comp = heldPawn.TryGetComp<CompStudiable>();
            int ticks = comp?.Props?.frequencyTicks ?? 0;
            return new ElementDescription
            {
                Label = $"{"StudyInterval".Translate()}: {ticks.ToStringTicksToPeriod()}",
                Extras = SanitizeTooltip("StudyIntervalDesc".Translate()),
            };
        }

        private ElementDescription DescribeKnowledgeGain()
        {
            var comp = heldPawn.TryGetComp<CompStudiable>();
            if (comp == null) return new ElementDescription();
            string label = "StudyKnowledgeGain".Translate();
            string amount = (comp.AdjustedAnomalyKnowledgePerStudy * 5f).ToStringDecimalIfSmall();
            string category = comp.KnowledgeCategory?.label ?? "";
            string headline = $"{label}: {amount} ({category})";

            string description = "StudyKnowledgeGainDesc".Translate();
            var factors = new StringBuilder();
            var compHpTarget = comp.Pawn?.TryGetComp<CompHoldingPlatformTarget>();
            if (compHpTarget != null && compHpTarget.CurrentlyHeldOnPlatform)
            {
                var holderComp = compHpTarget.HeldPlatform?.GetComp<CompEntityHolder>();
                float strengthMult = ContainmentUtility.GetStudyKnowledgeAmountMultiplier(comp.Pawn, holderComp);
                factors.Append($"{"FactorContainmentStrength".Translate()}: x{strengthMult:F1}");
                if (compHpTarget.HeldPlatform != null && compHpTarget.HeldPlatform.HasAttachedElectroharvester)
                {
                    factors.Append($". {"FactorElectroharvester".Translate()}: x{0.5f:F1}");
                }
            }
            if (comp.Pawn != null && comp.Pawn.TryGetComp<CompActivity>(out var activityComp))
            {
                if (factors.Length > 0) factors.Append(". ");
                factors.Append($"{"FactorActivity".Translate()}: x{activityComp.ActivityResearchFactor:F1}");
            }
            return new ElementDescription { Label = headline, Extras = CombineTooltip(description, factors.ToString()) };
        }

        private ElementDescription DescribeAllowMedicine()
        {
            string value = heldPawn?.playerSettings?.medCare.GetLabel() ?? "";
            return new ElementDescription
            {
                Label = "AllowMedicine".Translate(),
                Role = ElementRole.ComboBox,
                Value = value,
                Extras = SanitizeTooltip("MedicineQualityDescriptionEntity".Translate()),
            };
        }

        private ElementDescription DescribeContainmentModeRadio(Row row)
        {
            var comp = heldPawn?.TryGetComp<CompHoldingPlatformTarget>();
            string modeKey = "EntityStudyMode_" + row.RadioValue;
            string desc = (modeKey + "Desc").Translate();
            // Vanilla's Execute tooltip appends the CantBeExecuted reason when disabled
            // (ITab_Entity.cs:161).
            if (row.DisabledForInteraction && !string.IsNullOrEmpty(row.DisabledReason))
            {
                desc = desc + ". " + row.DisabledReason;
            }
            return new ElementDescription
            {
                Label = modeKey.Translate(),
                Role = ElementRole.RadioButton,
                Selected = comp != null && comp.containmentMode == row.RadioValue,
                Disabled = row.DisabledForInteraction,
                Extras = SanitizeTooltip(desc),
            };
        }

        private ElementDescription DescribeExtractBioferrite(Row row)
        {
            var comp = heldPawn?.TryGetComp<CompHoldingPlatformTarget>();
            bool on = comp != null && comp.extractBioferrite;
            string desc = "EntityStudyMode_ExtractDesc".Translate();
            if (row.DisabledForInteraction && !string.IsNullOrEmpty(row.DisabledReason))
            {
                desc = desc + ". " + row.DisabledReason;
            }
            return new ElementDescription
            {
                Label = "EntityStudyMode_Extract".Translate(),
                Role = ElementRole.Checkbox,
                Check = on ? CheckState.Checked : CheckState.Unchecked,
                Disabled = row.DisabledForInteraction,
                Extras = SanitizeTooltip(desc),
            };
        }

        /// <summary>
        /// Joins a headline's tooltips, stripping the embedded newlines vanilla uses as paragraph
        /// separators, which otherwise read as run-on sentences.
        /// </summary>
        private static string CombineTooltip(string tooltip, string detail)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(tooltip)) sb.Append(SanitizeTooltip(tooltip));
            if (!string.IsNullOrEmpty(detail))
            {
                if (sb.Length > 0) sb.Append(". ");
                sb.Append(SanitizeTooltip(detail));
            }
            return sb.ToString();
        }

        private static string SanitizeTooltip(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Replace("\n\n", ". ").Replace("\n", " ").Trim();
        }
    }

    /// <summary>
    /// Keeps <see cref="EntityTabScope"/> in lockstep with
    /// <see cref="RimWorldAccess.EntityTabState.IsActive"/>, reconciled every OnGUI pass.
    ///
    /// It also pops while an info card is open over the tab, so the card owns the keyboard.
    /// Popping drops the tab's AnyLiveModal contribution so the info-card handler runs
    /// unshadowed; the scope holds no state, so re-pushing on close restores the user's place.
    /// </summary>
    internal static class EntityTabScopeMirror
    {
        private static readonly EntityTabScope scope = new EntityTabScope();

        public static void Reconcile()
        {
            if (RimWorldAccess.EntityTabState.IsActive && !InfoCardState.IsActive)
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
