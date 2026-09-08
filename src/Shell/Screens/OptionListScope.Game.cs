namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Base for a screen whose content is exactly ONE flat pickable list plus
    /// the automatic Buttons region — the dev-mode option pickers
    /// (<see cref="DevOptionListScope"/>) and other simple mod picker dialogs
    /// with the same "choose one row from a list" grammar.
    ///
    /// Subclasses fill four seams instead of the full <see cref="ScreenScope"/>
    /// content contract: <see cref="OptionRegionName"/> names the single
    /// region (already localized — the dialog's own header or a translated
    /// fallback), <see cref="OptionCount"/> and <see cref="DescribeOption"/>
    /// describe the rows, and <see cref="ActivateOption"/> runs the picked
    /// row's action. This base seals the single-region mapping
    /// (<see cref="ContentRegionCount"/>, <see cref="ContentRegionName"/>,
    /// <see cref="ContentItemCount"/>, <see cref="DescribeContentItem"/>,
    /// <see cref="ActivateContentItem"/>) onto those seams and turns on
    /// typeahead by default, since the rows are named items worth searching
    /// (table-model T4).
    ///
    /// On first focus after the scope is pushed, <see cref="OnFocus"/>
    /// announces the region instead of the current item — the region name
    /// doubles as the picker's header announcement, so a separate header
    /// speak-out would be redundant. Every subsequent focus announces the
    /// current item as usual.
    ///
    /// <see cref="ScreenScope.RefreshContent"/> and
    /// <see cref="ScreenScope.CaptureWindowButtons"/> are left untouched —
    /// subclasses override them exactly as they would on a plain
    /// <see cref="ScreenScope"/>.
    ///
    /// Do NOT use this base for a multi-region browser (e.g. filters plus
    /// results plus parameters) — subclass <see cref="ScreenScope"/> directly
    /// for those. Do NOT use it for a picker opened from an already-focused
    /// scope with no window of its own; that rides
    /// <c>WindowlessFloatMenuState</c> instead.
    /// </summary>
    internal abstract class OptionListScope : ScreenScope
    {
        private bool announcedOpen;

        /// <summary>The options are named items worth searching (table-model T4).</summary>
        protected override bool EnableTypeahead => true;

        public override void OnFocus()
        {
            base.OnFocus();
            if (!announcedOpen)
            {
                announcedOpen = true;
                // The region name is the dialog's own header, so AnnounceRegion
                // already speaks it — no separate header announcement.
                AnnounceRegion();
                return;
            }
            AnnounceCurrentItem();
        }

        // ------------------------------------------------------------------
        // ScreenScope content contract — sealed onto the single-list seams.
        // ------------------------------------------------------------------

        protected sealed override int ContentRegionCount => 1;

        protected sealed override string ContentRegionName(int region)
        {
            return OptionRegionName;
        }

        protected sealed override int ContentItemCount(int region)
        {
            return OptionCount;
        }

        protected sealed override ElementDescription DescribeContentItem(int region, int index)
        {
            return DescribeOption(index);
        }

        protected sealed override void ActivateContentItem(int region, int index)
        {
            ActivateOption(index);
        }

        // ------------------------------------------------------------------
        // Seams the subclass fills.
        // ------------------------------------------------------------------

        /// <summary>The single region's already-localized title (a dialog header or a translated fallback).</summary>
        protected abstract string OptionRegionName { get; }

        /// <summary>Number of pickable rows currently in the list.</summary>
        protected abstract int OptionCount { get; }

        /// <summary>Describes the row at <paramref name="index"/>.</summary>
        protected abstract ElementDescription DescribeOption(int index);

        /// <summary>Runs the picked row's action.</summary>
        protected abstract void ActivateOption(int index);
    }
}
