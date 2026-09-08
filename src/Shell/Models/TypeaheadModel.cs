using System.Collections.Generic;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Typeahead buffer + match cursor: TypeaheadSearchHelper's state machine
    /// with the announcements removed and time injected (callers pass a
    /// monotonic seconds clock; the game side passes Time.realtimeSinceStartup)
    /// so the 3-second auto-reset is testable. Matching itself lives in
    /// TypeaheadMatcher — one algorithm for old helper and new model.
    ///
    /// PURE: links into the test project.
    /// </summary>
    public sealed class TypeaheadModel
    {
        public const double AutoResetSeconds = 3.0;

        /// <summary>
        /// Ignore the per-keystroke auto-reset (set during explicit CJK/IME
        /// search sessions where composing one character can take seconds).
        /// </summary>
        public bool SuppressAutoReset;

        /// <summary>
        /// Admit mid-word matches, ranked below every word-prefix tier
        /// (<see cref="TypeaheadMatcher"/>'s substring fallback). Set by scopes whose
        /// row list is itself the product of a substring filter, so the matcher can
        /// reach every row that filter leaves drawn.
        /// </summary>
        public bool SubstringFallback;

        private string buffer = "";
        private string lastFailedSearch = "";
        private double lastInputTime;
        private readonly List<int> matches = new List<int>();
        private int matchCursor;

        public string Buffer
        {
            get { return buffer; }
        }

        public bool HasActiveSearch
        {
            get { return buffer.Length > 0; }
        }

        public bool HasNoMatches
        {
            get { return buffer.Length > 0 && matches.Count == 0; }
        }

        /// <summary>The query that found nothing, kept for the "No matches for X" announcement.</summary>
        public string LastFailedSearch
        {
            get { return lastFailedSearch; }
        }

        public int MatchCount
        {
            get { return matches.Count; }
        }

        /// <summary>1-based position within matches, 0 when none.</summary>
        public int CurrentMatchPosition
        {
            get { return matches.Count > 0 ? matchCursor + 1 : 0; }
        }

        public IReadOnlyList<int> Matches
        {
            get { return matches; }
        }

        /// <summary>
        /// Appends a typed character and re-matches. Returns true with the
        /// first match's index when something matched; on no match, remembers
        /// the failed query and auto-clears (mirroring today's behavior).
        /// </summary>
        public bool Append(char c, IReadOnlyList<string> labels, double nowSeconds, out int newIndex)
        {
            newIndex = -1;

            if (!SuppressAutoReset && HasActiveSearch && nowSeconds - lastInputTime > AutoResetSeconds)
                Clear();

            lastInputTime = nowSeconds;
            buffer += c;

            Refilter(labels);
            if (matches.Count > 0)
            {
                matchCursor = 0;
                newIndex = matches[0];
                return true;
            }

            lastFailedSearch = buffer;
            Clear();
            return false;
        }

        /// <summary>Removes the last character; empty buffer clears the search. Returns false when nothing to erase.</summary>
        public bool Backspace(IReadOnlyList<string> labels, double nowSeconds, out int newIndex)
        {
            newIndex = -1;
            if (!HasActiveSearch)
                return false;

            buffer = buffer.Substring(0, buffer.Length - 1);
            lastInputTime = nowSeconds;

            if (buffer.Length == 0)
            {
                Clear();
                return true;
            }

            Refilter(labels);
            if (matches.Count > 0)
            {
                matchCursor = 0;
                newIndex = matches[0];
            }
            return true;
        }

        public void Clear()
        {
            buffer = "";
            matches.Clear();
            matchCursor = 0;
        }

        /// <summary>Next match after the given list index, wrapping; -1 when no matches.</summary>
        public int NextMatch(int currentIndex)
        {
            if (matches.Count == 0)
                return -1;
            int pos = matches.IndexOf(currentIndex);
            if (pos >= 0)
            {
                matchCursor = (pos + 1) % matches.Count;
            }
            else
            {
                matchCursor = 0;
                for (int i = 0; i < matches.Count; i++)
                {
                    if (matches[i] > currentIndex)
                    {
                        matchCursor = i;
                        break;
                    }
                }
            }
            return matches[matchCursor];
        }

        /// <summary>Previous match before the given list index, wrapping; -1 when no matches.</summary>
        public int PreviousMatch(int currentIndex)
        {
            if (matches.Count == 0)
                return -1;
            int pos = matches.IndexOf(currentIndex);
            if (pos >= 0)
            {
                matchCursor = (pos - 1 + matches.Count) % matches.Count;
            }
            else
            {
                matchCursor = matches.Count - 1;
                for (int i = matches.Count - 1; i >= 0; i--)
                {
                    if (matches[i] < currentIndex)
                    {
                        matchCursor = i;
                        break;
                    }
                }
            }
            return matches[matchCursor];
        }

        public int FirstMatch()
        {
            if (matches.Count == 0)
                return -1;
            matchCursor = 0;
            return matches[0];
        }

        public int LastMatch()
        {
            if (matches.Count == 0)
                return -1;
            matchCursor = matches.Count - 1;
            return matches[matchCursor];
        }

        private void Refilter(IReadOnlyList<string> labels)
        {
            matches.Clear();
            matches.AddRange(TypeaheadMatcher.FindMatches(buffer, labels, null, SubstringFallback));
        }
    }
}
