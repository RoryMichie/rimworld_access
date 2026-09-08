namespace RimWorldAccess
{
    /// <summary>Counts every MapArtScanner.Scan(Map) call, any trigger -- see RimTalkLiteratureCompat's batch-snapshot remarks. Applied manually via RimTalkLiteratureCompat.ApplyPatches.</summary>
    internal static class RimTalkLiteratureMapArtScanCountPatch
    {
        public static void Postfix()
        {
            RimTalkLiteratureCompat.NoteMapArtScanned();
        }
    }
}
