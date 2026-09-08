using Verse;

namespace RimWorldAccess
{
    /// <summary>Postfix on BookSynopsisCache.Clear() -- announces the count Literature's own "Clear Book Cache" button only logs. Applied manually via RimTalkLiteratureCompat.ApplyPatches.</summary>
    internal static class RimTalkLiteratureBookCacheClearPatch
    {
        public static void Postfix(int __result)
        {
            TolkHelper.SpeakData("RimWorldAccess.Compat.Literature.BookCacheCleared".Translate(__result));
        }
    }
}
