#if DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using RimWorldAccess.Shell;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// DEBUG-only completeness oracle over the one-pass card tabs: captures
    /// what each card tab ACTUALLY DRAWS for a pawn and reports every text
    /// fragment the hand-read adapter tree does not present. Advisory — the
    /// findings drive gap fixes in the hand-read adapters, which remain the
    /// production presentation for these tabs.
    ///
    /// Bridge-driven two-step like the capture probe (the bridge drains on
    /// arbitrary event types; the harness needs Repaint): eval 1 calls
    /// <see cref="Request"/>, the probe's pump patch runs one tab per Repaint
    /// frame, eval 2 reads <see cref="Report"/>.
    ///
    /// Also audits the two info-card surfaces (Records, Permits — drawn by
    /// static utilities inside Dialog_InfoCard, not by inspect tabs) against
    /// the info-card tree via the harness's TryCaptureDraw entry.
    ///
    /// Excluded by design: Social (viewport culling; readable
    /// PawnsForSocialInfo model) and Log (culling; readable model — capture is
    /// the wrong tool for both). FillableBar fragments are skipped: bars carry
    /// a percentage, not text, and the adapters present those values in their
    /// own wording.
    /// </summary>
    public static class InspectTabCaptureOracle
    {
        private static readonly Type[] cardTabs =
        {
            typeof(ITab_Pawn_Character),
            typeof(ITab_Pawn_Health),
            typeof(ITab_Pawn_Needs),
            typeof(ITab_Pawn_Training),
            typeof(ITab_Pawn_Gear),
        };

        // Info-card surfaces (not inspect tabs): drawn by static utilities
        // inside Dialog_InfoCard, captured via TryCaptureDraw and compared
        // against the info-card tree instead of the inspection tree. Both
        // utilities confusingly name their entry DrawRecordsCard.
        private sealed class CardSurface
        {
            public string Name;
            public Type Utility;
            public Action<Pawn, UnityEngine.Rect> Draw;
            public Func<Pawn, bool> Applies;
        }

        private static readonly CardSurface[] cardSurfaces =
        {
            new CardSurface
            {
                Name = "Records",
                Utility = typeof(RecordsCardUtility),
                Draw = (p, r) => RecordsCardUtility.DrawRecordsCard(r, p),
                Applies = p => true,
            },
            new CardSurface
            {
                Name = "Permits",
                Utility = typeof(PermitsCardUtility),
                Draw = (p, r) =>
                {
                    // Vanilla initializes this in StatsReportUtility.Reset when
                    // the info card opens; the draw path NREs without it.
                    if (PermitsCardUtility.selectedFaction == null)
                    {
                        PermitsCardUtility.selectedFaction =
                            ModLister.RoyaltyInstalled && Current.ProgramState == ProgramState.Playing
                                ? Faction.OfEmpire
                                : null;
                    }
                    PermitsCardUtility.DrawRecordsCard(r, p);
                },
                // The Dialog_InfoCard permits-tab gate, verbatim.
                Applies = p => ModsConfig.RoyaltyActive && p.RaceProps.Humanlike
                    && p.Faction == Faction.OfPlayer && !p.IsQuestLodger() && p.royalty != null
                    && (PermitsCardUtility.selectedFaction != null || Faction.OfEmpire != null),
            },
        };

        private static Pawn target;
        private static readonly List<Type> pendingTabs = new List<Type>();
        private static readonly List<CardSurface> pendingCards = new List<CardSurface>();
        private static readonly Dictionary<string, List<CapturedWidget>> captured =
            new Dictionary<string, List<CapturedWidget>>();
        private static readonly Dictionary<string, string> skipped =
            new Dictionary<string, string>();
        private static readonly List<string> surfaceOrder = new List<string>();

        /// <summary>Queue captures of every card tab and info-card surface for <paramref name="pawn"/>.</summary>
        public static string Request(Pawn pawn)
        {
            if (pawn == null)
            {
                return "null pawn";
            }
            target = pawn;
            pendingTabs.Clear();
            pendingTabs.AddRange(cardTabs);
            pendingCards.Clear();
            captured.Clear();
            skipped.Clear();
            surfaceOrder.Clear();
            foreach (Type tabType in cardTabs)
            {
                surfaceOrder.Add(tabType.Name);
            }
            foreach (CardSurface surface in cardSurfaces)
            {
                surfaceOrder.Add(surface.Name);
                if (surface.Applies(pawn))
                {
                    pendingCards.Add(surface);
                }
                else
                {
                    skipped[surface.Name] = "not applicable to this pawn";
                }
            }
            return $"queued {pendingTabs.Count} card tabs + {pendingCards.Count} info-card surfaces for {pawn.LabelShortCap}";
        }

        internal static void PumpOnRepaint()
        {
            if (target == null || Event.current == null || Event.current.type != EventType.Repaint)
            {
                return;
            }
            if (pendingTabs.Count > 0)
            {
                Type tabType = pendingTabs[0];
                pendingTabs.RemoveAt(0);
                try
                {
                    InspectTabBase tab = InspectTabManager.GetSharedInstance(tabType);
                    var rows = new List<CapturedWidget>();
                    if (InspectTabCaptureHarness.TryCapturePass(tab, target, rows, out string error))
                    {
                        captured[tabType.Name] = rows;
                    }
                    else
                    {
                        skipped[tabType.Name] = error;
                    }
                }
                catch (Exception ex)
                {
                    skipped[tabType.Name] = ex.ToString();
                }
                return;
            }
            if (pendingCards.Count > 0)
            {
                CardSurface surface = pendingCards[0];
                pendingCards.RemoveAt(0);
                try
                {
                    InspectTabCaptureHarness.ZeroScrollStatics(surface.Utility);
                    var rows = new List<CapturedWidget>();
                    Pawn pawn = target;
                    var rect = new UnityEngine.Rect(0f, 0f, 630f, 510f);
                    if (InspectTabCaptureHarness.TryCaptureDraw(
                        () => surface.Draw(pawn, rect), new Vector2(rect.width, rect.height), rows, out string error))
                    {
                        captured[surface.Name] = rows;
                    }
                    else
                    {
                        skipped[surface.Name] = error;
                    }
                }
                catch (Exception ex)
                {
                    skipped[surface.Name] = ex.ToString();
                }
            }
        }

        /// <summary>Pending status or the full per-surface coverage report.</summary>
        public static string Report()
        {
            if (target == null)
            {
                return "(no request)";
            }
            if (pendingTabs.Count > 0 || pendingCards.Count > 0)
            {
                return $"(pending, {captured.Count + skipped.Count}/{surfaceOrder.Count} surfaces)";
            }

            string infoCardText = null;
            var sb = new StringBuilder();
            sb.Append("capture oracle for ").Append(target.LabelShortCap).Append(':');
            foreach (string surface in surfaceOrder)
            {
                sb.Append('\n').Append(surface).Append(": ");
                if (skipped.TryGetValue(surface, out string reason))
                {
                    sb.Append("skipped (").Append(reason).Append(')');
                    continue;
                }
                bool isInfoCardSurface = cardSurfaces.Any(s => s.Name == surface);
                if (isInfoCardSurface && infoCardText == null)
                {
                    infoCardText = NormalizeForContainment(BuildInfoCardTreeText(target));
                }
                // Card tabs diff against the same per-tab containment basis as
                // the production parity rows (this tab's category plus every
                // non-tab node) so an oracle finding and an "Also shown" row
                // can never disagree — and so one tab's hand-authored row
                // cannot mask another tab's missing one.
                string treeText = isInfoCardSurface
                    ? infoCardText
                    : CaptureParityDiff.BuildNormalizedTreeText(
                        target,
                        InspectTabManager.GetSharedInstance(cardTabs.First(t => t.Name == surface)));
                List<string> fragments = ExtractFragments(captured[surface]);
                var missing = new List<string>();
                foreach (string fragment in fragments)
                {
                    if (!treeText.Contains(NormalizeForContainment(fragment)))
                    {
                        missing.Add(fragment);
                    }
                }
                sb.Append(fragments.Count).Append(" fragments, ").Append(missing.Count).Append(" missing");
                foreach (string m in missing)
                {
                    sb.Append("\n  MISSING: [").Append(m).Append(']');
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Every label in the pawn's fully-expanded info-card tree (the
        /// Records/Permits surfaces live in Dialog_InfoCard, not in inspect
        /// tabs). The dialog is constructed but never opened.
        /// </summary>
        private static string BuildInfoCardTreeText(Pawn pawn)
        {
            InspectionTreeItem root = InfoCardTreeBuilder.BuildTree(new Dialog_InfoCard(pawn));
            CaptureParityDiff.ExpandAll(root, 0);
            var sb = new StringBuilder();
            CaptureParityDiff.CollectText(root, sb);
            return sb.ToString();
        }

        /// <summary>
        /// Distinct comparable text fragments from a captured stream: widget
        /// labels and text-field contents, split into lines, truncation
        /// ellipses trimmed (Text.Truncate clips to the drawn rect; the
        /// adapters present the full string). Shares the per-line folding with
        /// the production parity diff via <see cref="CaptureTextNormalization"/>.
        /// </summary>
        private static List<string> ExtractFragments(List<CapturedWidget> rows)
        {
            var fragments = new List<string>();
            var seen = new HashSet<string>();
            foreach (CapturedWidget row in rows)
            {
                string text = row.Kind == WidgetKind.TextField ? row.Text : row.Label;
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }
                foreach (string rawLine in text.Split('\n'))
                {
                    string line = CaptureTextNormalization.NormalizeFragmentLine(rawLine);
                    if (line == null)
                    {
                        continue;
                    }
                    if (seen.Add(line))
                    {
                        fragments.Add(line);
                    }
                }
            }
            return fragments;
        }

        /// <summary>Case-, tag-, punctuation- and whitespace-insensitive form.</summary>
        private static string NormalizeForContainment(string text)
        {
            return CaptureTextNormalization.NormalizeForContainment(text.StripTags());
        }
    }
}
#endif
