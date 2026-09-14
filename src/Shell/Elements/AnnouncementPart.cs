namespace RimWorldAccess.Shell
{
    /// <summary>
    /// One fragment of the standard focus announcement (<see cref="AnnouncementComposer.ComposeFocus"/>).
    /// Named so the Configure Spoken Announcements screen can list, toggle, and reorder
    /// them without hand-rolling a second copy of the grammar.
    /// </summary>
    public enum AnnouncementPart
    {
        Label,
        Hotkey,
        Role,
        State,
        Level,
        Position,
        Extras,
        Hint,
    }

    /// <summary>PURE: the fragment order every screen uses until the player reorders it.</summary>
    public static class AnnouncementFormat
    {
        /// <summary>
        /// Level → Label → Hotkey → Role → State → Extras → Hint → Position: the shipped
        /// default <see cref="AnnouncementComposer.ComposeFocus"/> falls back to when the
        /// player has saved no order of their own, the order the Configure Spoken
        /// Announcements screen seeds its list from, and where a newly-added part lands
        /// in a saved list that predates it.
        /// </summary>
        public static readonly AnnouncementPart[] DefaultOrder =
        {
            AnnouncementPart.Level,
            AnnouncementPart.Label,
            AnnouncementPart.Hotkey,
            AnnouncementPart.Role,
            AnnouncementPart.State,
            AnnouncementPart.Extras,
            AnnouncementPart.Hint,
            AnnouncementPart.Position,
        };
    }
}
