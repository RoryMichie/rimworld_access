using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Keyboard scope for the real <see cref="Dialog_GrowthMomentChoices"/> window, Biotech's
    /// child-development passion and trait picker.
    ///
    /// Its six flat-list tabs are six content regions, cycled by Tab/Shift+Tab through the base's own
    /// claims, and each region's name carries vanilla's tab label plus the "choose n of m" line.
    /// <see cref="GrowthMomentState"/> keeps the data builders, the choice mutations, the radio-group
    /// settle rule (driven by <see cref="OnCursorSettled"/>) and one Describe per tab.
    ///
    /// <see cref="CaptureWindowButtons"/> is false: vanilla's OK/Later are text buttons, but so are
    /// the ones the character and health side panels draw whenever <c>letter.ShowInfoTabs</c> is set,
    /// which blanket capture would scrape too. The two real actions are declared instead, each
    /// running the state's own vehicle, with Later omitted in the archive view as vanilla omits it.
    ///
    /// <see cref="IsLive"/> tracks <see cref="GrowthMomentState.IsActive"/> alone: nothing here opens
    /// an info card, float menu or message box, and confirm and postpone both close the state before
    /// removing the dialog, so no overlay ever coexists with this scope.
    ///
    /// The dialog sets both closeOnAccept and closeOnCancel false in its own constructor, so vanilla's
    /// base Window handling already no-ops for Enter and Escape. <see cref="OwnsCancel"/> is
    /// nevertheless overridden to an unconditional true as ownership documentation, since the chassis
    /// owns cancel only while a search is live and this scope's Cancel claim is what postpones.
    ///
    /// Typeahead spans every tab at once, narrowed off the three plain-text tabs through
    /// <see cref="GrowthMomentState.TabSearchable"/>. The passion and trait rows carry a marker naming
    /// their own <c>SkillDef</c>/<c>Trait</c>, and <see cref="ListingRowCapture"/> rings whichever the
    /// keyboard is on; the other tabs and the bottom buttons stay unringed, since vanilla draws no
    /// listing rows for them.
    /// </summary>
    public sealed class GrowthMomentScope : ScreenScope, IListingRingClient
    {
        private readonly Dialog_GrowthMomentChoices dialog;
        private readonly List<ScreenAction> actions = new List<ScreenAction>();
        private bool landedOnChoiceTab;

        public GrowthMomentScope(Dialog_GrowthMomentChoices dialog)
        {
            this.dialog = dialog;

            // Escape postpones, or closes the archive view; the base's typeahead-active Escape claim
            // is registered first, so a live search clears instead.
            Claim(SharedMenuGrammar.Cancel, delegate { GrowthMomentState.HandleCancel(); });
            Claim("growthMoment.confirm", delegate { GrowthMomentState.HandleConfirmKey(); });
        }

        public override string Name
        {
            get { return "growth-moment"; }
        }

        /// <summary>Tracks the state's own liveness, folded with the chassis's model-failure standdown.</summary>
        public override bool IsLive
        {
            get { return GrowthMomentState.IsActive && base.IsLive; }
        }

        /// <summary>Unconditional, so this scope's Cancel claim is the only Escape path.</summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        protected internal override Window OwnedWindow
        {
            get { return dialog; }
        }

        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        /// <summary>The character and health side panels draw text buttons of their own.</summary>
        protected override bool CaptureWindowButtons
        {
            get { return false; }
        }

        protected override int ContentRegionCount
        {
            get { return GrowthMomentState.TabCount; }
        }

        protected override string ContentRegionName(int region)
        {
            return GrowthMomentState.TabName(region);
        }

        protected override bool ContentRegionSearchable(int region)
        {
            return GrowthMomentState.TabSearchable(region);
        }

        protected override int ContentItemCount(int region)
        {
            return GrowthMomentState.RowCount(region);
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            return GrowthMomentState.DescribeRow(region, index);
        }

        protected override void ActivateContentItem(int region, int index)
        {
            GrowthMomentState.ActivateRow(region, index);
        }

        /// <summary>The radio-group contract; <see cref="GrowthMomentState.SettleRow"/> no-ops outside a choice tab.</summary>
        protected override void OnCursorSettled(int region, int index)
        {
            GrowthMomentState.SettleRow(region, index);
        }

        /// <summary>
        /// Vanilla's OK and Later buttons, each running the state's own vehicle. The archive view
        /// draws no Later button, so neither does this; its OK row still advertises Alt+S, a no-op
        /// for a read-only letter, while the row itself closes the window as vanilla's does.
        /// </summary>
        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                if (!GrowthMomentState.IsArchiveView)
                {
                    actions.Add(new ScreenAction("Later".Translate().ToString(),
                        GrowthMomentState.HandleLaterButton, SharedMenuGrammar.Cancel));
                }
                actions.Add(new ScreenAction("OK".Translate().ToString(),
                    GrowthMomentState.HandleOkButton, "growthMoment.confirm"));
                return actions;
            }
        }

        /// <summary>Shift+Enter presses OK from anywhere on the screen.</summary>
        protected override string DefaultAcceptActionId
        {
            get { return "growthMoment.confirm"; }
        }

        protected override string ComposeOpenAnnouncement()
        {
            return GrowthMomentState.IsActive ? GrowthMomentState.BuildOpeningText() : null;
        }

        public override void OnPush()
        {
            base.OnPush();
            ScreenScopeDrawPatch.RegisterListingClient(this);
        }

        public override void OnPop()
        {
            ScreenScopeDrawPatch.UnregisterListingClient(this);
            base.OnPop();
        }

        /// <summary>
        /// Lands the cursor on the first selection tab, silently: the base has just armed the entry
        /// announcement, which fires after this and reads the destination tab and its row together.
        /// </summary>
        public override void OnFocus()
        {
            base.OnFocus();
            if (landedOnChoiceTab)
            {
                return;
            }
            landedOnChoiceTab = true;
            int target = GrowthMomentState.InitialRegion;
            if (target <= 0)
            {
                return;
            }
            MoveResult result = Model.MoveToRegion(target);
            if (result.Changed)
            {
                OnRegionChanged(result);
                NotifyCursorSettled();
            }
        }

        Window IListingRingClient.ListingRingWindow
        {
            get { return dialog; }
        }

        ListingRingFocus IListingRingClient.CurrentListingFocus()
        {
            ListModel region = Model.CurrentRegion;
            if (region == null || region.Index < 0)
            {
                return ListingRingFocus.None;
            }
            object row = GrowthMomentState.RowObject(Model.RegionIndex, region.Index);
            if (row == null)
            {
                return ListingRingFocus.None;
            }
            return new ListingRingFocus { RowObject = row };
        }
    }

    /// <summary>
    /// The Listing_Standard row calls this dialog makes, resolved by their exact overloads: both
    /// choice methods bind the 5-parameter RadioButton, the convenience overload rather than the
    /// 7-parameter core it forwards into, and the multi-passion branch the tabIn CheckboxLabeled.
    /// </summary>
    internal static class GrowthMomentRowCalls
    {
        internal static readonly MethodInfo RadioButton =
            AccessTools.Method(typeof(Listing_Standard), "RadioButton",
                new[] { typeof(string), typeof(bool), typeof(float), typeof(string), typeof(float?) });

        internal static readonly MethodInfo TabInCheckbox =
            AccessTools.Method(typeof(Listing_Standard), "CheckboxLabeled",
                new[] { typeof(string), typeof(bool).MakeByRefType(), typeof(float) });
    }

    /// <summary>
    /// Marks each passion row with its own <c>SkillDef</c>. The loop draws a checkbox when the letter
    /// grants more than one passion and a radio otherwise, so both shapes appear in IL and the
    /// unexecuted one emits nothing.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_GrowthMomentChoices), "DrawPassionChoices")]
    internal static class GrowthMomentPassionRowMarkerPatch
    {
        private static readonly MethodInfo PassionCurrent =
            AccessTools.Method(typeof(List<SkillDef>.Enumerator), "get_Current");

        private static readonly MarkerPlan Plan = new MarkerPlan
        {
            RowCallTargets = new[] { GrowthMomentRowCalls.TabInCheckbox, GrowthMomentRowCalls.RadioButton },
            Sites = new[]
            {
                new MarkerSite
                {
                    RowCall = GrowthMomentRowCalls.TabInCheckbox,
                    Payload = MarkerPayload.LoopLocal,
                    LocalSource = PassionCurrent,
                },
                new MarkerSite
                {
                    RowCall = GrowthMomentRowCalls.RadioButton,
                    Payload = MarkerPayload.LoopLocal,
                    LocalSource = PassionCurrent,
                },
            },
        };

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            return ListingRowMarkerInjector.Inject(instructions, __originalMethod, Plan);
        }
    }

    /// <summary>
    /// Marks each trait row with its own <c>Trait</c>, and the optional "no trait" row with the very
    /// static vanilla compares against, keeping every row of this dialog on a reference match.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_GrowthMomentChoices), "DrawTraitChoices")]
    internal static class GrowthMomentTraitRowMarkerPatch
    {
        private static readonly MarkerPlan Plan = new MarkerPlan
        {
            RowCallTargets = new[] { GrowthMomentRowCalls.RadioButton },
            Sites = new[]
            {
                new MarkerSite
                {
                    RowCall = GrowthMomentRowCalls.RadioButton,
                    Payload = MarkerPayload.LoopLocal,
                    LocalSource = AccessTools.Method(typeof(List<Trait>.Enumerator), "get_Current"),
                },
                new MarkerSite
                {
                    RowCall = GrowthMomentRowCalls.RadioButton,
                    Payload = MarkerPayload.StaticField,
                    StaticSource = AccessTools.Field(typeof(ChoiceLetter_GrowthMoment), "NoTrait"),
                },
            },
        };

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            return ListingRowMarkerInjector.Inject(instructions, __originalMethod, Plan);
        }
    }
}
