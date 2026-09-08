#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// ShellDev's Character Editor config guard. The Character Editor mod compat layer is about to wire
    /// ~80 def-editing fields whose values round-trip through that mod's own persisted config directory
    /// (computed exactly as the mod itself computes it: <c>GenFilePaths.ConfigFolderPath</c> with
    /// <c>"Config"</c> replaced by
    /// <c>"CharacterEditor"</c>). This lets a bridge script back that directory up before a smoke
    /// test, restore it afterward, and diff it to confirm a smoke test did not poison the mod's
    /// live settings.
    /// </summary>
    public static partial class ShellDev
    {
        private static string CharEdConfigDir()
        {
            return GenFilePaths.ConfigFolderPath.Replace("Config", "CharacterEditor");
        }

        private static string CharEdConfigBackupDir()
        {
            return CharEdConfigDir() + ".devbackup";
        }

        /// <summary>Copies every file in the live Character Editor config directory into a sibling <c>.devbackup</c> directory, overwriting any existing backup.</summary>
        public static string BackupCharEdConfig()
        {
            string sourceDir = CharEdConfigDir();
            if (!Directory.Exists(sourceDir))
            {
                return "Character Editor config directory not found: " + sourceDir; // l10n-exempt: dev-bridge-only diagnostic text, never player-facing.
            }

            string backupDir = CharEdConfigBackupDir();
            Directory.CreateDirectory(backupDir);

            var sb = new StringBuilder();
            sb.Append("Backed up '").Append(sourceDir).Append("' to '").Append(backupDir).Append("':");

            string[] files = Directory.GetFiles(sourceDir);
            Array.Sort(files, StringComparer.Ordinal);
            if (files.Length == 0)
            {
                sb.Append("\n  (no files)");
            }
            for (int i = 0; i < files.Length; i++)
            {
                string fileName = Path.GetFileName(files[i]);
                string dest = Path.Combine(backupDir, fileName);
                File.Copy(files[i], dest, overwrite: true);
                long size = new FileInfo(dest).Length;
                sb.Append("\n  ").Append(fileName).Append(" (").Append(size).Append(" bytes)");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Copies every file back from the <c>.devbackup</c> directory into the live Character
        /// Editor config directory, overwriting live files and deleting any live file absent from
        /// the backup. WARNING: the mod reads these files only at startup, so an in-session
        /// restore requires a game restart before it takes effect in memory.
        /// </summary>
        public static string RestoreCharEdConfig()
        {
            string liveDir = CharEdConfigDir();
            string backupDir = CharEdConfigBackupDir();
            if (!Directory.Exists(backupDir))
            {
                return "No backup directory found at '" + backupDir + "'; run BackupCharEdConfig first."; // l10n-exempt: dev-bridge-only diagnostic text, never player-facing.
            }

            Directory.CreateDirectory(liveDir);

            var sb = new StringBuilder();
            sb.Append("Restored '").Append(liveDir).Append("' from '").Append(backupDir).Append("'.");
            sb.Append(" WARNING: Character Editor reads these files only at startup; restart the game for this restore to take effect in memory.");

            string[] backupFiles = Directory.GetFiles(backupDir);
            Array.Sort(backupFiles, StringComparer.Ordinal);
            var backupNames = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < backupFiles.Length; i++)
            {
                string fileName = Path.GetFileName(backupFiles[i]);
                backupNames.Add(fileName);
                string dest = Path.Combine(liveDir, fileName);
                File.Copy(backupFiles[i], dest, overwrite: true);
                sb.Append("\n  restored ").Append(fileName);
            }

            string[] liveFiles = Directory.GetFiles(liveDir);
            Array.Sort(liveFiles, StringComparer.Ordinal);
            for (int i = 0; i < liveFiles.Length; i++)
            {
                string fileName = Path.GetFileName(liveFiles[i]);
                if (!backupNames.Contains(fileName))
                {
                    File.Delete(liveFiles[i]);
                    sb.Append("\n  deleted (absent from backup): ").Append(fileName);
                }
            }
            return sb.ToString();
        }

        /// <summary>Byte-for-byte comparison of the live Character Editor config directory against its <c>.devbackup</c>: the "did my smoke poison the settings" check.</summary>
        public static string DiffCharEdConfig()
        {
            string liveDir = CharEdConfigDir();
            string backupDir = CharEdConfigBackupDir();
            if (!Directory.Exists(backupDir))
            {
                return "No backup directory found at '" + backupDir + "'; run BackupCharEdConfig first."; // l10n-exempt: dev-bridge-only diagnostic text, never player-facing.
            }

            var liveFiles = new Dictionary<string, string>(StringComparer.Ordinal);
            if (Directory.Exists(liveDir))
            {
                string[] files = Directory.GetFiles(liveDir);
                for (int i = 0; i < files.Length; i++)
                {
                    liveFiles[Path.GetFileName(files[i])] = files[i];
                }
            }

            var backupFiles = new Dictionary<string, string>(StringComparer.Ordinal);
            {
                string[] files = Directory.GetFiles(backupDir);
                for (int i = 0; i < files.Length; i++)
                {
                    backupFiles[Path.GetFileName(files[i])] = files[i];
                }
            }

            var allNames = new List<string>();
            foreach (string name in liveFiles.Keys)
            {
                if (!allNames.Contains(name))
                {
                    allNames.Add(name);
                }
            }
            foreach (string name in backupFiles.Keys)
            {
                if (!allNames.Contains(name))
                {
                    allNames.Add(name);
                }
            }
            allNames.Sort(StringComparer.Ordinal);

            var sb = new StringBuilder();
            sb.Append("Diff of '").Append(liveDir).Append("' against backup '").Append(backupDir).Append("':");
            if (allNames.Count == 0)
            {
                sb.Append("\n  (no files)");
            }
            foreach (string name in allNames)
            {
                bool inLive = liveFiles.ContainsKey(name);
                bool inBackup = backupFiles.ContainsKey(name);
                if (inLive && inBackup)
                {
                    byte[] liveBytes = File.ReadAllBytes(liveFiles[name]);
                    byte[] backupBytes = File.ReadAllBytes(backupFiles[name]);
                    if (BytesEqual(liveBytes, backupBytes))
                    {
                        sb.Append("\n  ").Append(name).Append(": unchanged");
                    }
                    else
                    {
                        sb.Append("\n  ").Append(name).Append(": changed (old ").Append(backupBytes.Length)
                            .Append(" bytes -> new ").Append(liveBytes.Length).Append(" bytes)");
                    }
                }
                else if (inLive)
                {
                    sb.Append("\n  ").Append(name).Append(": new");
                }
                else
                {
                    sb.Append("\n  ").Append(name).Append(": missing");
                }
            }
            return sb.ToString();
        }

        private static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }
            return true;
        }
    }
}
#endif
