using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// File-IO helpers shared between <see cref="WindowlessScenarioSaveState"/> and
    /// <see cref="WindowlessScenarioLoadState"/>: both screens list the same on-disk custom scenario
    /// files and format the same last-write-time stamp for display.
    /// </summary>
    public static class ScenarioFileHelper
    {
        /// <summary>
        /// Loads every custom scenario file's <see cref="SaveFileInfo"/>, sorted by
        /// last write time, most recent first. A file that fails to load is logged
        /// and skipped rather than aborting the whole load.
        /// </summary>
        public static List<SaveFileInfo> LoadCustomScenarioFiles()
        {
            var files = new List<SaveFileInfo>();

            foreach (FileInfo file in GenFilePaths.AllCustomScenarioFiles)
            {
                try
                {
                    var saveInfo = new SaveFileInfo(file);
                    saveInfo.LoadData();
                    files.Add(saveInfo);
                }
                catch (Exception ex)
                {
                    Log.Error($"[RimWorld Access] Exception loading scenario file {file.Name}: {ex}");
                }
            }

            return files.OrderByDescending(f => f.LastWriteTime).ToList();
        }

        /// <summary>Formats a last-write-time stamp for display, honoring the 12/24-hour clock preference.</summary>
        public static string FormatDateTime(DateTime dateTime)
        {
            return Prefs.TwelveHourClockMode
                ? dateTime.ToString("yyyy-MM-dd h:mm tt")
                : dateTime.ToString("yyyy-MM-dd HH:mm");
        }
    }
}
