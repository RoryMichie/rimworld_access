using System;
using System.Collections.Generic;
using Verse;
using static RimWorldAccess.Shell.CompatText;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The Power tab's detail rows. The mod draws three history graphs here (overview, consumption,
    /// production); the graphs themselves are excluded by ruling, so this provider models only what
    /// carries information beyond the pixels: each legend's own chapter list (label, last count over
    /// true max) and the period control the overview panel shares between both underlying
    /// <c>History</c> objects (<c>ManagerTab_Power.DrawOverview/DrawConsumption/DrawProduction</c>).
    /// </summary>
    internal sealed class CmrPowerDetails : ICmrJobDetailsProvider
    {
        public bool Handles(object tab)
        {
            return CmrCompat.Power.HandlesTab(tab);
        }

        public List<CmrDetailRegion> Build(object tab, object job)
        {
            var regions = new List<CmrDetailRegion>();
            if (tab == null || job == null || !CmrCompat.Power.Ready)
            {
                return regions;
            }

            object overall = CmrCompat.Power.OverallHistory(job);
            object trading = CmrCompat.Power.TradingHistory(job);

            if (overall != null)
            {
                regions.Add(BuildOverall(overall, trading));
            }
            if (trading != null)
            {
                regions.Add(BuildSigned("RimWorldAccess.Cmr.Power.ConsumptionRegion".Translate(), trading, -1));
                regions.Add(BuildSigned("RimWorldAccess.Cmr.Power.ProductionRegion".Translate(), trading, 1));
            }
            return regions;
        }

        // ------------------------------------------------------------------
        // Power overview: the period control, then the unsigned overall legend.
        // ------------------------------------------------------------------

        private static CmrDetailRegion BuildOverall(object overall, object trading)
        {
            var region = new CmrDetailRegion("RimWorldAccess.Cmr.Power.OverallRegion".Translate());
            region.Rows.Add(PeriodRow(overall, trading));
            AddChapterRows(region, overall, sign: 1, signed: false);
            return region;
        }

        /// <summary>Mirrors the cog button's own float menu (ManagerTab_Power.cs:219-271): one choice per period, each writing BOTH histories.</summary>
        private static CmrDetailRow PeriodRow(object overall, object trading)
        {
            return new CmrDetailRow
            {
                Label = ModText("ColonyManagerRedux.Energy.PeriodShown"),
                Role = ElementRole.ComboBox,
                Value = () => PeriodLabel(overall),
                Tooltip = () => Flatten(ModArgs("ColonyManagerRedux.Energy.PeriodShownTooltip",
                    PeriodLabel(overall))),
                Choices = () => PeriodChoices(overall, trading),
            };
        }

        private static string PeriodLabel(object history)
        {
            string name = CmrCompat.Power.PeriodName(CmrCompat.Power.PeriodShown(history));
            return ModText("ColonyManagerRedux.History.PeriodShown." + name).CapitalizeFirst();
        }

        private static List<CmrPickerChoice> PeriodChoices(object overall, object trading)
        {
            int count = CmrCompat.Power.PeriodCount();
            var choices = new List<CmrPickerChoice>(count);
            for (int i = 0; i < count; i++)
            {
                int index = i;
                string label = ModText("ColonyManagerRedux.History.PeriodShown."
                    + CmrCompat.Power.PeriodName(index)).CapitalizeFirst();
                choices.Add(new CmrPickerChoice(label, delegate
                {
                    CmrCompat.Power.SetPeriodShown(overall, index);
                    CmrCompat.Power.SetPeriodShown(trading, index);
                }));
            }
            return choices;
        }

        // ------------------------------------------------------------------
        // Consumption / Production: the trading history's own signed legend.
        // ------------------------------------------------------------------

        private static CmrDetailRegion BuildSigned(string name, object trading, int sign)
        {
            var region = new CmrDetailRegion(name);
            AddChapterRows(region, trading, sign, signed: true);
            return region;
        }

        private static void AddChapterRows(CmrDetailRegion region, object history, int sign, bool signed)
        {
            int period = CmrCompat.Power.PeriodShown(history);
            List<object> chapters = OrderedChapters(history, period, sign, signed);
            if (chapters.Count == 0)
            {
                // The renderer's own empty-legend line (DetailedLegendRenderer.cs:117-127).
                region.Rows.Add(new CmrDetailRow
                {
                    Label = ModText("ColonyManagerRedux.History.NoChapters"),
                    Role = ElementRole.None,
                });
                return;
            }
            for (int i = 0; i < chapters.Count; i++)
            {
                region.Rows.Add(ChapterRow(history, chapters[i], period, sign));
            }
        }

        /// <summary>Mirrors DetailedLegendRenderer.cs:100-109: filtered by sign when the panel is signed, ordered descending by the period's own last count times sign.</summary>
        private static List<object> OrderedChapters(object history, int period, int sign, bool signed)
        {
            List<object> chapters = CmrCompat.Power.Chapters(history);
            var kept = new List<object>();
            for (int i = 0; i < chapters.Count; i++)
            {
                object chapter = chapters[i];
                if (!signed || CmrCompat.Power.ChapterHasSign(chapter, period, sign))
                {
                    kept.Add(chapter);
                }
            }
            kept.Sort((a, b) =>
                CmrCompat.Power.ChapterLastCount(b, period) * sign
                    - CmrCompat.Power.ChapterLastCount(a, period) * sign);
            return kept;
        }

        /// <summary>The legend bar's own in-bar text (DetailedLegendRenderer.cs:246-264): read-only, since the row's own click toggles a graph line, and the graph is excluded.</summary>
        private static CmrDetailRow ChapterRow(object history, object chapter, int period, int sign)
        {
            return new CmrDetailRow
            {
                Label = Flatten(CmrCompat.Power.ChapterLabel(chapter)),
                Value = () => ChapterValue(history, chapter, period, sign),
                Role = ElementRole.None,
                InfoCardDef = CmrCompat.Power.ChapterThingDef(chapter),
            };
        }

        private static string ChapterValue(object history, object chapter, int period, int sign)
        {
            string suffix = CmrCompat.Power.ChapterSuffix(chapter) ?? CmrCompat.Power.YAxisSuffix(history);
            string last = CmrCompat.Power.FormatCount(
                CmrCompat.Power.ChapterLastCount(chapter, period) * sign, suffix);
            string max = CmrCompat.Power.FormatCount(CmrCompat.Power.ChapterTrueMax(chapter), suffix);
            return last + " / " + max;
        }

    }
}
