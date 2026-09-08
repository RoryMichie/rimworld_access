using System.Collections.Generic;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// One row of Vehicle Framework's own stats report, flattened out of a
    /// <c>Vehicles.VehicleStatDrawEntry</c> at read time so nothing downstream has to reflect.
    /// <see cref="Hyperlinks"/> is a snapshot rather than the live enumerable because VF's
    /// cost-list entries hand out <c>BuildableDef.tmpHyperlinks</c> itself, a shared static list
    /// the next SpecialDisplayStats call clears.
    /// </summary>
    internal sealed class VehicleStatRow
    {
        public string Label;
        public string Value;
        public string CategoryLabel;
        public string Explanation;
        public List<Dialog_InfoCard.Hyperlink> Hyperlinks = new List<Dialog_InfoCard.Hyperlink>();
    }
}
