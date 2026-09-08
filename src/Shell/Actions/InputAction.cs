using System;
using System.Collections.Generic;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Where an action is visible, for dispatch and conflict purposes. Ambient
    /// categories (Map, World, MainMenu) are the mutually-exclusive base
    /// contexts; Menus actions are the shared menu grammar every list/table
    /// screen claims (arrows, Home/End, typeahead); Screen actions belong to one
    /// named screen scope and require a ScopeKey.
    /// </summary>
    public enum ActionCategory
    {
        /// <summary>Visible everywhere, regardless of what is focused (e.g. speech silence).</summary>
        Global,
        /// <summary>The colony-map ambient scope (cursor movement, tile info, orders).</summary>
        Map,
        /// <summary>The world/planet ambient scope.</summary>
        World,
        /// <summary>The pre-game main menu ambient scope.</summary>
        MainMenu,
        /// <summary>Shared menu grammar available inside every menu-like scope.</summary>
        Menus,
        /// <summary>Owned by one named screen scope; ScopeKey identifies it.</summary>
        Screen,
    }

    /// <summary>
    /// A named, rebindable thing the player can do. Screen code declares and
    /// handles actions; it never sees KeyCode. Ids are stable dotted names
    /// ("map.cursor.north", "trade.confirm") — they are the persistence key for
    /// user rebinds, so renaming one orphans that rebind.
    ///
    /// PURE: links into the test project. The optional vanilla mirror is carried
    /// as a KeyBindingDef defName string so this type never references Verse;
    /// the dispatcher resolves it game-side.
    /// </summary>
    public sealed class InputAction
    {
        /// <summary>Stable dotted identifier, e.g. "pawn.moodInfo".</summary>
        public string Id { get; }

        public ActionCategory Category { get; }

        /// <summary>
        /// Visibility scope for conflict checking: the owning screen's key for
        /// Screen actions, otherwise the category's own name.
        /// </summary>
        public string ScopeKey { get; }

        /// <summary>Exactly today's keys — players' muscle memory is sacred.</summary>
        public IReadOnlyList<KeyChord> Defaults { get; }

        /// <summary>
        /// Optional vanilla KeyBindingDef defName this action mirrors (time
        /// controls, camera, pause). The dispatcher additionally honors the
        /// player's vanilla binding for it, read-only.
        /// </summary>
        public string VanillaKeyBindingDefName { get; }

        private IReadOnlyList<KeyChord> overrideBindings;

        /// <summary>Effective chords: the user's rebind when present, else the defaults.</summary>
        public IReadOnlyList<KeyChord> Bindings
        {
            get { return overrideBindings ?? Defaults; }
        }

        /// <summary>True when the user has rebound this action away from its defaults.</summary>
        public bool IsRebound
        {
            get { return overrideBindings != null; }
        }

        public InputAction(
            string id,
            ActionCategory category,
            IReadOnlyList<KeyChord> defaults,
            string screenScopeKey = null,
            string vanillaKeyBindingDefName = null)
        {
            if (string.IsNullOrEmpty(id))
                throw new ArgumentException("Action id is required.", nameof(id));
            if (id.Trim() != id || id.IndexOf(' ') >= 0)
                throw new ArgumentException("Action id must not contain whitespace: '" + id + "'", nameof(id));
            if (id.IndexOf('.') < 0)
                throw new ArgumentException("Action id must be dotted, e.g. 'map.cursor.north': '" + id + "'", nameof(id));
            if (defaults == null)
                throw new ArgumentNullException(nameof(defaults));
            if (category == ActionCategory.Screen && string.IsNullOrEmpty(screenScopeKey))
                throw new ArgumentException("Screen actions require a screen scope key: '" + id + "'", nameof(screenScopeKey));
            if (category != ActionCategory.Screen && !string.IsNullOrEmpty(screenScopeKey))
                throw new ArgumentException("Only Screen actions may set a screen scope key: '" + id + "'", nameof(screenScopeKey));

            Id = id;
            Category = category;
            ScopeKey = category == ActionCategory.Screen ? screenScopeKey : category.ToString();
            Defaults = CopyChords(defaults, id);
            VanillaKeyBindingDefName = vanillaKeyBindingDefName;
        }

        /// <summary>Convenience for actions owned by one screen scope.</summary>
        public static InputAction ForScreen(
            string screenScopeKey,
            string id,
            IReadOnlyList<KeyChord> defaults,
            string vanillaKeyBindingDefName = null)
        {
            return new InputAction(id, ActionCategory.Screen, defaults, screenScopeKey, vanillaKeyBindingDefName);
        }

        /// <summary>
        /// Installs a user rebind. Passing null returns the action to its
        /// defaults. Called via ActionCatalog.SetBinding, which normalizes
        /// default-equal rebinds back to null so only true deltas persist.
        /// </summary>
        internal void SetOverride(IReadOnlyList<KeyChord> chords)
        {
            overrideBindings = chords == null ? null : CopyChords(chords, Id);
        }

        internal void ClearOverride()
        {
            overrideBindings = null;
        }

        private static IReadOnlyList<KeyChord> CopyChords(IReadOnlyList<KeyChord> source, string id)
        {
            var copy = new List<KeyChord>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                if (copy.Contains(source[i]))
                    throw new ArgumentException("Duplicate chord '" + source[i] + "' on action '" + id + "'");
                copy.Add(source[i]);
            }
            return copy;
        }

        public override string ToString()
        {
            return Id + " [" + string.Join(", ", ChordStrings()) + "]";
        }

        private IEnumerable<string> ChordStrings()
        {
            foreach (var chord in Bindings)
                yield return chord.Serialize();
        }
    }
}
