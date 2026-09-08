using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The keyboard focus scope for the real <see cref="Dialog_AssignCaravanDrugPolicies"/>
    /// window (opened from WITab_Caravan_Items' "Assign drug policies" button), registered
    /// through <see cref="ScopeForWindow"/> (task 6, slice A).
    ///
    /// One content region ("Pawns"): one row per caravan pawn matching vanilla's own
    /// <c>pawn.drugs != null &amp;&amp; !pawn.DevelopmentalStage.Baby()</c> filter
    /// (Dialog_AssignCaravanDrugPolicies.DoWindowContents), presented as an
    /// <see cref="ElementRole.ComboBox"/> row whose value is the pawn's current drug
    /// policy (with the same trait/chemical-dependency-gene suffix
    /// <see cref="DrugPolicyUIUtility.DoAssignDrugPolicyButtons"/> appends -- a plain read
    /// mirror, no mutation). A pawn whose <c>CurrentPolicy</c> is null draws no dropdown at
    /// all in vanilla either (that method's own early return), so the row presents as a
    /// plain read-only label instead of a broken combo box.
    ///
    /// Enter rides the SAME vehicle AssignMenuScope's drug-policy
    /// column already uses for the Assign tab (<see cref="DropdownColumnHandler"/>,
    /// PawnColumnHandlerRegistry.Game.cs): reflectively invoking
    /// <c>DrugPolicyUIUtility.Button_GenerateMenu</c> (vanilla's OWN
    /// Widgets.Dropdown menu generator for this exact control) and firing the chosen
    /// FloatMenuOption's own <c>action</c> delegate -- Category A, no
    /// MUTATION-C, so the write is vanilla's own
    /// <c>pawn.drugs.CurrentPolicy = assignedDrugs;</c> assignment, never a hand copy of
    /// it. The generated list opens as a real float menu (matching
    /// AssignMenuHelper.ActivateCell's "vanilla's own menu generator, opened as a real
    /// float menu" pattern exactly, including its trailing "Edit..." entry). Left/Right do
    /// NOT step through policies: these rows are combo boxes, and a combo box is a
    /// dropdown, not a slider (mod-wide ruling).
    ///
    /// <see cref="CaptureWindowButtons"/> is false: vanilla's per-pawn
    /// <c>Widgets.Dropdown</c> calls are ALSO real captured "buttons" (a dropdown opener
    /// reads as a combo box to the capture pipeline), so the default automatic capture
    /// would over-capture every pawn row into the Buttons region alongside the two real
    /// top-level buttons. <see cref="DeclaredActions"/> instead declares exactly those two,
    /// under vanilla's own labels: "ManageDrugPolicies" (vehicle A -- vanilla's own
    /// <c>Find.WindowStack.Add(new Dialog_ManageDrugPolicies(null))</c> line from this
    /// dialog's top button) and the base Window's own Close button (<c>doCloseButton</c>,
    /// vehicle A via <c>Window.Close()</c>), matching the pattern
    /// <see cref="VefGraphicCustomizationScope"/> already established for a
    /// CaptureWindowButtons-false real window.
    ///
    /// Opening the real <c>Dialog_ManageDrugPolicies</c> this way hands off to
    /// <see cref="DrugPolicyDialogScope"/>, which the
    /// <c>Dialog_ManagePolicies&lt;T&gt;</c> registration supplies. The per-pawn dropdown rows
    /// reach the same place through vanilla's own float menu, including its trailing
    /// "Edit..." entry.
    /// </summary>
    public sealed class CaravanDrugPoliciesScope : ScreenScope
    {
        private const int PawnsRegion = 0;

        private static readonly FieldInfo CaravanField =
            AccessTools.Field(typeof(Dialog_AssignCaravanDrugPolicies), "caravan");

        /// <summary>Vanilla's own Widgets.Dropdown menu generator for this exact control -- see the class remarks.</summary>
        private static readonly MethodInfo DrugPolicyMenuGenerator =
            AccessTools.Method(typeof(DrugPolicyUIUtility), "Button_GenerateMenu");

        private readonly Dialog_AssignCaravanDrugPolicies dialog;
        private readonly Caravan caravan;
        private readonly List<Pawn> pawns = new List<Pawn>();
        private readonly List<ScreenAction> actions = new List<ScreenAction>();
        private bool announcedOpen;

        public CaravanDrugPoliciesScope(Dialog_AssignCaravanDrugPolicies dialog)
        {
            this.dialog = dialog;
            caravan = CaravanField?.GetValue(dialog) as Caravan;
        }

        public override string Name
        {
            get { return "caravan-drug-policies"; }
        }

        /// <summary>Pawn names are worth searching (table-model T4).</summary>
        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        // ------------------------------------------------------------------
        // ScreenScope content contract.
        // ------------------------------------------------------------------

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        protected override string ContentRegionName(int region)
        {
            return "RimWorldAccess.Caravan.DrugPolicies.PawnsRegion".Translate();
        }

        protected override void RefreshContent()
        {
            pawns.Clear();
            if (caravan?.pawns == null)
            {
                return;
            }
            // Vanilla's own filter (Dialog_AssignCaravanDrugPolicies.DoWindowContents).
            foreach (Pawn pawn in caravan.pawns)
            {
                if (pawn.drugs != null && !pawn.DevelopmentalStage.Baby())
                {
                    pawns.Add(pawn);
                }
            }
        }

        protected override int ContentItemCount(int region)
        {
            return pawns.Count;
        }

        private Pawn PawnAt(int index)
        {
            return index >= 0 && index < pawns.Count ? pawns[index] : null;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            Pawn pawn = PawnAt(index);
            if (pawn == null)
            {
                return d;
            }
            d.Label = pawn.LabelCap;
            DrugPolicy policy = pawn.drugs?.CurrentPolicy;
            if (policy == null)
            {
                // Vanilla draws no dropdown at all here (DoAssignDrugPolicyButtons'
                // own `if (pawn.drugs.CurrentPolicy == null) return;`) -- present
                // the row as plain text, not a broken combo box.
                d.ReadOnly = true;
                return d;
            }
            d.Role = ElementRole.ComboBox;
            d.Value = ComposePolicyText(pawn, policy);
            return d;
        }

        /// <summary>
        /// Mirrors DrugPolicyUIUtility.DoAssignDrugPolicyButtons' own text composition
        /// verbatim (minus its Rect-width truncation, irrelevant to speech): the policy's
        /// label, plus a parenthetical trait or chemical-dependency-gene name when either
        /// applies. A plain read, not a mutation -- no MUTATION-C needed.
        /// </summary>
        private static string ComposePolicyText(Pawn pawn, DrugPolicy policy)
        {
            string text = policy.label;
            if (pawn.story?.traits != null)
            {
                Trait trait = pawn.story.traits.GetTrait(TraitDefOf.DrugDesire);
                if (trait != null)
                {
                    text = text + " (" + trait.Label + ")";
                }
                else if (ModsConfig.BiotechActive && PawnUtility.TryGetChemicalDependencyGene(pawn, out Gene_ChemicalDependency gene))
                {
                    text = text + " (" + gene.Label + ")";
                }
            }
            return text;
        }

        // ------------------------------------------------------------------
        // Enter -- see the class remarks for the shared vehicle. Every row is a
        // combo box, so CanAdjustContentItem stays at the base default (false)
        // and Left/Right never reassign a policy behind the player's back.
        // ------------------------------------------------------------------

        protected override void ActivateContentItem(int region, int index)
        {
            Pawn pawn = PawnAt(index);
            if (pawn == null || pawn.drugs?.CurrentPolicy == null)
            {
                AnnounceCurrentItem();
                return;
            }
            if (!DropdownColumnHandler.OpenGeneratedMenu(DrugPolicyMenuGenerator, null, pawn))
            {
                AnnounceCurrentItem();
            }
            // else: the float menu announces itself (matches AssignMenuScope's
            // ActivateContentCell OpenedUI case).
        }

        // ------------------------------------------------------------------
        // Buttons region -- see the class remarks for why capture is disabled.
        // ------------------------------------------------------------------

        protected override bool CaptureWindowButtons
        {
            get { return false; }
        }

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                actions.Add(new ScreenAction(
                    "ManageDrugPolicies".Translate().ToString(),
                    delegate { Find.WindowStack.Add(new Dialog_ManageDrugPolicies(null)); }));
                actions.Add(new ScreenAction(
                    "CloseButton".Translate().ToString(),
                    delegate { dialog.Close(); },
                    SharedMenuGrammar.Cancel));
                return actions;
            }
        }

        // ------------------------------------------------------------------
        // Lifecycle.
        // ------------------------------------------------------------------

        public override void OnPush()
        {
            base.OnPush();
            announcedOpen = false;
            CaravanDrugPolicyRowDrawPatch.Recording = true;
        }

        public override void OnPop()
        {
            CaravanDrugPolicyRowDrawPatch.Recording = false;
            base.OnPop();
        }

        protected internal override Rect FocusedContentRect()
        {
            ListModel region = Model.CurrentRegion;
            if (Model.RegionIndex != PawnsRegion || region == null
                || region.Index < 0 || region.Index >= pawns.Count)
            {
                return default(Rect);
            }
            return CaravanDrugPolicyRowDrawPatch.Rows.FindLast(pawns[region.Index]);
        }

        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen)
            {
                return;
            }
            announcedOpen = true;
            TolkHelper.SpeakData("AssignDrugPolicies".Translate().ToString());
            AnnounceCurrentItem();
        }
    }

    /// <summary>Records each pawn row's rect; the Pawn vanilla hands its own row method is the identity.</summary>
    [HarmonyPatch(typeof(Dialog_AssignCaravanDrugPolicies), "DoRow")]
    internal static class CaravanDrugPolicyRowDrawPatch
    {
        internal static readonly RowDrawCapture Rows = new RowDrawCapture();

        /// <summary>Set while a <see cref="CaravanDrugPoliciesScope"/> drives; the postfix is one static read otherwise.</summary>
        internal static bool Recording;

        [HarmonyPostfix]
        public static void Postfix(Rect rect, Pawn pawn)
        {
            if (Recording)
            {
                Rows.Record(pawn, rect);
            }
        }
    }
}
