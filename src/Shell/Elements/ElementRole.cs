namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The standard control vocabulary. A focusable item exposes one role; the role standardizes its
    /// spoken word (one translation table mod-wide), its interaction keys, its rendering in fallback
    /// windows, and its mapping to the real vanilla widget under the focus-highlight rules. Screen
    /// reader users know this grammar from every other application — "checkbox, checked" replaces a
    /// sentence of per-screen explanation.
    ///
    /// Extend only with an explicit design ruling: this is shared vocabulary.
    /// <see cref="Map"/> was added under that rule when the world-map work
    /// opened: "a map is a type of thing".
    /// </summary>
    public enum ElementRole
    {
        /// <summary>Plain item with no control semantics; no role word is spoken.</summary>
        None,
        /// <summary>Enter activates.</summary>
        Button,
        /// <summary>Enter or Space toggles; states checked / not checked / partially checked.</summary>
        Checkbox,
        /// <summary>Enter selects; one of a group (position fragment carries x of y).</summary>
        RadioButton,
        /// <summary>Enter opens the picker (a real float menu); state is the current value.</summary>
        ComboBox,
        /// <summary>Left/Right adjust; state is the current value.</summary>
        Slider,
        /// <summary>Left/Right or +/- step the value; bounds announce at minimum/maximum.</summary>
        Stepper,
        /// <summary>Enter opens the text-edit session; state is the text or blank.</summary>
        TextField,
        /// <summary>Menu/list entry; intentionally silent (the containing menu is the context).</summary>
        MenuItem,
        /// <summary>Selected, x of y within the tab strip.</summary>
        Tab,
        /// <summary>A navigable surface rather than a row: arrows move a cursor within it, and the item's identity is what the cursor is on.</summary>
        Map,
        /// <summary>Expanded/collapsed + level; the words are the state, no role word.</summary>
        TreeItem,
        /// <summary>Table position; column-header context is spoken by the table, no role word.</summary>
        TableCell,
    }
}
