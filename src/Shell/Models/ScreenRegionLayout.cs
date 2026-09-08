namespace RimWorldAccess.Shell
{
    /// <summary>What a region index in a <c>ScreenScope</c>'s model addresses.</summary>
    public enum ScreenRegionKind
    {
        /// <summary>Not a region this screen has.</summary>
        None,

        /// <summary>One of the screen's own typed content regions.</summary>
        Content,

        /// <summary>The automatic captured-extras ("Additional controls") region.</summary>
        Extras,

        /// <summary>The automatic Buttons/toolbar region.</summary>
        Actions,
    }

    /// <summary>
    /// The region layout a <c>ScreenScope</c> assembles for its model: N typed
    /// content regions, then the optional captured-extras region, then the
    /// optional Buttons region (the order <c>ScreenScope.RefreshModel</c> pushes
    /// its RegionSpecs in). Pure arithmetic over three inputs, extracted so
    /// every consumer classifies a region index the same way instead of
    /// re-deriving the offsets — the typeahead engine got this wrong, counting
    /// the extras region out of its region total while a catch-all branch built
    /// TOOLBAR entries and stamped them with the EXTRAS region's index, so
    /// typing a toolbar button's name jumped into the extras list and the real
    /// toolbar was never searched at all.
    ///
    /// PURE (no Unity/Verse types), so it links into the test project.
    /// </summary>
    public readonly struct ScreenRegionLayout
    {
        public readonly int ContentRegions;
        public readonly bool HasExtras;
        public readonly bool HasActions;

        public ScreenRegionLayout(int contentRegions, bool hasExtras, bool hasActions)
        {
            ContentRegions = contentRegions < 0 ? 0 : contentRegions;
            HasExtras = hasExtras;
            HasActions = hasActions;
        }

        /// <summary>Every region the model holds — content plus whichever automatic regions exist.</summary>
        public int TotalRegions
        {
            get { return ContentRegions + (HasExtras ? 1 : 0) + (HasActions ? 1 : 0); }
        }

        /// <summary>The captured-extras region's index (right after content), or -1 when it has no rows.</summary>
        public int ExtrasRegionIndex
        {
            get { return HasExtras ? ContentRegions : -1; }
        }

        /// <summary>
        /// The index the Buttons region occupies — after content, shifted by one
        /// when the extras region sits between them. Reported whether or not the
        /// region exists (matching ScreenScope's own long-standing
        /// ActionsRegionIndex semantics); <see cref="KindOf"/> is the check that
        /// answers whether an index really IS the toolbar.
        /// </summary>
        public int ActionsRegionIndex
        {
            get { return ContentRegions + (HasExtras ? 1 : 0); }
        }

        /// <summary>What <paramref name="region"/> addresses, or None when it addresses nothing.</summary>
        public ScreenRegionKind KindOf(int region)
        {
            if (region < 0)
            {
                return ScreenRegionKind.None;
            }
            if (region < ContentRegions)
            {
                return ScreenRegionKind.Content;
            }
            if (HasExtras && region == ExtrasRegionIndex)
            {
                return ScreenRegionKind.Extras;
            }
            if (HasActions && region == ActionsRegionIndex)
            {
                return ScreenRegionKind.Actions;
            }
            return ScreenRegionKind.None;
        }
    }
}
