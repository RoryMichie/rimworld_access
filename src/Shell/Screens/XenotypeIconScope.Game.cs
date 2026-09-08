using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Focus scope for <see cref="Dialog_SelectXenotypeIcon"/>, whose icon grid is unlabelled
    /// <c>Widgets.ButtonImage</c> tiles (<see cref="XenotypeIconDef"/> has only <c>texPath</c>).
    /// One content region: one <see cref="ElementRole.RadioButton"/> row per
    /// <see cref="DefDatabase{XenotypeIconDef}"/> def in vanilla's own iteration order, labelled
    /// <c>def.label</c> with a defName fallback (no stock def sets a label).
    /// Enter writes the dialog's private <c>selected</c> field (MUTATION-C: see
    /// <see cref="CommitSelection"/>).
    /// Escape means accept-current, exactly as in vanilla: closing by any route fires
    /// <c>PreClose</c>'s <c>iconSelector(selected)</c>, so there is no cancel-without-saving
    /// outcome to preserve and the base <see cref="ScreenScope.OwnsCancel"/> default stands.
    /// The Accept button is a real <c>Widgets.ButtonText</c> and reaches the Buttons region
    /// under the default <see cref="CaptureWindowButtons"/>.
    /// Reachability: vanilla opens this dialog from an unlabelled ButtonImage inside
    /// <c>GeneCreationDialogBase.DrawIconSelector</c>, so <see cref="XenotypeEditorState.OpenIconSelector"/>
    /// and <see cref="XenogermState.OpenIconSelector"/> add the missing button rows.
    /// </summary>
    public sealed class XenotypeIconScope : ScreenScope
    {
        private const int IconsRegion = 0;

        private static readonly FieldInfo SelectedField =
            AccessTools.Field(typeof(Dialog_SelectXenotypeIcon), "selected");
        private static readonly AccessTools.FieldRef<Dialog_SelectXenotypeIcon, Vector2> ScrollPositionField =
            AccessTools.FieldRefAccess<Dialog_SelectXenotypeIcon, Vector2>("scrollPosition");
        private static readonly Func<Window, float> MarginOf =
            AccessTools.MethodDelegate<Func<Window, float>>(AccessTools.PropertyGetter(typeof(Window), "Margin"));

        private readonly Dialog_SelectXenotypeIcon dialog;
        private readonly List<XenotypeIconDef> icons = new List<XenotypeIconDef>();
        private bool announcedOpen;

        public XenotypeIconScope(Dialog_SelectXenotypeIcon dialog)
        {
            this.dialog = dialog;
        }

        public override string Name
        {
            get { return "xenotype-icon"; }
        }

        /// <summary>Icon names (defName fallback included) are worth searching.</summary>
        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        protected override string ContentRegionName(int region)
        {
            return "RimWorldAccess.Biotech.XenotypeIcon.IconsRegion".Translate();
        }

        protected override void RefreshContent()
        {
            icons.Clear();
            icons.AddRange(DefDatabase<XenotypeIconDef>.AllDefs);
        }

        protected override int ContentItemCount(int region)
        {
            return icons.Count;
        }

        private XenotypeIconDef IconAt(int index)
        {
            return index >= 0 && index < icons.Count ? icons[index] : null;
        }

        private XenotypeIconDef CurrentSelected()
        {
            return SelectedField?.GetValue(dialog) as XenotypeIconDef;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            XenotypeIconDef def = IconAt(index);
            if (def == null)
            {
                return d;
            }
            // No stock XenotypeIconDef sets a label; defName is the fallback.
            d.Label = def.label.NullOrEmpty() ? def.defName : def.LabelCap.ToString();
            d.Role = ElementRole.RadioButton;
            d.Selected = def == CurrentSelected();
            return d;
        }

        protected override void ActivateContentItem(int region, int index)
        {
            XenotypeIconDef def = IconAt(index);
            if (def == null)
            {
                return;
            }
            CommitSelection(def);
            RefreshModel();
            AnnounceCurrentItem();
        }

        /// <summary>
        /// Radio-group contract: arrowing onto an icon selects it silently through the same field
        /// write Enter uses; the landing announcement carries "selected".
        /// </summary>
        protected override void OnCursorSettled(int region, int index)
        {
            if (region != IconsRegion)
            {
                // The Buttons region's own row indices would otherwise read as icon indices.
                return;
            }
            XenotypeIconDef def = IconAt(index);
            if (def == null)
            {
                return;
            }
            if (def != CurrentSelected())
            {
                CommitSelection(def);
            }
            FollowIconIntoView(index);
        }

        /// <summary>
        /// One corrective scroll.y write per settle, keeping the focused tile inside the dialog's
        /// own scroll view for modded def lists long enough to scroll.
        /// </summary>
        private void FollowIconIntoView(int index)
        {
            Rect outRect = OutRect();
            float rect3Width = outRect.width - 16f;
            float rel = RelativeTileTop(index, rect3Width);
            Vector2 scroll = ScrollPositionField(dialog);
            scroll.y = Mathf.Clamp(scroll.y, rel + 35f - outRect.height, rel);
            ScrollPositionField(dialog) = scroll;
        }

        /// <summary>
        /// Mirrors DoWindowContents' tile-layout loop up to <paramref name="focusedIndex"/>,
        /// tracking the y accumulator relative to outRect.y. The wrap condition carries the +6f
        /// slack vanilla checks BEFORE drawing each tile, so a per-row division would be wrong.
        /// </summary>
        private static float RelativeTileTop(int focusedIndex, float rect3Width)
        {
            float num = 6f;
            float num2 = 6f;
            for (int i = 0; i <= focusedIndex; i++)
            {
                if (num + 35f + 6f > rect3Width)
                {
                    num = 6f;
                    num2 += 41f;
                }
                if (i == focusedIndex)
                {
                    return num2;
                }
                num += 41f;
            }
            return num2;
        }

        /// <summary>
        /// The scrollable outRect DoWindowContents computes: the content rect with the header and
        /// close-button margins removed. No optionalTitle offset -- the dialog sets none.
        /// </summary>
        private Rect OutRect()
        {
            float margin = MarginOf(dialog);
            Rect rect2 = new Rect(0f, 0f, dialog.windowRect.width, dialog.windowRect.height).ContractedBy(margin);
            rect2.yMin += 39f;
            rect2.yMax -= Window.CloseButSize.y + 4f;
            Rect outRect = rect2;
            outRect.yMax -= 4f;
            return outRect;
        }

        /// <summary>The dialog's one selection write, shared by Enter and the cursor-landing auto-select.</summary>
        private void CommitSelection(XenotypeIconDef def)
        {
            // MUTATION-C: mirrors Dialog_SelectXenotypeIcon.DoWindowContents' own ButtonImage
            // branch (`if (Widgets.ButtonImage(rect4, allDef.Icon, XenotypeDef.IconColor)) {
            // selected = allDef; }`) -- an inline field assignment vanilla itself performs bare,
            // with no method to call instead.
            SelectedField?.SetValue(dialog, def);
        }

        public override void OnPush()
        {
            base.OnPush();
            announcedOpen = false;
        }

        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen)
            {
                return;
            }
            announcedOpen = true;
            // Vanilla's own dialog header text -- no new key.
            TolkHelper.SpeakData("SelectIcon".Translate().ToString());
            AnnounceCurrentItem();
        }
    }
}
