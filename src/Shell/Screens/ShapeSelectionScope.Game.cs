using System.Collections.Generic;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The keyboard focus scope for the shape selection menu: a flat list of the shapes available for
    /// the active build/order/zone designator, opened from the architect-placement flow via Tab. The
    /// mod owns no window — the surface is the windowless
    /// <see cref="ShapeSelectionMenuState"/> — so the scope rides the focus stack through
    /// <see cref="ShapeSelectionScopeMirror"/>. One content region over the state's shape list, the
    /// windowless Escape pattern below, and no Buttons region: the state draws nothing to capture.
    ///
    /// <see cref="ShapeSelectionMenuState"/> is deliberately OUT of
    /// <see cref="ShellGuards.MenuOwnsInput"/>'s own OR-chain; the two call sites that need to know
    /// about this specific state (GoToState, MapNavigationPatch) carry their own explicit terms.
    /// Being modal does transitively widen that predicate through
    /// <see cref="RimWorldAccess.Shell.FocusStack.AnyLiveModal"/> whenever the scope is pushed, and
    /// the widening is accepted rather than engineered around with exclusion terms.
    ///
    /// Modality also means the shell backstop swallows any key this scope does not claim. Typeahead
    /// is routed through the layout-aware <see cref="ICharSink"/>, so no keycode range check is
    /// needed or wanted here.
    ///
    /// Tab is the chassis's region-cycle chord — a no-op on a single-region screen — rather than
    /// falling through to PlacementScope, whose own Tab claim would reset the whole architect session
    /// out from under an open shape picker. That divergence is deliberate.
    ///
    /// The mirror gate needs nothing beyond IsActive: the sole hand-off path calls <c>Close()</c>
    /// BEFORE <c>ShapePlacementState.Enter</c> runs, so IsActive is already false by the time shape
    /// placement takes over, and this state never opens an info card.
    /// </summary>
    public sealed class ShapeSelectionScope : ScreenScope
    {
        public ShapeSelectionScope()
        {
            // Windowless: no window closes itself, so the scope owns Cancel outright and stamps the
            // frame. The base's typeahead Escape claim registers first and clears a live search.
            Claim(SharedMenuGrammar.Cancel, e =>
            {
                ShellFrameStamps.MarkCancelConsumed();
                ShapeSelectionMenuState.Cancel();
            }, when: () => !TypeaheadHasActiveSearch);
        }

        public override string Name
        {
            get { return "shape-selection-menu"; }
        }

        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        /// <summary>Unconditionally true — windowless; see the constructor's Escape claim.</summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        protected override bool IncludeActionsRegion
        {
            get { return false; }
        }

        /// <summary>
        /// This scope is a long-lived singleton whose Model outlives every open, and SetCount only
        /// clamps the cursor rather than resetting it, so each open resets to the first shape here and
        /// re-arms the opening announcement.
        /// </summary>
        public override void OnPush()
        {
            base.OnPush();
            ResetOpenAnnouncement();
            Model.MoveToRegion(0);
            ListModel region = Model.CurrentRegion;
            if (region != null)
            {
                region.MoveFirst();
            }
        }

        protected override string ComposeOpenAnnouncement()
        {
            return "RimWorldAccess.Building.ShapeSelect.MenuOpened".Translate().ToString();
        }

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        protected override string ContentRegionName(int region)
        {
            return "RimWorldAccess.Building.ShapeSelect.MenuOpened".Translate().ToString();
        }

        protected override int ContentItemCount(int region)
        {
            return ShapeSelectionMenuState.AvailableShapes.Count;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            IReadOnlyList<ShapeType> shapes = ShapeSelectionMenuState.AvailableShapes;
            if (index < 0 || index >= shapes.Count)
            {
                return new ElementDescription();
            }
            return new ElementDescription
            {
                Role = ElementRole.MenuItem,
                Label = ShapeHelper.GetShapeName(shapes[index]),
                Extras = ShapeHelper.GetShapeDescription(shapes[index]),
            };
        }

        protected override void ActivateContentItem(int region, int index)
        {
            ShapeSelectionMenuState.ConfirmAt(index);
        }
    }

    /// <summary>
    /// Keeps <see cref="ShapeSelectionScope"/> in lockstep with
    /// <see cref="ShapeSelectionMenuState.IsActive"/>, reconciled every OnGUI pass.
    /// Chain-independent: the sole opener never nests this screen inside another menu, and the
    /// handoff to shape placement closes this scope first.
    /// </summary>
    internal static class ShapeSelectionScopeMirror
    {
        private static readonly ShapeSelectionScope scope = new ShapeSelectionScope();

        public static void Reconcile()
        {
            if (ShapeSelectionMenuState.IsActive)
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
