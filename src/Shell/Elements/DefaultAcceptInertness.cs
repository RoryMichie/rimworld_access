namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Pure inert test for the Enter double-press proceed confirm
    /// (<see cref="ScreenScope.DefaultAcceptActionId"/>): true when a fresh
    /// <see cref="ElementDescription"/> carries no Enter behavior of its own — no
    /// control semantics, explicitly read-only, a radio button that is already
    /// selected, or a slider/stepper that Enter does not open an edit session on.
    /// A radio's selection is checked on either channel
    /// (<see cref="ElementDescription.Check"/> or <see cref="ElementDescription.Selected"/>)
    /// since different describe paths across the mod set only one or the other.
    ///
    /// An element keeps Enter only if Enter does something real on it. A plain
    /// slider owns Left/Right and nothing else, so its Enter belongs to the
    /// screen's proceed offer; the numeric-entry sliders and steppers whose Enter
    /// begins an announced edit session declare that with
    /// <see cref="ElementDescription.EntersEditOnAccept"/> and keep Enter; a row that
    /// sets <see cref="ElementDescription.KeepsAccept"/> is never inert.
    /// </summary>
    public static class DefaultAcceptInertness
    {
        public static bool IsInert(ElementDescription d)
        {
            if (d == null)
                return false;
            if (d.KeepsAccept)
                return false;
            if (d.ReadOnly || d.Role == ElementRole.None)
                return true;
            if (d.Role == ElementRole.Slider || d.Role == ElementRole.Stepper)
                return !d.EntersEditOnAccept;
            return d.Role == ElementRole.RadioButton
                && (d.Check == CheckState.Checked || d.Selected == true);
        }
    }
}
