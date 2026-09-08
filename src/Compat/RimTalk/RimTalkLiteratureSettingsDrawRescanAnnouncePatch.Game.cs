namespace RimWorldAccess
{
    /// <summary>Brackets LiteratureSettingsWindow.Draw to isolate the "Rescan Art/Books" button's own batch from unrelated background scans. Applied manually via RimTalkLiteratureCompat.ApplyPatches.</summary>
    internal static class RimTalkLiteratureSettingsDrawRescanAnnouncePatch
    {
        public static void Prefix()
        {
            RimTalkLiteratureCompat.BeginSettingsDrawScanSnapshot();
        }

        public static void Postfix()
        {
            RimTalkLiteratureCompat.AnnounceRescanIfBatched();
        }
    }
}
