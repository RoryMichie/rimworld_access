namespace RimWorldAccess.Shell
{
    /// <summary>
    /// One table column as the screen contract describes it
    /// (the table-model doctrine): header label, header tooltip, sortability. All
    /// texts arrive already localized. Cell values are not here — they come
    /// per row through the scope's ContentCellText, and sorting comparisons
    /// stay with the data owner (ApplyContentSort wires the game's own
    /// comparers) so this type never holds game state.
    ///
    /// PURE: links into the test project.
    /// </summary>
    public sealed class TableColumnInfo
    {
        /// <summary>Localized column header ("Mass", "Market value").</summary>
        public string Label;

        /// <summary>Localized header tooltip; null when the column has none.</summary>
        public string HeaderTip;

        /// <summary>Enter on the header cell (or the sort chord) cycles the sort.</summary>
        public bool Sortable;

        /// <summary>
        /// Control role of this column's DATA cells, when they are operable widgets rather than
        /// values (a per-row button column). <see cref="ElementRole.None"/> (the default) keeps
        /// the plain table-cell grammar.
        /// </summary>
        public ElementRole CellRole;

        public TableColumnInfo(string label = null, string headerTip = null, bool sortable = false,
            ElementRole cellRole = ElementRole.None)
        {
            Label = label;
            HeaderTip = headerTip;
            Sortable = sortable;
            CellRole = cellRole;
        }
    }
}
