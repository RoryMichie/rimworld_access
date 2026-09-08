using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Modal text-edit session for renaming a Zone. Thin facade over
    /// <see cref="SimpleRenameSession{T}"/>; routes through
    /// <see cref="TextInputController"/> via the unified pipeline.
    ///
    /// Reads/writes <see cref="Zone.label"/> directly rather than the
    /// <see cref="Zone.RenamableLabel"/> property: the property getter falls back
    /// to <c>baseLabel</c> when <c>label</c> is null, which the original
    /// implementation deliberately did not do when seeding the edit field.
    /// </summary>
    public static class ZoneRenameState
    {
        private static readonly SimpleRenameSession<Zone> Session = new SimpleRenameSession<Zone>(
            targetNoun: "zone",
            labelKey: "RimWorldAccess.TextInput.LabelZone",
            errorKey: "RimWorldAccess.Building.Rename.ZoneError",
            getLabel: zone => zone.label,
            setLabel: (zone, newName) => zone.label = newName);

        public static bool IsActive => Session.IsActive;

        public static void Open(Zone zone) => Session.Open(zone);
    }
}
