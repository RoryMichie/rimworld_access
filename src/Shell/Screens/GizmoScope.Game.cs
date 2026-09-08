using System.Collections.Generic;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Focus scope for gizmo navigation (the G menu): keyboard browsing and execution of the real
    /// vanilla gizmo bar, on the colony map and the world map alike. The list, cursor, and
    /// typeahead live in the windowless <see cref="GizmoNavigationState"/>, so the scope rides
    /// the focus stack through <see cref="GizmoScopeMirror"/>.
    /// NON-modal on purpose: the ambient <see cref="MapScope"/> claims beneath must keep flowing
    /// — Ctrl+Alt+Enter pawn inspection, Alt+arrow colonist-bar navigation, the multi-select
    /// cluster, the F1-F4 group keys — which is why <c>gizmos.activateModified</c> registers every
    /// modifier combination for Enter EXCEPT Ctrl+Alt. What must still be blocked is blocked
    /// without modality: the consume-only <c>gizmos.blockChar</c> claim takes the bare
    /// letter/digit keycode twins, map arrows stay suppressed through
    /// MapNavigationState.SuppressMapNavigation, every opener gates on
    /// ShellGuards.MenuOwnsInput(), and the dispatcher's modal swallow eats the remainder.
    /// Slider gizmos adjust with bare Left/Right (Shift for five steps), live and with no
    /// sub-mode, matching a sighted player's drag; those claims gate on
    /// <see cref="GizmoNavigationState.SelectedGizmoHasSlider"/> and on any other gizmo the
    /// arrows fall through and die in the dispatcher swallow. Enter keeps each gizmo's own action.
    /// The context menus open a windowless float menu over this screen, which the dispatcher's
    /// overlay stand-down hands the keyboard; the scope's statelessness restores the row after.
    /// Deliberately a plain FocusScope, not a ScreenScope: that contract is a modal screen of
    /// content regions plus Buttons, which does not fit a windowless overlay drawn over the live
    /// map. The surface conforms at the ELEMENT layer instead, describing every gizmo row as an
    /// <see cref="ElementDescription"/> rendered by <see cref="AnnouncementComposer"/>.
    /// </summary>
    public sealed class GizmoScope : FocusScope, ICharSink
    {
        public GizmoScope()
        {
            Claim(SharedMenuGrammar.Previous, delegate { GizmoNavigationState.NavigatePrevious(); });
            Claim(SharedMenuGrammar.Next, delegate { GizmoNavigationState.NavigateNext(); });
            Claim(SharedMenuGrammar.First, delegate { GizmoNavigationState.JumpToFirst(); });
            Claim(SharedMenuGrammar.Last, delegate { GizmoNavigationState.JumpToLast(); });

            // Unconditional, never gated on a hit: this scope is non-modal, so an unclaimed
            // chord would fall through to MapScope's pullFromPointer and move the map cursor
            // where the player asked for a refusal.
            Claim(SharedMenuGrammar.RouteToPointer, OnRouteToPointer);

            Claim(SharedMenuGrammar.Activate, delegate { GizmoNavigationState.ActivateCurrent(); });
            Claim("gizmos.activateModified", delegate { GizmoNavigationState.ActivateCurrent(); });
            // The CharSink suppresses the automatic Space alias, so claim it explicitly,
            // standing down while a typeahead search is live (ScreenScope's pattern).
            ClaimFallback(SharedMenuGrammar.ActivateAlias,
                delegate { GizmoNavigationState.ActivateCurrent(); },
                when: () => !GizmoNavigationState.HasActiveSearch);
            Claim(SharedMenuGrammar.Cancel, delegate { GizmoNavigationState.HandleCancel(); });

            Claim(SharedMenuGrammar.SearchBackspace, delegate { GizmoNavigationState.HandleBackspace(); },
                when: HasActiveSearch);

            Claim("gizmos.infoCard", delegate { GizmoNavigationState.OpenGizmoInfoCard(); });
            Claim("gizmos.rightClickOptions", delegate { GizmoNavigationState.HandleRightBracket(); });

            // Live only on a focused adjustable slider; elsewhere the arrows fall to the
            // dispatcher swallow.
            Claim("gizmos.slider.decrease", delegate { GizmoNavigationState.AdjustSlider(-1, false); }, when: HasSlider);
            Claim("gizmos.slider.increase", delegate { GizmoNavigationState.AdjustSlider(1, false); }, when: HasSlider);
            Claim("gizmos.slider.decreaseBig", delegate { GizmoNavigationState.AdjustSlider(-1, true); }, when: HasSlider);
            Claim("gizmos.slider.increaseBig", delegate { GizmoNavigationState.AdjustSlider(1, true); }, when: HasSlider);

            // Consume-only block of the bare letter/digit keycode twins; see the class remarks.
            Claim("gizmos.blockChar", delegate { });
        }

        public override string Name
        {
            get { return "gizmos"; }
        }

        public override bool IsModal
        {
            get { return false; }
        }

        /// <summary>Non-modal, so map keys coexist, but still an input owner for MenuOwnsInput.</summary>
        public override bool OwnsGameInput
        {
            get { return true; }
        }

        public override bool IsLive
        {
            get { return true; }
        }

        public override ICharSink CharSink
        {
            get { return this; }
        }

        /// <summary>
        /// The gizmo bar has no window, so ownership is the hover test rather than
        /// PointerRouting.PointerOwnedBy: a real dialog over the bar must not be read through.
        /// </summary>
        private static void OnRouteToPointer(KeyEventSnapshot e)
        {
            if (ShellGuards.NonImmediateWindowUnderPointer()
                || !GizmoNavigationState.RouteToPointer(PointerRouting.Pointer))
            {
                PointerRouting.RejectNoTarget();
                return;
            }
            ShellDispatcherPatch.NotifyCursorMoved();
        }

        private static bool HasSlider()
        {
            return GizmoNavigationState.SelectedGizmoHasSlider();
        }

        private static bool HasActiveSearch()
        {
            return GizmoNavigationState.HasActiveSearch;
        }

        /// <summary>Typeahead over the gizmo labels, letters and digits alike.</summary>
        public bool HandleChar(char c)
        {
            if (!TypeaheadMatcher.AcceptsSearchChar(c, GizmoNavigationState.HasActiveSearch))
            {
                return false;
            }
            GizmoNavigationState.HandleTypeahead(c);
            return true;
        }
    }

    /// <summary>
    /// Keeps <see cref="GizmoScope"/> in lockstep with
    /// <see cref="GizmoNavigationState.IsActive"/>, reconciled every OnGUI pass. Stands down
    /// while an info card is open over the menu, as every mirror does.
    /// Gizmo navigation coexists with two other screens, and reconcile ORDER is what sets
    /// precedence — a mirror reconciling later lands on top:
    /// <see cref="InspectionScopeMirror"/> reconciles BEFORE this one, so gizmo wins if both are
    /// ever active (the two mirrors once re-floated each other every frame;
    /// Ctrl+Alt+Enter now closes the gizmo menu first, so the keyboard no longer reaches that
    /// state). <see cref="WildlifeScopeMirror"/> reconciles AFTER, so wildlife wins: a mouse
    /// click on the Wildlife button bypasses every keyboard patch and opens it over an open
    /// gizmo menu.
    /// The float-menu overlay is handled by the dispatcher's blanket stand-down.
    /// </summary>
    internal static class GizmoScopeMirror
    {
        private static readonly GizmoScope scope = new GizmoScope();

        public static void Reconcile()
        {
            // Stand down while a Dialog_Slider from a gizmo's right-bracket option is up: this
            // mirror reconciles AFTER the attach sweep and Push re-floats an already-present
            // scope, so otherwise the dialog's keystrokes die in the consume-only char block.
            // Dialog_Slider does not absorbInputAroundWindow, so a window-flag test would miss
            // it; SliderDialogState.IsActive is the precise signal.
            if (GizmoNavigationState.IsActive && !InfoCardState.IsActive && !SliderDialogState.IsActive)
            {
                // Never climb over a live window's scope (a gizmo action can open a real dialog):
                // hold beneath it, entering beneath it if the ring is not on the stack yet.
                IReadOnlyList<FocusScope> stack = FocusStack.ScopesBottomUp;
                int ringIndex = -1;
                FocusScope lowestWindowScopeAbove = null;
                for (int i = 0; i < stack.Count; i++)
                {
                    if (ReferenceEquals(stack[i], scope))
                    {
                        ringIndex = i;
                        lowestWindowScopeAbove = null;
                    }
                    else if (lowestWindowScopeAbove == null && ScopeForWindow.IsAttachedScope(stack[i]))
                    {
                        lowestWindowScopeAbove = stack[i];
                    }
                }
                if (lowestWindowScopeAbove == null)
                {
                    FocusStack.Push(scope);
                }
                else if (ringIndex < 0)
                {
                    FocusStack.InsertBelow(scope, lowestWindowScopeAbove);
                }
            }
            else
            {
                FocusStack.Pop(scope);
            }
        }
    }
}
