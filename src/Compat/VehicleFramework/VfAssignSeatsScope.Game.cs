using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The keyboard focus scope for Vehicle Framework's <c>Vehicles.World.Dialog_AssignSeats</c>
    /// (assigns colonists to a vehicle's role handlers during caravan formation). All reflection
    /// lives in <see cref="VfAssignSeatsCompat"/>; this scope only reads its typed methods.
    ///
    /// Regions: region 0 is unassigned colonists, regions 1..N are one per vehicle role handler in
    /// the vehicle's own <c>handlers</c> order (the sighted group order), plus the automatic
    /// Buttons region. A handler region's name carries in words what the sighted underline/color
    /// code shows: VF's own "{role} (n remaining)", plus "needs N more to operate" when the role
    /// lacks its minimum crew, or "full" when every slot is taken.
    ///
    /// Sighted drag-and-drop lets a player choose a SPECIFIC role for an unassigned colonist; the
    /// keyboard path has no drag gesture, so Enter on an unassigned colonist opens a windowless
    /// float menu offering VF's own "Add to role" auto-pick plus one "Assign to {role}" option per
    /// handler with an open seat. Enter on an assigned colonist's row removes them UNLESS they are
    /// already physically aboard (<see cref="VfAssignSeatsCompat.IsInsideVehicle"/>), matching VF's
    /// own row, which draws no remove button for those pawns.
    ///
    /// <see cref="CaptureWindowButtons"/> is false: this dialog draws its own per-row
    /// Widgets.ButtonText add/remove calls, so blanket capture would over-collect.
    /// <see cref="DeclaredActions"/> reproduces the real bottom buttons in their own visual order.
    ///
    /// <see cref="OwnsAccept"/>/<see cref="OwnsCancel"/> stay at ScreenScope's defaults
    /// (true/false), and Accept must never be overridden to false: Dialog_AssignSeats does NOT
    /// override OnAcceptKeyPressed and leaves closeOnAccept at its Window default of true, so an
    /// unclaimed Enter would simply CLOSE the dialog without ever calling FinalizeSeats, silently
    /// discarding the player's assignments. Escape needs no such guard (closeOnCancel is set true
    /// by the dialog), but closing without Confirm leaves CaravanHelper.assignedSeats untouched --
    /// VF only writes it inside FinalizeSeats -- so <see cref="OnPop"/> announces that discard.
    /// </summary>
    internal sealed class VfAssignSeatsScope : ScreenScope
    {
        private readonly Window dialog;
        private readonly List<object> handlers = new List<object>();
        private readonly List<Pawn> unassignedPawns = new List<Pawn>();
        private readonly List<List<Pawn>> handlerPawns = new List<List<Pawn>>();
        private readonly List<ScreenAction> actions = new List<ScreenAction>();
        private bool announcedOpen;
        private bool dirty;
        private bool confirmed;

        public VfAssignSeatsScope(Window dialog)
        {
            this.dialog = dialog;
            // First-letter mnemonic on "Confirm".Translate().
            Claim("vfAssignSeats.confirm", e => HandleConfirm());
        }

        public override string Name => "vf-assign-seats";

        /// <summary>Shared cross-region typeahead: both colonists and role rows are named items.</summary>
        protected override bool EnableTypeahead => true;

        // ------------------------------------------------------------------
        // ScreenScope content contract.
        // ------------------------------------------------------------------

        protected override int ContentRegionCount => 1 + handlers.Count;

        protected override string ContentRegionName(int region)
        {
            if (region == 0)
                return "RimWorldAccess.Compat.Vf.SeatsColonistsRegion".Translate();

            object handler = HandlerAt(region);
            if (handler == null)
                return "";

            int slots = VfAssignSeatsCompat.Slots(handler);
            int assigned = VfAssignSeatsCompat.AssignedCount(dialog, handler);
            int remaining = Math.Max(slots - assigned, 0);
            string name = VfAssignSeatsCompat.RoleLabel(handler) + " (" + remaining + ")";

            if (VfAssignSeatsCompat.RequiredForCaravan(handler) && assigned < VfAssignSeatsCompat.SlotsToOperate(handler))
                name += "RimWorldAccess.Compat.Vf.SeatsRoleNeeds".Translate(VfAssignSeatsCompat.SlotsToOperate(handler) - assigned);
            else if (assigned >= slots)
                name += "RimWorldAccess.Compat.Vf.SeatsRoleFull".Translate();

            return name;
        }

        protected override int ContentItemCount(int region)
        {
            if (region == 0)
                return unassignedPawns.Count;
            int index = region - 1;
            return index >= 0 && index < handlerPawns.Count ? handlerPawns[index].Count : 0;
        }

        /// <summary>
        /// Handler set is stable for the dialog's lifetime (populated once); the unassigned list
        /// and every handler's row list are rebuilt every refresh since seat assignments change
        /// on nearly every action.
        /// </summary>
        protected override void RefreshContent()
        {
            if (!VfAssignSeatsCompat.Ready)
                return;

            if (handlers.Count == 0)
                handlers.AddRange(VfAssignSeatsCompat.GetHandlers(dialog));

            unassignedPawns.Clear();
            foreach (Pawn pawn in VfAssignSeatsCompat.GetPawns(dialog))
            {
                if (!VfAssignSeatsCompat.IsAssigned(dialog, pawn))
                    unassignedPawns.Add(pawn);
            }

            List<(Pawn pawn, object handler)> assignments = VfAssignSeatsCompat.Assignments(dialog);
            handlerPawns.Clear();
            for (int i = 0; i < handlers.Count; i++)
            {
                object handler = handlers[i];
                var rows = new List<Pawn>();
                foreach (var pair in assignments)
                {
                    if (ReferenceEquals(pair.handler, handler))
                        rows.Add(pair.pawn);
                }
                handlerPawns.Add(rows);
            }
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            Pawn pawn = PawnAt(region, index);
            if (pawn == null)
                return d;

            if (region != 0 && VfAssignSeatsCompat.IsInsideVehicle(dialog, pawn))
                d.Label = pawn.LabelCap + "RimWorldAccess.Compat.Vf.SeatsAlreadyAboard".Translate();
            else
                d.Label = pawn.LabelCap;
            // Enter opens the assign menu (region 0) or removes/refuses an assignment (other regions), so these rows keep Enter (DefaultAcceptInertness).
            d.KeepsAccept = true;
            return d;
        }

        /// <summary>
        /// Region 0 (unassigned colonist): opens the keyboard drop-in for vanilla's drag gesture.
        /// A handler region row: removes the pawn, unless they are already aboard the vehicle --
        /// VF draws no remove button for those, so they stay in place and the row explains why.
        /// </summary>
        protected override void ActivateContentItem(int region, int index)
        {
            Pawn pawn = PawnAt(region, index);
            if (pawn == null)
                return;

            if (region == 0)
            {
                OpenAssignMenu(pawn);
                return;
            }

            if (VfAssignSeatsCompat.IsInsideVehicle(dialog, pawn))
            {
                TolkHelper.SpeakData(pawn.LabelShortCap + "RimWorldAccess.Compat.Vf.SeatsAlreadyAboard".Translate());
                return;
            }

            object handler = HandlerAt(region);
            string roleLabel = VfAssignSeatsCompat.RoleLabel(handler);
            VfAssignSeatsCompat.RemoveAssignment(dialog, pawn);
            dirty = true;
            RefreshModel();
            int remaining = Math.Max(VfAssignSeatsCompat.Slots(handler) - VfAssignSeatsCompat.AssignedCount(dialog, handler), 0);
            TolkHelper.SpeakData((string)"RimWorldAccess.Compat.Vf.SeatsRemovedRemaining".Translate(pawn.LabelShortCap, roleLabel, remaining));
        }

        /// <summary>The per-row add/remove buttons are the only other window-drawn buttons; declaring the bottom buttons instead avoids over-capturing them.</summary>
        protected override bool CaptureWindowButtons => false;

        /// <summary>Shift+Enter presses Confirm from anywhere on this screen: the one-chord proceed. Closing without it silently discards every assignment (see class remarks), so naming it as the proceed button is unambiguous.</summary>
        protected override string DefaultAcceptActionId
        {
            get { return "vfAssignSeats.confirm"; }
        }

        /// <summary>The dialog's four real bottom buttons, in DoBottomButtons' own draw order.</summary>
        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                actions.Add(new ScreenAction(
                    "CancelButton".Translate().ToString(),
                    delegate { OwnedWindow?.Close(); },
                    SharedMenuGrammar.Cancel));
                actions.Add(new ScreenAction(
                    "RimWorldAccess.Compat.Vf.SeatsAutoAssignButton".Translate().ToString(),
                    HandleAutoAssignButton));
                actions.Add(new ScreenAction(
                    "RimWorldAccess.Compat.Vf.SeatsClearSeatsButton".Translate().ToString(),
                    HandleClearSeats));
                actions.Add(new ScreenAction(
                    "Confirm".Translate().ToString(),
                    HandleConfirm,
                    "vfAssignSeats.confirm"));
                return actions;
            }
        }

        // ------------------------------------------------------------------
        // Lifecycle.
        // ------------------------------------------------------------------

        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen)
                return;
            announcedOpen = true;

            string vehicleLabel = VfAssignSeatsCompat.GetVehicle(dialog)?.LabelShortCap ?? "";
            TolkHelper.SpeakData((string)"RimWorldAccess.Compat.Vf.SeatsOpen".Translate(
                vehicleLabel, unassignedPawns.Count, handlers.Count));
            AnnounceCurrentItem();
        }

        /// <summary>
        /// Escape (or the Cancel button) closes this dialog without ever calling FinalizeSeats, so
        /// VF itself silently discards any assignment made this session. Announced whenever it
        /// actually happened, so a screen reader user isn't left believing their work was saved.
        /// </summary>
        public override void OnPop()
        {
            if (dirty && !confirmed)
                TolkHelper.SpeakData((string)"RimWorldAccess.Compat.Vf.SeatsDiscarded".Translate());
            base.OnPop();
        }

        // ------------------------------------------------------------------
        // Region 0: the keyboard drop-in for vanilla's drag gesture.
        // ------------------------------------------------------------------

        private void OpenAssignMenu(Pawn pawn)
        {
            Pawn capturedPawn = pawn;
            var options = new List<FloatMenuOption>();

            options.Add(new FloatMenuOption("RimWorldAccess.Compat.Vf.SeatsAddToRoleOption".Translate(), delegate
            {
                HandleAutoAssign(capturedPawn);
            }));

            foreach (object handler in handlers)
            {
                if (!VfAssignSeatsCompat.HandlerHasOpenSlot(dialog, handler))
                    continue;
                object capturedHandler = handler;
                string roleLabel = VfAssignSeatsCompat.RoleLabel(capturedHandler);
                int remaining = VfAssignSeatsCompat.Slots(capturedHandler) - VfAssignSeatsCompat.AssignedCount(dialog, capturedHandler);
                options.Add(new FloatMenuOption(
                    "RimWorldAccess.Compat.Vf.SeatsAssignTo".Translate(roleLabel, remaining),
                    delegate { HandleAssign(capturedPawn, capturedHandler); }));
            }

            // Each option speaks its own assignment result, so suppress the generic "{label} selected" echo.
            WindowlessFloatMenuState.Open(options, colonistOrders: false, announceSelection: false, titleText: pawn.LabelShortCap);
        }

        private void HandleAutoAssign(Pawn pawn)
        {
            bool ok = VfAssignSeatsCompat.TryAutoAssign(dialog, pawn, out VfAssignSeatsCompat.AssignOutcome outcome, out object handler);
            if (ok)
                dirty = true;
            RefreshModel();
            if (!ok)
            {
                TolkHelper.Speak("RimWorldAccess.Compat.Vf.SeatsNoRoleAvailable".Loc(), SpeechPriority.High);
                return;
            }
            AnnounceAssignResult(pawn, handler, outcome);
        }

        private void HandleAssign(Pawn pawn, object handler)
        {
            VfAssignSeatsCompat.AssignOutcome outcome = VfAssignSeatsCompat.TryAssign(dialog, pawn, handler);
            if (outcome != VfAssignSeatsCompat.AssignOutcome.Rejected)
                dirty = true;
            RefreshModel();
            AnnounceAssignResult(pawn, handler, outcome);
        }

        /// <summary>
        /// One announcement per action. The compat's caution/reject branches already mirror VF's
        /// own Messages.Message, which the message pipeline speaks on its own, so this adds only
        /// what that channel does NOT carry: the assignment itself plus the role's remaining seat
        /// count, computed AFTER the caller's RefreshModel so it reflects the assignment just made.
        /// </summary>
        private void AnnounceAssignResult(Pawn pawn, object handler, VfAssignSeatsCompat.AssignOutcome outcome)
        {
            if (outcome == VfAssignSeatsCompat.AssignOutcome.Rejected)
                return;

            int remaining = Math.Max(VfAssignSeatsCompat.Slots(handler) - VfAssignSeatsCompat.AssignedCount(dialog, handler), 0);
            TolkHelper.SpeakData((string)"RimWorldAccess.Compat.Vf.SeatsAssignedRemaining".Translate(
                pawn.LabelShortCap, VfAssignSeatsCompat.RoleLabel(handler), remaining));
        }

        // ------------------------------------------------------------------
        // Buttons region.
        // ------------------------------------------------------------------

        private void HandleAutoAssignButton()
        {
            VfAssignSeatsCompat.AutoAssign(dialog);
            dirty = true;
            RefreshModel();
            TolkHelper.SpeakData((string)"RimWorldAccess.Compat.Vf.SeatsAutoAssigned".Translate(VfAssignSeatsCompat.Assignments(dialog).Count));
        }

        private void HandleClearSeats()
        {
            VfAssignSeatsCompat.ClearSeats(dialog);
            dirty = true;
            RefreshModel();
            TolkHelper.SpeakData((string)"RimWorldAccess.Compat.Vf.SeatsCleared".Translate());
        }

        private void HandleConfirm()
        {
            bool ok = VfAssignSeatsCompat.TryConfirm(dialog, out string _);
            if (!ok)
            {
                // TryConfirm already mirrored VF's reject message via Messages.Message, which the
                // message pipeline speaks on its own; speaking it here would double-announce.
                return;
            }

            string vehicleLabel = VfAssignSeatsCompat.GetVehicle(dialog)?.LabelShortCap ?? "";
            confirmed = true;
            OwnedWindow?.Close();
            TolkHelper.SpeakData((string)"RimWorldAccess.Compat.Vf.SeatsConfirmed".Translate(vehicleLabel));
        }

        // ------------------------------------------------------------------
        // Row lookups.
        // ------------------------------------------------------------------

        private object HandlerAt(int region)
        {
            int index = region - 1;
            return index >= 0 && index < handlers.Count ? handlers[index] : null;
        }

        private Pawn PawnAt(int region, int index)
        {
            if (region == 0)
                return index >= 0 && index < unassignedPawns.Count ? unassignedPawns[index] : null;
            int handlerIndex = region - 1;
            if (handlerIndex < 0 || handlerIndex >= handlerPawns.Count)
                return null;
            List<Pawn> rows = handlerPawns[handlerIndex];
            return index >= 0 && index < rows.Count ? rows[index] : null;
        }
    }
}
