using System.Collections;
using System.Collections.Generic;

namespace RimWorldAccess.Shell
{
    /// <summary>Tiny local helper: boxes a non-IList IEnumerable (defensive fallback only -- GetSortedPawnStates always actually returns something IList-castable in practice) into a List&lt;object&gt;.</summary>
    internal static class RimTalkDebugEnumerableExtensions
    {
        internal static IList ToBoxedList(this IEnumerable source)
        {
            var list = new List<object>();
            foreach (object item in source)
            {
                list.Add(item);
            }
            return list;
        }
    }
}
