using System;
using HarmonyLib;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Keeps vanilla's automatic wall-attachment facing keyed to the keyboard cursor.
    ///
    /// A wall lamp or torch (<c>building.isAttachment</c>) has to face a wall, so vanilla turns it
    /// for the player at the tail of <c>Designator_Place.SelectedUpdate</c> (decompiled
    /// RimWorld/Designator_Place.cs:159-162). It decides from <see cref="UI.MouseCell"/> and it
    /// mutates <c>placingRot</c>, so it changes what gets built, not just what is drawn. That runs
    /// during our keyboard sessions too: <c>ArchitectState.EnterPlacementMode</c> selects through
    /// <c>DesignatorManager.Select</c> (ArchitectState.cs:227), and <c>DesignatorManagerUpdate</c>
    /// drives <c>SelectedUpdate</c> off the manager's designator every frame (decompiled
    /// Verse/DesignatorManager.cs:142-148, RimWorld/MapInterface.cs:121) — so the lamp was being
    /// turned to face whatever the parked pointer happened to sit next to, fighting the facing the
    /// keyboard player chose and desyncing <c>ArchitectState.CurrentRotation</c> from the designator.
    ///
    /// While our session owns placement, vanilla's own turn is run once at the keyboard cursor and
    /// the vanilla body's own rotation write is then discarded (mask and restore around the vanilla
    /// body). Every draw in that body is left untouched, and with no keyboard session owning
    /// placement — or while a mouse drag is in flight, where the pointer is what the player is
    /// aiming — this patch does nothing at all, so a mouse player gets vanilla exactly.
    ///
    /// The patch sits on the declaring type: <c>Designator_Build</c> and <c>Designator_Install</c>
    /// override SelectedUpdate but both call <c>base.SelectedUpdate()</c>.
    /// </summary>
    [HarmonyPatch(typeof(Designator_Place), nameof(Designator_Place.SelectedUpdate))]
    internal static class AttachmentFacingPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Designator_Place __instance, out Rot4 __state)
        {
            __state = Rot4.Invalid;
            try
            {
                if (!OwnsPlacement(__instance) || !BuildingReflection.HasPlacingRotField)
                    return;

                TurnToCursor(__instance);

                // Whatever it faces now is what the player gets: the vanilla body is about to decide
                // again from the pointer, and the postfix throws that answer away.
                __state = BuildingReflection.GetPlacingRot(__instance);
            }
            catch (Exception ex)
            {
                __state = Rot4.Invalid;
                ModLogger.LimitedError("Attachment facing error", ex);
            }
        }

        /// <summary>
        /// Runs the turn from the cursor-move path, so the facing is spoken BEFORE the cell the
        /// cursor landed on rather than a frame after it. The prefix above only sees a cursor that
        /// moved during OnGUI on the NEXT frame's Update, which is what used to put "Facing West"
        /// behind the position announcement. Calling here makes the turn already done by then, so
        /// the prefix finds nothing left to do and nothing is said twice; a cursor moved by any
        /// other route still gets the prefix's own late announcement.
        /// </summary>
        internal static void SpeakTurnAtCursor()
        {
            try
            {
                if (!BuildingReflection.HasPlacingRotField)
                    return;
                Designator_Place place = ArchitectPlacementInputPatch.GetActiveDesignator() as Designator_Place;
                if (place == null || !OwnsPlacement(place))
                    return;

                TurnToCursor(place);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Attachment facing error", ex);
            }
        }

        /// <summary>
        /// Vanilla's own attachment turn, run once at the keyboard cursor and announced when it
        /// actually moves the ghost.
        /// </summary>
        private static void TurnToCursor(Designator_Place designator)
        {
            Map map = designator.Map;
            IntVec3 cell = MapNavigationState.CurrentCursorPosition;
            if (map == null || !cell.InBounds(map))
                return;

            Rot4 before = BuildingReflection.GetPlacingRot(designator);
            if (!NeedsAttachmentTurn(designator, cell, before, map))
                return;

            // HandleRotation reads UI.MouseCell() itself, so vanilla's loop runs under the
            // shell's cursor override and picks the wall next to the keyboard cursor.
            DevToolTargeting.WithCursorOverride(cell, () =>
            {
                BuildingReflection.HandleAttachmentRotation(designator, RotationDirection.Clockwise);
                return true;
            });

            Rot4 after = BuildingReflection.GetPlacingRot(designator);
            if (after == before)
                return;

            // Keep the rotate key counting from where the building actually faces.
            ArchitectState.CurrentRotation = after;
            // The ghost turning is the sighted player's only cue that this happened.
            TolkHelper.Speak("RimWorldAccess.Building.Architect.NowFacing".Loc(
                ArchitectState.GetRotationName(after)));
        }

        [HarmonyPostfix]
        public static void Postfix(Designator_Place __instance, Rot4 __state)
        {
            if (__state == Rot4.Invalid)
                return;
            if (BuildingReflection.GetPlacingRot(__instance) != __state)
                BuildingReflection.SetPlacingRot(__instance, __state);
        }

        /// <summary>
        /// True only for the designator our own keyboard placement session is driving. A mouse-driven
        /// Select never enters that session (DesignatorManagerPatch.MouseOriginatedSelect), and a
        /// live drag is the pointer's own gesture.
        /// </summary>
        private static bool OwnsPlacement(Designator_Place designator)
        {
            if (!ShapePlacementState.IsActive && !ArchitectState.IsInPlacementMode)
                return false;
            if (Find.DesignatorManager == null || Find.DesignatorManager.Dragger.Dragging)
                return false;
            return designator == ArchitectPlacementInputPatch.GetActiveDesignator();
        }

        /// <summary>
        /// Vanilla's own tail condition, read-only, evaluated at the keyboard cursor: an attachment
        /// def whose current facing has no wall behind it, on a cell that has a wall on some side.
        /// The last clause is what vanilla's private <c>HasPotentialAttachment</c> asks, through the
        /// same public <see cref="GenConstruct.GetWallAttachedTo"/> it asks it with.
        /// </summary>
        private static bool NeedsAttachmentTurn(Designator_Place designator, IntVec3 cell, Rot4 rot, Map map)
        {
            if (!(designator.PlacingDef is ThingDef thingDef)
                || thingDef.building == null
                || !thingDef.building.isAttachment)
            {
                return false;
            }
            if (GenConstruct.GetWallAttachedTo(cell, rot, map) != null)
                return false;

            foreach (Rot4 candidate in Rot4.AllRotations)
            {
                if (GenConstruct.GetWallAttachedTo(cell, candidate, map) != null)
                    return true;
            }
            return false;
        }
    }
}
