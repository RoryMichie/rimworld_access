namespace RimWorldAccess
{
    /// <summary>
    /// Shared text shaping for gizmo announcement handlers.
    /// </summary>
    internal static class GizmoTextUtility
    {
        /// <summary>
        /// Replaces newline runs with sentence separators so multi-line tooltip
        /// text reads as continuous speech (announcements never use newlines as
        /// separators). A run becomes ". " unless the preceding character already
        /// ends a clause; whitespace around the run is trimmed.
        /// </summary>
        internal static string FlattenNewlines(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";
            var sb = new System.Text.StringBuilder(text.Length);
            bool inBreak = false;
            foreach (char c in text)
            {
                if (c == '\n' || c == '\r')
                {
                    if (!inBreak)
                    {
                        while (sb.Length > 0 && char.IsWhiteSpace(sb[sb.Length - 1]))
                            sb.Length--;
                        if (sb.Length > 0 && sb[sb.Length - 1] != '.' && sb[sb.Length - 1] != '!'
                            && sb[sb.Length - 1] != '?' && sb[sb.Length - 1] != ':' && sb[sb.Length - 1] != ',')
                            sb.Append('.');
                        if (sb.Length > 0)
                            sb.Append(' ');
                        inBreak = true;
                    }
                }
                else
                {
                    if (!inBreak || !char.IsWhiteSpace(c))
                    {
                        sb.Append(c);
                        inBreak = false;
                    }
                }
            }
            return sb.ToString().Trim();
        }
    }
}
