using System;
using System.Collections.Generic;

namespace RimWorldAccess
{
    /// <summary>
    /// One filter row of a Character Editor browser dialog. Exactly one shape is
    /// set: a checkbox (CheckState/Toggle non-null) or a combo (Candidates/
    /// CurrentIndex/SetCandidate/ValueLabel non-null). Delegates wrap the dialog's own
    /// vehicle-A filter writes; the scope never filters locally.
    /// </summary>
    internal sealed class CharEditorBrowserFilter
    {
        public string Label;
        public Func<bool> CheckState;
        public Action Toggle;
        public Func<List<string>> Candidates;
        public Func<int> CurrentIndex;
        public Action<int> SetCandidate;

        /// <summary>
        /// The combo's spoken value. Normally the current candidate's own label with the dialog's
        /// unmatched fallback (<see cref="CharEditorBrowserAdapterBase.Combo"/> builds exactly
        /// that), but a dialog whose current value is not necessarily IN its candidate list reads
        /// that value directly instead.
        /// </summary>
        public Func<string> ValueLabel;

        public bool IsCheckbox { get { return CheckState != null; } }
    }
}
