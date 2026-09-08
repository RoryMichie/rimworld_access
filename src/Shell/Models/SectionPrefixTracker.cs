using System;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Tracks which section name was last landed on so the name is spoken
    /// exactly once, folded into the utterance of the row that crosses into
    /// it. Composition stays with the caller (plain "Section. Row" at some
    /// sites, a translate template at others).
    ///
    /// trackSilentRows: whether a row with an empty section still updates the
    /// state — when true, leaving a section for sectionless rows and coming
    /// back re-announces it; when false it does not (the generic reader's
    /// behavior). speakFirstLanding: whether the first landing after Reset
    /// announces its section — false where a rebuild lands the cursor back on
    /// the same logical row and re-announcing would be noise
    /// (IdeoTypedPreceptScreenScope's behavior).
    ///
    /// PURE: links into the test project.
    /// </summary>
    public sealed class SectionPrefixTracker
    {
        private readonly bool trackSilentRows;
        private readonly bool speakFirstLanding;
        private string last;
        private bool armed;

        public SectionPrefixTracker(bool trackSilentRows, bool speakFirstLanding)
        {
            this.trackSilentRows = trackSilentRows;
            this.speakFirstLanding = speakFirstLanding;
        }

        public void Reset()
        {
            last = null;
            armed = false;
        }

        /// <summary>Records the landing; returns the section name to speak, or null for a silent landing.</summary>
        public string Cross(string section)
        {
            bool wasArmed = armed;
            string previous = last;
            if (trackSilentRows || !string.IsNullOrEmpty(section))
            {
                last = section;
                armed = true;
            }
            if (string.IsNullOrEmpty(section))
                return null;
            if (wasArmed ? string.Equals(section, previous, StringComparison.Ordinal) : !speakFirstLanding)
                return null;
            return section;
        }

        /// <summary>Seeds the state without speaking (a caller that lands the cursor itself and wants the next landing silent).</summary>
        public void Prime(string section)
        {
            last = section;
            armed = true;
        }
    }
}
