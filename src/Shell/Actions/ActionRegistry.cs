using System;
using System.Collections.Generic;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// One action that would fire on the same chord as another.
    /// </summary>
    public readonly struct BindingConflict
    {
        public readonly InputAction First;
        public readonly InputAction Second;
        public readonly KeyChord Chord;

        public BindingConflict(InputAction first, InputAction second, KeyChord chord)
        {
            First = first;
            Second = second;
            Chord = chord;
        }

        public override string ToString()
        {
            return Chord.Serialize() + ": " + First.Id + " vs " + Second.Id;
        }
    }

    /// <summary>
    /// Result of asking "may this chord go on this action?" — the actions that
    /// would also fire on it in some shared context.
    /// </summary>
    public sealed class ConflictReport
    {
        public InputAction Action { get; }
        public KeyChord Chord { get; }
        public IReadOnlyList<InputAction> ConflictingActions { get; }

        public bool HasConflict
        {
            get { return ConflictingActions.Count > 0; }
        }

        public ConflictReport(InputAction action, KeyChord chord, IReadOnlyList<InputAction> conflictingActions)
        {
            Action = action;
            Chord = chord;
            ConflictingActions = conflictingActions;
        }
    }

    /// <summary>
    /// The set of all shell actions: registration, lookup, rebinds, and
    /// scope-aware conflict checking. Instance class so tests build small
    /// catalogs; the mod's single catalog lives behind the static
    /// ActionRegistry front.
    ///
    /// PURE: links into the test project.
    /// </summary>
    public sealed class ActionCatalog
    {
        private readonly Dictionary<string, InputAction> byId =
            new Dictionary<string, InputAction>(StringComparer.Ordinal);
        private readonly List<InputAction> ordered = new List<InputAction>();

        public void Register(InputAction action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            if (byId.ContainsKey(action.Id))
                throw new InvalidOperationException("Duplicate action id: '" + action.Id + "'");
            byId.Add(action.Id, action);
            ordered.Add(action);
        }

        public bool TryGet(string id, out InputAction action)
        {
            return byId.TryGetValue(id, out action);
        }

        public InputAction Get(string id)
        {
            InputAction action;
            if (!byId.TryGetValue(id, out action))
                throw new KeyNotFoundException("No action registered with id '" + id + "'");
            return action;
        }

        public IReadOnlyList<InputAction> All
        {
            get { return ordered; }
        }

        public int Count
        {
            get { return ordered.Count; }
        }

        /// <summary>
        /// Whether two actions can ever both be visible, i.e. whether sharing a
        /// chord between them is a real collision. Global actions are visible
        /// under everything. Menus actions coexist with every menu-like scope,
        /// so they collide with Screen actions and each other. Ambient contexts
        /// (Map/World/MainMenu) are mutually exclusive, and Screen scopes are
        /// modal over them and over each other.
        ///
        /// Deliberately conservative: non-modal overlay scopes layered over an
        /// ambient (targeting, placement) will refine this once the FocusStack
        /// exists — the checker is advisory until the rebind UI.
        /// </summary>
        public static bool CanCoexist(InputAction a, InputAction b)
        {
            if (a.Category == ActionCategory.Global || b.Category == ActionCategory.Global)
                return true;
            if (a.ScopeKey == b.ScopeKey)
                return true;
            if (IsMenusScreenShadowing(a, b))
                return true;
            return false;
        }

        /// <summary>
        /// A screen action sharing a chord with the shared menu grammar is the
        /// DESIGNED override mechanism, not a mistake: a scope that gives an
        /// arrow key screen-specific meaning (slider decrement, quantity step,
        /// its own Escape) claims its own action for that chord and simply
        /// does not claim the grammar one; claim order and when-guards resolve
        /// it. The rebind UI still surfaces these pairs (a user creating new
        /// shadowing deserves a warning), but the catalog-wide defaults sweep
        /// filters the whole class with this predicate instead of allowlisting
        /// every slider screen forever.
        /// </summary>
        public static bool IsMenusScreenShadowing(InputAction a, InputAction b)
        {
            return (a.Category == ActionCategory.Menus && b.Category == ActionCategory.Screen)
                || (a.Category == ActionCategory.Screen && b.Category == ActionCategory.Menus);
        }

        /// <summary>
        /// All actions (other than the one being bound) that would also fire on
        /// this chord in some context both can see. Checked against effective
        /// bindings, so a rebind that dodges a default collision is honored.
        /// </summary>
        public ConflictReport CheckConflict(InputAction action, KeyChord chord)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            var conflicts = new List<InputAction>();
            for (int i = 0; i < ordered.Count; i++)
            {
                InputAction other = ordered[i];
                if (ReferenceEquals(other, action))
                    continue;
                if (!CanCoexist(action, other))
                    continue;
                if (HasChord(other.Bindings, chord))
                    conflicts.Add(other);
            }
            return new ConflictReport(action, chord, conflicts);
        }

        /// <summary>
        /// Catalog-wide sweep over effective bindings; each colliding pair is
        /// reported once. Used by the inventory test to prove today's defaults
        /// carry no unintended collisions, and later by the rebind UI.
        /// </summary>
        public IReadOnlyList<BindingConflict> FindConflicts()
        {
            var found = new List<BindingConflict>();
            for (int i = 0; i < ordered.Count; i++)
            {
                for (int j = i + 1; j < ordered.Count; j++)
                {
                    InputAction a = ordered[i];
                    InputAction b = ordered[j];
                    if (!CanCoexist(a, b))
                        continue;
                    foreach (var chord in a.Bindings)
                    {
                        if (HasChord(b.Bindings, chord))
                            found.Add(new BindingConflict(a, b, chord));
                    }
                }
            }
            return found;
        }

        /// <summary>
        /// Installs a user rebind for an action. A rebind identical to the
        /// defaults is normalized back to "not rebound" so only true deltas
        /// exist (and persist). Returns false when the id is unknown.
        /// </summary>
        public bool SetBinding(string id, IReadOnlyList<KeyChord> chords)
        {
            InputAction action;
            if (!byId.TryGetValue(id, out action))
                return false;
            if (chords == null || SameChords(chords, action.Defaults))
                action.ClearOverride();
            else
                action.SetOverride(chords);
            return true;
        }

        public void ResetToDefaults()
        {
            for (int i = 0; i < ordered.Count; i++)
                ordered[i].ClearOverride();
        }

        public bool ResetToDefaults(string id)
        {
            InputAction action;
            if (!byId.TryGetValue(id, out action))
                return false;
            action.ClearOverride();
            return true;
        }

        /// <summary>
        /// Whether two actions have any effective chord in common — the same
        /// test <see cref="FindConflicts"/> applies, without the coexistence
        /// filter, for a scope asking "does one of my other claims already own
        /// this key?" (see <c>ScreenScope</c>'s shared-Space claim guard).
        /// Honors rebinds on both sides, so a player who moves Space off a
        /// screen's own action gets the shared grammar back there.
        /// </summary>
        public static bool ShareAnyChord(InputAction a, InputAction b)
        {
            if (a == null || b == null)
                return false;
            IReadOnlyList<KeyChord> chords = a.Bindings;
            for (int i = 0; i < chords.Count; i++)
            {
                if (HasChord(b.Bindings, chords[i]))
                    return true;
            }
            return false;
        }

        private static bool HasChord(IReadOnlyList<KeyChord> chords, KeyChord chord)
        {
            for (int i = 0; i < chords.Count; i++)
            {
                if (chords[i] == chord)
                    return true;
            }
            return false;
        }

        private static bool SameChords(IReadOnlyList<KeyChord> a, IReadOnlyList<KeyChord> b)
        {
            if (a.Count != b.Count)
                return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i] != b[i])
                    return false;
            }
            return true;
        }
    }

    /// <summary>
    /// The mod's single action catalog. Registration happens once at startup,
    /// registering everything into a dormant catalog; dispatch reads from it
    /// once wired up.
    /// </summary>
    public static class ActionRegistry
    {
        public static readonly ActionCatalog Catalog = new ActionCatalog();

        public static InputAction Get(string id)
        {
            return Catalog.Get(id);
        }

        public static IReadOnlyList<InputAction> All
        {
            get { return Catalog.All; }
        }

        public static ConflictReport CheckConflict(InputAction action, KeyChord chord)
        {
            return Catalog.CheckConflict(action, chord);
        }
    }
}
