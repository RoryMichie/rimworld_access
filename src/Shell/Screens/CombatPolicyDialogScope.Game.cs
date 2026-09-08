using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The <see cref="PolicyDialogScope"/> for <see cref="Dialog_ManageCombatPolicies"/>: one
    /// contents region of checkbox rows. The dialog is our own, so state is read directly
    /// and row rects come from its draw recording rather than a Harmony patch.
    /// </summary>
    public sealed class CombatPolicyDialogScope : PolicyDialogScope
    {
        private const int TogglesRegion = FirstContentsRegion;

        private CombatPolicy policy;

        public CombatPolicyDialogScope(Window dialog) : base(dialog)
        {
        }

        protected override int ContentsRegionCount
        {
            get { return 1; }
        }

        protected override string ContentsRegionName(int region)
        {
            return "RimWorldAccess.Autopilot.PolicyTitle".Translate().ToString();
        }

        protected override string TreeRegionLabel
        {
            get { return ContentsRegionName(TogglesRegion); }
        }

        protected override void RebuildContents()
        {
            policy = Selected as CombatPolicy;
        }

        protected override int ContentItemCount(int region)
        {
            if (region != TogglesRegion)
            {
                return base.ContentItemCount(region);
            }
            return policy == null ? 0 : CombatPolicyToggles.KeyParts.Length;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            if (region != TogglesRegion)
            {
                return base.DescribeContentItem(region, index);
            }
            var d = new ElementDescription();
            if (policy == null || index < 0 || index >= CombatPolicyToggles.KeyParts.Length)
            {
                return d;
            }
            string keyPart = CombatPolicyToggles.KeyParts[index];
            d.Label = ("RimWorldAccess.Autopilot." + keyPart + ".Label").Translate();
            d.Extras = ("RimWorldAccess.Autopilot." + keyPart + ".Desc").Translate();
            d.Role = ElementRole.Checkbox;
            d.Check = CombatPolicyToggles.Get(policy, index) ? CheckState.Checked : CheckState.Unchecked;
            return d;
        }

        protected override void ActivateContentItem(int region, int index)
        {
            if (region != TogglesRegion)
            {
                base.ActivateContentItem(region, index);
                return;
            }
            if (policy == null || index < 0 || index >= CombatPolicyToggles.KeyParts.Length)
            {
                return;
            }
            bool value = !CombatPolicyToggles.Get(policy, index);
            CombatPolicyToggles.Set(policy, index, value);
            (value ? SoundDefOf.Checkbox_TurnedOn : SoundDefOf.Checkbox_TurnedOff).PlayOneShotOnCamera();
            AnnounceCurrentItem();
        }

        protected internal override Rect FocusedContentRect()
        {
            if (Model.RegionIndex != TogglesRegion || !(dialog is Dialog_ManageCombatPolicies own))
            {
                return base.FocusedContentRect();
            }
            ListModel rows = Model.Region(TogglesRegion);
            Rect rect = own.RowScreenRect(rows != null ? rows.Index : -1);
            return rect.width > 0f ? rect : base.FocusedContentRect();
        }
    }
}
