using System;
using System.Collections.Generic;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Data and mutation vehicles for the saved-pawn-filter picker.
    ///
    /// Shares the S1 <see cref="StartingPawnState"/> shape, the same promotion
    /// <see cref="WindowlessScenarioLoadState"/> had: lifecycle plus
    /// <see cref="PresetNames"/> plus the load/delete vehicles. The cursor, the
    /// typeahead and every announcement now live on
    /// <see cref="RimWorldAccess.Shell.FilterPresetLoadScope"/>.
    ///
    /// <b>The picker stays active under its own delete confirmation.</b> It used to flip
    /// itself INACTIVE while the confirm was open, which was how the retired ladder kept the
    /// two blocks from both handling Enter; the confirm scope is modal now, so the flag can
    /// stay honest — and keeping it up means the picker is merely covered rather than closed
    /// and reopened, so the cursor stays on the row the player was standing on when the
    /// confirm goes away.
    /// </summary>
    public static class PawnFilterPresetLoadState
    {
        public static bool IsActive { get; private set; }

        private static List<string> presetNames = new List<string>();
        private static Action<PawnFilter> onPresetLoaded;

        /// <summary>Every saved preset name, in serializer order.</summary>
        public static IReadOnlyList<string> PresetNames => presetNames;

        public static void Open(Action<PawnFilter> onLoaded)
        {
            onPresetLoaded = onLoaded;
            ReloadPresets();

            if (presetNames.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.PawnFilter.PresetLoad.NoSavedPresets".Loc());
                onPresetLoaded?.Invoke(null);
                return;
            }

            IsActive = true;
            // The opening announcement is the scope's own job (FilterPresetLoadScope's
            // ComposeOpenAnnouncement) — see this class's remarks.
        }

        public static void Close()
        {
            IsActive = false;
            presetNames.Clear();
            onPresetLoaded = null;
        }

        private static void ReloadPresets()
        {
            presetNames = PawnFilterPresetSerializer.GetPresetNames();
        }

        /// <summary>Enter on a preset row: load it and hand it to the editor that opened this picker.</summary>
        public static void LoadSelected(int index)
        {
            if (index < 0 || index >= presetNames.Count)
            {
                TolkHelper.Speak("RimWorldAccess.PawnFilter.PresetLoad.NoPresetSelected".Loc());
                return;
            }

            string name = presetNames[index];

            try
            {
                var loadedFilter = PawnFilterPresetSerializer.LoadPreset(index);
                var callback = onPresetLoaded;
                Close();
                if (loadedFilter != null)
                {
                    TolkHelper.Speak("RimWorldAccess.PawnFilter.PresetLoad.Loaded".Loc(name));
                    callback?.Invoke(loadedFilter);
                }
                else
                {
                    TolkHelper.Speak("RimWorldAccess.PawnFilter.PresetLoad.ErrorLoading".Loc());
                    callback?.Invoke(null);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorld Access] Error loading preset: {ex}");
                TolkHelper.Speak("RimWorldAccess.PawnFilter.PresetLoad.ErrorLoadingDetail".Loc(ex.Message));
            }
        }

        /// <summary>Delete on a preset row: opens the confirmation, which owns the deletion itself.</summary>
        public static void RequestDelete(int index)
        {
            if (index < 0 || index >= presetNames.Count)
                return;

            string name = presetNames[index];
            TolkHelper.Speak("RimWorldAccess.PawnFilter.PresetLoad.DeleteConfirm".Loc(name));
            PawnFilterPresetDeleteConfirmState.Open(index, name, ReloadAfterDelete);
        }

        /// <summary>
        /// Fired once the confirmation closes, either way. The row list is re-read so the
        /// scope's next refresh sees it; the picker closes itself only when nothing is left
        /// to pick.
        /// </summary>
        private static void ReloadAfterDelete()
        {
            ReloadPresets();
            if (presetNames.Count > 0)
                return;
            TolkHelper.Speak("RimWorldAccess.PawnFilter.PresetLoad.NoneRemaining".Loc());
            var callback = onPresetLoaded;
            Close();
            callback?.Invoke(null);
        }

        /// <summary>Escape: cancel out with the null-result callback the editor waits on.</summary>
        public static void HandleCancel()
        {
            var callback = onPresetLoaded;
            Close();
            TolkHelper.Speak("RimWorldAccess.UI.Cancelled".Loc());
            callback?.Invoke(null);
        }
    }

    /// <summary>
    /// The delete-preset confirmation inside the picker: two buttons and the deletion itself.
    /// <see cref="RimWorldAccess.Shell.FilterPresetDeleteConfirmScope"/> presents them as the
    /// two rows of its Buttons region.
    /// </summary>
    public static class PawnFilterPresetDeleteConfirmState
    {
        public static bool IsActive { get; private set; }

        private static int indexToDelete;
        private static string nameToDelete;
        private static Action onComplete;

        public static void Open(int index, string name, Action onCompleteCallback)
        {
            indexToDelete = index;
            nameToDelete = name;
            onComplete = onCompleteCallback;
            IsActive = true;
        }

        public static void Confirm()
        {
            if (!IsActive) return;

            try
            {
                PawnFilterPresetSerializer.DeletePreset(indexToDelete);
                TolkHelper.Speak("RimWorldAccess.PawnFilter.PresetLoad.Deleted".Loc(nameToDelete));
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorld Access] Error deleting preset: {ex}");
                TolkHelper.Speak("RimWorldAccess.PawnFilter.PresetLoad.ErrorDeleting".Loc(ex.Message));
            }

            Close();
            onComplete?.Invoke();
        }

        public static void Cancel()
        {
            if (!IsActive) return;

            TolkHelper.Speak("RimWorldAccess.PawnFilter.PresetLoad.DeleteCancelled".Loc());
            Close();
            onComplete?.Invoke();
        }

        private static void Close()
        {
            IsActive = false;
            nameToDelete = null;
            onComplete = null;
        }

        /// <summary>
        /// Silent teardown reset (wave-I law 7, I7): drops the flag WITHOUT
        /// invoking the completion callback (which would re-read the load
        /// state's list) or speaking — for the host PostClose patches only.
        /// </summary>
        internal static void ForceClose()
        {
            Close();
        }
    }
}
