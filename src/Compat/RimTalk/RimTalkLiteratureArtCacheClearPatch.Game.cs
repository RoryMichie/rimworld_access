using Verse;

namespace RimWorldAccess
{
    /// <summary>Postfix on ArtDescriptionCache.Clear() -- announces the count Literature's own "Clear Art Cache" button only logs. Applied manually via RimTalkLiteratureCompat.ApplyPatches.</summary>
    internal static class RimTalkLiteratureArtCacheClearPatch
    {
        public static void Postfix(int __result)
        {
            TolkHelper.SpeakData("RimWorldAccess.Compat.Literature.ArtCacheCleared".Translate(__result));
        }
    }
}
