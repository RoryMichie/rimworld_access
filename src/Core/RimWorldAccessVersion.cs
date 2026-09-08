namespace RimWorldAccess
{
    /// <summary>
    /// The single source of truth for the mod's own version number at runtime.
    ///
    /// This constant is the anchor the update-detection feature compares against the
    /// player's persisted <see cref="RimWorldAccessSettings.LastSeenVersion"/> to decide
    /// whether to show the "What's New" message on launch.
    ///
    /// It MUST stay in sync with two other places, which the build enforces via
    /// scripts/check_version_sync.py (the build fails if any disagree):
    ///   - About/About.xml  &lt;modVersion&gt;
    ///   - CHANGELOG.md      the top-most "## [x.y.z]" heading
    ///
    /// Versioning is semver-lite (MAJOR.MINOR.PATCH): most releases bump PATCH,
    /// a notable new feature bumps MINOR, and a MAJOR bump is rare.
    /// </summary>
    public static class RimWorldAccessVersion
    {
        public const string Current = "2.0.0";
    }
}
