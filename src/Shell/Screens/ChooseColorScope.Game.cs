using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The keyboard focus scope for vanilla's <see cref="Dialog_ChooseColor"/> (the mech
    /// accent-colour picker off <c>MainTabWindow_Mechs</c>). One content region over the
    /// dialog's own <c>colors</c> palette, named through <see cref="ColorNameHelper"/>
    /// (disambiguating same-named shades) — the shape <see cref="PromptEnhanceColorPickerScope"/>
    /// established. Activation writes the dialog's own <c>selectedColor</c> field, mirroring
    /// <c>Widgets.ColorSelector</c>'s <c>ref</c> write exactly; the real game change happens only
    /// when the player reaches vanilla's own OK button, captured (not declared) so Enter injects
    /// the real click.
    /// </summary>
    public sealed class ChooseColorScope : ScreenScope
    {
        private static readonly FieldInfo HeaderField = AccessTools.Field(typeof(Dialog_ChooseColor), "header");
        private static readonly FieldInfo SelectedColorField = AccessTools.Field(typeof(Dialog_ChooseColor), "selectedColor");
        private static readonly FieldInfo ColorsField = AccessTools.Field(typeof(Dialog_ChooseColor), "colors");

        private readonly Dialog_ChooseColor dialog;
        private readonly List<Color> colors;
        private readonly List<string> names;

        public ChooseColorScope(Dialog_ChooseColor dialog)
        {
            this.dialog = dialog;
            colors = ColorsField?.GetValue(dialog) as List<Color> ?? new List<Color>();
            names = ColorNameHelper.NamesForColors(colors);
        }

        public override string Name
        {
            get { return "choose-color"; }
        }

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        protected override string ContentRegionName(int region)
        {
            string header = HeaderField?.GetValue(dialog) as string;
            if (!header.NullOrEmpty())
            {
                return header;
            }
            return "RimWorldAccess.Shell.ChooseColor.RegionName".Translate();
        }

        protected override int ContentItemCount(int region)
        {
            return colors.Count;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            Color current = SelectedColorField != null ? (Color)SelectedColorField.GetValue(dialog) : default;
            return new ElementDescription
            {
                Role = ElementRole.RadioButton,
                Label = names[index],
                Selected = colors[index].IndistinguishableFrom(current),
            };
        }

        protected override void ActivateContentItem(int region, int index)
        {
            if (SelectedColorField == null || index < 0 || index >= colors.Count)
            {
                return;
            }
            // MUTATION-C: mirrors Widgets.ColorSelector's `ref selectedColor` write; the widget
            // cannot be invoked without a mouse click.
            SelectedColorField.SetValue(dialog, colors[index]);
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            AnnounceCurrentItem();
        }

        protected override bool IncludeCapturedExtrasRegion
        {
            get { return true; }
        }

        // ------------------------------------------------------------------
        // Focus ring and scroll-follow. Vanilla outlines the SELECTED swatch
        // only, and this dialog does not select on settle, so the focused
        // swatch is ringed from the rects ColorSelectorDrawPatch records off
        // vanilla's own draw pass.
        // ------------------------------------------------------------------

        private static readonly AccessTools.FieldRef<Dialog_ChooseColor, Vector2> ScrollRef =
            AccessTools.FieldRefAccess<Dialog_ChooseColor, Vector2>("scrollPosition");

        public override void OnPush()
        {
            base.OnPush();
            ColorSelectorDrawPatch.AddInterest();
        }

        public override void OnPop()
        {
            ColorSelectorDrawPatch.RemoveInterest();
            base.OnPop();
        }

        protected internal override Rect FocusedContentRect()
        {
            if (Model.RegionIndex != 0)
            {
                return default(Rect);
            }
            ListModel list = Model.CurrentRegion;
            if (list == null || list.IsEmpty)
            {
                return default(Rect);
            }
            Rect screen, local, groupRect;
            if (!ColorSelectorDrawPatch.TryGetSoleSwatch(colors.Count, list.Index, out screen, out local, out groupRect))
            {
                return default(Rect);
            }
            return screen;
        }

        /// <summary>
        /// Scrolls the focused swatch into the palette's visible band, one corrective write per
        /// settle. Mirrors Dialog_ChooseColor.DoWindowContents (decompiled :44-53): the rect the
        /// dialog hands ColorSelector is the scroll view's OUT rect, and its view rect shares
        /// that origin, so a swatch's group-local top is also its offset down the scrolled
        /// content and the visible band is the out rect's own height. Skipped when no pass has
        /// recorded the palette yet, which the next settle heals.
        /// </summary>
        protected override void OnCursorSettled(int region, int index)
        {
            if (region != 0 || ScrollRef == null)
            {
                return;
            }
            Rect screen, local, groupRect;
            if (!ColorSelectorDrawPatch.TryGetSoleSwatch(colors.Count, index, out screen, out local, out groupRect))
            {
                return;
            }
            ref Vector2 scroll = ref ScrollRef(dialog);
            float y = Mathf.Max(0f, Mathf.Clamp(scroll.y, local.yMax - groupRect.height, local.yMin));
            if (y != scroll.y)
            {
                scroll.y = y;
            }
        }
    }
}
