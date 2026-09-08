using System;
using System.Collections.Generic;
using System.Text;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The persistence model for user rebinds: only deltas from defaults, as
    /// "actionId=Chord;Chord" lines, so improving a default later reaches every
    /// player who hasn't deliberately moved that action. An entry with no
    /// chords ("actionId=") means the player unbound the action entirely.
    ///
    /// PURE: string lines in, string lines out. The game side stores the lines
    /// in RimWorldAccessSettings via Scribe (ShellBindingPersistence).
    /// </summary>
    public sealed class BindingOverrideSet
    {
        // Sorted for deterministic output so the settings file doesn't churn.
        private readonly SortedDictionary<string, List<KeyChord>> byActionId =
            new SortedDictionary<string, List<KeyChord>>(StringComparer.Ordinal);

        public int Count
        {
            get { return byActionId.Count; }
        }

        public bool TryGet(string actionId, out IReadOnlyList<KeyChord> chords)
        {
            List<KeyChord> list;
            if (byActionId.TryGetValue(actionId, out list))
            {
                chords = list;
                return true;
            }
            chords = null;
            return false;
        }

        public void Set(string actionId, IEnumerable<KeyChord> chords)
        {
            if (string.IsNullOrEmpty(actionId))
                throw new ArgumentException("Action id is required.", nameof(actionId));
            byActionId[actionId] = new List<KeyChord>(chords);
        }

        public bool Remove(string actionId)
        {
            return byActionId.Remove(actionId);
        }

        /// <summary>One "actionId=Alt+M;Ctrl+K" line per override, sorted by action id.</summary>
        public List<string> ToLines()
        {
            var lines = new List<string>(byActionId.Count);
            foreach (var pair in byActionId)
            {
                var sb = new StringBuilder(pair.Key);
                sb.Append('=');
                for (int i = 0; i < pair.Value.Count; i++)
                {
                    if (i > 0)
                        sb.Append(';');
                    sb.Append(pair.Value[i].Serialize());
                }
                lines.Add(sb.ToString());
            }
            return lines;
        }

        /// <summary>
        /// Parses persisted lines. A malformed line — or a line with any
        /// unparseable chord — is dropped whole, reverting that one action to
        /// its defaults rather than applying half a rebind; everything else
        /// survives. Later duplicates of an action id win.
        /// </summary>
        public static BindingOverrideSet FromLines(IEnumerable<string> lines)
        {
            var set = new BindingOverrideSet();
            if (lines == null)
                return set;

            foreach (string line in lines)
            {
                if (string.IsNullOrEmpty(line))
                    continue;
                int eq = line.IndexOf('=');
                if (eq <= 0)
                    continue;

                string actionId = line.Substring(0, eq).Trim();
                if (actionId.Length == 0)
                    continue;

                string chordPart = line.Substring(eq + 1).Trim();
                var chords = new List<KeyChord>();
                bool valid = true;
                if (chordPart.Length > 0)
                {
                    string[] tokens = chordPart.Split(';');
                    for (int i = 0; i < tokens.Length; i++)
                    {
                        KeyChord chord;
                        if (!KeyChord.TryParse(tokens[i], out chord))
                        {
                            valid = false;
                            break;
                        }
                        if (!chords.Contains(chord))
                            chords.Add(chord);
                    }
                }
                if (!valid)
                    continue;

                set.byActionId[actionId] = chords;
            }
            return set;
        }

        /// <summary>
        /// Pushes these overrides onto a catalog. Ids the catalog doesn't know
        /// are left in the set untouched (a removed or not-yet-registered action
        /// keeps its rebind for a possible return) and reported back.
        /// </summary>
        public List<string> ApplyTo(ActionCatalog catalog)
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));
            var unknown = new List<string>();
            foreach (var pair in byActionId)
            {
                if (!catalog.SetBinding(pair.Key, pair.Value))
                    unknown.Add(pair.Key);
            }
            return unknown;
        }

        /// <summary>
        /// Snapshots a catalog's rebound actions (deltas only). Pass the
        /// previously loaded set as carryOver to preserve entries whose action
        /// ids the catalog doesn't currently know.
        /// </summary>
        public static BindingOverrideSet CaptureFrom(ActionCatalog catalog, BindingOverrideSet carryOver = null)
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));
            var set = new BindingOverrideSet();

            if (carryOver != null)
            {
                foreach (var pair in carryOver.byActionId)
                {
                    InputAction known;
                    if (!catalog.TryGet(pair.Key, out known))
                        set.byActionId[pair.Key] = new List<KeyChord>(pair.Value);
                }
            }

            var all = catalog.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].IsRebound)
                    set.byActionId[all[i].Id] = new List<KeyChord>(all[i].Bindings);
            }
            return set;
        }
    }
}
