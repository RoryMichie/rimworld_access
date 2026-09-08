using System;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Editor-level commands shared by every ideoligion section editor — the worldgen builder hub
    /// (<see cref="IdeoBuilderHubState"/>), the in-game reform dialog
    /// (<see cref="RimWorldAccess.Shell.IdeoReformScreenScope"/>), and the Archonexus reform editor
    /// (<see cref="RimWorldAccess.Shell.ArchonexusIdeoScreenScope"/>, via the shared
    /// <see cref="RimWorldAccess.Shell.IdeoEditorRegionCore"/> — formerly the host-independent
    /// <c>IdeoSectionEditorState</c>, retired).
    /// Keeping randomize / save-to-file / ritual-sound preview in one place is what guarantees every
    /// editor behaves identically; each host supplies its own "refresh after the command" callback
    /// since only the host knows how to re-announce its list.
    ///
    /// All commands operate on whichever <see cref="Ideo"/> is passed in.
    /// </summary>
    public static class IdeoEditorCommands
    {
        private static Sustainer ritualPreviewSustainer;

        /// <summary>Whether the ritual-ambience preview sustainer is currently playing — read by the builder hub's preview toggle row to describe its Checkbox state.</summary>
        public static bool RitualPreviewPlaying => ritualPreviewSustainer != null && !ritualPreviewSustainer.Ended;

        /// <summary>
        /// Replaces the ENTIRE ideoligion (memes, name, description, precepts, the lot) via the same
        /// foundation re-init vanilla's "Randomize all" button uses. Caller is responsible for the
        /// confirmation prompt and for refreshing/announcing afterwards. Returns false if the action
        /// was blocked (tutorial) or the ideo was null.
        /// </summary>
        public static bool RandomizeAll(Ideo ideo)
        {
            if (ideo == null) return false;
            if (!TutorSystem.AllowAction("ConfiguringIdeo")) return false;
            var parms = new IdeoGenerationParms(
                IdeoUIUtility.FactionForRandomization(ideo),
                forceNoExpansionIdeo: false,
                null, null, null,
                classicExtra: false,
                forceNoWeaponPreference: false,
                ideo.Fluid);
            ideo.foundation.Init(parms);
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            return true;
        }

        public static void SaveIdeoligion(Ideo ideo)
        {
            if (ideo == null) return;
            // Vehicle A: the real save dialog vanilla's Save button opens (IdeoUIUtility
            // DoIdeoSaveLoad), file list and overwrite rows included; FileListScope hosts it.
            Find.WindowStack.Add(new Dialog_IdeoList_Save(ideo));
        }

        public static void ToggleRitualPreview(Ideo ideo)
        {
            if (ritualPreviewSustainer != null && !ritualPreviewSustainer.Ended)
            {
                StopRitualPreview();
                TolkHelper.Speak("RimWorldAccess.Ideology.RitualSound.Stopped".Loc((string)"RitualAmbienceSound".Translate()));
                return;
            }
            var sound = ideo?.SoundOngoingRitual;
            if (sound == null)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }
            // Force on-camera playback so the sustainer is actually audible; MaintainRitualPreview
            // then ducks the game music (in a running game).
            var info = SoundInfo.OnCamera(MaintenanceType.PerFrame);
            info.forcedPlayOnCamera = true;
            info.testPlay = true;
            ritualPreviewSustainer = sound.TrySpawnSustainer(info);
            TolkHelper.Speak("RimWorldAccess.Ideology.RitualSound.Playing".Loc((string)"RitualAmbienceSound".Translate()));
        }

        /// <summary>
        /// Keeps the ritual-sound preview alive; call every frame from the host patch. Wrapped so a
        /// sound-system failure can never propagate and stall the host's input handling.
        /// </summary>
        public static void MaintainRitualPreview()
        {
            if (ritualPreviewSustainer == null) return;
            try
            {
                if (ritualPreviewSustainer.Ended)
                {
                    ritualPreviewSustainer = null;
                    return;
                }
                ritualPreviewSustainer.Maintain();
                // ForceSilenceFor lives only on MusicManagerPlay, and Find.MusicManagerPlay casts
                // Current.Root to Root_Play — which throws pre-game (the main-menu builder runs in a
                // Root_Entry). Only duck the music when actually in a running game.
                if (Current.ProgramState == ProgramState.Playing)
                    Find.MusicManagerPlay?.ForceSilenceFor(0.1f);
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimWorld Access] Ritual sound preview stopped after an error: {ex.Message}");
                StopRitualPreview();
            }
        }

        public static void StopRitualPreview()
        {
            if (ritualPreviewSustainer != null)
            {
                if (!ritualPreviewSustainer.Ended) ritualPreviewSustainer.End();
                ritualPreviewSustainer = null;
            }
        }
    }
}
