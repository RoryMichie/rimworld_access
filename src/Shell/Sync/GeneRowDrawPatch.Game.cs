using System;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Every gene row on every gene surface — both xenotype editors, the xenogerm assembler,
    /// the gene viewer, the inspect pane's Genes tab — is drawn by one of two vanilla methods
    /// carrying both the row's gene and its rect: <c>GeneUIUtility.DrawGeneDef</c> (decompiled
    /// RimWorld/GeneUIUtility.cs:392) for gene DEFS and <c>GeneUIUtility.DrawGene</c> (:373)
    /// for a live pawn's genes. Postfixing both records exactly the geometry a focus ring
    /// needs; each scope resolves its focused gene against the capture in
    /// <c>FocusedContentRect</c>.
    ///
    /// Recording is gated on a gene scope being live, so both postfixes are one static read
    /// in normal play. The gate counts rather than flags: gene surfaces stack (the viewer over
    /// a starting-pawn page, the inspect tab behind a dialog), and the first to pop must not
    /// silence the one still driving.
    /// </summary>
    internal static class GeneRowDrawPatch
    {
        internal static readonly RowDrawCapture Rows = new RowDrawCapture();

        /// <summary>
        /// The inspect pane's Genes tab draws outside every <c>Window</c>, so
        /// ScreenScopeDrawPatch's ring arm never runs for the windowless
        /// <see cref="GeneInspectionScope"/>. It publishes its focused row's Gene or GeneDef
        /// here instead and the recorder rings that row as vanilla draws it — the shape
        /// <c>QuantityMenuRingPatch</c> already uses for the equally windowless quantity menu.
        /// </summary>
        internal static Func<object> WindowlessRingProvider;

        private static int liveScopes;

        internal static void BeginRecording()
        {
            liveScopes++;
        }

        internal static void EndRecording()
        {
            if (liveScopes > 0)
            {
                liveScopes--;
            }
        }

        internal static void Record(object gene, Rect geneRect)
        {
            try
            {
                if (liveScopes == 0 || gene == null)
                {
                    return;
                }
                Rows.Record(gene, geneRect);

                Func<object> provider = WindowlessRingProvider;
                object focused = provider != null ? provider() : null;
                if (focused != null && Equals(focused, gene))
                {
                    FocusRing.Draw(geneRect);
                    UiPointerFollow.NotifyFocusedRect(GuiSpace.VisibleScreenRect(geneRect));
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Gene row capture error", ex);
            }
        }
    }

    /// <summary>Records the gene-def rows of the xenotype editors, the xenogerm assembler, and every non-pawn gene set.</summary>
    [HarmonyPatch(typeof(GeneUIUtility), "DrawGeneDef")]
    internal static class GeneDefRowDrawPatch
    {
        [HarmonyPostfix]
        public static void Postfix(GeneDef gene, Rect geneRect)
        {
            GeneRowDrawPatch.Record(gene, geneRect);
        }
    }

    /// <summary>Records a live pawn's gene rows (the gene viewer and the inspect pane's Genes tab).</summary>
    [HarmonyPatch(typeof(GeneUIUtility), "DrawGene")]
    internal static class PawnGeneRowDrawPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Gene gene, Rect geneRect)
        {
            GeneRowDrawPatch.Record(gene, geneRect);
        }
    }
}
