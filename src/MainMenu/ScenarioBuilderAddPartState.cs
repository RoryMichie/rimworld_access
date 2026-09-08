using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Data/mutation source for the Add Part picker in the Scenario Builder — every
    /// addable <see cref="ScenPartDef"/> for the current scenario. Follows the
    /// StartingPawnState pattern: lifecycle (<see cref="Open"/>/
    /// <see cref="Confirm"/>/<see cref="Cancel"/>) plus the row source
    /// (<see cref="AvailableParts"/>) only. The old <see cref="FlatListCursor"/>-driven
    /// nav/typeahead/announce surface (NavigatePrevious/Next/JumpToStart/End/
    /// AnnounceCurrentPart/HandleTypeaheadChar/…) is gone — it is now
    /// <see cref="RimWorldAccess.Shell.ScenarioAddPartScreenScope"/>'s own
    /// <see cref="RimWorldAccess.Shell.ScreenScope"/> row model and shared typeahead.
    /// </summary>
    public static class ScenarioBuilderAddPartState
    {
        public static bool IsActive { get; private set; }

        private static Scenario currentScenario;
        private static Action<ScenPartDef> onPartSelected;

        private static readonly List<ScenPartDef> availableParts = new List<ScenPartDef>();

        /// <summary>Every addable part for the current scenario, in the shown order (label-ordered, PlayerAddRemovable).</summary>
        public static IReadOnlyList<ScenPartDef> AvailableParts => availableParts;

        /// <summary>
        /// Opens the add part menu.
        /// </summary>
        public static void Open(Scenario scenario, Action<ScenPartDef> onSelected)
        {
            currentScenario = scenario;
            onPartSelected = onSelected;

            BuildPartsList();

            if (availableParts.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.AddPart.NothingToAdd".Loc());
                return;
            }

            IsActive = true;
        }

        /// <summary>Enter on a part row: confirm it and hand it to the builder's callback.</summary>
        public static void Confirm(ScenPartDef selected)
        {
            if (!IsActive) return;
            Close();
            onPartSelected?.Invoke(selected);
        }

        /// <summary>Escape: cancel the picker with no selection.</summary>
        public static void Cancel()
        {
            if (!IsActive) return;
            Close();
            onPartSelected?.Invoke(null);
        }

        private static void Close()
        {
            IsActive = false;
            currentScenario = null;
            availableParts.Clear();
        }

        /// <summary>
        /// Builds the list of parts that can be added to the scenario.
        /// </summary>
        private static void BuildPartsList()
        {
            availableParts.Clear();

            if (currentScenario == null) return;

            // Get all addable parts, filtered by PlayerAddRemovable
            var addable = ScenarioMaker.AddableParts(currentScenario)
                .Where(p => p.PlayerAddRemovable)
                .OrderBy(p => p.label)
                .ToList();

            availableParts.AddRange(addable);
        }
    }
}
