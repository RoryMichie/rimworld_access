using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Modal text-edit session for renaming an animal pen marker. Thin facade over
    /// <see cref="SimpleRenameSession{T}"/>; routes through
    /// <see cref="TextInputController"/> via the unified pipeline.
    /// </summary>
    public static class PenRenameState
    {
        private static readonly SimpleRenameSession<CompAnimalPenMarker> Session = new SimpleRenameSession<CompAnimalPenMarker>(
            targetNoun: "pen marker",
            labelKey: "RimWorldAccess.TextInput.LabelPen",
            errorKey: "RimWorldAccess.Building.Rename.PenError",
            getLabel: marker => marker.RenamableLabel,
            setLabel: (marker, newName) => marker.RenamableLabel = newName);

        public static bool IsActive => Session.IsActive;

        public static void Open(CompAnimalPenMarker marker) => Session.Open(marker);
    }
}
