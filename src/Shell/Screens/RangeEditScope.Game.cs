using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The keyboard focus scope for the hit-points / quality / mental-break-chance range
    /// sub-editor (<see cref="RangeEditMenuState"/>): a screen-reader representation of
    /// vanilla's min/max range slider pair. This scope is a real
    /// <see cref="ScreenScope"/> with one region
    /// holding two Stepper rows, Min and Max — <see cref="ScreenModel"/> owns the cursor (D2: no
    /// <c>selectedOption</c> field survives in the state). The mod owns no window here — the
    /// scope rides the focus stack through <see cref="RangeEditScopeMirror"/> instead of a
    /// WindowStack mirror, the same shape as <see cref="BillsScope"/>/<see cref="BillConfigScope"/>.
    ///
    /// Enter and Escape both simply close — every Left/Right press has already been written
    /// through, so there is no apply and no discard, matching a released vanilla slider.
    /// Left/Right adjust the focused row's value via the
    /// base ScreenScope's generic CanAdjustContentItem/AdjustContentItem path (the old bespoke
    /// rangeEdit.decrease/increase action ids are now orphaned — same chords, ridden by the
    /// shared claim instead, mirroring StorageSettingsScope's own expand/collapse retirement).
    /// Up/Down ride the base's generic region-item navigation (no bespoke Previous/Next claims
    /// needed — RangeEditMenuState.SelectPrevious/SelectNext/ToggleSelection are retired). There
    /// is no typeahead in this menu (no <see cref="ICharSink"/>), matching all four callers' shared
    /// grammar (none of them route text through this sub-screen).
    /// </summary>
    public sealed class RangeEditScope : ScreenScope
    {
        private bool announcedOpen;

        public RangeEditScope()
        {
            Claim(SharedMenuGrammar.Cancel, OnCancel);
        }

        public override string Name
        {
            get { return "range-edit"; }
        }

        /// <summary>Windowless overlay: this scope owns Escape itself unconditionally — there is no typeahead here to gate on.</summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        protected override bool IncludeActionsRegion
        {
            get { return false; }
        }

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        protected override string ContentRegionName(int region)
        {
            return "RimWorldAccess.Shell.RangeEdit.RegionName".Translate();
        }

        protected override int ContentItemCount(int region)
        {
            return 2;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            return new ElementDescription
            {
                Label = RangeEditMenuState.BuildAnnouncement(index),
                Role = ElementRole.Stepper,
            };
        }

        protected override bool CanAdjustContentItem(int region, int index)
        {
            return true;
        }

        /// <summary>Announcement rides RangeEditMenuState's own post-adjust speech, not a second composer-based echo — same pattern as TempControlScope's stepper row.</summary>
        protected override void AdjustContentItem(int region, int index, int direction)
        {
            if (direction > 0)
            {
                RangeEditMenuState.IncreaseValue(index);
            }
            else
            {
                RangeEditMenuState.DecreaseValue(index);
            }
        }

        protected override void ActivateContentItem(int region, int index)
        {
            RangeEditMenuState.Dismiss();
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
            // Always start on Min, matching every legacy Open* call's selectedOption = 0 reset.
            Model.CurrentRegion?.MoveFirst();
            AnnounceCurrentItem();
        }

        private static void OnCancel(KeyEventSnapshot e)
        {
            ShellFrameStamps.MarkCancelConsumed();
            RangeEditMenuState.Dismiss();
        }
    }

    /// <summary>
    /// Keeps <see cref="RangeEditScope"/> in lockstep with <see cref="RangeEditMenuState.IsActive"/>,
    /// reconciled every OnGUI pass AFTER <see cref="BillConfigScopeMirror"/>,
    /// <see cref="ThingFilterMenuScopeMirror"/> and <see cref="StorageSettingsScopeMirror"/>
    /// so it always lands topmost when active — matching the
    /// retired handlers' nested gate, which checked RangeEditMenuState before anything else in
    /// every one of the (now four) callers' branches.
    /// </summary>
    internal static class RangeEditScopeMirror
    {
        private static readonly RangeEditScope scope = new RangeEditScope();

        public static void Reconcile()
        {
            if (RangeEditMenuState.IsActive && !InfoCardState.IsActive)
            {
                FocusStack.Push(scope);
            }
            else
            {
                FocusStack.Pop(scope);
            }
        }
    }
}
