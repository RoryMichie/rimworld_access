using System.Collections.Generic;
using System.Text.RegularExpressions;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The keyboard focus scope for Vanilla Expanded Framework's
    /// <c>VEF.Planet.Dialog_Hire</c> (the mercenary-hiring quantity configurator, opened from a
    /// comms console), registered through <see cref="ScopeForWindow.RegisterHierarchy"/> via
    /// <see cref="VefDialogCompat"/>. All reflection lives in <see cref="VefHireCompat"/>; this
    /// scope only reads its typed methods, matching the <see cref="VfAssignSeatsScope"/> facade
    /// split.
    ///
    /// This is the "quantity configurator" pattern: one content region per hireable faction
    /// (each a row per pawn kind), plus a terms region for contract length and the live cost
    /// breakdown. Left/Right step a row's count (or the contract length); Enter opens a
    /// <see cref="TextFieldEditSession"/> for exact numeric entry, mirroring
    /// <see cref="VfPainterScope"/>'s hex field. Once any pawn kind's count is above zero, vanilla
    /// locks the choice to that one faction (<c>Dialog_Hire.curFaction</c>); the other factions'
    /// rows stay navigable and say so rather than disappearing.
    ///
    /// <see cref="CaptureWindowButtons"/> is false: Dialog_Hire draws its own Cancel/Confirm
    /// <c>Widgets.ButtonText</c> calls, so blanket capture would over-collect them (the same trap
    /// <see cref="VfAssignSeatsScope"/> documents for Dialog_AssignSeats). <see cref="DeclaredActions"/>
    /// reproduces both buttons, running the dialog's own <c>OnAcceptKeyPressed</c>/
    /// <c>OnCancelKeyPressed</c> (vehicle A) so the whole-hire commit and the sighted Confirm
    /// button's affordability gate (<c>CostFinal &gt; availableSilver</c>) are never
    /// reimplemented.
    /// </summary>
    internal sealed class VefHireScope : ScreenScope
    {
        private const int TermsDaysRow = 0;
        private const int TermsBaseCostRow = 1;
        private const int TermsRiskRow = 2;
        private const int TermsTotalRow = 3;
        private const int TermsSilverRow = 4;
        private const int TermsRowCount = 5;

        private readonly Window dialog;
        private readonly List<object> factions;
        private readonly TextFieldEditSession countSession = new TextFieldEditSession();
        // No length cap: vanilla's DrawCountAdjuster bounds by numeric VALUE (0-99 units,
        // 0-60 days), enforced in VefHireCompat.SetPawnCount/SetDays -- not by string length.
        // Digits-only entry here; the value clamp is the harvested game limit.
        private readonly TextFieldSpec countSpec = new TextFieldSpec(
            labelKey: null,
            minLength: 0,
            allowedChars: new Regex("^[0-9]*$"));
        private readonly List<ScreenAction> actions = new List<ScreenAction>();

        private int editRegion = -1;
        private int editIndex = -1;

        public VefHireScope(Window w)
        {
            dialog = w;
            factions = VefHireCompat.Factions(w) ?? new List<object>();
            // First-letter mnemonic on "Confirm".Translate().
            Claim("vefHire.confirm", e => ConfirmHire());

            RegisterPopTeardown(countSession.CancelIfActive);
        }

        public override string Name => "vef-hire";

        /// <summary>Faction and pawn-kind rows are named items worth searching (table-model T4).</summary>
        protected override bool EnableTypeahead => true;

        protected internal override Window OwnedWindow => dialog;

        // ------------------------------------------------------------------
        // ScreenScope content contract.
        // ------------------------------------------------------------------

        protected override int ContentRegionCount => factions.Count + 1;

        private bool IsTermsRegion(int region)
        {
            return region == factions.Count;
        }

        protected override string ContentRegionName(int region)
        {
            if (IsTermsRegion(region))
                return "RimWorldAccess.Compat.Vef.HireTermsRegion".Translate();

            object faction = factions[region];
            Def def = VefHireCompat.FactionDefOf(faction);
            if (VefHireCompat.FactionLocked(dialog, faction))
                return "RimWorldAccess.Compat.Vef.HireFactionLockedRegion".Translate(def.LabelCap);
            return def.LabelCap;
        }

        protected override int ContentItemCount(int region)
        {
            if (IsTermsRegion(region))
                return TermsRowCount;
            return VefHireCompat.PawnKindsOf(factions[region])?.Count ?? 0;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            if (IsTermsRegion(region))
            {
                DescribeTermsRow(d, index);
                return d;
            }

            object faction = factions[region];
            List<PawnKindDef> kinds = VefHireCompat.PawnKindsOf(faction);
            if (kinds == null || index < 0 || index >= kinds.Count)
                return d;
            DescribePawnKindRow(d, faction, kinds[index]);
            return d;
        }

        // Enter opens the count-entry editor, so these rows keep Enter (DefaultAcceptInertness).
        private void DescribePawnKindRow(ElementDescription d, object faction, PawnKindDef pk)
        {
            d.Label = pk.LabelCap;
            d.Role = ElementRole.Stepper;
            int count = VefHireCompat.CountOf(dialog, pk);
            d.Value = count.ToString();
            d.AtMinimum = count <= 0;
            d.AtMaximum = count >= 99;
            d.EntersEditOnAccept = true;
            d.Extras = "RimWorldAccess.Compat.Vef.HireCombatPower".Translate(pk.combatPower.ToString("F0"));
            if (VefHireCompat.FactionLocked(dialog, faction))
            {
                d.Disabled = true;
                d.Extras = d.Extras + ", " + "RimWorldAccess.Compat.Vef.HireLockedReason".Translate();
            }
        }

        private void DescribeTermsRow(ElementDescription d, int index)
        {
            switch (index)
            {
                case TermsDaysRow:
                {
                    d.Label = "RimWorldAccess.Compat.Vef.HireDays".Translate();
                    d.Role = ElementRole.Stepper;
                    int days = VefHireCompat.Days(dialog);
                    d.Value = days.ToString();
                    d.AtMinimum = days <= 0;
                    d.AtMaximum = days >= 60;
                    d.EntersEditOnAccept = true;
                    break;
                }
                case TermsBaseCostRow:
                    d.Label = "RimWorldAccess.Compat.Vef.HireBaseCost".Translate();
                    d.Value = VefHireCompat.CostBase(dialog).ToStringMoney();
                    d.ReadOnly = true;
                    break;
                case TermsRiskRow:
                    d.Label = "RimWorldAccess.Compat.Vef.HireRiskMultiplier".Translate();
                    d.Value = VefHireCompat.RiskMultiplier(dialog).ToStringPercent();
                    d.ReadOnly = true;
                    break;
                case TermsTotalRow:
                    d.Label = "RimWorldAccess.Compat.Vef.HireTotalPrice".Translate();
                    d.Value = VefHireCompat.CostFinal(dialog).ToStringMoney();
                    d.ReadOnly = true;
                    break;
                case TermsSilverRow:
                    d.Label = "RimWorldAccess.Compat.Vef.HireAvailableSilver".Translate();
                    d.Value = VefHireCompat.AvailableSilver(dialog).ToStringMoney();
                    d.ReadOnly = true;
                    break;
            }
        }

        // ------------------------------------------------------------------
        // Left/Right stepping.
        // ------------------------------------------------------------------

        protected override bool CanAdjustContentItem(int region, int index)
        {
            if (IsTermsRegion(region))
                return index == TermsDaysRow;
            return !VefHireCompat.FactionLocked(dialog, factions[region]);
        }

        protected override void AdjustContentItem(int region, int index, int direction)
        {
            if (IsTermsRegion(region))
            {
                if (index != TermsDaysRow)
                    return;
                int days = VefHireCompat.Days(dialog);
                VefHireCompat.SetDays(dialog, days + direction);
                SoundDefOf.DragSlider.PlayOneShotOnCamera();
                RefreshModel();
                AnnounceAdjustResult(region, index);
                return;
            }

            object faction = factions[region];
            List<PawnKindDef> kinds = VefHireCompat.PawnKindsOf(faction);
            if (kinds == null || index < 0 || index >= kinds.Count)
                return;
            PawnKindDef pk = kinds[index];
            int count = VefHireCompat.CountOf(dialog, pk);
            VefHireCompat.SetPawnCount(dialog, faction, pk, count + direction);
            SoundDefOf.DragSlider.PlayOneShotOnCamera();
            RefreshModel();
            AnnounceAdjustResult(region, index);
        }

        /// <summary>
        /// One composed utterance carrying the changed value, the boundary word if the row is
        /// now pinned, and the running total -- the configurator's whole point. Re-describes the
        /// row through the SAME Describe* method the browse path uses, so the AtMinimum/AtMaximum
        /// flags it already computes are never dropped the way the old hand-concatenated string did.
        /// </summary>
        private void AnnounceAdjustResult(int region, int index)
        {
            var d = new ElementDescription();
            if (IsTermsRegion(region))
            {
                DescribeTermsRow(d, index);
            }
            else
            {
                object faction = factions[region];
                List<PawnKindDef> kinds = VefHireCompat.PawnKindsOf(faction);
                if (kinds == null || index < 0 || index >= kinds.Count)
                    return;
                DescribePawnKindRow(d, faction, kinds[index]);
            }

            string stateText = AnnouncementComposer.ComposeStateChange(d, TranslatedShellVocabulary.Instance);
            string msg = stateText + ". " + "RimWorldAccess.Compat.Vef.HireRunningTotal".Translate(VefHireCompat.CostFinal(dialog).ToStringMoney());
            if (!VefHireCompat.CanAfford(dialog))
                msg += ", " + "RimWorldAccess.Compat.Vef.HireOverBudget".Translate();
            TolkHelper.SpeakData(msg);
        }

        // ------------------------------------------------------------------
        // Enter: direct numeric entry, or a re-announce for non-adjustable rows.
        // ------------------------------------------------------------------

        protected override void ActivateContentItem(int region, int index)
        {
            if (IsTermsRegion(region))
            {
                if (index == TermsDaysRow)
                {
                    BeginCountEdit(region, index, VefHireCompat.Days(dialog).ToString(),
                        "RimWorldAccess.Compat.Vef.HireDays".Translate());
                }
                else
                {
                    AnnounceCurrentItem();
                }
                return;
            }

            object faction = factions[region];
            if (VefHireCompat.FactionLocked(dialog, faction))
            {
                AnnounceCurrentItem();
                return;
            }

            List<PawnKindDef> kinds = VefHireCompat.PawnKindsOf(faction);
            if (kinds == null || index < 0 || index >= kinds.Count)
                return;
            PawnKindDef pk = kinds[index];
            BeginCountEdit(region, index, VefHireCompat.CountOf(dialog, pk).ToString(), pk.LabelCap);
        }

        private void BeginCountEdit(int region, int index, string current, string label)
        {
            editRegion = region;
            editIndex = index;
            countSession.EnterEdit(current, countSpec, label, ApplyCountEdit, OnCountExit, announcePrompt: true);
        }

        /// <summary>
        /// Writes the typed value into the dialog. Per the <see cref="TextFieldEditSession"/>
        /// contract this fires once, on Enter-confirm, before the exit announcement; Escape never
        /// calls it, so a cancelled edit leaves the original count untouched. Clamping lives in
        /// <see cref="VefHireCompat.SetPawnCount"/>/<see cref="VefHireCompat.SetDays"/>.
        /// </summary>
        private void ApplyCountEdit(string value)
        {
            int.TryParse(value, out int n);

            if (IsTermsRegion(editRegion) && editIndex == TermsDaysRow)
            {
                VefHireCompat.SetDays(dialog, n);
            }
            else if (editRegion >= 0 && editRegion < factions.Count)
            {
                List<PawnKindDef> kinds = VefHireCompat.PawnKindsOf(factions[editRegion]);
                if (kinds != null && editIndex >= 0 && editIndex < kinds.Count)
                    VefHireCompat.SetPawnCount(dialog, factions[editRegion], kinds[editIndex], n);
            }
            RefreshModel();
        }

        /// <summary>
        /// Fires on both Enter and Escape after the session tears down. The value is already
        /// written (Enter) or unchanged (Escape), so a single announcement of the fresh value plus
        /// the running total keeps one voice -- no double-speak against <see cref="ApplyCountEdit"/>.
        /// Routes through the same composed-announcement mechanism as the Left/Right adjust paths.
        /// </summary>
        private void OnCountExit()
        {
            AnnounceAdjustResult(editRegion, editIndex);
        }

        // ------------------------------------------------------------------
        // Buttons region.
        // ------------------------------------------------------------------

        /// <summary>Dialog_Hire draws its own Cancel/Confirm buttons; blanket capture would over-collect (see class remarks).</summary>
        protected override bool CaptureWindowButtons => false;

        /// <summary>Shift+Enter presses Confirm from anywhere on this screen: the one-chord proceed.</summary>
        protected override string DefaultAcceptActionId
        {
            get { return "vefHire.confirm"; }
        }

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                actions.Add(new ScreenAction("Confirm".Translate(), ConfirmHire, "vefHire.confirm"));
                actions.Add(new ScreenAction(
                    "CancelButton".Translate(),
                    delegate { dialog.OnCancelKeyPressed(); },
                    SharedMenuGrammar.Cancel));
                return actions;
            }
        }

        /// <summary>Vehicle A: mirrors the sighted Confirm button's own affordability gate (Dialog_Hire.DoWindowContents), then invokes the dialog's own OnAcceptKeyPressed, which performs the whole hire.</summary>
        private void ConfirmHire()
        {
            if (!VefHireCompat.CanAfford(dialog))
            {
                Messages.Message("RimWorldAccess.Compat.Vef.HireNotEnoughSilver".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }
            dialog.OnAcceptKeyPressed();
        }

        // ------------------------------------------------------------------
        // Lifecycle.
        // ------------------------------------------------------------------

        protected override string ComposeOpenAnnouncement()
        {
            return (string)"RimWorldAccess.Compat.Vef.HireEditorOpen".Translate(VefHireCompat.CallLabel(dialog));
        }
    }
}
