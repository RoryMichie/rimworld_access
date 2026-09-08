using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Keyboard focus scope for vanilla's <see cref="Dialog_ManagePolicies{T}"/> — apparel, food,
    /// drug and reading policies plus any modded closed generic, covered by one registration on
    /// the open generic.
    ///
    /// One window, one scope. Vanilla draws the policy list and the selected policy's contents
    /// editor side by side, so both are REGIONS of one screen: region 0 is the list, regions 1..
    /// the contents, and the automatic Buttons region the header controls. A second scope layered
    /// on top would claim Escape without owning it, letting vanilla's GUI pass close the window
    /// first and orphan the overlay over a dead window; <see cref="ClaimsFilterTreeCancel"/> is
    /// false here so Escape resolves to nobody and vanilla closes its own window.
    ///
    /// Subclasses mirror vanilla's own <c>DoContentsRect</c> polymorphism:
    /// <see cref="FilterPolicyDialogScope"/>, <see cref="DrugPolicyDialogScope"/> and
    /// <see cref="PlainPolicyDialogScope"/>. All three report the same <see cref="Name"/>, so one
    /// screen id covers them.
    ///
    /// Non-public members are reached through AccessTools per CLOSED generic and cached by
    /// concrete type: one accessor cannot span two closed forms, which are different CLR types.
    /// </summary>
    public abstract class PolicyDialogScope : FilterTreeScopeBase
    {
        protected const int PoliciesRegion = 0;

        /// <summary>The first contents region — everything after the policy list.</summary>
        protected const int FirstContentsRegion = 1;

        /// <summary>Per-closed-generic reflection surface — see the class remarks.</summary>
        private sealed class Accessors
        {
            public MethodInfo GetSelectedPolicy;
            public MethodInfo SetSelectedPolicy;
            public MethodInfo GetPolicies;
            public MethodInfo GetDefaultPolicy;
            public MethodInfo SetDefaultPolicy;
            public MethodInfo CreateNewPolicy;
            public MethodInfo TryDeletePolicy;
        }

        private static readonly Dictionary<Type, Accessors> accessorCache = new Dictionary<Type, Accessors>();

        private static Accessors ResolveAccessors(Type dialogType)
        {
            Accessors a;
            if (accessorCache.TryGetValue(dialogType, out a))
            {
                return a;
            }
            PropertyInfo selected = AccessTools.Property(dialogType, "SelectedPolicy");
            a = new Accessors
            {
                GetSelectedPolicy = selected?.GetGetMethod(true),
                SetSelectedPolicy = selected?.GetSetMethod(true),
                GetPolicies = AccessTools.Method(dialogType, "GetPolicies"),
                GetDefaultPolicy = AccessTools.Method(dialogType, "GetDefaultPolicy"),
                SetDefaultPolicy = AccessTools.Method(dialogType, "SetDefaultPolicy"),
                CreateNewPolicy = AccessTools.Method(dialogType, "CreateNewPolicy"),
                TryDeletePolicy = AccessTools.Method(dialogType, "TryDeletePolicy"),
            };
            accessorCache[dialogType] = a;
            return a;
        }

        protected readonly Window dialog;
        private readonly Accessors accessors;
        private readonly List<Policy> policies = new List<Policy>();
        private readonly List<ScreenAction> actions = new List<ScreenAction>();
        private bool announcedOpen;

        protected PolicyDialogScope(Window dialog)
        {
            this.dialog = dialog;
            accessors = ResolveAccessors(dialog.GetType());
        }

        public override string Name
        {
            get { return "policy-dialog"; }
        }

        /// <summary>Policy names are worth searching, and vanilla offers its own QuickSearchWidget here.</summary>
        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        /// <summary>Escape belongs to the window — see the class remarks.</summary>
        protected override bool ClaimsFilterTreeCancel
        {
            get { return false; }
        }

        /// <summary>
        /// A contents region mixes always-flat prefix rows with tree rows, so sibling-scoped
        /// counts would change partway down one region. Level still carries depth.
        /// </summary>
        protected override bool UseFlatRegionPositions
        {
            get { return true; }
        }

        // ------------------------------------------------------------------
        // The contents contract a subclass fills in.
        // ------------------------------------------------------------------

        /// <summary>Number of regions the selected policy's contents editor occupies (0 for a modded policy we cannot read).</summary>
        protected abstract int ContentsRegionCount { get; }

        /// <summary>Localized name of one contents region, by ABSOLUTE region index.</summary>
        protected abstract string ContentsRegionName(int region);

        /// <summary>
        /// Rebuilds the contents regions against the selected policy, on open and on every
        /// selection change. Must be SILENT — the row landing carries the announcement — and must
        /// tolerate a null selection, where the regions stand empty but stay Tab-reachable.
        /// </summary>
        protected abstract void RebuildContents();

        protected override int TreeRegionIndex
        {
            get { return FirstContentsRegion; }
        }

        /// <summary>
        /// No region is a filter tree by default: the policy list never is, and neither is a
        /// contents editor that is not a ThingFilterUI panel (the drug table).
        /// <see cref="FilterPolicyDialogScope"/> is the one subclass that maps panels here.
        /// </summary>
        protected override TreePanel PanelFor(int region)
        {
            return null;
        }

        /// <summary>Clear all / Allow all / the range sliders belong to a filter tree region only.</summary>
        protected override int PrefixRowCountFor(int region)
        {
            return PanelFor(region) != null ? base.PrefixRowCountFor(region) : 0;
        }

        // ------------------------------------------------------------------
        // Vanilla accessors.
        // ------------------------------------------------------------------

        /// <summary>
        /// Vanilla's own SelectedPolicy, invoked as the property accessors it is. The setter is a
        /// gated vanilla method rather than a raw field write: it runs <c>ValidateName()</c>
        /// first, which rewrites an empty label. Reflection is unavoidable — the property is
        /// protected on an open generic whose closed forms are distinct CLR types.
        /// </summary>
        protected Policy Selected
        {
            get { return accessors.GetSelectedPolicy?.Invoke(dialog, null) as Policy; }
            set { accessors.SetSelectedPolicy?.Invoke(dialog, new object[] { value }); }
        }

        private Policy DefaultPolicy
        {
            get { return accessors.GetDefaultPolicy?.Invoke(dialog, null) as Policy; }
        }

        // ------------------------------------------------------------------
        // ScreenScope content contract: region 0 here, the rest to the tree base.
        // ------------------------------------------------------------------

        protected override int ContentRegionCount
        {
            get { return 1 + ContentsRegionCount; }
        }

        protected override string ContentRegionName(int region)
        {
            return region == PoliciesRegion
                ? "AvailablePolicies".Translate().ToString()
                : ContentsRegionName(region);
        }

        /// <summary>
        /// Vanilla's own list in vanilla's own order: default first, then by label. Vanilla's
        /// search filter is NOT applied — typeahead is the search surface here, and hiding rows
        /// would break the position counts a screen reader navigates by.
        /// </summary>
        protected override void RefreshContent()
        {
            base.RefreshContent();
            policies.Clear();
            var raw = accessors.GetPolicies?.Invoke(dialog, null) as IEnumerable;
            if (raw == null)
            {
                return;
            }
            foreach (object item in raw)
            {
                var policy = item as Policy;
                if (policy != null)
                {
                    policies.Add(policy);
                }
            }
            Policy defaultPolicy = DefaultPolicy;
            policies.Sort(delegate(Policy a, Policy b)
            {
                bool aDefault = a == defaultPolicy;
                bool bDefault = b == defaultPolicy;
                if (aDefault != bDefault)
                {
                    return aDefault ? -1 : 1;
                }
                return string.Compare(a.label, b.label, StringComparison.CurrentCulture);
            });
        }

        protected override int ContentItemCount(int region)
        {
            return region == PoliciesRegion ? policies.Count : base.ContentItemCount(region);
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            if (region != PoliciesRegion)
            {
                return base.DescribeContentItem(region, index);
            }
            var d = new ElementDescription();
            if (index < 0 || index >= policies.Count)
            {
                return d;
            }
            Policy policy = policies[index];
            d.Label = policy.label;
            d.Role = ElementRole.RadioButton;
            d.Selected = policy == Selected;
            if (policy == DefaultPolicy)
            {
                // The spoken form of vanilla's gray default asterisk, read from the same
                // GetDefaultPolicy vanilla draws it from.
                d.Extras = "default".Translate().ToString();
            }
            return d;
        }

        /// <summary>
        /// The radio contract: arriving on a row selects it, as vanilla's ButtonInvisible does on
        /// click, so the contents regions always match the cursor. Idempotent and silent — the
        /// landing announcement carries the new state.
        /// </summary>
        protected override void OnCursorSettled(int region, int index)
        {
            if (region != PoliciesRegion || index < 0 || index >= policies.Count)
            {
                return;
            }
            if (Selected != policies[index])
            {
                Selected = policies[index];
                RebuildContents();
            }
        }

        /// <summary>Enter/Space on a policy row selects it; the contents regions are already in step via <see cref="OnCursorSettled"/>.</summary>
        protected override void ActivateContentItem(int region, int index)
        {
            if (region != PoliciesRegion)
            {
                base.ActivateContentItem(region, index);
                return;
            }
            if (index < 0 || index >= policies.Count)
            {
                return;
            }
            if (Selected != policies[index])
            {
                Selected = policies[index];
                RebuildContents();
            }
            RefreshModel();
            AnnounceCurrentItem();
        }

        protected override bool CanAdjustContentItem(int region, int index)
        {
            return region != PoliciesRegion && base.CanAdjustContentItem(region, index);
        }

        protected override void AdjustContentItem(int region, int index, int direction)
        {
            if (region != PoliciesRegion)
            {
                base.AdjustContentItem(region, index, direction);
            }
        }

        // ------------------------------------------------------------------
        // Buttons — vanilla's header controls, each on vanilla's own line.
        // ------------------------------------------------------------------

        protected override bool CaptureWindowButtons
        {
            get { return false; }
        }

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                // "New..." is a real vanilla button label; the other four are icon-only, so their
                // tooltip is the only text vanilla has and becomes the label.
                actions.Add(new ScreenAction("NewPolicy".Translate().ToString(), NewPolicy));

                Policy selected = Selected;
                if (selected != null)
                {
                    actions.Add(new ScreenAction("RenamePolicyTip".Translate().ToString(),
                        delegate { Find.WindowStack.Add(new Dialog_RenamePolicy(selected)); }));
                    actions.Add(new ScreenAction("DuplicatePolicyTip".Translate().ToString(), DuplicatePolicy));
                    actions.Add(new ScreenAction("DeletePolicyTip".Translate().ToString(), DeleteSelectedPolicy));

                    bool isDefault = selected == DefaultPolicy;
                    actions.Add(new ScreenAction(
                        "DefaultPolicyTip".Translate().ToString(),
                        delegate { accessors.SetDefaultPolicy?.Invoke(dialog, new object[] { selected }); RefreshModel(); },
                        disabled: isDefault,
                        disabledReason: isDefault ? "default".Translate().ToString() : null));
                }

                actions.Add(new ScreenAction(
                    "CloseButton".Translate().ToString(),
                    delegate { dialog.Close(); },
                    SharedMenuGrammar.Cancel));
                return actions;
            }
        }

        private void NewPolicy()
        {
            var created = accessors.CreateNewPolicy?.Invoke(dialog, null) as Policy;
            if (created == null)
            {
                return;
            }
            Selected = created;
            RebuildContents();
            RefreshModel();
            MoveCursorToSelected();
        }

        private void DuplicatePolicy()
        {
            Policy source = Selected;
            var created = accessors.CreateNewPolicy?.Invoke(dialog, null) as Policy;
            if (created == null || source == null)
            {
                return;
            }
            created.CopyFrom(source);
            Selected = created;
            RebuildContents();
            RefreshModel();
            MoveCursorToSelected();
        }

        private void DeleteSelectedPolicy()
        {
            Policy selected = Selected;
            if (selected == null)
            {
                return;
            }
            AssignMenuHelper.DeletePolicyWithConfirm(
                selected,
                delegate(Policy p) { return (AcceptanceReport)accessors.TryDeletePolicy.Invoke(dialog, new object[] { p }); },
                delegate
                {
                    // Vanilla clears the selection after a successful delete.
                    Selected = null;
                    RebuildContents();
                    RefreshModel();
                });
        }

        private void MoveCursorToSelected()
        {
            Policy selected = Selected;
            if (selected == null)
            {
                return;
            }
            int index = policies.IndexOf(selected);
            if (index >= 0)
            {
                Model.Region(PoliciesRegion)?.MoveTo(index);
            }
        }

        // ------------------------------------------------------------------
        // Lifecycle.
        // ------------------------------------------------------------------

        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen)
            {
                return;
            }
            announcedOpen = true;
            RebuildContents();
            RefreshModel();
            MoveCursorToSelected();
            AnnounceRegion();
        }
    }

    /// <summary>
    /// A modded <c>Dialog_ManagePolicies&lt;T&gt;</c> whose <c>DoContentsRect</c> cannot be read:
    /// the policy list and header buttons are fully navigable, and the contents pane has no
    /// keyboard surface.
    /// </summary>
    public sealed class PlainPolicyDialogScope : PolicyDialogScope
    {
        public PlainPolicyDialogScope(Window dialog) : base(dialog)
        {
        }

        protected override int ContentsRegionCount
        {
            get { return 0; }
        }

        protected override string ContentsRegionName(int region)
        {
            return "";
        }

        protected override string TreeRegionLabel
        {
            get { return ""; }
        }

        protected override void RebuildContents()
        {
        }
    }
}
