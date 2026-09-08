using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The keyboard screen for the prisoner/slave management tab. The surface is the windowless
    /// <see cref="RimWorldAccess.PrisonerTabState"/> facade, so the scope rides the focus stack
    /// through <see cref="PrisonerTabScopeMirror"/>.
    ///
    /// The tab's sections are content regions: information (read-only stat rows), medical care,
    /// interaction modes (prisoner or slave set), non-exclusive modes, and the ideoligion picker.
    /// Medical care browses as a PREVIEW; only Enter commits, so the cursor never changes the
    /// setting underfoot. Medical care and non-exclusive modes report zero items for a slave, so
    /// the empty-region rule keeps Tab off them with no bespoke availability test — vanilla
    /// renders the care selector in DoPrisonerTab only, slaves set it from the Health tab.
    ///
    /// THE ARMED IDEOLIGION REGION. Selecting Convert with more than one player ideoligion opens
    /// the target picker, reachable only that way and left only with Escape: non-empty yet
    /// deliberately not Tab-reachable, which ScreenScope has no concept for. The region therefore
    /// reports zero items unless <see cref="ideologyArmed"/>, so the empty-region rule excludes
    /// it for free; arming and disarming move the cursor EXPLICITLY (onto the picker, then back
    /// onto the Convert row) rather than leaning on ReconcileEmptyRegion's relocation, which goes
    /// to the nearest non-empty region and would not necessarily land on Convert.
    ///
    /// Claim ids <c>prisoner.previousSection</c>/<c>prisoner.nextSection</c> alias
    /// <see cref="ScreenScope.MoveRegion"/> and <c>prisoner.toggleCheckbox</c> acts only on the
    /// non-exclusive region, so existing rebindings keep working.
    /// </summary>
    public sealed class PrisonerTabScope : ScreenScope
    {
        private const int RegionInformation = 0;
        private const int RegionMedicalCare = 1;
        private const int RegionInteractionModes = 2;
        private const int RegionNonExclusiveModes = 3;
        private const int RegionIdeoligion = 4;

        /// <summary>The ideoligion picker is reachable only by selecting Convert.</summary>
        private bool ideologyArmed;

        private bool announcedOpen;

        public PrisonerTabScope()
        {
            Claim("prisoner.previousSection", delegate { MoveRegion(false); });
            Claim("prisoner.nextSection", delegate { MoveRegion(true); });
            Claim("prisoner.toggleCheckbox", delegate { ToggleCheckboxRow(); });

            // The base's typeahead claim, registered first, clears an active search before
            // Escape reaches this claim.
            Claim(SharedMenuGrammar.Cancel, delegate { HandleCancelKey(); });
        }

        public override string Name
        {
            get { return "prisoner-tab"; }
        }

        public override bool IsLive
        {
            get { return true; }
        }

        /// <summary>No owning <c>Window</c> exists for this windowless tab — self-claim Escape.</summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        /// <summary>
        /// Excludes the armed ideoligion picker from the cross-region search: a match there
        /// could jump the cursor into an unrelated region while <see cref="ideologyArmed"/>
        /// stayed true, so Escape would later disarm and relocate from a region the user was
        /// never looking at.
        /// </summary>
        protected override bool ContentRegionSearchable(int region)
        {
            return region != RegionIdeoligion;
        }

        /// <summary>Windowless: there is no window whose buttons could be captured.</summary>
        protected override bool IncludeActionsRegion
        {
            get { return false; }
        }

        // ScreenScope content contract.

        protected override int ContentRegionCount
        {
            get { return 5; }
        }

        protected override void RefreshContent()
        {
            RimWorldAccess.PrisonerTabState.Refresh();
        }

        protected override string ContentRegionName(int region)
        {
            Pawn pawn = RimWorldAccess.PrisonerTabState.CurrentPawn;
            switch (region)
            {
                case RegionInformation:
                    return "RimWorldAccess.Prisoner.Section.Information".Translate();

                case RegionMedicalCare:
                    return "RimWorldAccess.Prisoner.Section.MedicalCare".Translate(
                        "AllowMedicine".Translate(),
                        RimWorldAccess.PrisonerTabState.MedicalCareLevels().Length);

                case RegionInteractionModes:
                    if (pawn != null && pawn.IsSlaveOfColony)
                    {
                        return "RimWorldAccess.Prisoner.Section.SlaveModes".Translate(
                            RimWorldAccess.PrisonerTabState.SlaveModes.Count,
                            pawn.guest.slaveInteractionMode.LabelCap);
                    }
                    return "RimWorldAccess.Prisoner.Section.ExclusiveModes".Translate(
                        RimWorldAccess.PrisonerTabState.ExclusiveModes.Count,
                        pawn != null ? pawn.guest.ExclusiveInteractionMode.LabelCap.ToString() : "");

                case RegionNonExclusiveModes:
                    return "RimWorldAccess.Prisoner.Section.NonExclusiveModes".Translate(
                        RimWorldAccess.PrisonerTabState.NonExclusiveModes.Count);

                default:
                    return "RimWorldAccess.Prisoner.Section.IdeologySelection".Translate();
            }
        }

        protected override int ContentItemCount(int region)
        {
            Pawn pawn = RimWorldAccess.PrisonerTabState.CurrentPawn;
            if (pawn == null)
                return 0;

            switch (region)
            {
                case RegionInformation:
                    return RimWorldAccess.PrisonerTabState.InfoLines.Count;

                case RegionMedicalCare:
                    // Prisoner only: vanilla renders the care selector in DoPrisonerTab.
                    return pawn.IsPrisonerOfColony
                        ? RimWorldAccess.PrisonerTabState.MedicalCareLevels().Length
                        : 0;

                case RegionInteractionModes:
                    return pawn.IsSlaveOfColony
                        ? RimWorldAccess.PrisonerTabState.SlaveModes.Count
                        : RimWorldAccess.PrisonerTabState.ExclusiveModes.Count;

                case RegionNonExclusiveModes:
                    return pawn.IsPrisonerOfColony
                        ? RimWorldAccess.PrisonerTabState.NonExclusiveModes.Count
                        : 0;

                default:
                    // Zero unless armed, so Tab steps over the picker.
                    return ideologyArmed ? PrisonerTabHelper.GetPlayerIdeologies().Count : 0;
            }
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            Pawn pawn = RimWorldAccess.PrisonerTabState.CurrentPawn;
            if (pawn == null)
                return new ElementDescription();

            switch (region)
            {
                case RegionInformation:
                    return DescribeInfoRow(index);
                case RegionMedicalCare:
                    return DescribeMedicalCareRow(pawn, index);
                case RegionInteractionModes:
                    return pawn.IsSlaveOfColony
                        ? DescribeSlaveModeRow(pawn, index)
                        : DescribeExclusiveModeRow(pawn, index);
                case RegionNonExclusiveModes:
                    return DescribeNonExclusiveRow(pawn, index);
                default:
                    return DescribeIdeoligionRow(pawn, index);
            }
        }

        protected override void ActivateContentItem(int region, int index)
        {
            Pawn pawn = RimWorldAccess.PrisonerTabState.CurrentPawn;
            if (pawn == null)
                return;

            switch (region)
            {
                case RegionInformation:
                    // Read-only: Enter just re-announces.
                    AnnounceCurrentItem();
                    break;
                case RegionMedicalCare:
                    CommitMedicalCare(index);
                    break;
                case RegionInteractionModes:
                    if (pawn.IsSlaveOfColony)
                    {
                        CommitSlaveMode(pawn, index);
                    }
                    else
                    {
                        CommitExclusiveMode(index);
                    }
                    break;
                case RegionNonExclusiveModes:
                    CommitNonExclusiveToggle(pawn, index);
                    break;
                default:
                    CommitIdeoligion(index);
                    break;
            }
        }

        // Row descriptions.

        private static ElementDescription DescribeInfoRow(int index)
        {
            IReadOnlyList<string> lines = RimWorldAccess.PrisonerTabState.InfoLines;
            if (index < 0 || index >= lines.Count)
                return new ElementDescription();
            return new ElementDescription { Label = lines[index], ReadOnly = true };
        }

        private static ElementDescription DescribeMedicalCareRow(Pawn pawn, int index)
        {
            MedicalCareCategory[] levels = RimWorldAccess.PrisonerTabState.MedicalCareLevels();
            if (index < 0 || index >= levels.Length)
                return new ElementDescription();
            return new ElementDescription
            {
                Label = PrisonerTabHelper.GetMedicalCareLabel(levels[index]),
                Role = ElementRole.RadioButton,
                Selected = pawn.playerSettings != null && pawn.playerSettings.medCare == levels[index],
            };
        }

        private static ElementDescription DescribeExclusiveModeRow(Pawn pawn, int index)
        {
            IReadOnlyList<PrisonerInteractionModeDef> modes = RimWorldAccess.PrisonerTabState.ExclusiveModes;
            if (index < 0 || index >= modes.Count)
                return new ElementDescription();

            PrisonerInteractionModeDef mode = modes[index];
            bool selected = pawn.guest.ExclusiveInteractionMode == mode;
            string extras = PrisonerTabHelper.GetInteractionModeDescription(pawn, mode);

            // Vanilla shows the conversion target beside the Convert row, and only while that
            // mode is active.
            if (mode == PrisonerInteractionModeDefOf.Convert && selected && pawn.guest.ideoForConversion != null)
            {
                string target = (string)"IdeoConversionTarget".Translate() + ": " + pawn.guest.ideoForConversion.name;
                extras = string.IsNullOrEmpty(extras) ? target : extras + ". " + target;
            }

            return new ElementDescription
            {
                Label = mode.LabelCap,
                Role = ElementRole.RadioButton,
                Selected = selected,
                Extras = extras,
            };
        }

        private static ElementDescription DescribeSlaveModeRow(Pawn pawn, int index)
        {
            IReadOnlyList<SlaveInteractionModeDef> modes = RimWorldAccess.PrisonerTabState.SlaveModes;
            if (index < 0 || index >= modes.Count)
                return new ElementDescription();

            SlaveInteractionModeDef mode = modes[index];
            return new ElementDescription
            {
                Label = mode.LabelCap,
                Role = ElementRole.RadioButton,
                Selected = pawn.guest.slaveInteractionMode == mode,
                Extras = PrisonerTabHelper.GetSlaveInteractionModeDescription(pawn, mode),
            };
        }

        private static ElementDescription DescribeNonExclusiveRow(Pawn pawn, int index)
        {
            IReadOnlyList<PrisonerInteractionModeDef> modes = RimWorldAccess.PrisonerTabState.NonExclusiveModes;
            if (index < 0 || index >= modes.Count)
                return new ElementDescription();

            PrisonerInteractionModeDef mode = modes[index];
            return new ElementDescription
            {
                Label = mode.LabelCap,
                Role = ElementRole.Checkbox,
                Check = pawn.guest.IsInteractionEnabled(mode) ? CheckState.Checked : CheckState.Unchecked,
                Extras = mode.description,
            };
        }

        private static ElementDescription DescribeIdeoligionRow(Pawn pawn, int index)
        {
            List<Ideo> ideologies = PrisonerTabHelper.GetPlayerIdeologies();
            if (index < 0 || index >= ideologies.Count)
                return new ElementDescription();

            Ideo ideo = ideologies[index];
            return new ElementDescription
            {
                Label = ideo.name,
                Role = ElementRole.RadioButton,
                Selected = pawn.guest.ideoForConversion == ideo,
            };
        }

        // Row activations. Each speaks the refreshed state alone, since the cursor stays on the
        // row the user just heard, except where the action opens something that announces itself.

        private void CommitMedicalCare(int index)
        {
            MedicalCareCategory[] levels = RimWorldAccess.PrisonerTabState.MedicalCareLevels();
            if (index < 0 || index >= levels.Length)
                return;
            RimWorldAccess.PrisonerTabState.SetMedicalCare(levels[index]);
            SpeakSelected();
        }

        private void CommitExclusiveMode(int index)
        {
            IReadOnlyList<PrisonerInteractionModeDef> modes = RimWorldAccess.PrisonerTabState.ExclusiveModes;
            if (index < 0 || index >= modes.Count)
                return;

            bool needsIdeoPicker = RimWorldAccess.PrisonerTabState.SetExclusiveMode(modes[index]);
            if (needsIdeoPicker)
            {
                ArmIdeoligionPicker();
                return;
            }
            SpeakSelected();
        }

        private void CommitSlaveMode(Pawn pawn, int index)
        {
            IReadOnlyList<SlaveInteractionModeDef> modes = RimWorldAccess.PrisonerTabState.SlaveModes;
            if (index < 0 || index >= modes.Count)
                return;

            SlaveInteractionModeDef mode = modes[index];
            SlaveInteractionModeDef previousMode = pawn.guest.slaveInteractionMode;

            // Vanilla applies the mode immediately, then confirms, reverting on cancel.
            RimWorldAccess.PrisonerTabState.SetSlaveMode(mode);

            if (!RimWorldAccess.PrisonerTabState.SlaveModeNeedsConfirmation(mode))
            {
                SpeakSelected();
                return;
            }

            SlaveInteractionModeDef revertTo = previousMode;
            string confirmation = "ExectueNeutralFactionSlave".Translate(
                pawn.Named("PAWN"), pawn.SlaveFaction.Named("FACTION"));

            // Vanilla's own Dialog_MessageBox, so the prompt is the standard window announced and
            // driven by MessageBoxScope. Both callbacks name the mode rather than routing through
            // the bare SpeakSelected other commits use: the dialog's question has intervened, so
            // a bare state word would arrive with no context.
            Find.WindowStack.Add(new Dialog_MessageBox(
                confirmation,
                "Confirm".Translate(),
                delegate
                {
                    string desc = PrisonerTabHelper.GetSlaveInteractionModeDescription(pawn, mode);
                    TolkHelper.SpeakData($"{mode.LabelCap}. {desc}");
                },
                "Cancel".Translate(),
                delegate
                {
                    RimWorldAccess.PrisonerTabState.SetSlaveMode(revertTo);
                    TolkHelper.SpeakData(revertTo.LabelCap);
                }));
        }

        private void CommitNonExclusiveToggle(Pawn pawn, int index)
        {
            IReadOnlyList<PrisonerInteractionModeDef> modes = RimWorldAccess.PrisonerTabState.NonExclusiveModes;
            if (index < 0 || index >= modes.Count)
                return;

            PrisonerInteractionModeDef mode = modes[index];
            bool enabled = !pawn.guest.IsInteractionEnabled(mode);
            RimWorldAccess.PrisonerTabState.ToggleNonExclusive(mode, enabled);

            var d = new ElementDescription
            {
                Check = enabled ? CheckState.Checked : CheckState.Unchecked,
            };
            TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(d, TranslatedShellVocabulary.Instance));
        }

        private void CommitIdeoligion(int index)
        {
            List<Ideo> ideologies = PrisonerTabHelper.GetPlayerIdeologies();
            if (index < 0 || index >= ideologies.Count)
                return;

            Ideo selected = ideologies[index];
            RimWorldAccess.PrisonerTabState.SetIdeoForConversion(selected);

            // The Convert row's own GetInteractionModeDescription already carries the chosen
            // target and the no-warden warning, so the landing announcement covers both.
            DisarmIdeoligionPicker();
        }

        /// <summary>Radio commits speak the new state alone; the cursor is already on the row.</summary>
        private static void SpeakSelected()
        {
            var d = new ElementDescription { Selected = true };
            TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(d, TranslatedShellVocabulary.Instance));
        }

        private void ToggleCheckboxRow()
        {
            // Clearing the search before ActivateCurrent means its settle-first grammar finds
            // nothing to settle and toggles in the same keypress, whatever region is current.
            TypeaheadReset();
            RefreshModel();
            if (Model.RegionIndex != RegionNonExclusiveModes)
                return;
            ActivateCurrent();
        }

        // The armed ideoligion region.

        private void ArmIdeoligionPicker()
        {
            ideologyArmed = true;
            RefreshModel();
            if (Model.MoveToRegion(RegionIdeoligion).Kind == MoveKind.Empty)
            {
                ideologyArmed = false;
                return;
            }

            // Land on the target already in force, so Escape without choosing changes nothing.
            Pawn pawn = RimWorldAccess.PrisonerTabState.CurrentPawn;
            List<Ideo> ideologies = PrisonerTabHelper.GetPlayerIdeologies();
            int current = pawn != null && pawn.guest != null && pawn.guest.ideoForConversion != null
                ? ideologies.IndexOf(pawn.guest.ideoForConversion)
                : -1;
            ListModel region = Model.CurrentRegion;
            if (region != null && current > 0 && current < region.Count)
            {
                region.MoveTo(current);
            }
            AnnounceRegion();
        }

        private void DisarmIdeoligionPicker()
        {
            // Leave the picker region BEFORE it empties: disarming first would let RefreshModel's
            // empty-region reconciliation relocate AND announce, doubling the utterance.
            RefreshModel();
            if (Model.MoveToRegion(RegionInteractionModes).Kind == MoveKind.Empty)
            {
                ideologyArmed = false;
                return;
            }
            ideologyArmed = false;
            RefreshModel();

            // Back onto Convert specifically — the row the picker belonged to.
            int convert = -1;
            IReadOnlyList<PrisonerInteractionModeDef> modes = RimWorldAccess.PrisonerTabState.ExclusiveModes;
            for (int i = 0; i < modes.Count; i++)
            {
                if (modes[i] == PrisonerInteractionModeDefOf.Convert)
                {
                    convert = i;
                    break;
                }
            }
            ListModel region = Model.CurrentRegion;
            if (region != null && convert > 0 && convert < region.Count)
            {
                region.MoveTo(convert);
            }
            AnnounceRegion();
        }

        // Lifecycle and Escape.

        /// <summary>
        /// The pushed instance, for <see cref="VisitorTabRingPatch"/>. The facade state holds no
        /// cursor, so the ring bracket reaches the focused row through here.
        /// </summary>
        internal static PrisonerTabScope Current { get; private set; }

        public override void OnPush()
        {
            base.OnPush();
            Current = this;
            ideologyArmed = false;
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
        /// Which vanilla widget the focused row rings, for <see cref="VisitorTabRingPatch"/>.
        ///
        /// The two mode regions pair by ordinal: their builders enumerate the same defs, through
        /// the same predicate, in the same listOrder as vanilla's tmpPrisonerInteractionModes /
        /// tmpSlaveInteractionModes (decompiled RimWorld/ITab_Pawn_Visitor.cs:141, :368, :384).
        /// Medical care and the ideoligion picker are many-to-one — vanilla puts every choice
        /// behind a single dropdown button (:365) and a single ideo icon (:309).
        ///
        /// The information region rings nothing: its rows are built for the ear (synthesized
        /// header, tooltips promoted to rows, per-line breakdowns vanilla folds into one label),
        /// so no ordinal pairing with the drawn bands exists and a wrong ring is worse than none.
        /// </summary>
        internal VisitorTabRingFocus CurrentRingFocus()
        {
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty)
            {
                return VisitorTabRingFocus.None;
            }

            switch (Model.RegionIndex)
            {
                case RegionMedicalCare:
                    return Single(VisitorTabRingKind.MedicalCare);

                case RegionInteractionModes:
                    return Ordinal(VisitorTabRingKind.InteractionRadio, region);

                case RegionNonExclusiveModes:
                    return Ordinal(VisitorTabRingKind.NonExclusiveCheckbox, region);

                case RegionIdeoligion:
                    return Single(VisitorTabRingKind.IdeoConversionIcon);

                default:
                    return VisitorTabRingFocus.None;
            }
        }

        private static VisitorTabRingFocus Single(VisitorTabRingKind kind)
        {
            return new VisitorTabRingFocus { Kind = kind, Index = 0, ExpectedCount = 1 };
        }

        private static VisitorTabRingFocus Ordinal(VisitorTabRingKind kind, ListModel region)
        {
            return new VisitorTabRingFocus { Kind = kind, Index = region.Index, ExpectedCount = region.Count };
        }

        /// <summary>
        /// Landing on the medical care region always previews the level actually in
        /// force, never a remembered browse position — the retired
        /// AnnounceCurrentSection recomputed this on every section entry (medical
        /// care can change underfoot from the Health tab while this region sits
        /// unvisited), and the region's own ListModel has no way to know that on
        /// its own.
        /// </summary>
        protected override void OnRegionChanged(MoveResult result)
        {
            if (Model.RegionIndex != RegionMedicalCare)
                return;
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty)
                return;
            region.MoveTo(RimWorldAccess.PrisonerTabState.CurrentMedicalCareIndex());
        }

        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen)
                return;
            announcedOpen = true;

            Pawn pawn = RimWorldAccess.PrisonerTabState.CurrentPawn;
            if (pawn == null)
                return;

            TolkHelper.SpeakData(pawn.IsSlaveOfColony ? BuildSlaveOpening(pawn) : BuildPrisonerOpening(pawn));
            AnnounceCurrentItem();
        }

        /// <summary>
        /// The tab's opening summary: who this is, the mode in force (with its
        /// conversion target when Convert), the medical care level, and the navigation
        /// hint — the retired AnnouncePrisonerOpened, verbatim.
        /// </summary>
        private static string BuildPrisonerOpening(Pawn pawn)
        {
            PrisonerInteractionModeDef currentMode = pawn.guest.ExclusiveInteractionMode;
            string mode = currentMode.LabelCap;
            if (currentMode == PrisonerInteractionModeDefOf.Convert && pawn.guest.ideoForConversion != null)
            {
                mode = $"{mode} ({pawn.guest.ideoForConversion.name})";
            }
            string care = PrisonerTabHelper.GetMedicalCareLabel(pawn.playerSettings.medCare);
            return (string)"RimWorldAccess.Prisoner.Tab.OpenedPrisoner".Translate(pawn.LabelShort)
                + ". " + (string)"RimWorldAccess.Prisoner.Tab.CurrentMode".Translate(mode)
                + ". " + (string)"AllowMedicine".Translate() + ": " + care
                + ". " + (string)"RimWorldAccess.Prisoner.Tab.NavigationHintInline".Translate();
        }

        /// <summary>The slave counterpart, including the suppression level — the retired AnnounceSlaveOpened, verbatim.</summary>
        private static string BuildSlaveOpening(Pawn pawn)
        {
            string announcement = (string)"RimWorldAccess.Prisoner.Tab.OpenedSlave".Translate(pawn.LabelShort)
                + ". " + (string)"RimWorldAccess.Prisoner.Tab.CurrentMode".Translate(pawn.guest.slaveInteractionMode.LabelCap);

            if (pawn.needs.TryGetNeed(out Need_Suppression suppressionNeed))
            {
                announcement += ". " + (string)"RimWorldAccess.Prisoner.Slave.Suppression".Translate(
                    suppressionNeed.CurLevel.ToStringPercent());
            }

            return announcement + ". " + (string)"RimWorldAccess.Prisoner.Tab.NavigationHintInline".Translate();
        }

        /// <summary>
        /// Escape: back out of the ideoligion picker if it is open, otherwise close the
        /// tab and hand the user back to the inspection tree underneath. An active
        /// search never reaches here — the base's own typeahead claim, registered
        /// first, clears it.
        /// </summary>
        private void HandleCancelKey()
        {
            if (ideologyArmed)
            {
                DisarmIdeoligionPicker();
                return;
            }
            RimWorldAccess.PrisonerTabState.Close();
            InspectionReturnHelper.AnnounceParentOrFallback(null);
        }
    }

    /// <summary>
    /// Keeps <see cref="PrisonerTabScope"/> in lockstep with
    /// <see cref="RimWorldAccess.PrisonerTabState.IsActive"/>, reconciled every OnGUI
    /// pass immediately after <see cref="InspectionScopeMirror"/> — PrisonerTabState
    /// opens FROM the inspection tree without closing it, so stack order is what gives
    /// this tab the keyboard (the same invariant HealthTabScope relies on).
    ///
    /// Stands down (pops) while an info card is open, the universal D-wave yield every
    /// mirror applies (see e.g. RangeEditScopeMirror's identical gate
    /// despite having no Alt+I of its own either) — verified no live path exists for
    /// this tab to reach an info card, kept as the established defensive default
    /// rather than removed on the strength of an absence.
    ///
    /// PrisonerTabState and HealthTabState are mutually exclusive: both open from the
    /// SAME inspection tree, and each fully masks the tree beneath it while open, so
    /// the tree cannot be navigated to the other tab's category row until the active
    /// one closes. No sibling gate is needed between them.
    /// </summary>
    internal static class PrisonerTabScopeMirror
    {
        private static readonly PrisonerTabScope scope = new PrisonerTabScope();

        public static void Reconcile()
        {
            if (RimWorldAccess.PrisonerTabState.IsActive && !InfoCardState.IsActive)
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
