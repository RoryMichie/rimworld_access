using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The keyboard focus scope for Vanilla Expanded Framework's
    /// <c>VEF.Graphics.Dialog_GraphicCustomization</c> (the appearance editor opened from a float
    /// menu on an item with <c>CompGraphicCustomization</c>), registered through
    /// <see cref="ScopeForWindow.RegisterHierarchy"/> via <see cref="VefDialogCompat"/>. All
    /// reflection lives in <see cref="VefGraphicCustomizationCompat"/>; this scope only reads its
    /// typed methods, matching the <see cref="VefHireScope"/> facade split.
    ///
    /// This is the "variant previewer" pattern: the dialog's whole point is a live preview image
    /// a screen-reader user cannot see, so each appearance part is a row that announces its
    /// CURRENT VARIANT NAME instead. Enter opens a keyboard float menu picker (mirroring the
    /// dialog's own center dropdown) and is the only way a variant changes -- these rows are
    /// combo boxes, so Left/Right never step one in place -- matching
    /// <see cref="VfPainterScope"/>'s pattern/skin picker rows. An optional rename row (only when
    /// the item has a generated name) reuses <see cref="VefHireScope"/>'s
    /// <see cref="TextFieldEditSession"/> idiom: apply writes live, onExit re-announces once.
    ///
    /// <see cref="CaptureWindowButtons"/> is false: Dialog_GraphicCustomization draws its own
    /// Randomize/Confirm/Cancel <c>Widgets.ButtonText</c> calls with no <c>closeOnAccept</c>/
    /// <c>closeOnCancel</c> of its own, so <see cref="DeclaredActions"/> reproduces all three,
    /// running the dialog's own <c>Randomize</c> method (vehicle A) and mirroring its hand-rolled
    /// Confirm delegate (MUTATION-C, in <see cref="VefGraphicCustomizationCompat.Confirm"/>).
    /// </summary>
    internal sealed class VefGraphicCustomizationScope : ScreenScope
    {
        private const int AppearanceRegion = 0;

        private readonly Window dialog;
        private List<object> parts = new List<object>();
        private bool hasNameRow;
        private readonly TextFieldEditSession nameSession = new TextFieldEditSession();
        // Free-form: the vanilla field is a bare Widgets.TextField with no length/char rules, so
        // there is no game-harvested limit to enforce here.
        private readonly TextFieldSpec nameSpec = new TextFieldSpec(labelKey: null, minLength: 0);
        private readonly List<ScreenAction> actions = new List<ScreenAction>();

        public VefGraphicCustomizationScope(Window w)
        {
            dialog = w;
            // First-letter mnemonic on "Confirm".Translate().
            Claim("vefGraphicCustomization.confirm", e => VefGraphicCustomizationCompat.Confirm(dialog));

            RegisterPopTeardown(nameSession.CancelIfActive);
        }

        public override string Name => "vef-graphic-customization";

        /// <summary>Part rows are named items worth searching (table-model T4).</summary>
        protected override bool EnableTypeahead => true;

        protected internal override Window OwnedWindow => dialog;

        // ------------------------------------------------------------------
        // Row mapping: the name row (if present) sits first, part rows follow.
        // ------------------------------------------------------------------

        private bool IsNameRow(int index) => hasNameRow && index == 0;
        private int PartIndex(int index) => hasNameRow ? index - 1 : index;

        // ------------------------------------------------------------------
        // ScreenScope content contract.
        // ------------------------------------------------------------------

        protected override int ContentRegionCount => 1;

        protected override void RefreshContent()
        {
            parts = VefGraphicCustomizationCompat.Parts(dialog);
            hasNameRow = VefGraphicCustomizationCompat.HasNameField(dialog);
        }

        protected override string ContentRegionName(int region)
        {
            return "RimWorldAccess.Compat.Vef.GraphicAppearanceRegion".Translate();
        }

        protected override int ContentItemCount(int region)
        {
            return (hasNameRow ? 1 : 0) + parts.Count;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            if (IsNameRow(index))
            {
                d.Label = "RimWorldAccess.Compat.Vef.GraphicNameRow".Translate();
                d.Role = ElementRole.TextField;
                string name = VefGraphicCustomizationCompat.CurrentName(dialog);
                if (string.IsNullOrEmpty(name))
                    d.ValueBlank = true;
                else
                    d.Value = name;
                return d;
            }

            int pi = PartIndex(index);
            if (pi < 0 || pi >= parts.Count)
                return d;
            object part = parts[pi];
            d.Label = VefGraphicCustomizationCompat.PartName(part);
            d.Role = ElementRole.ComboBox; // a value chosen from a list; Enter opens the picker, Left/Right do not change it
            d.Value = VefGraphicCustomizationCompat.CurrentVariantName(dialog, part);
            d.Hint = "RimWorldAccess.Compat.Vef.GraphicVariantHint".Translate();
            return d;
        }

        // Part rows are combo boxes, so CanAdjustContentItem stays at the base
        // default (false): Enter/Space open the variant picker and Left/Right
        // never step a variant in place. The name row has nothing to adjust
        // either.

        // ------------------------------------------------------------------
        // Enter: rename field, or the variant picker.
        // ------------------------------------------------------------------

        protected override void ActivateContentItem(int region, int index)
        {
            if (IsNameRow(index))
            {
                BeginNameEdit();
                return;
            }
            int pi = PartIndex(index);
            if (pi < 0 || pi >= parts.Count)
            {
                AnnounceCurrentItem();
                return;
            }
            OpenVariantMenu(parts[pi]);
        }

        // ------------------------------------------------------------------
        // Name edit (VefHireScope idiom: apply writes live, onExit announces once).
        // ------------------------------------------------------------------

        private void BeginNameEdit()
        {
            string current = VefGraphicCustomizationCompat.CurrentName(dialog);
            nameSession.EnterEdit(current, nameSpec, "RimWorldAccess.Compat.Vef.GraphicNameRow".Translate(),
                ApplyNameEdit, OnNameExit, announcePrompt: true);
        }

        private void ApplyNameEdit(string value)
        {
            VefGraphicCustomizationCompat.SetName(dialog, value);
            RefreshModel();
        }

        private void OnNameExit()
        {
            AnnounceCurrentItem();
        }

        // ------------------------------------------------------------------
        // Variant picker: mirror the center dropdown as a keyboard-navigable FloatMenu. The
        // FloatMenu is keyboard-accessible through the shell's existing float-menu handling, which
        // announces each option; the selected option's texName is spoken on focus/selection.
        // ------------------------------------------------------------------

        private void OpenVariantMenu(object part)
        {
            var options = new List<FloatMenuOption>();
            foreach (object variant in VefGraphicCustomizationCompat.PartVariants(part))
            {
                object v = variant; // capture
                options.Add(new FloatMenuOption(VefGraphicCustomizationCompat.VariantName(v), delegate
                {
                    VefGraphicCustomizationCompat.SetVariant(dialog, part, v);
                    RefreshModel();
                }));
            }
            if (options.Count > 0)
                Find.WindowStack.Add(new FloatMenu(options));
        }

        // ------------------------------------------------------------------
        // Buttons region: Dialog_GraphicCustomization draws its own Randomize/Confirm/Cancel.
        // ------------------------------------------------------------------

        protected override bool CaptureWindowButtons => false;

        /// <summary>Shift+Enter presses Confirm from anywhere on this screen: the one-chord proceed.</summary>
        protected override string DefaultAcceptActionId
        {
            get { return "vefGraphicCustomization.confirm"; }
        }

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                actions.Add(new ScreenAction("RimWorldAccess.Compat.Vef.GraphicRandomize".Translate(), DoRandomize));
                actions.Add(new ScreenAction("Confirm".Translate(), delegate { VefGraphicCustomizationCompat.Confirm(dialog); }, "vefGraphicCustomization.confirm"));
                actions.Add(new ScreenAction(
                    "CancelButton".Translate(),
                    delegate { dialog.Close(true); },
                    SharedMenuGrammar.Cancel));
                return actions;
            }
        }

        private void DoRandomize()
        {
            VefGraphicCustomizationCompat.Randomize(dialog);
            RefreshModel();
            TolkHelper.SpeakData((string)"RimWorldAccess.Compat.Vef.GraphicRandomized".Translate());
        }

        // ------------------------------------------------------------------
        // Lifecycle.
        // ------------------------------------------------------------------

        protected override string ComposeOpenAnnouncement()
        {
            return (string)"RimWorldAccess.Compat.Vef.GraphicOpen".Translate(VefGraphicCustomizationCompat.ItemLabel(dialog));
        }
    }
}
