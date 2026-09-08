using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Keyboard support for the Archonexus "choose new colony site" world-tile
    /// pick (MoveColonyUtility.PickNewColonyTile → Find.TilePicker.StartTargeting
    /// with allowEscape: false). Navigation comes from WorldNavigationState as
    /// usual; this state announces the pick, gates Enter through the game's own
    /// TilePicker validator + tileChosen callbacks (so an invalid tile is rejected
    /// by the vanilla rules), and explains on Escape that the player cannot back
    /// out of this stage of the quest.
    /// </summary>
    public static class NewColonyTilePickState
    {
        public static bool IsActive { get; private set; }

        private static FieldInfo validatorField;
        private static FieldInfo tileChosenField;

        public static void Open()
        {
            if (IsActive) return;
            IsActive = true;
            TolkHelper.SpeakData(
                (string)"ChooseNextColonySite".Translate()
                + ". " + (string)"RimWorldAccess.Archonexus.TilePick.OpenInstructions".Translate(),
                SpeechPriority.High);
        }

        public static void Close()
        {
            IsActive = false;
        }

        /// <summary>
        /// Escape handler: PickNewColonyTile sets allowEscape: false, so the
        /// player genuinely cannot cancel. Make that audible instead of silent.
        /// Internal: called from TargetingScope's
        /// newColonyTile.cancel claim, which replaces the legacy handler's
        /// Escape branch verbatim.
        /// </summary>
        internal static void AnnounceCannotCancel()
        {
            TolkHelper.Speak("RimWorldAccess.Archonexus.TilePick.MustChooseValid".Loc(), SpeechPriority.High);
        }

        /// <summary>
        /// Internal: called from TargetingScope's
        /// newColonyTile.confirm claim. The legacy handler's own TilePicker
        /// staleness check (self-close when Find.TilePicker stops) moved into
        /// TargetingScopeMirror.Reconcile — the mirror-reconcile-liveness
        /// pattern, which runs every frame instead of only on
        /// keypress.
        /// </summary>
        internal static void ConfirmCurrentTile()
        {
            if (!WorldNavigationState.IsActive)
            {
                TolkHelper.Speak("RimWorldAccess.Guard.WorldNavigationNotActive".Loc(), SpeechPriority.High);
                return;
            }

            PlanetTile tile = WorldNavigationState.CurrentSelectedTile;
            if (!tile.Valid)
            {
                WorldObject selected = Find.WorldSelector?.SingleSelectedObject;
                if (selected != null && selected.Tile.Valid)
                    tile = selected.Tile;
                else
                {
                    TolkHelper.Speak("RimWorldAccess.Guard.NoValidTileSelected".Loc(), SpeechPriority.High);
                    return;
                }
            }

            try
            {
                if (validatorField == null) validatorField = VanillaAccess.GetField(typeof(TilePicker), "validator");
                if (tileChosenField == null) tileChosenField = VanillaAccess.GetField(typeof(TilePicker), "tileChosen");

                var validator = validatorField?.GetValue(Find.TilePicker) as Func<PlanetTile, bool>;
                var tileChosen = tileChosenField?.GetValue(Find.TilePicker) as Action<PlanetTile>;
                if (validator == null || tileChosen == null)
                {
                    TolkHelper.Speak("RimWorldAccess.Gravships.Destination.CannotAccessCallbacks".Loc(), SpeechPriority.High);
                    return;
                }

                // Validator runs TileFinder.IsValidTileForNewSettlement; on failure
                // it raises a Messages.Message which the mod already announces.
                if (!validator(tile))
                    return;

                var stopIntMethod = VanillaAccess.GetMethod(typeof(TilePicker), "StopTargetingInt");
                stopIntMethod?.Invoke(Find.TilePicker, null);

                tileChosen(tile);

                // Tile committed; the picker is done. Close now so the state doesn't linger into
                // the reform-ideoligion screen or the freshly loaded map (where it would otherwise
                // only self-clear on the next keystroke).
                Close();
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimWorld Access] Error confirming new colony tile: {ex}");
                TolkHelper.Speak("RimWorldAccess.Archonexus.TilePick.ErrorSelectingTile".Loc(), SpeechPriority.High);
            }
        }
    }

    /// <summary>
    /// Activates NewColonyTilePickState whenever the game opens the Archonexus
    /// new-colony tile picker. Patching MoveColonyUtility.PickNewColonyTile as a
    /// Postfix is the cleanest entry point: it runs immediately after
    /// Find.TilePicker.StartTargeting returns, so the picker is already Active
    /// and uniquely identifies this caller (we don't have to sniff labels).
    /// </summary>
    [HarmonyPatch(typeof(MoveColonyUtility), nameof(MoveColonyUtility.PickNewColonyTile))]
    public static class NewColonyTilePickPatch
    {
        [HarmonyPostfix]
        static void Postfix()
        {
            NewColonyTilePickState.Open();
        }
    }
}
