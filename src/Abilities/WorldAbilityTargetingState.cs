using System;
using System.Linq;
using System.Reflection;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// State management for world map ability targeting (e.g., Farskip).
    /// Follows the pattern from TransportPodLaunchState.
    ///
    /// Two entry points. <see cref="Open"/> takes a vanilla
    /// <see cref="RimWorld.Ability"/> and can therefore offer per-tile validity
    /// from the ability itself. <see cref="OpenExternal"/> serves an ability
    /// framework that is not vanilla's — it calls Find.WorldTargeter.BeginTargeting
    /// directly, so there is no Ability to consult and the caller supplies only a
    /// spoken label. Confirm and cancel are identical for both: the destination is
    /// committed by invoking WorldTargeter's own callback, never by
    /// reimplementing what that callback does.
    /// </summary>
    public static class WorldAbilityTargetingState
    {
        private static bool isActive = false;
        private static Ability currentAbility = null;

        // Set only by OpenExternal: the caster to return the camera to on cancel,
        // for sessions that carry no vanilla Ability to read it from.
        private static Pawn externalCaster = null;

        /// <summary>
        /// Gets whether world ability targeting mode is currently active.
        /// </summary>
        public static bool IsActive => isActive;

        /// <summary>
        /// Gets the current ability being targeted.
        /// </summary>
        public static Ability CurrentAbility => currentAbility;

        /// <summary>
        /// Opens world ability targeting state.
        /// </summary>
        public static void Open(Ability ability)
        {
            if (ability == null)
            {
                Log.Warning("RimWorld Access: WorldAbilityTargetingState.Open called with null ability");
                return;
            }

            currentAbility = ability;
            isActive = true;

            // Announce targeting start, including affected pawns for AOE abilities
            // (the AOE is centered on the caster and can't be changed during world targeting)
            var sb = new System.Text.StringBuilder();
            sb.Append("RimWorldAccess.Abilities.World.TargetingStart".Translate(ability.def.LabelCap));

            if (ability.def.HasAreaOfEffect && ability.pawn?.Map != null)
            {
                var affected = AbilityTargetingHelper.GetAffectedPawns(
                    ability, ability.pawn.Position, ability.pawn.Map);
                if (affected.Count > 0)
                {
                    var names = affected.Select(p => p.LabelShort).ToCommaList(useAnd: true);
                    string bringingKey = affected.Count == 1
                        ? "RimWorldAccess.Abilities.World.BringingOne"
                        : "RimWorldAccess.Abilities.World.BringingMany";
                    sb.Append(bringingKey.Translate(affected.Count, names));
                }
            }

            sb.Append("RimWorldAccess.Abilities.World.SelectDestination".Translate());
            TolkHelper.SpeakData(sb.ToString(), SpeechPriority.Normal);
        }

        /// <summary>
        /// Opens world ability targeting for a session started outside vanilla's
        /// Ability system — a mod verb that calls Find.WorldTargeter.BeginTargeting
        /// itself, where no <see cref="RimWorld.Ability"/> exists to describe the
        /// session. Confirm, cancel and the TargetingScope claims are shared with
        /// <see cref="Open"/>; only the per-tile validity clause is unavailable,
        /// because that clause comes from the vanilla ability's own
        /// WorldMapExtraLabel/ValidateGlobalTarget.
        /// </summary>
        internal static void OpenExternal(string label, Pawn caster)
        {
            currentAbility = null;
            externalCaster = caster;
            isActive = true;

            var sb = new System.Text.StringBuilder();
            if (!string.IsNullOrEmpty(label))
                sb.Append("RimWorldAccess.Abilities.World.TargetingStart".Translate(label));
            sb.Append("RimWorldAccess.Abilities.World.SelectDestination".Translate());
            TolkHelper.SpeakData(sb.ToString(), SpeechPriority.Normal);
        }

        /// <summary>
        /// Closes world ability targeting state.
        /// </summary>
        public static void Close()
        {
            isActive = false;
            currentAbility = null;
            externalCaster = null;
        }

        /// <summary>
        /// Confirms the currently selected world tile as the destination.
        /// Internal: called from TargetingScope's
        /// worldAbilityTargeting.confirm claim, which replaces the legacy
        /// handler's Enter branch. That branch's own "skip if
        /// WindowlessFloatMenuState is active" guard is now redundant —
        /// ShellDispatcherPatch's blanket LegacyKeyboardOverlayActive
        /// stand-down already keeps the whole shell (this claim included)
        /// from dispatching while a float menu is up. The legacy handler's own
        /// WorldTargeter staleness self-close does not need a mirror-reconcile
        /// replacement — this state already has a dedicated
        /// WorldTargeter.StopTargeting postfix (AbilityTargetingPatch.cs)
        /// that closes it properly.
        /// </summary>
        internal static void ConfirmCurrentDestination()
        {
            if (!WorldNavigationState.IsActive)
            {
                TolkHelper.Speak("RimWorldAccess.Guard.WorldNavigationNotActive".Loc(), SpeechPriority.High);
                return;
            }

            PlanetTile selectedTile = WorldNavigationState.CurrentSelectedTile;
            if (!selectedTile.Valid)
            {
                TolkHelper.Speak("RimWorldAccess.Guard.NoValidTileSelected".Loc(), SpeechPriority.High);
                return;
            }

            if (Find.WorldTargeter == null || !Find.WorldTargeter.IsTargeting)
            {
                TolkHelper.Speak("RimWorldAccess.Abilities.World.NotActive".Loc(), SpeechPriority.High);
                return;
            }

            try
            {
                // Get the action field from WorldTargeter using reflection
                var actionField = typeof(WorldTargeter).GetField("action", BindingFlags.NonPublic | BindingFlags.Instance);
                if (actionField == null)
                {
                    TolkHelper.Speak("RimWorldAccess.Abilities.World.CannotAccessAction".Loc(), SpeechPriority.High);
                    return;
                }

                var action = actionField.GetValue(Find.WorldTargeter) as Func<GlobalTargetInfo, bool>;
                if (action == null)
                {
                    TolkHelper.Speak("RimWorldAccess.Abilities.World.NoActionAvailable".Loc(), SpeechPriority.High);
                    return;
                }

                // Create GlobalTargetInfo for the selected tile
                GlobalTargetInfo targetInfo;
                var worldObjects = Find.WorldObjects?.ObjectsAt(selectedTile)?.ToList();
                if (worldObjects != null && worldObjects.Count > 0)
                {
                    // Use the first world object (typically a settlement or site)
                    targetInfo = new GlobalTargetInfo(worldObjects[0]);
                }
                else
                {
                    // Just a tile with no world object
                    targetInfo = new GlobalTargetInfo(selectedTile);
                }

                // Invoke the action callback
                bool completed = action(targetInfo);

                if (completed)
                {
                    // Target was accepted
                    Find.WorldTargeter.StopTargeting();
                    TolkHelper.Speak("RimWorldAccess.Abilities.World.TargetSelected".Loc(), SpeechPriority.Normal);
                }
                // If not completed, target was rejected or a float menu was created
            }
            catch (Exception ex)
            {
                Log.Warning($"RimWorld Access: Error confirming world ability destination: {ex}");
                TolkHelper.Speak("RimWorldAccess.Abilities.World.ErrorSelecting".Loc(), SpeechPriority.High);
            }
        }

        /// <summary>
        /// Cancels world ability targeting and returns to map. Internal:
        /// called from TargetingScope's worldAbilityTargeting.cancel claim.
        /// </summary>
        internal static void CancelTargeting()
        {
            // Cache the return target
            Pawn caster = currentAbility?.pawn ?? externalCaster;
            Map returnMap = caster?.Map;

            // Stop world targeting
            if (Find.WorldTargeter != null && Find.WorldTargeter.IsTargeting)
            {
                Find.WorldTargeter.StopTargeting();
            }

            // Close our state
            Close();
            TolkHelper.Speak("RimWorldAccess.Abilities.World.Cancelled".Loc(), SpeechPriority.Normal);

            // Return to map view
            if (returnMap != null && caster != null)
            {
                CameraJumper.TryJump(caster);
            }
        }

        /// <summary>
        /// Gets destination info for a world tile.
        /// Uses the ability's WorldMapExtraLabel for validity information.
        /// </summary>
        public static string GetDestinationInfo(int tile)
        {
            if (!isActive || currentAbility == null)
                return null;

            GlobalTargetInfo targetInfo;
            var worldObjects = Find.WorldObjects?.ObjectsAt(tile)?.ToList();
            if (worldObjects != null && worldObjects.Count > 0)
            {
                targetInfo = new GlobalTargetInfo(worldObjects[0]);
            }
            else
            {
                targetInfo = new GlobalTargetInfo(tile);
            }

            // Get the extra label from the ability (e.g., "No ally to skip to" for Farskip)
            var extraLabel = currentAbility.WorldMapExtraLabel(targetInfo);
            if (!extraLabel.NullOrEmpty())
            {
                return extraLabel;
            }

            // Check if the ability can target this tile
            if (!currentAbility.ValidateGlobalTarget(targetInfo))
            {
                return "RimWorldAccess.Abilities.World.InvalidTarget".Translate();
            }

            return null;
        }

    }
}
