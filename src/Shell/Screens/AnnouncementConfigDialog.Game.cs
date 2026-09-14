using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Caches the parsed <see cref="RimWorldAccessSettings.AnnouncementPartOrder"/> (persisted as
    /// enum-name strings) so no announcement re-parses it. Invalidation rides reference equality:
    /// <see cref="AnnouncementFormatSession.Commit"/> assigns a new list on save, so the next
    /// read re-parses on its own and no Invalidate() call is needed.
    /// </summary>
    internal static class AnnouncementPartOrderCache
    {
        private static List<string> lastRaw;
        private static AnnouncementPart[] cached;

        public static IReadOnlyList<AnnouncementPart> Get(RimWorldAccessSettings settings)
        {
            List<string> raw = settings != null ? settings.AnnouncementPartOrder : null;
            if (raw == null || raw.Count == 0)
            {
                return null; // default order
            }
            if (cached != null && ReferenceEquals(raw, lastRaw))
            {
                return cached;
            }
            cached = Parse(raw);
            lastRaw = raw;
            return cached;
        }

        /// <summary>
        /// Enum-name strings to the full <see cref="AnnouncementPart"/> rotation. Unknown names
        /// are skipped; a default part missing from the persisted list is appended rather than
        /// dropped, so a part added by a later build never silently disappears.
        /// </summary>
        internal static AnnouncementPart[] Parse(List<string> raw)
        {
            var result = new List<AnnouncementPart>(AnnouncementFormat.DefaultOrder.Length);
            for (int i = 0; i < raw.Count; i++)
            {
                // Legacy combined part: expand in place so a saved order keeps type and state
                // where RoleAndState sat rather than dropping both to the end.
                if (raw[i] == "RoleAndState")
                {
                    if (!result.Contains(AnnouncementPart.Role))
                        result.Add(AnnouncementPart.Role);
                    if (!result.Contains(AnnouncementPart.State))
                        result.Add(AnnouncementPart.State);
                    continue;
                }
                AnnouncementPart part;
                if (Enum.TryParse(raw[i], out part) && !result.Contains(part))
                {
                    result.Add(part);
                }
            }
            for (int i = 0; i < AnnouncementFormat.DefaultOrder.Length; i++)
            {
                AnnouncementPart part = AnnouncementFormat.DefaultOrder[i];
                if (!result.Contains(part))
                {
                    result.Add(part);
                }
            }
            return result.ToArray();
        }
    }

    /// <summary>
    /// The WORKING (unsaved) copy of the announcement-format toggles and order while
    /// <see cref="Dialog_ConfigureAnnouncements"/> is open.
    /// <see cref="TextDialogShared.StandardComposeOptions"/> reads this instead of settings
    /// whenever <see cref="IsActive"/>, so every announcement in the shell demonstrates the
    /// in-progress edit live. <see cref="Clear"/> discards it without touching settings;
    /// <see cref="Commit"/> is the only path that writes it back.
    /// </summary>
    internal static class AnnouncementFormatSession
    {
        public static bool IsActive { get; private set; }

        public static bool IncludeLabel;
        public static bool IncludeHotkey;
        public static bool IncludeRole;
        public static bool IncludeState;
        public static bool IncludeExtras;
        public static bool IncludePosition;
        public static bool IncludeLevels;
        public static bool IncludeHints;
        public static List<AnnouncementPart> PartOrder;

        // Standalone verbosity toggles listed after the reorderable parts: table-cell
        // "row/column x of y" fragments, the table-shape entry announcement, and the
        // section-count entry announcement. Not parts of the focus grammar, so not reorderable.
        public static bool IncludeRowColumnPosition;
        public static bool IncludeTableDimensions;
        public static bool IncludeSectionCount;

        /// <summary>Seeds the working copy from persisted settings; the dialog's constructor calls it once.</summary>
        public static void Open()
        {
            SeedFrom(RimWorldAccessMod_Settings.Settings);
        }

        /// <summary>
        /// Restores the working copy to the defaults, read off a fresh
        /// <see cref="RimWorldAccessSettings"/> so its field initializers stay the only place
        /// those values are declared. Save still has to persist it.
        /// </summary>
        public static void ResetToDefaults()
        {
            SeedFrom(new RimWorldAccessSettings());
        }

        private static void SeedFrom(RimWorldAccessSettings settings)
        {
            // The Name part is always on: silencing it would leave the player unable to tell
            // what is focused. Forcing it here also heals a save that has it unchecked.
            IncludeLabel = true;
            IncludeHotkey = settings == null || settings.AnnounceHotkeyPart;
            IncludeRole = settings == null || settings.AnnounceRolePart;
            IncludeState = settings == null || settings.AnnounceStatePart;
            IncludeExtras = settings == null || settings.AnnounceExtrasPart;
            IncludePosition = settings == null || settings.AnnouncePosition;
            IncludeLevels = settings == null || settings.AnnounceLevels;
            IncludeHints = settings == null || settings.AnnounceInteractionHints;
            IncludeRowColumnPosition = settings == null || settings.AnnounceRowColumnPosition;
            IncludeTableDimensions = settings == null || settings.AnnounceTableDimensions;
            IncludeSectionCount = settings == null || settings.AnnounceTabCount;
            IReadOnlyList<AnnouncementPart> persisted = AnnouncementPartOrderCache.Get(settings);
            PartOrder = new List<AnnouncementPart>(persisted ?? AnnouncementFormat.DefaultOrder);
            IsActive = true;
        }

        /// <summary>Discards the working copy without touching settings — the Cancel/Escape/X path. Idempotent.</summary>
        public static void Clear()
        {
            IsActive = false;
            PartOrder = null;
        }

        /// <summary>Writes the working copy into settings and persists immediately — the Save path only.</summary>
        public static void Commit()
        {
            RimWorldAccessSettings settings = RimWorldAccessMod_Settings.Settings;
            if (settings == null || PartOrder == null)
            {
                Clear();
                return;
            }
            settings.AnnounceHotkeyPart = IncludeHotkey;
            settings.AnnounceRolePart = IncludeRole;
            settings.AnnounceStatePart = IncludeState;
            settings.AnnounceExtrasPart = IncludeExtras;
            settings.AnnouncePosition = IncludePosition;
            settings.AnnounceLevels = IncludeLevels;
            settings.AnnounceInteractionHints = IncludeHints;
            settings.AnnounceRowColumnPosition = IncludeRowColumnPosition;
            settings.AnnounceTableDimensions = IncludeTableDimensions;
            settings.AnnounceTabCount = IncludeSectionCount;

            var order = new List<string>(PartOrder.Count);
            for (int i = 0; i < PartOrder.Count; i++)
            {
                order.Add(PartOrder[i].ToString());
            }
            settings.AnnouncementPartOrder = order; // a NEW list reference -- AnnouncementPartOrderCache re-parses automatically.

            Mod mod = LoadedModManager.GetMod<RimWorldAccessMod_Settings>();
            if (mod != null)
            {
                mod.WriteSettings();
            }
            Clear();
        }

        public static bool IsPartEnabled(AnnouncementPart part)
        {
            switch (part)
            {
                case AnnouncementPart.Label: return IncludeLabel;
                case AnnouncementPart.Hotkey: return IncludeHotkey;
                case AnnouncementPart.Role: return IncludeRole;
                case AnnouncementPart.State: return IncludeState;
                case AnnouncementPart.Level: return IncludeLevels;
                case AnnouncementPart.Position: return IncludePosition;
                case AnnouncementPart.Extras: return IncludeExtras;
                case AnnouncementPart.Hint: return IncludeHints;
                default: return false;
            }
        }

        public static void SetPartEnabled(AnnouncementPart part, bool enabled)
        {
            switch (part)
            {
                // Locked on; the UI never offers disabling it.
                case AnnouncementPart.Label: break;
                case AnnouncementPart.Hotkey: IncludeHotkey = enabled; break;
                case AnnouncementPart.Role: IncludeRole = enabled; break;
                case AnnouncementPart.State: IncludeState = enabled; break;
                case AnnouncementPart.Level: IncludeLevels = enabled; break;
                case AnnouncementPart.Position: IncludePosition = enabled; break;
                case AnnouncementPart.Extras: IncludeExtras = enabled; break;
                case AnnouncementPart.Hint: IncludeHints = enabled; break;
            }
        }

        /// <summary>
        /// Swaps the part at <paramref name="index"/> with its neighbor (-1 up, +1 down).
        /// Returns the new index, or -1 when already at that edge.
        /// </summary>
        public static int Reorder(int index, int direction)
        {
            if (PartOrder == null || index < 0 || index >= PartOrder.Count)
            {
                return -1;
            }
            int newIndex = index + direction;
            if (newIndex < 0 || newIndex >= PartOrder.Count)
            {
                return -1;
            }
            AnnouncementPart moved = PartOrder[index];
            PartOrder.RemoveAt(index);
            PartOrder.Insert(newIndex, moved);
            return newIndex;
        }
    }

    /// <summary>
    /// The standalone verbosity toggles listed after the reorderable parts, in display order.
    /// </summary>
    internal enum AnnounceExtraToggle
    {
        RowColumnPosition,
        TableDimensions,
        SectionCount,
    }

    /// <summary>Localized part names/example fragments — shared by the window's visual rows and the scope's speech.</summary>
    internal static class AnnounceConfigLabels
    {
        public static readonly AnnounceExtraToggle[] ExtraToggles =
        {
            AnnounceExtraToggle.RowColumnPosition,
            AnnounceExtraToggle.TableDimensions,
            AnnounceExtraToggle.SectionCount,
        };

        public static string ExtraLabel(AnnounceExtraToggle toggle)
        {
            switch (toggle)
            {
                case AnnounceExtraToggle.RowColumnPosition: return (string)"RimWorldAccess.AnnounceConfig.Extra.RowColumnPosition".Translate();
                case AnnounceExtraToggle.TableDimensions: return (string)"RimWorldAccess.AnnounceConfig.Extra.TableDimensions".Translate();
                case AnnounceExtraToggle.SectionCount: return (string)"RimWorldAccess.AnnounceConfig.Extra.SectionCount".Translate();
                default: return toggle.ToString();
            }
        }

        public static string ExtraExample(AnnounceExtraToggle toggle)
        {
            switch (toggle)
            {
                case AnnounceExtraToggle.RowColumnPosition: return (string)"RimWorldAccess.AnnounceConfig.Extra.RowColumnPosition.Example".Translate();
                case AnnounceExtraToggle.TableDimensions: return (string)"RimWorldAccess.AnnounceConfig.Extra.TableDimensions.Example".Translate();
                case AnnounceExtraToggle.SectionCount: return (string)"RimWorldAccess.AnnounceConfig.Extra.SectionCount.Example".Translate();
                default: return "";
            }
        }

        public static bool ExtraEnabled(AnnounceExtraToggle toggle)
        {
            switch (toggle)
            {
                case AnnounceExtraToggle.RowColumnPosition: return AnnouncementFormatSession.IncludeRowColumnPosition;
                case AnnounceExtraToggle.TableDimensions: return AnnouncementFormatSession.IncludeTableDimensions;
                case AnnounceExtraToggle.SectionCount: return AnnouncementFormatSession.IncludeSectionCount;
                default: return false;
            }
        }

        public static void SetExtraEnabled(AnnounceExtraToggle toggle, bool enabled)
        {
            switch (toggle)
            {
                case AnnounceExtraToggle.RowColumnPosition: AnnouncementFormatSession.IncludeRowColumnPosition = enabled; break;
                case AnnounceExtraToggle.TableDimensions: AnnouncementFormatSession.IncludeTableDimensions = enabled; break;
                case AnnounceExtraToggle.SectionCount: AnnouncementFormatSession.IncludeSectionCount = enabled; break;
            }
        }

        public static string PartLabel(AnnouncementPart part)
        {
            switch (part)
            {
                case AnnouncementPart.Label: return (string)"RimWorldAccess.AnnounceConfig.Part.Label".Translate();
                case AnnouncementPart.Hotkey: return (string)"RimWorldAccess.AnnounceConfig.Part.Hotkey".Translate();
                case AnnouncementPart.Role: return (string)"RimWorldAccess.AnnounceConfig.Part.Role".Translate();
                case AnnouncementPart.State: return (string)"RimWorldAccess.AnnounceConfig.Part.State".Translate();
                case AnnouncementPart.Level: return (string)"RimWorldAccess.AnnounceConfig.Part.Level".Translate();
                case AnnouncementPart.Position: return (string)"RimWorldAccess.AnnounceConfig.Part.Position".Translate();
                case AnnouncementPart.Extras: return (string)"RimWorldAccess.AnnounceConfig.Part.Extras".Translate();
                case AnnouncementPart.Hint: return (string)"RimWorldAccess.AnnounceConfig.Part.Hint".Translate();
                default: return part.ToString();
            }
        }

        public static string ExampleFragment(AnnouncementPart part)
        {
            switch (part)
            {
                case AnnouncementPart.Label: return (string)"RimWorldAccess.AnnounceConfig.Example.Label".Translate();
                case AnnouncementPart.Hotkey: return (string)"RimWorldAccess.AnnounceConfig.Example.Hotkey".Translate();
                case AnnouncementPart.Role: return (string)"RimWorldAccess.AnnounceConfig.Example.Role".Translate();
                case AnnouncementPart.State: return (string)"RimWorldAccess.AnnounceConfig.Example.State".Translate();
                case AnnouncementPart.Level: return (string)"RimWorldAccess.AnnounceConfig.Example.Level".Translate();
                case AnnouncementPart.Position: return (string)"RimWorldAccess.AnnounceConfig.Example.Position".Translate();
                case AnnouncementPart.Extras: return (string)"RimWorldAccess.AnnounceConfig.Example.Extras".Translate();
                case AnnouncementPart.Hint: return (string)"RimWorldAccess.AnnounceConfig.Example.Hint".Translate();
                default: return "";
            }
        }
    }

    /// <summary>
    /// The mod's own "Configure spoken announcements" dialog, opened from a button in both
    /// RimWorld Access settings surfaces
    /// (<see cref="RimWorldAccessMod_Settings.DoSettingsWindowContents"/> and
    /// <see cref="OptionsRwaCategory.DrawSettings"/>), drawn with vanilla chrome and margins.
    /// The four bottom buttons are plain <see cref="Widgets.ButtonText"/> calls, captured once
    /// <see cref="AnnouncementConfigScope"/> attaches, so Enter replays the mouse's own click
    /// branch. The seven part rows are real <see cref="Listing_Standard.CheckboxLabeled"/>
    /// widgets for the mouse and a separate typed content region for the keyboard, both over the
    /// same <see cref="AnnouncementFormatSession"/> fields so they cannot drift apart.
    /// </summary>
    public sealed class Dialog_ConfigureAnnouncements : Window
    {
        private const float ButtonHeight = 35f;
        private const float ButtonSpacing = 10f;
        private const int ButtonCount = 4;

        /// <summary>Each part row's last-drawn rect in absolute UI points, refreshed every DoWindowContents pass.</summary>
        private readonly Dictionary<AnnouncementPart, Rect> partScreenRects = new Dictionary<AnnouncementPart, Rect>();

        /// <summary>The extra-toggle rows' last-drawn rects, same contract as <see cref="partScreenRects"/>.</summary>
        private readonly Dictionary<AnnounceExtraToggle, Rect> extraScreenRects = new Dictionary<AnnounceExtraToggle, Rect>();

        public Dialog_ConfigureAnnouncements()
        {
            doCloseX = true;
            absorbInputAroundWindow = true;
            AnnouncementFormatSession.Open();
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(640f, 600f); }
        }

        /// <summary>
        /// Fires for every close path. The saving paths already committed (which clears the
        /// session), so this is a no-op there and the deterministic discard on Cancel and X.
        /// </summary>
        public override void PreClose()
        {
            base.PreClose();
            AnnouncementFormatSession.Clear();
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            float titleHeight = Text.LineHeight;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, titleHeight),
                (string)"RimWorldAccess.AnnounceConfig.Title".Translate());
            Text.Font = GameFont.Small;

            float y = inRect.y + titleHeight + 8f;
            string intro = (string)"RimWorldAccess.AnnounceConfig.Intro".Translate();
            float introHeight = Text.CalcHeight(intro, inRect.width);
            Widgets.Label(new Rect(inRect.x, y, inRect.width, introHeight), intro);
            y += introHeight + 10f;

            float buttonRowY = inRect.yMax - ButtonHeight;
            float buttonWidth = (inRect.width - ButtonSpacing * (ButtonCount - 1)) / ButtonCount;
            Rect saveRect = new Rect(inRect.x, buttonRowY, buttonWidth, ButtonHeight);
            Rect cancelRect = new Rect(saveRect.xMax + ButtonSpacing, buttonRowY, buttonWidth, ButtonHeight);
            Rect previewRect = new Rect(cancelRect.xMax + ButtonSpacing, buttonRowY, buttonWidth, ButtonHeight);
            Rect resetRect = new Rect(previewRect.xMax + ButtonSpacing, buttonRowY, buttonWidth, ButtonHeight);

            // Draw order fixes the capture index that
            // AnnouncementConfigScope.CapturedButtonHotkey keys off.
            if (Widgets.ButtonText(saveRect, "Save".Translate()))
            {
                AnnouncementFormatSession.Commit();
                Close();
            }
            if (Widgets.ButtonText(cancelRect, "Cancel".Translate()))
            {
                Close(); // PreClose discards the working session -- nothing was written to settings.
            }
            if (Widgets.ButtonText(previewRect, (string)"RimWorldAccess.AnnounceConfig.Preview.Button".Translate()))
            {
                SpeakPreview();
            }
            if (Widgets.ButtonText(resetRect, "RestoreToDefaultSettings".Translate()))
            {
                AnnouncementConfigScope.ResetToDefaultsAndAnnounce();
            }

            Rect listRect = new Rect(inRect.x, y, inRect.width, buttonRowY - 10f - y);
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(listRect);
            List<AnnouncementPart> order = AnnouncementFormatSession.PartOrder;
            partScreenRects.Clear();
            if (order != null)
            {
                for (int i = 0; i < order.Count; i++)
                {
                    AnnouncementPart part = order[i];
                    if (part == AnnouncementPart.Label)
                    {
                        // Listing_Standard.CheckboxLabeled has no disabled overload, so this
                        // locked row repeats its rect math and hover/tooltip pieces and passes
                        // Widgets.CheckboxLabeled's own disabled parameter.
                        Rect labelRect = listing.GetRect(30f, 0.6f);
                        labelRect.width = Mathf.Min(labelRect.width + 24f, listing.ColumnWidth);
                        partScreenRects[part] = GuiSpace.VisibleScreenRect(labelRect);
                        string tooltip = AnnounceConfigLabels.ExampleFragment(part);
                        if (Mouse.IsOver(labelRect))
                        {
                            Widgets.DrawHighlight(labelRect);
                        }
                        TooltipHandler.TipRegion(labelRect, tooltip);
                        bool locked = true;
                        Widgets.CheckboxLabeled(labelRect, AnnounceConfigLabels.PartLabel(part), ref locked, disabled: true);
                        listing.Gap(listing.verticalSpacing);
                        continue;
                    }
                    // Listing_Standard.CheckboxLabeled's own rect math
                    // (Verse/Listing_Standard.cs:215-233), repeated so the row's rect can be
                    // captured before the widget draws over it.
                    bool enabled = AnnouncementFormatSession.IsPartEnabled(part);
                    bool before = enabled;
                    Rect rowRect = listing.GetRect(30f, 0.6f);
                    rowRect.width = Mathf.Min(rowRect.width + 24f, listing.ColumnWidth);
                    partScreenRects[part] = GuiSpace.VisibleScreenRect(rowRect);
                    string rowTooltip = AnnounceConfigLabels.ExampleFragment(part);
                    if (!rowTooltip.NullOrEmpty())
                    {
                        if (Mouse.IsOver(rowRect))
                        {
                            Widgets.DrawHighlight(rowRect);
                        }
                        TooltipHandler.TipRegion(rowRect, rowTooltip);
                    }
                    Widgets.CheckboxLabeled(rowRect, AnnounceConfigLabels.PartLabel(part), ref enabled);
                    listing.Gap(listing.verticalSpacing);
                    if (enabled != before)
                    {
                        AnnouncementFormatSession.SetPartEnabled(part, enabled);
                    }
                }
            }
            extraScreenRects.Clear();
            for (int i = 0; i < AnnounceConfigLabels.ExtraToggles.Length; i++)
            {
                AnnounceExtraToggle toggle = AnnounceConfigLabels.ExtraToggles[i];
                bool enabled = AnnounceConfigLabels.ExtraEnabled(toggle);
                bool before = enabled;
                Rect rowRect = listing.GetRect(30f, 0.6f);
                rowRect.width = Mathf.Min(rowRect.width + 24f, listing.ColumnWidth);
                extraScreenRects[toggle] = GuiSpace.VisibleScreenRect(rowRect);
                string rowTooltip = AnnounceConfigLabels.ExtraExample(toggle);
                if (!rowTooltip.NullOrEmpty())
                {
                    if (Mouse.IsOver(rowRect))
                    {
                        Widgets.DrawHighlight(rowRect);
                    }
                    TooltipHandler.TipRegion(rowRect, rowTooltip);
                }
                Widgets.CheckboxLabeled(rowRect, AnnounceConfigLabels.ExtraLabel(toggle), ref enabled);
                listing.Gap(listing.verticalSpacing);
                if (enabled != before)
                {
                    AnnounceConfigLabels.SetExtraEnabled(toggle, enabled);
                }
            }
            listing.End();
        }

        /// <summary>The focused part's last-drawn row rect (absolute UI points), or default when not drawn this pass — AnnouncementConfigScope.FocusedContentRect's backing store.</summary>
        internal Rect RowScreenRect(AnnouncementPart part)
        {
            return partScreenRects.TryGetValue(part, out Rect rect) ? rect : default(Rect);
        }

        /// <summary>The extra-toggle twin of <see cref="RowScreenRect(AnnouncementPart)"/>.</summary>
        internal Rect RowScreenRect(AnnounceExtraToggle toggle)
        {
            return extraScreenRects.TryGetValue(toggle, out Rect rect) ? rect : default(Rect);
        }

        /// <summary>
        /// The Preview button's canned demo item, composed through the same
        /// <see cref="AnnouncementComposer.ComposeFocus"/> path every real announcement uses, so
        /// it reflects whatever the player has toggled or reordered so far.
        /// </summary>
        private static void SpeakPreview()
        {
            var d = new ElementDescription
            {
                Label = (string)"RimWorldAccess.AnnounceConfig.Preview.Label".Translate(),
                Hotkey = (string)"RimWorldAccess.AnnounceConfig.Preview.Hotkey".Translate(),
                Role = ElementRole.Checkbox,
                Check = CheckState.Checked,
                Level = 2,
                PositionIndex = 3,
                PositionCount = 7,
                Extras = (string)"RimWorldAccess.AnnounceConfig.Preview.Extras".Translate(),
                Hint = (string)"RimWorldAccess.AnnounceConfig.Preview.Hint".Translate(),
            };
            string composed = AnnouncementComposer.ComposeFocus(
                d, TranslatedShellVocabulary.Instance, TextDialogShared.StandardComposeOptions());
            TolkHelper.SpeakData(composed);
        }
    }

    /// <summary>
    /// Focus scope for <see cref="Dialog_ConfigureAnnouncements"/>. One content region: seven
    /// rows, one per <see cref="AnnouncementPart"/> in the working order, toggled with
    /// Space/Enter and moved with Ctrl+Up/Down. Both write into
    /// <see cref="AnnouncementFormatSession"/>, which every announcement reads while this scope
    /// is live, so arrowing the list demonstrates the edit. The Buttons region is captured from
    /// the window's own <see cref="Widgets.ButtonText"/> calls.
    /// Escape SAVES here, matching the settings menus that persist as you go: the scope owns
    /// Escape and routes it to <c>PerformSave</c>. That claim MUST stamp
    /// <see cref="ShellFrameStamps.MarkCancelConsumed"/> before the close — <c>OwnsCancel</c>
    /// only diverts Escape while this scope is on top, and the commit pops it, leaving the
    /// <c>closeOnCancel</c> options window underneath to take the same frame's Escape through
    /// <see cref="Window.OnCancelKeyPressed"/>. <c>WindowCancelKeyRouterPatch</c> checks the
    /// stamp before the focus stack, which is what keeps one Escape to one window.
    /// The mouse keeps a discarding exit in the Cancel and X buttons, which never reach the
    /// dispatcher and discard via <see cref="Dialog_ConfigureAnnouncements.PreClose"/>.
    /// </summary>
    public sealed class AnnouncementConfigScope : ScreenScope
    {
        private const int PartsRegion = 0;

        private readonly Dialog_ConfigureAnnouncements window;
        private bool announcedOpen;

        public AnnouncementConfigScope(Dialog_ConfigureAnnouncements window)
        {
            this.window = window;

            // No region guard: Save is meaningful from anywhere in the dialog.
            Claim("announceConfig.save", e => PerformSave());

            // The base's typeahead Escape claim is registered ahead of this one and wins while
            // a search is live, so reaching here means no search. The stamp lives here rather
            // than in PerformSave because it is about this Escape, and must precede its Close:
            // closing pops the scope, and the options window underneath would otherwise take
            // the same Escape through its own OnCancelKeyPressed.
            Claim(SharedMenuGrammar.Cancel, delegate
            {
                ShellFrameStamps.MarkCancelConsumed();
                PerformSave();
            });

            Claim("announceConfig.resetToDefaults", e => ResetToDefaultsAndAnnounce());
        }

        public override string Name
        {
            get { return "announce-config"; }
        }

        protected internal override Window OwnedWindow
        {
            get { return window; }
        }

        /// <summary>Escape commits rather than closing through vanilla — see the Cancel claim in the constructor.</summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        /// <summary>Captured-button draw indices: Save is 0, Restore defaults is 3.</summary>
        protected override string CapturedButtonHotkey(int captureIndex)
        {
            if (captureIndex == 0)
            {
                return ChordDisplay("announceConfig.save");
            }
            return captureIndex == 3 ? ChordDisplay("announceConfig.resetToDefaults") : null;
        }

        /// <summary>
        /// Shared by the Restore defaults chord and the button's own click branch. Static so the
        /// window can run it without reaching for the live scope.
        /// </summary>
        internal static void ResetToDefaultsAndAnnounce()
        {
            AnnouncementFormatSession.ResetToDefaults();
            TolkHelper.SpeakData((string)"RimWorldAccess.AnnounceConfig.ResetToDefaults.Done".Translate());
        }

        /// <summary>The Save button's vehicle, shared by its memorized chord and the proceed grammar.</summary>
        private void PerformSave()
        {
            AnnouncementFormatSession.Commit();
            window.Close();
        }

        /// <summary>
        /// Save is drawn by the window itself, so it arrives as a captured row and cannot be
        /// named by <see cref="ScreenScope.DefaultAcceptActionId"/>; same label and vehicle.
        /// </summary>
        protected override ScreenAction CapturedDefaultAcceptAction
        {
            get { return new ScreenAction("Save".Translate(), PerformSave); }
        }

        private bool PartsRegionLive()
        {
            RefreshModel();
            return Model.RegionIndex == PartsRegion;
        }

        /// <summary>The focused row's rect, from the window's own draw-time capture.</summary>
        protected internal override Rect FocusedContentRect()
        {
            if (!PartsRegionLive())
            {
                return default(Rect);
            }
            ListModel region = Model.CurrentRegion;
            List<AnnouncementPart> order = AnnouncementFormatSession.PartOrder;
            if (region == null || region.IsEmpty || order == null || region.Index < 0)
            {
                return default(Rect);
            }
            if (region.Index < order.Count)
            {
                return window.RowScreenRect(order[region.Index]);
            }
            int extraIndex = region.Index - order.Count;
            if (extraIndex >= AnnounceConfigLabels.ExtraToggles.Length)
            {
                return default(Rect);
            }
            return window.RowScreenRect(AnnounceConfigLabels.ExtraToggles[extraIndex]);
        }

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        protected override string ContentRegionName(int region)
        {
            return (string)"RimWorldAccess.AnnounceConfig.PartsRegion".Translate();
        }

        protected override int ContentItemCount(int region)
        {
            int parts = AnnouncementFormatSession.PartOrder != null ? AnnouncementFormatSession.PartOrder.Count : 0;
            return parts + AnnounceConfigLabels.ExtraToggles.Length;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            List<AnnouncementPart> order = AnnouncementFormatSession.PartOrder;
            if (order == null || index < 0)
            {
                return new ElementDescription();
            }
            if (index >= order.Count)
            {
                int extraIndex = index - order.Count;
                if (extraIndex >= AnnounceConfigLabels.ExtraToggles.Length)
                {
                    return new ElementDescription();
                }
                AnnounceExtraToggle toggle = AnnounceConfigLabels.ExtraToggles[extraIndex];
                return new ElementDescription
                {
                    Label = AnnounceConfigLabels.ExtraLabel(toggle),
                    Role = ElementRole.Checkbox,
                    Check = AnnounceConfigLabels.ExtraEnabled(toggle) ? CheckState.Checked : CheckState.Unchecked,
                    Extras = AnnounceConfigLabels.ExtraExample(toggle),
                };
            }
            AnnouncementPart part = order[index];
            string extras = AnnounceConfigLabels.ExampleFragment(part);
            if (part == AnnouncementPart.Label)
            {
                extras = extras + ". " + (string)"RimWorldAccess.AnnounceConfig.NameAlwaysOn".Translate();
            }
            return new ElementDescription
            {
                Label = AnnounceConfigLabels.PartLabel(part),
                Role = ElementRole.Checkbox,
                Check = AnnouncementFormatSession.IsPartEnabled(part) ? CheckState.Checked : CheckState.Unchecked,
                Extras = extras,
            };
        }

        protected override void ActivateContentItem(int region, int index)
        {
            List<AnnouncementPart> order = AnnouncementFormatSession.PartOrder;
            if (order == null || index < 0)
            {
                return;
            }
            if (index >= order.Count)
            {
                int extraIndex = index - order.Count;
                if (extraIndex >= AnnounceConfigLabels.ExtraToggles.Length)
                {
                    return;
                }
                AnnounceExtraToggle toggle = AnnounceConfigLabels.ExtraToggles[extraIndex];
                bool nextValue = !AnnounceConfigLabels.ExtraEnabled(toggle);
                AnnounceConfigLabels.SetExtraEnabled(toggle, nextValue);
                TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(
                    new ElementDescription { Check = nextValue ? CheckState.Checked : CheckState.Unchecked },
                    TranslatedShellVocabulary.Instance));
                return;
            }
            AnnouncementPart part = order[index];
            if (part == AnnouncementPart.Label)
            {
                TolkHelper.SpeakData((string)"RimWorldAccess.AnnounceConfig.NameAlwaysOn".Translate());
                return;
            }
            bool next = !AnnouncementFormatSession.IsPartEnabled(part);
            AnnouncementFormatSession.SetPartEnabled(part, next);
            TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(
                new ElementDescription { Check = next ? CheckState.Checked : CheckState.Unchecked },
                TranslatedShellVocabulary.Instance));
        }

        /// <summary>Only the part rows reorder; the trailing extra toggles are fixed in place.</summary>
        protected override bool CanReorderContentItem(int region, int index, int direction)
        {
            if (region != PartsRegion || AnnouncementFormatSession.PartOrder == null)
            {
                return false;
            }
            if (index >= AnnouncementFormatSession.PartOrder.Count)
            {
                return false;
            }
            int newIndex = index + direction;
            return newIndex >= 0 && newIndex < AnnouncementFormatSession.PartOrder.Count;
        }

        protected override int ReorderContentItem(int region, int index, int direction)
        {
            if (region != PartsRegion)
            {
                return -1;
            }
            return AnnouncementFormatSession.Reorder(index, direction);
        }

        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen)
            {
                AnnounceCurrentItem();
                return;
            }
            announcedOpen = true;
            string title = (string)"RimWorldAccess.AnnounceConfig.Title".Translate();
            string intro = (string)"RimWorldAccess.AnnounceConfig.Intro".Translate();
            TolkHelper.SpeakData(title + ". " + intro);
            AnnounceCurrentItem();
        }
    }
}
