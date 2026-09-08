namespace RimWorldAccess
{
    /// <summary>
    /// Lifecycle-only residual for the IdeoBuilder hub (Page_ConfigureIdeo / Page_ConfigureFluidIdeo).
    ///
    /// Everything this class used to own — the section list, the two-tab navigation, typeahead,
    /// the ideoligion-list browsing, the read-only viewer delegation, all key handling, and every
    /// announcement — moved onto <see cref="RimWorldAccess.Shell.IdeoBuilderScreenScope"/>.
    /// What remains is just the session-boundary flag
    /// <see cref="StateResetRegistry"/> and <see cref="IdeoBuilderHubPatch"/>'s residual prefix
    /// depend on: is the builder currently open, and how to close it. The scope reads
    /// <c>page.ideo</c> live on every refresh, so there is no section cache left to key or reset.
    /// </summary>
    public static class IdeoBuilderHubState
    {
        public static bool IsActive { get; private set; }

        public static void EnsureOpen()
        {
            IsActive = true;
        }

        public static void Close()
        {
            IsActive = false;
        }
    }
}
