using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Lifecycle, data and mutation vehicles for the saved-ideoligion picker
    /// (Dialog_IdeoList_Load). Navigation, typeahead and announcements belong to
    /// <see cref="RimWorldAccess.Shell.IdeoLoadScope"/>, which is a real
    /// <see cref="RimWorldAccess.Shell.ScreenScope"/>. This class was slimmed to a leaner state shape
    /// at the same time: the FlatListCursor, the search buffer, and the row/opening announcements all
    /// moved onto the chassis, and what remains is the file enumeration plus the two mutations
    /// (<see cref="LoadSelected"/>, <see cref="RequestDelete"/>), each on vanilla's own vehicle.
    ///
    /// <see cref="IsActive"/> is still the lifecycle flag <see cref="StateResetRegistry"/>
    /// and the dialog's PostOpen/PostClose patches drive (see IdeoLoadPatch.cs).
    /// </summary>
    public static class IdeoLoadState
    {
        public static bool IsActive { get; private set; }

        /// <summary>The dialog's OWN file list (the same instance — see <see cref="RebuildFromDialog"/>), in vanilla's order.</summary>
        internal static IReadOnlyList<SaveFileInfo> Files => files;

        private static Dialog_IdeoList_Load dialog;
        private static List<SaveFileInfo> files = new List<SaveFileInfo>();

        private static readonly System.Reflection.FieldInfo FilesField =
            AccessTools.Field(typeof(Dialog_FileList), "files");
        private static readonly System.Reflection.MethodInfo DoFileInteractionMethod =
            AccessTools.Method(typeof(Dialog_IdeoList_Load), "DoFileInteraction");
        private static readonly System.Reflection.MethodInfo ReloadFilesMethod =
            AccessTools.Method(typeof(Dialog_IdeoList), "ReloadFiles");

        public static void EnsureOpen(Dialog_IdeoList_Load d)
        {
            // Reference equality alone is the right guard. After Close sets
            // IsActive=false, the window-stack's snapshot iteration still calls
            // DoWindowContents one more time on the just-removed dialog in the
            // SAME frame. Keying off IsActive there would re-open on every
            // close. Compare the dialog reference (which we deliberately keep
            // through Close) so the teardown re-render is a no-op.
            if (System.Object.ReferenceEquals(dialog, d))
                return;
            dialog = d;
            IsActive = true;
            RebuildFromDialog();
        }

        public static void Close()
        {
            IsActive = false;
            files.Clear();
            // Keep `dialog` set so EnsureOpen's reference check can detect the
            // post-close snapshot re-render (see EnsureOpen). The reference is
            // replaced naturally when a new load dialog opens.
        }

        private static void RebuildFromDialog()
        {
            files = (FilesField.GetValue(dialog) as List<SaveFileInfo>) ?? new List<SaveFileInfo>();
        }

        /// <summary>Hands the file name to vanilla's own DoFileInteraction so its version/meme-compatibility checks still run.</summary>
        internal static void LoadSelected(int index)
        {
            if (index < 0 || index >= files.Count) return;
            string fileName = Path.GetFileNameWithoutExtension(files[index].FileName);
            DoFileInteractionMethod.Invoke(dialog, new object[] { fileName });
        }

        /// <summary>
        /// Opens the SAME vanilla ConfirmDelete dialog the per-row delete button opens
        /// (decompiled Dialog_FileList.cs:105-113) and, on confirmation, runs the same two
        /// steps its own inline delegate runs before handing control back to
        /// <paramref name="onDeleted"/> for the cursor and the announcement.
        /// </summary>
        internal static void RequestDelete(int index, Action onDeleted)
        {
            if (index < 0 || index >= files.Count) return;
            var fileInfo = files[index].FileInfo;
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "ConfirmDelete".Translate(fileInfo.Name),
                delegate
                {
                    fileInfo.Delete();
                    ReloadFilesMethod.Invoke(dialog, null);
                    RebuildFromDialog();
                    TolkHelper.SpeakData($"{fileInfo.Name}, {(string)"RimWorldAccess.Ideology.Builder.Status.Removed".Translate()}");
                    if (onDeleted != null)
                    {
                        onDeleted();
                    }
                },
                destructive: true));
        }
    }
}
