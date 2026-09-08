using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Data and mutation backend for the windowless, read-only "What's New" reader, whose regions
    /// and cursor all live on <see cref="Shell.WhatsNewScope"/>. Nothing here touches cursor state
    /// or speaks an opening announcement; the scope's own <c>OnFocus</c>, guarded by
    /// <see cref="OpenGeneration"/>, does that once per fresh open.
    ///
    /// Announcements come from <see cref="WhatsNewCatalog"/>. One is unread until its version is
    /// recorded in <see cref="RimWorldAccessSettings.ReadAnnouncementVersions"/>, and viewing it
    /// marks it read. The reader is surfaced automatically on the main menu while any remain
    /// unread, and can be reopened on demand from the main menu and the pause menu.
    /// </summary>
    public static class WhatsNewState
    {
        /// <summary>The changelog page on the docs site, opened by the Open Changelog button.</summary>
        private const string ChangelogUrl = "https://rimworldaccess.com/reference/changelog/";

        private static bool isActive = false;
        private static int currentIndex = 0;
        private static string[] lines = new string[0];
        private static int openGeneration = 0;

        // Set by CheckForUpdateAtStartup, consumed once by NotifyMainMenuReached. shownThisLaunch
        // stops a re-show when the player returns to the main menu later in the same session.
        private static bool pendingUpdate = false;
        private static bool shownThisLaunch = false;

        public static bool IsActive => isActive;

        /// <summary>Bumped on every fresh <see cref="Open"/>, so the scope can reset its one-shot opening-announcement guard.</summary>
        internal static int OpenGeneration => openGeneration;

        /// <summary>Number of arrow-able content lines in the currently shown announcement.</summary>
        public static int LineCount => lines.Length;

        /// <summary>One content line of the currently shown announcement, or "" out of range.</summary>
        public static string LineAt(int index)
        {
            return index >= 0 && index < lines.Length ? lines[index] : "";
        }

        /// <summary>Total announcements in the catalog (for button labels).</summary>
        public static int CatalogCount => Catalog.Count;

        private static IReadOnlyList<Announcement> Catalog => WhatsNewCatalog.Announcements;

        /// <summary>
        /// Flags whether any announcement is unread, for surfacing when the player reaches the main
        /// menu. A brand-new install has an empty read list, so every announcement is unread.
        /// </summary>
        public static void CheckForUpdateAtStartup()
        {
            pendingUpdate = AnyUnread();
        }

        /// <summary>
        /// Opens the reader for pending announcements once per launch, or, with auto-popups
        /// disabled, speaks a brief notice and marks them read so it does not repeat every launch.
        /// </summary>
        public static void NotifyMainMenuReached()
        {
            if (!pendingUpdate || shownThisLaunch)
                return;

            shownThisLaunch = true;
            pendingUpdate = false;

            var settings = RimWorldAccessMod_Settings.Settings;
            if (settings == null || settings.ShowWhatsNewOnUpdate)
            {
                Open();
            }
            else
            {
                string newestVersion = Catalog.Count > 0 ? Catalog[Catalog.Count - 1].Version : RimWorldAccessVersion.Current;
                TolkHelper.Speak("RimWorldAccess.WhatsNew.UpdateWhisper".Loc(newestVersion), SpeechPriority.High);
                MarkAllReadInternal(silent: true);
            }
        }

        /// <summary>
        /// Opens the reader on the oldest unread announcement, or the newest overall once caught
        /// up. Refuses silently on an empty catalog: nothing is pushed, so there is nothing to say.
        /// </summary>
        public static void Open()
        {
            if (Catalog.Count == 0)
                return;

            isActive = true;
            openGeneration++;

            int start = OldestUnreadIndex();
            if (start < 0)
                start = Catalog.Count - 1; // caught up: show the newest
            ShowAnnouncement(start);
        }

        /// <summary>Closes the reader silently.</summary>
        public static void Close()
        {
            isActive = false;
            lines = new string[0];
        }

        /// <summary>Closes the reader and announces the closure.</summary>
        public static void CloseMenu()
        {
            Close();
            TolkHelper.Speak("RimWorldAccess.WhatsNew.Closed".Loc());
        }

        /// <summary>
        /// Shows the announcement at a catalog index: rebuilds its lines and marks it read. Touches
        /// no cursor state and announces nothing — the scope repositions and speaks afterward.
        /// </summary>
        public static void ShowAnnouncement(int index)
        {
            currentIndex = Mathf.Clamp(index, 0, Catalog.Count - 1);
            Announcement announcement = Catalog[currentIndex];
            lines = BuildLines(announcement);
            MarkRead(announcement.Version);
        }

        /// <summary>
        /// The header line: the version, plus "announcement X of Y" only when more than one exists,
        /// so a lone announcement never speaks a redundant "1 of 1".
        /// </summary>
        public static string HeaderAnnouncement()
        {
            Announcement a = Catalog[currentIndex];
            if (Catalog.Count <= 1)
                return "RimWorldAccess.WhatsNew.HeaderSingle".Translate(a.Version).ToString();
            return "RimWorldAccess.WhatsNew.Header".Translate(a.Version, currentIndex + 1, Catalog.Count).ToString();
        }

        /// <summary>Index of the oldest unread announcement (the catalog is oldest-first), or -1 when none remain.</summary>
        public static int OldestUnreadIndex()
        {
            for (int i = 0; i < Catalog.Count; i++)
            {
                if (IsUnread(Catalog[i].Version))
                    return i;
            }
            return -1;
        }

        public static int UnreadCount()
        {
            return Catalog.Count(a => IsUnread(a.Version));
        }

        public static void OpenChangelog()
        {
            Application.OpenURL(ChangelogUrl);
            TolkHelper.Speak("RimWorldAccess.WhatsNew.OpeningChangelog".Loc());
        }

        public static void SuppressAutoPopup()
        {
            var settings = RimWorldAccessMod_Settings.Settings;
            if (settings != null)
            {
                settings.ShowWhatsNewOnUpdate = false;
                settings.Write();
            }
            Close();
            TolkHelper.Speak("RimWorldAccess.WhatsNew.Suppressed".Loc());
        }

        /// <summary>Marks every announcement read and, while the reader is open, announces it (the "Mark all as read" button).</summary>
        public static void MarkAllRead()
        {
            MarkAllReadInternal(silent: false);
        }

        private static void MarkAllReadInternal(bool silent)
        {
            var settings = RimWorldAccessMod_Settings.Settings;
            if (settings != null)
            {
                bool changed = false;
                foreach (Announcement a in Catalog)
                {
                    if (!settings.ReadAnnouncementVersions.Contains(a.Version))
                    {
                        settings.ReadAnnouncementVersions.Add(a.Version);
                        changed = true;
                    }
                }
                if (changed)
                    settings.Write();
            }

            if (silent || !isActive)
                return;

            TolkHelper.Speak("RimWorldAccess.WhatsNew.AllMarkedRead".Loc());
        }

        private static bool IsUnread(string version)
        {
            var settings = RimWorldAccessMod_Settings.Settings;
            return settings != null && !settings.ReadAnnouncementVersions.Contains(version);
        }

        private static bool AnyUnread() => Catalog.Any(a => IsUnread(a.Version));

        private static void MarkRead(string version)
        {
            var settings = RimWorldAccessMod_Settings.Settings;
            if (settings == null)
                return;

            if (!settings.ReadAnnouncementVersions.Contains(version))
            {
                settings.ReadAnnouncementVersions.Add(version);
                settings.Write();
            }
        }

        /// <summary>
        /// The arrow-able lines from the announcement's localized message, falling back to a generic
        /// one when the version-specific key is unauthored. Splits on both real newlines and literal
        /// "\n" escapes, so authored Keyed text works however the value is stored.
        /// </summary>
        private static string[] BuildLines(Announcement announcement)
        {
            string text = announcement.MessageKey.CanTranslate()
                ? announcement.MessageKey.Translate().ToString()
                : "RimWorldAccess.WhatsNew.Message.Default".Translate().ToString();

            string[] split = text
                .Replace("\\n", "\n")
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            string[] trimmed = split
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToArray();

            return trimmed.Length > 0 ? trimmed : new[] { text.Trim() };
        }
    }
}
