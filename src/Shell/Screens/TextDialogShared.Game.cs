using System;
using System.Collections.Generic;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Small helpers shared by the text-dialog scopes (RenameScope,
    /// GiveNameScope, NamePawnScope). Not a base class: the
    /// three scopes differ too much in shape (native vs mirror mode, element
    /// models, Enter/Escape ownership) to share behavior beyond these leaves,
    /// but all three need the identical §4.5 compose-options snippet, the
    /// identical WrapNavigation read, and the identical mechanism-2
    /// foreign-window guard (OptionsScope shape).
    /// </summary>
    internal static class TextDialogShared
    {
        /// <summary>
        /// Resolves the scope that owns <paramref name="window"/> from anywhere on the focus
        /// stack, for a window draw patch's prefix/postfix.
        ///
        /// A draw patch must NOT resolve its scope as <see cref="FocusStack.Top"/>: opening a
        /// modal <see cref="TextFieldEditSession"/> pushes the text-input scope ABOVE the
        /// dialog's own scope, so a top-only lookup silently stops running the whole per-pass
        /// body for exactly the frames the player is typing — taking with it the
        /// <see cref="TextFieldRawPollGuard"/> mask (vanilla's focus-blind Return poll then
        /// submits behind the session's back) and <c>MirrorLive</c> (so the submit reads the
        /// stale pre-edit value). Both defects reproduced on Dialog_NamePlayerFactionAndSettlement.
        /// </summary>
        public static T ScopeOwning<T>(Window window, Func<T, Window, bool> owns) where T : FocusScope
        {
            if (window == null)
            {
                return null;
            }
            IReadOnlyList<FocusScope> scopes = FocusStack.ScopesBottomUp;
            for (int i = scopes.Count - 1; i >= 0; i--)
            {
                T typed = scopes[i] as T;
                if (typed != null && owns(typed, window))
                {
                    return typed;
                }
            }
            return null;
        }

        /// <summary>
        /// Standard §4.5 compose options, matching every other live scope's own helper of
        /// the same shape. While the Configure Spoken Announcements screen is open,
        /// <see cref="AnnouncementFormatSession"/> holds the WORKING (unsaved) copy of the
        /// four part toggles/order and this reads that instead of settings, so every
        /// ComposeFocus call in the whole shell — including the config screen's own rows —
        /// demonstrates the in-progress edit live. This is the one choke point every
        /// ScreenScope's <c>ComposeCurrentText</c> calls, which is what keeps the live demo
        /// a single edit.
        /// </summary>
        public static ComposeOptions StandardComposeOptions()
        {
            RimWorldAccessSettings settings = RimWorldAccessMod_Settings.Settings;
            ComposeOptions options = default(ComposeOptions);
            options.IncludePosition = PositionPartEnabled;
            options.IncludeRowColumnPosition = RowColumnPositionEnabled;
            options.IncludeLevels = LevelsPartEnabled;

            if (AnnouncementFormatSession.IsActive)
            {
                options.IncludeHints = AnnouncementFormatSession.IncludeHints;
                // Label is never suppressed -- see SetPartEnabled's Label case.
                options.SuppressHotkey = !AnnouncementFormatSession.IncludeHotkey;
                options.SuppressRole = !AnnouncementFormatSession.IncludeRole;
                options.SuppressState = !AnnouncementFormatSession.IncludeState;
                options.SuppressExtras = !AnnouncementFormatSession.IncludeExtras;
                options.PartOrder = AnnouncementFormatSession.PartOrder;
                return options;
            }

            options.IncludeHints = settings == null || settings.AnnounceInteractionHints;
            options.SuppressHotkey = settings != null && !settings.AnnounceHotkeyPart;
            options.SuppressRole = settings != null && !settings.AnnounceRolePart;
            options.SuppressState = settings != null && !settings.AnnounceStatePart;
            options.SuppressExtras = settings != null && !settings.AnnounceExtrasPart;
            options.PartOrder = AnnouncementPartOrderCache.Get(settings);
            return options;
        }

        /// <summary>
        /// The session-aware gate for the Position part ("3 of 7" fragments): reads the
        /// Configure Spoken Announcements screen's working copy while it is open, else the
        /// persisted <see cref="RimWorldAccessSettings.AnnouncePosition"/> setting. Defaults
        /// true when settings are unavailable. This is the composer-routed call sites' own
        /// gate (<see cref="StandardComposeOptions"/> above); the sites listed in the
        /// announcement-format-single-source-of-truth changelog predate ComposeOptions
        /// routing entirely and read this directly instead.
        /// </summary>
        public static bool PositionPartEnabled
        {
            get
            {
                if (AnnouncementFormatSession.IsActive)
                    return AnnouncementFormatSession.IncludePosition;
                RimWorldAccessSettings settings = RimWorldAccessMod_Settings.Settings;
                return settings == null || settings.AnnouncePosition;
            }
        }

        /// <summary>The Level part's equivalent of <see cref="PositionPartEnabled"/> ("level N" fragments).</summary>
        public static bool LevelsPartEnabled
        {
            get
            {
                if (AnnouncementFormatSession.IsActive)
                    return AnnouncementFormatSession.IncludeLevels;
                RimWorldAccessSettings settings = RimWorldAccessMod_Settings.Settings;
                return settings == null || settings.AnnounceLevels;
            }
        }

        /// <summary>Row/column "x of y" fragments in tables; session-aware like <see cref="PositionPartEnabled"/>.</summary>
        public static bool RowColumnPositionEnabled
        {
            get
            {
                if (AnnouncementFormatSession.IsActive)
                    return AnnouncementFormatSession.IncludeRowColumnPosition;
                RimWorldAccessSettings settings = RimWorldAccessMod_Settings.Settings;
                return settings == null || settings.AnnounceRowColumnPosition;
            }
        }

        /// <summary>The table-shape announcement on table entry; session-aware.</summary>
        public static bool AnnounceTableDimensions
        {
            get
            {
                if (AnnouncementFormatSession.IsActive)
                    return AnnouncementFormatSession.IncludeTableDimensions;
                RimWorldAccessSettings settings = RimWorldAccessMod_Settings.Settings;
                return settings == null || settings.AnnounceTableDimensions;
            }
        }

        /// <summary>The section-count announcement on screen entry ("2 sections"); session-aware.</summary>
        public static bool AnnounceTabCount
        {
            get
            {
                if (AnnouncementFormatSession.IsActive)
                    return AnnouncementFormatSession.IncludeSectionCount;
                RimWorldAccessSettings settings = RimWorldAccessMod_Settings.Settings;
                return settings == null || settings.AnnounceTabCount;
            }
        }

        public static bool WrapNavigation
        {
            get { return RimWorldAccessMod_Settings.Settings != null && RimWorldAccessMod_Settings.Settings.WrapNavigation; }
        }

        /// <summary>
        /// Mechanism 2 (foreign-window guard, OptionsScope shape): true when a
        /// scopeless, non-immediate real window sits above <paramref name="owned"/>
        /// on the real WindowStack. Tooltips render as Super-layer
        /// ImmediateWindows and must never count; a window WITH its own
        /// attached scope takes over FocusStack.Top itself (this scope would
        /// not even be consulted while one is up), so only a scopeless,
        /// non-immediate real window above counts as foreign. See
        /// OptionsScope.ForeignWindowAbove for the original rationale;
        /// OptionsScope and ModMismatchScope both delegate here now.
        ///
        /// The dev-tools <see cref="LudeonTK.EditWindow"/> family (debug log,
        /// tweak values, inspector, def editor) never counts while scopeless:
        /// those windows coexist with the surface beneath BY DESIGN and stay
        /// scopeless until the player deliberately arms one through the F12
        /// dev tabs (ScopeForWindow.AttachOnDemand) — at which point the
        /// attached scope takes FocusStack.Top and this guard is moot. The
        /// case that bites: dev mode auto-opens EditWindow_Log on any
        /// Log.Error, and a mod that spams errors during save load (RWoM's
        /// think-node keys) parks it above whatever dialog vanilla queued
        /// (VEF's new-faction dialog) — treating it as foreign muted every
        /// claim on the dialog and left the whole keyboard falling through
        /// into silence.
        /// </summary>
        public static bool ForeignWindowAbove(Window owned)
        {
            IList<Window> windows = Find.WindowStack.Windows;
            bool above = false;
            for (int i = 0; i < windows.Count; i++)
            {
                Window window = windows[i];
                if (ReferenceEquals(window, owned))
                {
                    above = true;
                    continue;
                }
                if (!above || window is ImmediateWindow || window is LudeonTK.EditWindow)
                {
                    continue;
                }
                if (!ScopeForWindow.HasAttachedScope(window))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
