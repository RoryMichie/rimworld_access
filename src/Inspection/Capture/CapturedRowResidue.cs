using System.Collections.Generic;

namespace RimWorldAccess
{
    /// <summary>
    /// Whether a folded captured row says anything BEYOND the controls it is
    /// made of. <c>CapturedRowFolder</c> joins a visual row's rendered fragments
    /// with ", ", so a band that is nothing but its own buttons folds to
    /// "Rename colonist. button, Banish. button, Open expertise panel. button" —
    /// text that carries no context at all, only the three controls repeated
    /// verbatim from their own fragments. A presenter that reads such a row as a
    /// summary line and THEN reads each control speaks everything twice
    /// (observed on the colonist inspect tab).
    ///
    /// The technique mirrors <c>ScreenScope.RemainderIsUnmirrored</c>: subtract
    /// each member's own fragment from the row text and judge what is left.
    /// PURE (no Unity/Verse types), so it links into the test project.
    /// </summary>
    public static class CapturedRowResidue
    {
        /// <summary>
        /// The row text left after each member's own rendered fragment is
        /// removed. Fragments are removed as literal substrings — they came from
        /// this same row text by construction.
        /// </summary>
        public static string Strip(string rowText, IReadOnlyList<string> fragments)
        {
            string residue = rowText ?? "";
            if (fragments == null)
            {
                return residue;
            }
            for (int i = 0; i < fragments.Count; i++)
            {
                string fragment = fragments[i];
                if (!string.IsNullOrEmpty(fragment))
                {
                    residue = residue.Replace(fragment, "");
                }
            }
            return residue;
        }

        /// <summary>
        /// Whether the residue holds real context worth speaking. Judged on
        /// letters and digits only: what survives stripping a fully-operable
        /// band is the joiner's own punctuation and whitespace (", , "), which
        /// says nothing, while a real neighbouring label ("Hunger", "3 of 5")
        /// always brings letters or digits with it.
        /// </summary>
        public static bool HasContext(string rowText, IReadOnlyList<string> fragments)
        {
            string residue = Strip(rowText, fragments);
            for (int i = 0; i < residue.Length; i++)
            {
                if (char.IsLetterOrDigit(residue[i]))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
