using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Keyboard focus scope for the real vanilla <see cref="Dialog_AdvancedGameConfig"/> (the
    /// world-setup page's Advanced settings dialog), registered through
    /// <see cref="ScopeForWindow"/>. Its three columns become three regions: map-size radios
    /// (with vanilla's size-class words as each row's extras), start-season radios, and the
    /// read-only Notice column, then the automatic Buttons region with vanilla's captured Close.
    /// Radio activations mirror the dialog's own inline radio writes and re-sync the
    /// world-params screen's cached value strings, so the Advanced settings row beneath reads
    /// fresh when this dialog closes.
    /// </summary>
    public sealed class AdvancedGameConfigScope : ScreenScope
    {
        private const int MapSizeRegion = 0;
        private const int SeasonRegion = 1;
        private const int NoticeRegion = 2;

        private static readonly Season[] Seasons =
        {
            Season.Undefined, Season.Spring, Season.Summer, Season.Fall, Season.Winter,
        };

        private readonly Window dialog;
        private readonly List<int> mapSizes = new List<int>();
        private readonly List<string> notices = new List<string>();
        private bool announcedOpen;
        private string lastSpokenNotices;

        public AdvancedGameConfigScope(Window dialog)
        {
            this.dialog = dialog;
            // An unclaimed Escape is modal-swallowed and never reaches vanilla's closeOnCancel.
            Claim(SharedMenuGrammar.Cancel, e =>
            {
                ShellFrameStamps.MarkCancelConsumed();
                dialog.Close();
            });
        }

        public override string Name
        {
            get { return "advanced-game-config"; }
        }

        protected internal override Window OwnedWindow
        {
            get { return dialog; }
        }

        // ScreenScope content contract.

        protected override int ContentRegionCount
        {
            get { return 3; }
        }

        protected override string ContentRegionName(int region)
        {
            switch (region)
            {
                case MapSizeRegion: return "MapSize".Translate().ToString();
                case SeasonRegion:  return "MapStartSeason".Translate().ToString();
                default:            return "Notice".Translate().ToString();
            }
        }

        protected override void RefreshContent()
        {
            mapSizes.Clear();
            mapSizes.AddRange(Dialog_AdvancedGameConfig.MapSizes);
            if (Prefs.TestMapSizes)
            {
                // Vanilla's TestMapSizes array is private; its two values are fixed.
                mapSizes.Add(350);
                mapSizes.Add(400);
            }

            // The Notice column's own two conditions (Dialog_AdvancedGameConfig.DoWindowContents).
            notices.Clear();
            if (Find.GameInitData.startingSeason == Season.Winter)
            {
                notices.Add("MapWinterWarning".Translate().ToString());
            }
            if (Find.GameInitData.mapSize > 280)
            {
                notices.Add("MapSizePerformanceWarning".Translate().ToString());
            }
            if (notices.Count == 0)
            {
                notices.Add("NoneBrackets".Translate().ToString());
            }
        }

        protected override int ContentItemCount(int region)
        {
            switch (region)
            {
                case MapSizeRegion: return mapSizes.Count;
                case SeasonRegion:  return Seasons.Length;
                default:            return notices.Count;
            }
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            if (region == MapSizeRegion)
            {
                if (index >= 0 && index < mapSizes.Count)
                {
                    int size = mapSizes[index];
                    d.Label = "MapSizeDesc".Translate(size, size * size).ToString();
                    d.Role = ElementRole.RadioButton;
                    d.Selected = Find.GameInitData.mapSize == size;
                    d.Extras = SizeClassLabel(size);
                }
                return d;
            }
            if (region == SeasonRegion)
            {
                if (index >= 0 && index < Seasons.Length)
                {
                    Season season = Seasons[index];
                    d.Label = season == Season.Undefined
                        ? "MapStartSeasonDefault".Translate().ToString()
                        : season.LabelCap();
                    d.Role = ElementRole.RadioButton;
                    d.Selected = Find.GameInitData.startingSeason == season;
                }
                return d;
            }
            if (index >= 0 && index < notices.Count)
            {
                d.Label = notices[index];
            }
            return d;
        }

        /// <summary>Vanilla's size-class heading for the group this size opens (Small/Medium/Large/Extreme).</summary>
        private static string SizeClassLabel(int size)
        {
            switch (size)
            {
                case 200:
                case 225: return "MapSizeSmall".Translate().ToString();
                case 250:
                case 275: return "MapSizeMedium".Translate().ToString();
                case 300:
                case 325: return "MapSizeLarge".Translate().ToString();
                default:  return "MapSizeExtreme".Translate().ToString();
            }
        }

        protected override void ActivateContentItem(int region, int index)
        {
            if (ApplyRadio(region, index))
            {
                RefreshModel();
                AnnounceCurrentItem();
                SpeakNoticeChange();
                return;
            }
            // Notice rows are read-only.
            AnnounceCurrentItem();
        }

        private bool ApplyRadio(int region, int index)
        {
            if (region == MapSizeRegion && index >= 0 && index < mapSizes.Count
                && Find.GameInitData.mapSize != mapSizes[index])
            {
                // MUTATION-C: mirrors the dialog's own mapSize radio write
                // (Dialog_AdvancedGameConfig.cs:59-62); GameInitData.mapSize has no gated setter.
                Find.GameInitData.mapSize = mapSizes[index];
                WorldParamsFieldValues.SyncAdvancedFromGame();
                return true;
            }
            if (region == SeasonRegion && index >= 0 && index < Seasons.Length
                && Find.GameInitData.startingSeason != Seasons[index])
            {
                // MUTATION-C: mirrors the dialog's startingSeason radio writes (:69-88).
                Find.GameInitData.startingSeason = Seasons[index];
                WorldParamsFieldValues.SyncAdvancedFromGame();
                return true;
            }
            return false;
        }

        /// <summary>Radio rows select on arrival, silently — the landing announcement says "selected".</summary>
        protected override void OnCursorSettled(int region, int index)
        {
            base.OnCursorSettled(region, index);
            if (ApplyRadio(region, index))
            {
                RefreshModel();
                SpeakNoticeChange();
            }
        }

        /// <summary>The Notice column's text appears the moment a selection triggers it; speak it then.</summary>
        private void SpeakNoticeChange()
        {
            string joined = string.Join(". ", notices);
            if (joined == lastSpokenNotices)
            {
                return;
            }
            lastSpokenNotices = joined;
            if (notices.Count == 1 && notices[0] == "NoneBrackets".Translate().ToString())
            {
                return;
            }
            TolkHelper.SpeakData((string)"Notice".Translate() + ". " + joined);
        }

        public override void OnPush()
        {
            base.OnPush();
            announcedOpen = false;
            // Radio-group entry: each radio region's cursor lands on its selected row.
            RefreshModel();
            Model.Region(MapSizeRegion)?.MoveTo(
                System.Math.Max(0, mapSizes.IndexOf(Find.GameInitData.mapSize)));
            Model.Region(SeasonRegion)?.MoveTo(
                System.Math.Max(0, System.Array.IndexOf(Seasons, Find.GameInitData.startingSeason)));
            lastSpokenNotices = string.Join(". ", notices);
        }

        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen)
            {
                return;
            }
            announcedOpen = true;
            AnnounceRegion();
        }
    }
}
