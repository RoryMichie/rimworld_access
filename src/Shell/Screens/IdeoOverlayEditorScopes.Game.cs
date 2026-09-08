using UnityEngine;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The three surviving windowless IdeoBuilder overlay editors (the base issue-based precept
    /// editor, the typed precept lists, the deity list) are full <see cref="ScreenScope"/>
    /// subclasses, each in its own file (<see cref="IdeoPreceptScreenScope"/>,
    /// <see cref="IdeoTypedPreceptScreenScope"/>, <see cref="IdeoDeityScreenScope"/>).
    /// A fourth, the appearance editor, is gone entirely: vanilla's own window and
    /// <see cref="StyleItemsDialogScope"/> replaced it. The three share the dual-role
    /// ComboBox+expandable/Button+expandable rows, Left/Right value-cycling or expand/collapse, and
    /// TextFieldEditSession-backed renames that replaced these classes' old
    /// TreeNavigationHelper-driven RouteKey shims. <see cref="IdeoOverlayScopesMirror"/> below
    /// reconciles all three in the same state-mirrored lockstep.
    ///
    /// <b>The precedent this mirrors: wave-I2's scenario-builder overlay family</b>
    /// (<see cref="ScenarioOverlayScopeMirror"/>) — state-mirrored push/pop, one singleton scope
    /// instance per state, pushed while its state is active and popped when it is not. At the
    /// time this class was written the five scenario overlays were all NON-modal (unclaimed keys
    /// cascaded down to the next overlay and ultimately to the builder page beneath, reproducing
    /// the retired sequential-rung fallthrough); the three survivors (add-part/save/load) are
    /// real ScreenScopes and MODAL for exactly the reason THIS class is — see
    /// ScenarioOverlayScreenScopes.Game.cs's own remarks, which cite this class as ITS precedent. These three are MODAL — the retired host branch
    /// (<c>if (IdeoBuilderOverlays.AnyActive) { RouteKeyDown(...); return; }</c>) never fell through
    /// to the host's own tab/stage handling while an overlay was open, so a live modal scope
    /// reproduces that exclusivity structurally. Each scope's <c>OwnsCancel</c> override is
    /// unconditional true — these are windowless with no real window that could ever legitimately
    /// sit "above the host" in window terms; the two scopes that DO stack above them by design
    /// (<see cref="WindowlessFloatMenuState"/>'s float-menu overlay for the value/gender/frequency/
    /// action sub-pickers, and each scope's own <see cref="TextFieldEditSession"/> for name/title
    /// edits) are reconciled independently and land higher on the stack by ordinary Push order (see
    /// this mirror's placement remarks), not by any fold these scopes need to compute themselves.
    ///
    /// <b>OwnsCancel = OwnsAccept = true on all three.</b> An overlay scope has no <c>Window</c> of
    /// its own — it always sits on top of a window-attached host (the hub Page, the reform Dialog,
    /// or the Archonexus reform Dialog). Declaring both flags true means the shell's consolidated
    /// <c>Window.OnCancelKeyPressed</c>/<c>OnAcceptKeyPressed</c> routers block vanilla's
    /// Escape/Enter handling for WHATEVER window sits beneath, for as long as this scope is the top
    /// live scope — exactly right: an Escape or Enter reaching an overlay editor must never also
    /// close or advance the host underneath it on the same keystroke.
    /// </summary>
    /// <summary>
    /// Reconciles the three overlay scopes in state-mirrored lockstep — the wave-I2
    /// <see cref="ScenarioOverlayScopeMirror"/> shape: push a scope while its state is active and
    /// not yet pushed, pop it when the state goes inactive. The three states are mutually exclusive
    /// by construction today (only one of the <c>IdeoBuilderSectionActions.Activate</c> section
    /// rows can be open at a time), but each is reconciled independently anyway, matching the I2
    /// idiom rather than special-casing the current exclusivity.
    ///
    /// <b>Placement in the dispatcher.</b> Reconciled in the same neighborhood as the three
    /// window-attached ideo hosts — directly after <see cref="ArchonexusReformIdeoScopeMirror"/>'s
    /// call (the only one of the three that still does real per-frame work; IB-5 retired
    /// <c>IdeoReformScope</c>'s and the worldgen hub scope's (then <c>IdeoBuilderHubScope</c>, now
    /// <see cref="IdeoBuilderScreenScope"/>) own mirror classes entirely, since their sole remaining
    /// job — this overlay bookkeeping — moved here) and above the unrelated
    /// Scenario/CustomDifficulty/WorldParams families. All three hosts can open one of these
    /// overlays, so this call belongs with its siblings rather than off in the Scenario
    /// neighborhood. This call MUST land above
    /// <see cref="FloatMenuOverlayScopeMirror"/>'s reconcile (later in the dispatcher — see that
    /// call's own remarks) so a sub-picker float menu always lands ABOVE whichever of these
    /// overlay scopes opened it, exactly like every other windowless-overlay-plus-float-menu pairing
    /// in the shell.
    /// </summary>
    internal static class IdeoOverlayScopesMirror
    {
        private static readonly IdeoPreceptScreenScope preceptSelection = new IdeoPreceptScreenScope();
        private static readonly IdeoTypedPreceptScreenScope typedPrecept = new IdeoTypedPreceptScreenScope();
        private static readonly IdeoDeityScreenScope deityList = new IdeoDeityScreenScope();

        public static void Reconcile()
        {
            ReconcileOne(preceptSelection, IdeoPreceptSelectionState.IsActive);
            ReconcileOne(typedPrecept, IdeoTypedPreceptState.IsActive);
            // No windowless MirrorLive tick is needed by any of these any more: the typed-precept
            // and deity lists both edit through vanilla's own dialogs now (EditPreceptDialogScope /
            // EditDeityDialogScope tick their live buffers from the dialog's own draw), and the
            // precept selection tree has no text fields.
            ReconcileOne(deityList, IdeoDeityListState.IsActive);
        }

        // Push ONLY when the scope is not already on the stack — never re-float an
        // already-present one to the top. When a sub-picker float menu is open, its
        // FloatMenuOverlayScope legitimately sits ABOVE the overlay scope that opened it;
        // an unconditional per-frame Push would re-float the buried overlay scope back to
        // the top every frame, firing FocusScope.OnFocus each time. For these overlay
        // scopes OnFocus re-announces the focused precept (the "returned from the picker"
        // arm), so the unconditional push turned a single re-announcement into a 60/sec
        // flood that buried the screen reader for the whole time the menu stayed open. The
        // genuine return-from-picker re-announcement still fires exactly once, from
        // FocusStackCore.Pop re-focusing the new top when the float menu closes.
        private static void ReconcileOne(FocusScope scope, bool live)
        {
            if (live)
            {
                if (!FocusStack.Contains(scope))
                {
                    FocusStack.Push(scope);
                }
            }
            else
            {
                FocusStack.Pop(scope);
            }
        }
    }
}
