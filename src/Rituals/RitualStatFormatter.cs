using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Formats announcements for Lord-job dialog views. Takes adapter-projected views,
    /// not dialog-specific types, so the same formatting drives ideology rituals,
    /// psychic rituals, and gravship launches.
    /// </summary>
    public static class RitualStatFormatter
    {
        public static string FormatExtraToggle(LordJobExtraToggle toggle, bool includeTooltip = true)
        {
            var sb = new StringBuilder();
            sb.Append(toggle.Label);
            sb.Append(": ");
            sb.Append(toggle.Checked
                ? (string)"RimWorldAccess.Rituals.Checkbox.Checked".Translate()
                : (string)"RimWorldAccess.Rituals.Checkbox.Unchecked".Translate());
            sb.Append(".");

            if (includeTooltip && !string.IsNullOrEmpty(toggle.Tooltip))
            {
                sb.Append(" ");
                sb.Append(toggle.Tooltip);
            }
            return sb.ToString();
        }

        public static string FormatQualityRow(LordJobQualityRow row)
        {
            var sb = new StringBuilder();
            sb.Append(row.Label);
            sb.Append(": ");
            sb.Append(row.Change);
            sb.Append(".");

            if (!row.IsInformational)
            {
                if (row.IsUncertain)
                {
                    sb.Append(" ");
                    sb.Append("RimWorldAccess.Rituals.Quality.UncertainOutcome".Translate());
                }
                else if (!row.HasCountContext)
                {
                    if (row.IsPresent)
                        sb.Append(row.IsPositive
                            ? " " + (string)"RimWorldAccess.Rituals.Quality.Bonus".Translate()
                            : " " + (string)"RimWorldAccess.Rituals.Quality.Penalty".Translate());
                    else if (row.Quality != 0)
                        sb.Append(row.Quality > 0
                            ? " " + (string)"RimWorldAccess.Rituals.Quality.Bonus".Translate()
                            : " " + (string)"RimWorldAccess.Rituals.Quality.Penalty".Translate());
                    else
                        sb.Append(" " + (string)"RimWorldAccess.Rituals.Quality.NotMet".Translate());
                }
            }

            if (!string.IsNullOrEmpty(row.Tooltip))
            {
                sb.Append(" ");
                sb.Append(row.Tooltip);
            }
            return sb.ToString();
        }
    }
}
