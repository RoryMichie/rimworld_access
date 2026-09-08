namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The tri-state arithmetic behind Colony Manager Redux's group shortcuts ("All", "Predators",
    /// "Aggressive"...), taken from the mod's own <c>ManagerTab.DrawShortcutToggleCore</c>
    /// (ManagerTab.cs:759-789): a shortcut reads <c>allSelected</c>/<c>noneSelected</c> over its
    /// subset and, on click, allows the whole subset unless the whole subset is already allowed, in
    /// which case it disallows all of it.
    ///
    /// An EMPTY subset reads as fully checked, because <c>options.All(...)</c> is true for an empty
    /// list -- the mod really does draw a ticked box for, say, "Predators" on a map whose animal list
    /// has none, and clicking it does nothing. That is faithful, not a bug to fix here.
    ///
    /// PURE: no Unity/Verse types, links into the test project.
    /// </summary>
    public static class CmrSubsetToggle
    {
        /// <summary>The box the mod draws for a subset: checked when all of it is allowed, unchecked when none of it is, partial in between.</summary>
        public static CheckState StateOf(int subsetCount, int allowedCount)
        {
            if (allowedCount >= subsetCount)
            {
                return CheckState.Checked;
            }
            return allowedCount <= 0 ? CheckState.Unchecked : CheckState.PartiallyChecked;
        }

        /// <summary>
        /// What one click sets the whole subset to: allow it, unless it is already fully allowed.
        /// A partially allowed subset therefore fills up rather than emptying, matching the mod's
        /// <c>if (!allSelected) on(); else off();</c>.
        /// </summary>
        public static bool ValueForNextClick(int subsetCount, int allowedCount)
        {
            return allowedCount < subsetCount;
        }
    }
}
