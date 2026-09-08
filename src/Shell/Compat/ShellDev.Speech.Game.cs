#if DEBUG
using System.Text;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// ShellDev's speech-ring surface: reads
    /// <see cref="TolkHelper"/>'s rolling utterance ring, independent of the
    /// BeginCapture/EndCapture window <see cref="InjectAndCapture"/> already uses — this ring is
    /// always recording, so a developer can ask "what got said recently" after the fact instead
    /// of having to bracket the moment ahead of time.
    /// </summary>
    public static partial class ShellDev
    {
        /// <summary>
        /// Newest-last dump of the last <paramref name="count"/> recorded utterances, one per
        /// line ("&lt;frame&gt; t=&lt;seconds&gt;s: &lt;text&gt;"). Clears the ring afterward when
        /// <paramref name="clear"/> is true.
        /// </summary>
        public static string RecentSpeech(int count = 20, bool clear = false)
        {
            TolkHelper.SpeechRingEntry[] snapshot = TolkHelper.SpeechRingSnapshot();
            string report;
            if (snapshot.Length == 0)
            {
                report = "(no speech recorded)";
            }
            else
            {
                int start = snapshot.Length > count ? snapshot.Length - count : 0;
                var sb = new StringBuilder();
                for (int i = start; i < snapshot.Length; i++)
                {
                    AppendSpeechLine(sb, snapshot[i]);
                }
                report = sb.ToString();
            }
            if (clear)
            {
                TolkHelper.ClearSpeechRing();
            }
            return report;
        }

        /// <summary>Monotonic snapshot stamp (the sequence engine uses this to bracket a run).</summary>
        internal static int SpeechRingStamp()
        {
            return TolkHelper.SpeechRingTotalRecorded;
        }

        /// <summary>Every utterance recorded after <paramref name="stamp"/>, newest last, same line shape as <see cref="RecentSpeech"/>.</summary>
        internal static string SpeechSince(int stamp)
        {
            TolkHelper.SpeechRingEntry[] snapshot = TolkHelper.SpeechRingSnapshot();
            var sb = new StringBuilder();
            for (int i = 0; i < snapshot.Length; i++)
            {
                if (snapshot[i].Seq > stamp)
                {
                    AppendSpeechLine(sb, snapshot[i]);
                }
            }
            return sb.Length == 0 ? "(no speech recorded)" : sb.ToString();
        }

        private static void AppendSpeechLine(StringBuilder sb, TolkHelper.SpeechRingEntry entry)
        {
            if (sb.Length > 0)
            {
                sb.Append('\n');
            }
            sb.Append(entry.Frame).Append(" t=").Append(entry.RealtimeSinceStartup.ToString("F1"))
              .Append("s: ").Append(entry.Text);
        }
    }
}
#endif
