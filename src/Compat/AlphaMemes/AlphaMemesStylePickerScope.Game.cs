using System.Collections.Generic;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>Which of Alpha Memes' four style grids a <see cref="AlphaMemesStylePickerScope"/> is driving.</summary>
    internal enum AlphaMemesStyleVariant
    {
        Single,
        Area,
        SwapSource,
        SwapTarget,
    }

    /// <summary>
    /// The keyboard focus scope for Alpha Memes' <c>Dialog_ChangeStyles</c>,
    /// <c>Dialog_ChangeStyles_Area</c>, <c>Dialog_ChangeStyles_Swap</c> and
    /// <c>Dialog_ChangeStyles_Swap_Second</c> -- the icon grids its style-change ability opens.
    /// The four are one grid with four click bodies, so they share one scope behind
    /// <see cref="AlphaMemesStyleVariant"/>; all reflection lives in
    /// <see cref="AlphaMemesStyleCompat"/>.
    ///
    /// Every tile is a raw <c>GUI.DrawTexture</c> plus a captionless
    /// <c>Widgets.ButtonInvisible</c> plus a trailing <c>TooltipHandler.TipRegion</c>. The
    /// capture engine drops that shape as an orphan, and the one path that names a hotspot from
    /// its tooltip needs the tooltip laid down FIRST, so the generic reader finds nothing here.
    /// This mirrors <see cref="StyleSelectionScope"/> instead, vanilla's own icon-only style
    /// picker: one content region, one row per style plus the leading "no style" tile, and the
    /// click bodies re-authored as MUTATION-C because they are inline IMGUI with no delegate to
    /// invoke and the mod itself assigns <c>Thing.StyleDef</c> with no gate of its own.
    ///
    /// <b>Escape.</b> These windows do not absorb input, but their <c>closeOnClickedOutside</c>
    /// keeps <see cref="ShellGuards.MenuOwnsInput"/> true, so an unclaimed Escape is swallowed
    /// before vanilla's <c>closeOnCancel</c> chain ever runs and the player is stranded. The
    /// cancel claim closes the dialog through its own vanilla <c>Close</c>, the non-absorbing
    /// branch of <see cref="GenericWindowScope.OnCancelClaim"/>: routing through
    /// <c>Notify_PressedCancel</c> instead would cancel whatever <c>closeOnCancel</c> window is
    /// topmost, which for a coexisting window need not be this one.
    /// <see cref="OwnsCancel"/> stays at its inherited default.
    ///
    /// <b>Enter.</b> These dialogs inherit <c>closeOnAccept</c>, so vanilla's deferred Accept
    /// re-test would close the window behind this scope's back after activation already ran.
    /// <see cref="ScreenScope"/> already covers that: its <see cref="OwnsAccept"/> is true and
    /// its activation path stamps <c>ShellFrameStamps.MarkAcceptConsumed</c> before reaching
    /// <see cref="ActivateContentItem"/>, so nothing extra is needed here.
    /// </summary>
    internal sealed class AlphaMemesStylePickerScope : ScreenScope
    {
        private readonly Window dialog;
        private readonly AlphaMemesStyleVariant variant;

        public AlphaMemesStylePickerScope(Window dialog, AlphaMemesStyleVariant variant)
        {
            this.dialog = dialog;
            this.variant = variant;
            Claim(SharedMenuGrammar.Cancel, OnCancel);
        }

        public override string Name
        {
            get { return "alpha-memes-style-picker"; }
        }

        /// <summary>The single region names itself on arrival; the entry item follows from the base.</summary>
        protected override string ComposeOpenAnnouncement()
        {
            return ContentRegionName(0);
        }

        private void OnCancel(KeyEventSnapshot e)
        {
            // Coexisting-window branch, same as GenericWindowScope's: close the window through
            // its own vanilla Close so the remove hook pops this scope and focus returns
            // beneath, exactly like any other window close.
            ShellFrameStamps.MarkCancelConsumed();
            dialog.Close();
        }

        // ------------------------------------------------------------------
        // Rows: the leading "no style" tile, then the dialog's own style list.
        // ------------------------------------------------------------------

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        protected override string ContentRegionName(int region)
        {
            switch (variant)
            {
                case AlphaMemesStyleVariant.Area:
                    return CompatText.ModText("AM_ChooseStyle_Multiple");
                case AlphaMemesStyleVariant.SwapSource:
                    return CompatText.ModText("AM_ChooseStyle_Swap_First");
                case AlphaMemesStyleVariant.SwapTarget:
                    return CompatText.ModText("AM_ChooseStyle_Swap_Second");
                default:
                    return CompatText.ModText("AM_ChooseStyle");
            }
        }

        protected override int ContentItemCount(int region)
        {
            // An empty style list is the dialog's AM_NoStyles state: it draws no grid at all,
            // not even the "no style" tile, so the region is genuinely empty.
            List<StyleCategoryDef> styles = Styles();
            return styles.Count == 0 ? 0 : styles.Count + 1;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            ElementDescription d = new ElementDescription();
            d.Role = ElementRole.Button;
            if (index == 0)
            {
                d.Label = CompatText.ModText("AM_DefaultStyle");
                return d;
            }
            List<StyleCategoryDef> styles = Styles();
            if (index - 1 >= styles.Count)
            {
                return d;
            }
            StyleCategoryDef style = styles[index - 1];
            d.Label = style.LabelCap;
            d.Extras = CompatText.Flatten(style.description);
            return d;
        }

        /// <summary>
        /// The tiles the dialog actually draws. The single-thing dialog re-tests every category
        /// for a style matching its target inside the draw loop and skips the ones with none, so
        /// the keyboard must skip them too or it would offer tiles that are not on screen.
        /// </summary>
        private List<StyleCategoryDef> Styles()
        {
            List<StyleCategoryDef> styles = AlphaMemesStyleCompat.Styles(dialog);
            if (variant != AlphaMemesStyleVariant.Single)
            {
                return styles;
            }
            Thing thing = AlphaMemesStyleCompat.SingleThing(dialog);
            if (thing == null)
            {
                return styles;
            }
            List<StyleCategoryDef> drawn = new List<StyleCategoryDef>();
            for (int i = 0; i < styles.Count; i++)
            {
                if (styles[i].GetStyleForThingDef(thing.def, null) != null)
                {
                    drawn.Add(styles[i]);
                }
            }
            return drawn;
        }

        // ------------------------------------------------------------------
        // Activation
        // ------------------------------------------------------------------

        protected override void ActivateContentItem(int region, int index)
        {
            List<StyleCategoryDef> styles = Styles();
            if (index < 0 || index > styles.Count)
            {
                return;
            }
            StyleCategoryDef chosen = index == 0 ? null : styles[index - 1];

            switch (variant)
            {
                case AlphaMemesStyleVariant.Single:
                    ApplyToSingle(chosen);
                    break;
                case AlphaMemesStyleVariant.Area:
                    ApplyToArea(chosen);
                    break;
                case AlphaMemesStyleVariant.SwapSource:
                    OpenSwapSecond(chosen);
                    return;
                case AlphaMemesStyleVariant.SwapTarget:
                    ApplyToSwapTarget(chosen);
                    break;
            }
            dialog.Close();
        }

        // MUTATION-C: mirrors AlphaMemes.Dialog_ChangeStyles.DoWindowContents' tile
        // ButtonInvisible branch. The branch is inline IMGUI with no delegate to invoke, and the
        // mod assigns Thing.StyleDef raw -- it ships no Can*/Try* twin to ride.
        private void ApplyToSingle(StyleCategoryDef chosen)
        {
            Thing thing = AlphaMemesStyleCompat.SingleThing(dialog);
            if (thing == null)
            {
                return;
            }
            thing.StyleDef = chosen == null ? null : chosen.GetStyleForThingDef(thing.def, null);
            thing.DirtyMapMesh(thing.Map);
        }

        // MUTATION-C: mirrors AlphaMemes.Dialog_ChangeStyles_Area.DoWindowContents' tile
        // ButtonInvisible branch. Same reasoning as ApplyToSingle: inline IMGUI, ungated
        // Thing.StyleDef assignment, no vanilla vehicle to invoke.
        private void ApplyToArea(StyleCategoryDef chosen)
        {
            foreach (Thing thing in AlphaMemesStyleCompat.Things(dialog))
            {
                thing.StyleDef = chosen == null ? null : chosen.GetStyleForThingDef(thing.def, null);
                thing.DirtyMapMesh(thing.Map);
            }
        }

        // MUTATION-C: mirrors AlphaMemes.Dialog_ChangeStyles_Swap_Second.DoWindowContents' tile
        // ButtonInvisible branch, including its defName-string comparison against the source
        // style -- the mod's own semantics decide which things a swap touches, so a reference
        // comparison here would restyle a different set. Inline IMGUI, ungated assignment, no
        // vanilla vehicle to invoke.
        private void ApplyToSwapTarget(StyleCategoryDef chosen)
        {
            StyleCategoryDef source = AlphaMemesStyleCompat.SourceStyle(dialog);
            foreach (Thing thing in AlphaMemesStyleCompat.Things(dialog))
            {
                if (thing.StyleDef != null || source != null)
                {
                    if (thing.StyleDef == null)
                    {
                        continue;
                    }
                    ThingStyleDef equivalent = source == null ? null : source.GetStyleForThingDef(thing.def, null);
                    if (thing.StyleDef.defName != (equivalent == null ? null : equivalent.defName))
                    {
                        continue;
                    }
                }
                thing.StyleDef = chosen == null ? null : chosen.GetStyleForThingDef(thing.def, null);
                thing.DirtyMapMesh(thing.Map);
            }
        }

        /// <summary>
        /// The swap's first step mutates nothing: it records the source style by opening the
        /// second window with it. That successor is registered too, so its own scope attaches on
        /// arrival and the flow continues without special handling here.
        /// </summary>
        private void OpenSwapSecond(StyleCategoryDef chosen)
        {
            Window second = AlphaMemesStyleCompat.CreateSwapSecond(dialog, chosen);
            if (second == null)
            {
                return;
            }
            Find.WindowStack.Add(second);
            dialog.Close();
        }
    }
}
