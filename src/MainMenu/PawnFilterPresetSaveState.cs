using System;
using System.Collections.Generic;
using Verse;
using RimWorldAccess.Shell;

namespace RimWorldAccess
{
    /// <summary>
    /// Data and mutation vehicles for the save-a-pawn-filter-preset prompt: a "Save as"
    /// name slot (row 0) plus one row per existing preset (overwrite targets).
    ///
    /// Shares the S1 <see cref="StartingPawnState"/> shape, the same promotion
    /// <see cref="WindowlessScenarioSaveState"/> had: lifecycle
    /// (<see cref="Open"/>/<see cref="Close"/>), data (<see cref="ExistingPresets"/>,
    /// <see cref="CurrentName"/>, <see cref="NameSpec"/>) and the two write vehicles
    /// (<see cref="SaveNewName"/>, <see cref="SaveToExistingPreset"/>). The browse cursor,
    /// the jump-to-preset typeahead and the <see cref="TextFieldEditSession"/> itself now
    /// live on <see cref="RimWorldAccess.Shell.FilterPresetSaveScope"/>, whose shared
    /// typeahead and row model replace the hand-rolled ones.
    /// </summary>
    public static class PawnFilterPresetSaveState
    {
        public static bool IsActive { get; private set; }

        private static PawnFilter filterToSave;
        private static List<string> existingPresets = new List<string>();
        private static string currentName = string.Empty;

        // MUTATION-C: MustBeFilename already gates commit on GenText.IsValidFilename
        // (Verse/GenText.cs L317-324), which itself enforces a 40-char cap; maxLength here
        // used to hand-pick 64, letting the buffer grow past what MustBeFilename would ever
        // accept. 40 mirrors that same cap so the field's own limit and its validator agree.
        public static readonly TextFieldSpec NameSpec = new TextFieldSpec(
            labelKey: "RimWorldAccess.TextInput.LabelFilename",
            maxLength: 40,
            minLength: 1,
            mustBeFilename: true);

        /// <summary>Every saved preset name, in serializer order (the overwrite targets).</summary>
        public static IReadOnlyList<string> ExistingPresets => existingPresets;

        /// <summary>The "Save as" slot's current typed name.</summary>
        public static string CurrentName => currentName;

        public static void Open(PawnFilter filter)
        {
            filterToSave = filter;
            currentName = "MyPreset";
            ReloadPresets();
            IsActive = true;
            // The opening announcement is the scope's own job (FilterPresetSaveScope's
            // ComposeOpenAnnouncement) — see this class's remarks.
        }

        public static void Close()
        {
            IsActive = false;
            filterToSave = null;
            existingPresets.Clear();
        }

        private static void ReloadPresets()
        {
            existingPresets = PawnFilterPresetSerializer.GetPresetNames();
        }

        /// <summary>Live buffer -> backing store, called by the scope's own edit session apply.</summary>
        public static void SetCurrentName(string value)
        {
            currentName = value ?? string.Empty;
        }

        /// <summary>Enter-confirm on the name edit (row 0): save the filter under the typed name.</summary>
        public static void SaveNewName()
        {
            string name = currentName;
            if (string.IsNullOrWhiteSpace(name))
            {
                TolkHelper.Speak("RimWorldAccess.PawnFilter.PresetSave.NameEmpty".Loc());
                return;
            }

            try
            {
                int existingIndex = existingPresets.FindIndex(n =>
                    string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
                if (existingIndex >= 0)
                {
                    PawnFilterPresetSerializer.OverwritePreset(filterToSave, name, existingIndex);
                    Close();
                    TolkHelper.Speak("RimWorldAccess.PawnFilter.PresetSave.PresetOverwritten".Loc(name));
                }
                else
                {
                    PawnFilterPresetSerializer.SavePreset(filterToSave, name);
                    Close();
                    TolkHelper.Speak("RimWorldAccess.PawnFilter.PresetSave.PresetSavedAs".Loc(name));
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorld Access] Error saving preset: {ex}");
                TolkHelper.Speak("RimWorldAccess.PawnFilter.PresetSave.ErrorSaving".Loc(ex.Message));
            }
        }

        /// <summary>Enter on an existing-preset row: overwrite that preset.</summary>
        public static void SaveToExistingPreset(int index)
        {
            if (index < 0 || index >= existingPresets.Count)
            {
                TolkHelper.Speak("RimWorldAccess.PawnFilter.PresetSave.InvalidSelection".Loc());
                return;
            }

            string name = existingPresets[index];
            try
            {
                PawnFilterPresetSerializer.OverwritePreset(filterToSave, name, index);
                Close();
                TolkHelper.Speak("RimWorldAccess.PawnFilter.PresetSave.PresetOverwritten".Loc(name));
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorld Access] Error saving preset: {ex}");
                TolkHelper.Speak("RimWorldAccess.PawnFilter.PresetSave.ErrorSaving".Loc(ex.Message));
            }
        }

        public static void HandleCancel()
        {
            Close();
            TolkHelper.Speak("RimWorldAccess.UI.Cancelled".Loc());
        }
    }
}
