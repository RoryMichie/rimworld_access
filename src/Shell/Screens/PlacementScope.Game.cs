using Verse;
using RimWorld;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Focus scope for designator placement: the architect-menu flow
    /// (<see cref="RimWorldAccess.ArchitectState.IsInPlacementMode"/>), the gizmo-invoked flow
    /// (<c>Find.DesignatorManager.SelectedDesignator</c>), and transport-pod landing targeting
    /// are ONE scope. The surfaces are windowless state machines, so the scope rides the focus
    /// stack through <see cref="PlacementScopeMirror"/>.
    ///
    /// Non-modal by design: placement coexists with map navigation, so every claim carries a
    /// <c>when:</c> gate and anything unclaimed falls through. Tab, Ctrl+A and Enter gate on dry
    /// (side-effect-free) predicate mirrors of their handlers' own internal conditions, so a
    /// designator with no shapes lets Tab reach vanilla colonist-cycling and a gizmo-selected
    /// orders designator lets Enter through unconsumed. Space's 0.2s cooldown lives inside
    /// <c>HandleSpace</c>, not the gate — the key stays consumed while it is silently no-oping.
    /// <see cref="KeyChord.Matches"/>'s exact modifier equality subsumes any bare-key
    /// modifier-absence guard.
    ///
    /// The mirror's gate (<see cref="PlacementScopeMirror.ShouldBeLive"/>) stands the scope down
    /// for competing states explicitly rather than relying on stack masking; the
    /// GizmoNavigationState term is load-bearing because <see cref="GizmoScope"/> is non-modal.
    /// WindowlessFloatMenuState is omitted on purpose — the dispatcher's blanket
    /// <c>LegacyKeyboardOverlayActive</c> stand-down covers it. The GoTo and scanner-search
    /// yields are NOT gate terms: they only ever yielded specific keys, so folding them in would
    /// wrongly block Tab/R/Ctrl+A/Shift+Space; <see cref="MapOverlayScopeMirror"/> is instead
    /// reconciled after this mirror so <see cref="GoToScope"/>/<see cref="ScannerSearchScope"/>
    /// re-float above.
    ///
    /// Transport-pod claims are mutually exclusive by construction with every
    /// <c>architectPlacement.*</c> claim (each requires the opposite of
    /// <see cref="RimWorldAccess.ArchitectPlacementInputPatch.InTransportPodTargeting"/>), so
    /// their relative order never matters; that sub-case's ambiguity against
    /// <c>TargetingPatch</c> over the same <c>Find.Targeter</c> session is described on
    /// <see cref="TargetingScope"/>. Paint and plan color-picker reopen share one action id
    /// because their designator types are mutually exclusive.
    ///
    /// <c>architectPlacement.confirmPlacementModified</c> is the same Enter under any modifier
    /// combination except Ctrl+Alt (the ambient map scope's colonist-bar inspection): mods read
    /// the physical modifier keys while their designator runs (Allow Tool's Alt lifts Select
    /// Similar's cap; Shift/Ctrl narrow Harvest Fully Grown), which a sighted player supplies by
    /// holding them during the click our Enter stands in for.
    /// </summary>
    public sealed class PlacementScope : FocusScope
    {
        public PlacementScope()
        {
            Claim("architectPlacement.openShapeMenu",
                delegate { ArchitectPlacementInputPatch.HandleTab(false); },
                when: MainFlowCanUseShapes);
            Claim("architectPlacement.switchToManualMode",
                delegate { ArchitectPlacementInputPatch.HandleTab(true); },
                when: MainFlowCanUseShapes);
            Claim("architectPlacement.expandSelectionScope",
                delegate { ArchitectPlacementInputPatch.HandleCtrlA(); },
                when: MainFlowShapePlacementActive);
            Claim("architectPlacement.previousSelectionScope",
                delegate { ArchitectPlacementInputPatch.HandleCtrlShiftA(); },
                when: MainFlowShapePlacementActive);
            Claim("architectPlacement.removePointOrCancelBlueprint",
                delegate { ArchitectPlacementInputPatch.HandleShiftSpace(); },
                when: MainFlowReady);
            Claim("architectPlacement.rotateCounterclockwise",
                delegate { ArchitectPlacementInputPatch.HandleRotate(RotationDirection.Counterclockwise); },
                when: MainFlowReady);
            Claim("architectPlacement.rotateClockwise",
                delegate { ArchitectPlacementInputPatch.HandleRotate(RotationDirection.Clockwise); },
                when: MainFlowReady);
            Claim("architectPlacement.placeOrToggleCell",
                delegate { ArchitectPlacementInputPatch.HandleSpace(); },
                when: MainFlowReady);
            Claim("architectPlacement.confirmPlacement",
                delegate { ArchitectPlacementInputPatch.HandleEnter(); },
                when: MainFlowCanHandleEnter);
            Claim("architectPlacement.confirmPlacementModified",
                delegate { ArchitectPlacementInputPatch.HandleEnter(); },
                when: MainFlowCanHandleEnter);
            Claim("architectPlacement.designatorOptions",
                delegate { ArchitectPlacementInputPatch.HandleDesignatorOptions(); },
                when: MainFlowReady);
            Claim("shapePlacement.reopenColorPicker",
                delegate { HandleReopenColorPicker(); },
                when: CanReopenColorPicker);

            Claim("targetingPodLanding.confirm",
                delegate { ArchitectPlacementInputPatch.HandleTransportPodConfirm(); },
                when: ArchitectPlacementInputPatch.InTransportPodTargeting);

            Claim(SharedMenuGrammar.Cancel, delegate
            {
                if (ArchitectPlacementInputPatch.InTransportPodTargeting())
                {
                    ArchitectPlacementInputPatch.HandleTransportPodCancel();
                }
                else
                {
                    ArchitectPlacementInputPatch.HandleEscape();
                }
            }, when: CanCancel);
        }

        public override string Name
        {
            get { return "placement"; }
        }

        public override bool IsModal
        {
            get { return false; }
        }

        /// <summary>Non-modal, but still an input owner for both the shape-placement and
        /// architect placement-mode arms of <c>MenuOwnsInput</c>.</summary>
        public override bool OwnsGameInput
        {
            get { return true; }
        }

        /// <summary>
        /// Owns Enter exactly when a confirm claim would fire (transparent otherwise). Without
        /// this, vanilla's closeOnAccept pass eats the confirm first: a main tab still open
        /// beneath placement closes on Enter and its close-out tears down the armed designator.
        /// </summary>
        public override bool OwnsAccept
        {
            get
            {
                return MainFlowCanHandleEnter()
                    || ArchitectPlacementInputPatch.InTransportPodTargeting();
            }
        }

        public override bool IsLive
        {
            get { return true; }
        }

        /// <summary>Shared guard: the main (non-pod-targeting) flow, with an active designator.</summary>
        private static bool MainFlowReady()
        {
            return !ArchitectPlacementInputPatch.InTransportPodTargeting()
                && ArchitectPlacementInputPatch.GetActiveDesignator() != null;
        }

        private static bool MainFlowCanUseShapes()
        {
            return MainFlowReady() && ArchitectPlacementInputPatch.CanUseShapes();
        }

        private static bool MainFlowShapePlacementActive()
        {
            return MainFlowReady() && ShapePlacementState.IsActive;
        }

        private static bool MainFlowCanHandleEnter()
        {
            return MainFlowReady() && ArchitectPlacementInputPatch.CanHandleEnter();
        }

        /// <summary>Escape is valid in either flow: main (needs a designator) or pod-targeting.</summary>
        private static bool CanCancel()
        {
            return ArchitectPlacementInputPatch.InTransportPodTargeting()
                || ArchitectPlacementInputPatch.GetActiveDesignator() != null;
        }

        private static bool CanReopenColorPicker()
        {
            if (!ShapePlacementState.IsActive || Find.CurrentMap == null)
            {
                return false;
            }
            Designator selected = Find.DesignatorManager?.SelectedDesignator;
            return PaintColorHelper.IsPaintDesignator(selected)
                || PlanColorHelper.IsPlanColorDesignator(selected);
        }

        private static void HandleReopenColorPicker()
        {
            Designator selected = Find.DesignatorManager?.SelectedDesignator;
            if (PaintColorHelper.IsPaintDesignator(selected))
            {
                PaintColorHelper.OpenColorPicker((Designator_Paint)selected);
            }
            else if (PlanColorHelper.IsPlanColorDesignator(selected))
            {
                PlanColorHelper.OpenColorPicker((Designator_Plan_Add)selected);
            }
        }
    }

    /// <summary>
    /// Pushes/pops <see cref="PlacementScope"/> and carries the placement chain's per-frame
    /// housekeeping (map-null and stale-state cleanup), which runs every OnGUI pass rather than
    /// only while a key is down.
    ///
    /// Must be reconciled BEFORE <see cref="MapOverlayScopeMirror"/> and
    /// <see cref="ShapeSelectionScopeMirror"/> so those scopes re-float above this one whenever
    /// they are simultaneously live — opening the shape menu does not clear the underlying
    /// designator selection, so this scope's gate stays true the whole time.
    /// </summary>
    internal static class PlacementScopeMirror
    {
        private static readonly PlacementScope scope = new PlacementScope();

        public static void Reconcile()
        {
            // Both ahead of every gate below: the placement-spot category tracks the selected
            // designator itself, so it must survive the drill-ins (gizmo nav, inspection) that
            // stand this scope down without ending placement, and a tile handed to the placement
            // help channel is owed to the player whatever became of the designator. Each has its
            // own not-Playing guard.
            PlacementSpotScanner.Reconcile();

            PlacementHelpSpeech.HealHeldTile();

            if (Current.ProgramState != ProgramState.Playing)
            {
                FocusStack.Pop(scope);
                return;
            }

            ArchitectPlacementInputPatch.CleanupIfMapMissing();
            ArchitectPlacementInputPatch.CleanupStaleState();
            ArchitectPlacementInputPatch.ReconcileManualSession();

            if (ShouldBeLive())
            {
                FocusStack.Push(scope);
            }
            else
            {
                FocusStack.Pop(scope);
            }
        }

        /// <summary>The activation gate; see the scope's remarks for why each stand-down term is explicit.</summary>
        private static bool ShouldBeLive()
        {
            bool inArchitectMode = ArchitectPlacementInputPatch.InArchitectMode();
            bool hasActiveDesignator = ArchitectPlacementInputPatch.HasActiveDesignator();
            bool inTransportPodTargeting = ArchitectPlacementInputPatch.InTransportPodTargeting();

            if (!inArchitectMode && !hasActiveDesignator && !inTransportPodTargeting)
            {
                return false;
            }

            if (ShapeSelectionMenuState.IsActive) return false;
            if (WindowlessInventoryState.IsActive) return false;
            if (GizmoNavigationState.IsActive) return false;
            if (WindowlessInspectionState.IsActive) return false;

            // The zone-deletion confirmation keeps ShapePlacementState active under a real
            // Dialog_MessageBox, so without this the per-frame Push re-floats above the dialog.
            if (ShellGuards.ForeignInputOwningWindowAbove()) return false;
            if (ViewingModeState.IsActive && !ShapePlacementState.IsActive) return false;

            return true;
        }
    }
}
