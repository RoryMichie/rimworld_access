#if DEBUG
using System.Text;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// ShellDev's error-ergonomics surface: the last few real errors the game itself logged, for a
    /// bridge script to check "did that action throw anywhere" without a human watching the dev
    /// console.
    /// </summary>
    public static partial class ShellDev
    {
        /// <summary>Last <paramref name="count"/> Error-type entries from <see cref="Log.Messages"/>, oldest first, plus a total error/warning count line.</summary>
        public static string RecentErrors(int count = 10)
        {
            int errorCount = 0;
            int warningCount = 0;
            var recent = new System.Collections.Generic.List<string>();
            foreach (LogMessage message in Log.Messages)
            {
                if (message.type == LogMessageType.Error)
                {
                    errorCount++;
                    recent.Add(message.ToString());
                    if (recent.Count > count)
                    {
                        recent.RemoveAt(0);
                    }
                }
                else if (message.type == LogMessageType.Warning)
                {
                    warningCount++;
                }
            }

            var sb = new StringBuilder();
            sb.Append(errorCount).Append(" error(s), ").Append(warningCount).Append(" warning(s) total\n");
            if (recent.Count == 0)
            {
                sb.Append("(no errors)");
            }
            else
            {
                for (int i = 0; i < recent.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append('\n');
                    }
                    sb.Append(recent[i]);
                }
            }
            return sb.ToString();
        }
    }
}
#endif
