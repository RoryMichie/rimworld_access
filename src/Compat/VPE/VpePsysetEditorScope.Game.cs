using System.Collections.Generic;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The keyboard focus scope for Vanilla Psycasts Expanded's
    /// <c>UI.Dialog_Psyset</c> (the psyset membership editor), registered
    /// through <see cref="ScopeForWindow.RegisterHierarchy"/> via
    /// <see cref="RimWorldAccess.VpeTabCompat"/>. All reflection lives in
    /// <see cref="VpePsysetCompat"/>; this scope only reads
    /// its typed methods.
    ///
    /// This is the "membership editor" pattern: two content regions, a
    /// "members" list (region 0, the psycasts currently in this set) and a
    /// "candidates" list (region 1, the pawn's learned psycasts that are not
    /// yet in the set). The sighted interaction is drag-and-drop between two
    /// panels; the keyboard replacement is Enter, which removes from the
    /// members region or adds from the candidates region. Vanilla's
    /// pagination (`&lt;`/`&gt;` buttons over the candidate list) is a pure
    /// visual paging constraint dropped here in favor of one flat candidate
    /// list. Changes apply live to the psyset's own <c>Abilities</c> set and
    /// persist with the save -- there is no separate Confirm/Save step, so
    /// this scope declares only Close.
    ///
    /// <see cref="CaptureWindowButtons"/> is false: Dialog_Psyset draws its
    /// own per-page `&lt;`/`&gt;` Widgets.ButtonText calls inside
    /// DoWindowContents, so blanket capture would over-collect them as if
    /// they were the dialog's real bottom buttons (the same trap
    /// VfAssignSeatsScope documents for Dialog_AssignSeats).
    /// <see cref="DeclaredActions"/> declares a single Close action using the
    /// vanilla "CloseButton" key, matching the dialog's own doCloseButton.
    /// </summary>
    internal sealed class VpePsysetEditorScope : ScreenScope
    {
        private readonly Window dialog;
        private readonly object psyset;
        private readonly Pawn pawn;
        private readonly ThingComp compAbilities;
        private readonly List<Def> members = new List<Def>();
        private readonly List<Def> candidates = new List<Def>();
        private readonly List<ScreenAction> actions = new List<ScreenAction>();
        private bool announcedOpen;

        public VpePsysetEditorScope(Window dialog)
        {
            this.dialog = dialog;
            psyset = VpePsysetCompat.PsysetOf(dialog);
            pawn = VpePsysetCompat.PawnOf(dialog);
            compAbilities = VpePsysetCompat.CompAbilitiesOf(dialog);
        }

        public override string Name => "vpe-psyset-editor";

        /// <summary>Shared cross-region typeahead (table-model T4): both members and candidates are named items.</summary>
        protected override bool EnableTypeahead => true;

        // ------------------------------------------------------------------
        // ScreenScope content contract.
        // ------------------------------------------------------------------

        protected override int ContentRegionCount => 2;

        protected override string ContentRegionName(int region)
        {
            return region == 0
                ? "RimWorldAccess.Compat.Vpe.PsysetMembersRegion".Translate()
                : "RimWorldAccess.Compat.Vpe.PsysetAvailableRegion".Translate();
        }

        protected override void RefreshContent()
        {
            members.Clear();
            candidates.Clear();
            if (!VpePsysetCompat.Ready || psyset == null)
                return;

            members.AddRange(VpePsysetCompat.Members(psyset));
            if (compAbilities != null)
                candidates.AddRange(VpePsysetCompat.Candidates(psyset, compAbilities));
        }

        protected override int ContentItemCount(int region)
        {
            return region == 0 ? members.Count : candidates.Count;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            Def ability = AbilityAt(region, index);
            if (ability != null)
                d.Label = ability.LabelCap;
            return d;
        }

        /// <summary>Region 0 (member): removes the psycast from the set. Region 1 (candidate): adds it.</summary>
        protected override void ActivateContentItem(int region, int index)
        {
            Def ability = AbilityAt(region, index);
            if (ability == null)
                return;

            if (region == 0)
            {
                if (VpePsysetCompat.RemoveAbility(psyset, ability))
                {
                    RefreshModel();
                    TolkHelper.SpeakData((string)"RimWorldAccess.Compat.Vpe.PsysetRemovedFromSet".Translate(ability.LabelCap));
                }
            }
            else
            {
                if (VpePsysetCompat.AddAbility(psyset, compAbilities, ability))
                {
                    RefreshModel();
                    TolkHelper.SpeakData((string)"RimWorldAccess.Compat.Vpe.PsysetAdded".Translate(ability.LabelCap));
                }
            }
        }

        /// <summary>Dialog_Psyset draws its own `&lt;`/`&gt;` pagination ButtonText calls inside DoWindowContents; blanket capture would over-collect (see class remarks).</summary>
        protected override bool CaptureWindowButtons => false;

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                actions.Add(new ScreenAction(
                    "CloseButton".Translate().ToString(),
                    delegate { OwnedWindow?.Close(); },
                    SharedMenuGrammar.Cancel));
                return actions;
            }
        }

        // ------------------------------------------------------------------
        // Lifecycle.
        // ------------------------------------------------------------------

        public override void OnFocus()
        {
            base.OnFocus(); // REQUIRED -- Buttons-region bracket registration lives in base.
            if (announcedOpen)
                return;
            announcedOpen = true;

            TolkHelper.SpeakData((string)"RimWorldAccess.Compat.Vpe.PsysetEditorOpen".Translate(VpePsysetCompat.PsysetName(psyset)));
        }

        // ------------------------------------------------------------------
        // Row lookups.
        // ------------------------------------------------------------------

        private Def AbilityAt(int region, int index)
        {
            if (region == 0)
                return index >= 0 && index < members.Count ? members[index] : null;
            return index >= 0 && index < candidates.Count ? candidates[index] : null;
        }
    }
}
