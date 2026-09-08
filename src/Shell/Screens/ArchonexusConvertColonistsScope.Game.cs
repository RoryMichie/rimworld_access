using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// <c>Dialog_ChooseColonistsForIdeo</c>, the "assign colonists" sub-dialog of the Archonexus
    /// reform-ideoligion screen: one content region over
    /// <see cref="RimWorldAccess.ArchonexusConvertColonistsState"/>'s colonist list, attached by
    /// window through ShellBootstrap.
    ///
    /// <see cref="CaptureWindowButtons"/> is false because every colonist row draws its own
    /// Convert/Revert button through <c>Widgets.ButtonText</c>, which would scrape one captured
    /// row per colonist; the dialog's single real action, its bottom Close button, is declared
    /// instead and runs the same <c>Close()</c>.
    ///
    /// The dialog sets <c>closeOnCancel = false</c>, so Escape is already a vanilla no-op and
    /// this scope's Cancel claim is the only thing that closes it. <see cref="OwnsCancel"/> is
    /// still set explicitly, which is also what keeps the Escape-closes-the-dialog claim below
    /// the chassis's search-clearing one. <c>closeOnAccept</c> stays true, so an un-owned Enter
    /// would silently close the dialog and discard unsaved toggles; the chassis default
    /// OwnsAccept covers that.
    ///
    /// With no eligible colonists the content region is empty and therefore not navigable, and
    /// this scope's modality swallows the keys; Escape stays claimed unconditionally so the
    /// dialog is still closeable.
    ///
    /// The colonist rows are bare <c>Listing_Standard.GetRect</c> allocations with no row widget
    /// to tap, so the focus ring rides the marker
    /// <see cref="ArchonexusConvertRowMarkerPatch"/> plants and paints before vanilla fills the
    /// row, leaving the highlight and icons slightly over its inner edge.
    /// </summary>
    public sealed class ArchonexusConvertColonistsScope : ScreenScope, IListingRingClient
    {
        private readonly Dialog_ChooseColonistsForIdeo dialog;
        private readonly List<ScreenAction> actions = new List<ScreenAction>();

        public ArchonexusConvertColonistsScope(Dialog_ChooseColonistsForIdeo dialog)
        {
            this.dialog = dialog;

            // Claimed unconditionally so an empty list is still closeable. The base's
            // typeahead-active Escape claim is registered first, so a live search clears instead.
            Claim(SharedMenuGrammar.Cancel, delegate
            {
                ShellFrameStamps.MarkCancelConsumed();
                ArchonexusConvertColonistsState.CloseDialog();
            });
        }

        public override string Name
        {
            get { return "archonexus-convert-colonists"; }
        }

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

        /// <summary>See the class remarks: the per-colonist Convert/Revert buttons would over-capture.</summary>
        protected override bool CaptureWindowButtons
        {
            get { return false; }
        }

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        /// <summary>Vanilla's own dialog title.</summary>
        protected override string ContentRegionName(int region)
        {
            return "ChooseColonistsForIdeoTitle".Translate().ToString();
        }

        protected override int ContentItemCount(int region)
        {
            return ArchonexusConvertColonistsState.PawnCount;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            return ArchonexusConvertColonistsState.DescribeRow(index);
        }

        protected override void ActivateContentItem(int region, int index)
        {
            ArchonexusConvertColonistsState.Toggle(index);
        }

        /// <summary>The dialog's bottom Close button, running vanilla's own <c>Close()</c>; Escape does the same.</summary>
        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                actions.Add(new ScreenAction("Close".Translate().ToString(),
                    ArchonexusConvertColonistsState.CloseDialog, SharedMenuGrammar.Cancel));
                return actions;
            }
        }

        protected override string ComposeOpenAnnouncement()
        {
            return ArchonexusConvertColonistsState.IsActive
                ? ArchonexusConvertColonistsState.BuildOpeningText()
                : null;
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

        Window IListingRingClient.ListingRingWindow
        {
            get { return dialog; }
        }

        ListingRingFocus IListingRingClient.CurrentListingFocus()
        {
            ListModel region = Model.CurrentRegion;
            if (region == null || region.Index < 0 || Model.RegionIndex != 0)
            {
                return ListingRingFocus.None;
            }
            Pawn pawn = ArchonexusConvertColonistsState.PawnAt(region.Index);
            if (pawn == null)
            {
                return ListingRingFocus.None;
            }
            return new ListingRingFocus { RowObject = pawn };
        }

    }

    /// <summary>
    /// Marks each colonist row with its own pawn. The dialog's counted loop keeps the pawn in a
    /// plain local, with no lambda in the method to turn it into a display-class field, and its
    /// bottom Close button sits outside the listing, so the method holds exactly one row call.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_ChooseColonistsForIdeo), "DoWindowContents")]
    internal static class ArchonexusConvertRowMarkerPatch
    {
        private static readonly MethodInfo GetRectMethod =
            AccessTools.Method(typeof(Listing), "GetRect", new[] { typeof(float), typeof(float) });

        private static readonly MarkerPlan Plan = new MarkerPlan
        {
            RowCallTargets = new[] { GetRectMethod },
            Sites = new[]
            {
                new MarkerSite
                {
                    RowCall = GetRectMethod,
                    Payload = MarkerPayload.LoopLocal,
                    LocalSource = AccessTools.Method(typeof(List<Pawn>), "get_Item"),
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
