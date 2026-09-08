using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The keyboard focus scope for vanilla's <see cref="Dialog_SelectXenogerm"/> (the health-tab
    /// xenogerm implant picker). One content region over the dialog's own <c>xenogerms</c> list,
    /// each row's <c>Extras</c> naming the same genes the row draws icons for, in the same order,
    /// then counting the remainder; the complete list stays in the row's own tooltip. Activation
    /// writes the dialog's own
    /// <c>selected</c> field, mirroring the row's <c>ButtonInvisible</c> handler exactly; the
    /// metabolism/ideo gates and the real selection happen only inside vanilla's own <c>Accept()</c>,
    /// reached through the captured Accept button.
    /// </summary>
    public sealed class SelectXenogermScope : ScreenScope
    {
        // Mirrors vanilla's own gene-icon budget per row (Dialog_SelectXenogerm.DrawXenogerm).
        private const int GeneIconBudget = 10;

        private static readonly FieldInfo XenogermsField = AccessTools.Field(typeof(Dialog_SelectXenogerm), "xenogerms");
        private static readonly FieldInfo SelectedField = AccessTools.Field(typeof(Dialog_SelectXenogerm), "selected");

        private readonly Dialog_SelectXenogerm dialog;

        public SelectXenogermScope(Dialog_SelectXenogerm dialog)
        {
            this.dialog = dialog;

            // Unified Alt+I drill-in (feedback_unified_alt_i_drill_in): the vanilla info card for
            // the focused row's xenogerm, matching the real Widgets.InfoCardButton that row draws.
            Claim(SharedMenuGrammar.Info, e => ActivateInfoCard(), when: OnXenogermRow);
        }

        public override string Name
        {
            get { return "select-xenogerm"; }
        }

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        protected override string ContentRegionName(int region)
        {
            return "RimWorldAccess.Shell.SelectXenogerm.RegionName".Translate();
        }

        private List<Xenogerm> Xenogerms()
        {
            return XenogermsField?.GetValue(dialog) as List<Xenogerm> ?? new List<Xenogerm>();
        }

        protected override int ContentItemCount(int region)
        {
            return Xenogerms().Count;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            List<Xenogerm> xenogerms = Xenogerms();
            Xenogerm xenogerm = xenogerms[index];
            object current = SelectedField?.GetValue(dialog);

            List<GeneDef> genes = xenogerm.GeneSet.GenesListForReading;

            var sb = new StringBuilder();
            sb.Append(((string)"Genes".Translate()).CapitalizeFirst());
            sb.Append(": ");
            sb.Append(string.Join(", ", genes.Take(GeneIconBudget).Select(g => g.LabelCap.ToString())));
            if (genes.Count > GeneIconBudget)
            {
                sb.Append(". ");
                sb.Append("RimWorldAccess.Shell.SelectXenogerm.MoreGenes".Translate(genes.Count - GeneIconBudget));
            }

            return new ElementDescription
            {
                Role = ElementRole.RadioButton,
                Label = xenogerm.LabelCap,
                Selected = ReferenceEquals(current, xenogerm),
                Extras = sb.ToString(),
            };
        }

        protected override void ActivateContentItem(int region, int index)
        {
            List<Xenogerm> xenogerms = Xenogerms();
            if (SelectedField == null || index < 0 || index >= xenogerms.Count)
            {
                return;
            }
            // MUTATION-C: mirrors the row's own ButtonInvisible handler (`selected = xenogerm;`);
            // the real mutation happens inside vanilla's own Accept(), reached through the
            // captured Accept button.
            SelectedField.SetValue(dialog, xenogerms[index]);
            AnnounceCurrentItem();
        }

        protected override bool IncludeCapturedExtrasRegion
        {
            get { return true; }
        }

        private bool OnXenogermRow()
        {
            ListModel region = Model.CurrentRegion;
            return Model.RegionIndex == 0 && region != null && region.Index >= 0
                && region.Index < Xenogerms().Count;
        }

        private void ActivateInfoCard()
        {
            Find.WindowStack.Add(new Dialog_InfoCard(Xenogerms()[Model.CurrentRegion.Index]));
            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        public override void OnPush()
        {
            base.OnPush();
            SelectXenogermRowDrawPatch.Recording = true;
        }

        public override void OnPop()
        {
            SelectXenogermRowDrawPatch.Recording = false;
            base.OnPop();
        }

        protected internal override UnityEngine.Rect FocusedContentRect()
        {
            return OnXenogermRow()
                ? SelectXenogermRowDrawPatch.Rows.FindLast(Model.CurrentRegion.Index)
                : default(UnityEngine.Rect);
        }
    }

    /// <summary>Records each xenogerm row's rect; vanilla's own row index is the identity (decompiled RimWorld/Dialog_SelectXenogerm.cs:111 passes the list position).</summary>
    [HarmonyPatch(typeof(Dialog_SelectXenogerm), "DrawXenogerm")]
    internal static class SelectXenogermRowDrawPatch
    {
        internal static readonly RowDrawCapture Rows = new RowDrawCapture();

        /// <summary>Set while a <see cref="SelectXenogermScope"/> drives; the postfix is one static read otherwise.</summary>
        internal static bool Recording;

        [HarmonyPostfix]
        public static void Postfix(UnityEngine.Rect rect, int index)
        {
            if (Recording)
            {
                Rows.Record(index, rect);
            }
        }
    }
}
