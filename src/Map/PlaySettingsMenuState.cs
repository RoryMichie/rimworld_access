using System;
using System.Collections.Generic;
using Verse;
using RimWorld;
using RimWorldAccess.Shell;

namespace RimWorldAccess
{
    /// <summary>
    /// One control on vanilla's play-settings row (PlaySettings.DoMapControls) that this menu
    /// OFFERS, named after the vanilla state it mirrors rather than its own label, so
    /// <c>PlaySettingsCatalogSelfAudit</c> can check the catalog against vanilla's own field list.
    /// </summary>
    internal sealed class PlaySettingsEntry
    {
        /// <summary>
        /// The <see cref="PlaySettings"/> field this entry drives, or empty for a row control backed
        /// by something else. Matched by NAME against reflection over vanilla's own class, never
        /// against a label, which is translated.
        /// </summary>
        public string CoveredField { get; }

        /// <summary>Vanilla's own visibility condition for the control, or null when it always draws.</summary>
        public Func<bool> Available { get; }

        /// <summary>The localized menu label, resolved fresh on every rebuild so a toggle's own state is current.</summary>
        public Func<string> Label { get; }

        /// <summary>What Enter does. Announcing is the entry's own job (one announcement per action); the menu rebuild is not.</summary>
        public Action Activate { get; }

        public PlaySettingsEntry(string coveredField, Func<string> label, Action activate, Func<bool> available = null)
        {
            CoveredField = coveredField ?? "";
            Label = label;
            Activate = activate;
            Available = available;
        }
    }

    /// <summary>
    /// One control this menu deliberately does NOT offer, with its reason in code. An exclusion is a
    /// decision, not an omission: the self-audit fails on an unlisted field, so a toggle a future
    /// game version adds cannot slip through unnoticed.
    /// </summary>
    internal sealed class PlaySettingsExclusion
    {
        /// <summary>The <see cref="PlaySettings"/> field name, or empty for a row control that owns no field on that class.</summary>
        public string CoveredField { get; }

        /// <summary>Why it is not offered. Developer diagnostic only: read by the DEBUG audit's log line, never spoken or shown.</summary>
        public string Reason { get; }

        public PlaySettingsExclusion(string coveredField, string reason)
        {
            CoveredField = coveredField ?? "";
            Reason = reason ?? "";
        }
    }

    /// <summary>
    /// The declarative inventory of vanilla's play-settings row: every control
    /// <c>PlaySettings.DoMapControls</c> draws, and every public bool field on
    /// <see cref="PlaySettings"/> including the world-view row's, appears here exactly once, in
    /// <see cref="Offered"/> or in <see cref="Excluded"/>. <c>PlaySettingsCatalogSelfAudit</c>
    /// enforces that in DEBUG by reflecting vanilla's own field list.
    ///
    /// Most of the row is excluded because the overlay toggles only repaint pixels, and the tile-stat
    /// keys already serve the underlying need. Revisit any entry whose toggle gains a consequence
    /// beyond its own rendering.
    /// </summary>
    internal static class PlaySettingsCatalog
    {
        /// <summary>The controls this menu offers, in menu order.</summary>
        public static readonly List<PlaySettingsEntry> Offered = new List<PlaySettingsEntry>
        {
            new PlaySettingsEntry(
                "autoRebuild",
                () => (Find.PlaySettings.autoRebuild
                    ? "RimWorldAccess.Map.PlaySettings.AutoRebuild.LabelOn"
                    : "RimWorldAccess.Map.PlaySettings.AutoRebuild.LabelOff").Translate(),
                delegate
                {
                    // MUTATION-C: mirrors PlaySettings.DoMapControls' row.ToggleableIcon(ref autoRebuild, ...)
                    // bare-field flip (RimWorld/PlaySettings.cs:170) — vanilla's own checkbox has no CanX/TryX
                    // gate; WidgetRow.ToggleableIcon (Verse/WidgetRow.cs:193) does the identical unconditional flip.
                    Find.PlaySettings.autoRebuild = !Find.PlaySettings.autoRebuild;
                    // One announcement per action: the state change speaks, and the rebuilt label is
                    // heard on the next navigation.
                    TolkHelper.Speak((Find.PlaySettings.autoRebuild
                        ? "RimWorldAccess.Map.PlaySettings.AutoRebuild.Enabled"
                        : "RimWorldAccess.Map.PlaySettings.AutoRebuild.Disabled").Loc());
                }),

            new PlaySettingsEntry(
                "autoHomeArea",
                () => (Find.PlaySettings.autoHomeArea
                    ? "RimWorldAccess.Map.PlaySettings.AutoHomeArea.LabelOn"
                    : "RimWorldAccess.Map.PlaySettings.AutoHomeArea.LabelOff").Translate(),
                delegate
                {
                    // MUTATION-C: mirrors PlaySettings.DoMapControls' row.ToggleableIcon(ref autoHomeArea, ...)
                    // bare-field flip (RimWorld/PlaySettings.cs:169) — vanilla's own checkbox has no CanX/TryX
                    // gate; WidgetRow.ToggleableIcon (Verse/WidgetRow.cs:193) does the identical unconditional flip.
                    Find.PlaySettings.autoHomeArea = !Find.PlaySettings.autoHomeArea;
                    TolkHelper.Speak((Find.PlaySettings.autoHomeArea
                        ? "RimWorldAccess.Map.PlaySettings.AutoHomeArea.Enabled"
                        : "RimWorldAccess.Map.PlaySettings.AutoHomeArea.Disabled").Loc());
                }),

            // The Entity Codex button (RimWorld/PlaySettings.cs:198-205), the only always-available
            // opener for that dialog: the gizmo, the monolith dialogue option and the letter option
            // each exist only in passing. Not a visual overlay — it opens a window full of text.
            new PlaySettingsEntry(
                "",
                // Vanilla's own codex label, the string its gizmo and dialog title share
                // (RimWorld/Dialog_EntityCodex.cs:79), rather than a key of ours to translate.
                () => "EntityCodex".Translate(),
                PlaySettingsMenuState.OpenEntityCodex,
                PlaySettingsMenuState.EntityCodexAvailable),
        };

        /// <summary>
        /// Every other row control and public bool field on <see cref="PlaySettings"/>, with its
        /// reason. Reasons are developer diagnostics for the DEBUG audit's log line, never spoken,
        /// and so deliberately not localized.
        /// </summary>
        public static readonly List<PlaySettingsExclusion> Excluded = new List<PlaySettingsExclusion>
        {
            // Map-row overlays: pixels only, no keyboard meaning.
            new PlaySettingsExclusion("showZones",
                "Visual overlay (PlaySettings.cs:156): overlays have no keyboard meaning; the tile-stat keys 1-7 serve the need."),
            new PlaySettingsExclusion("showBeauty",
                "Visual overlay (PlaySettings.cs:159); vanilla also binds it to ToggleBeautyDisplay. Overlay-exclusion rule."),
            new PlaySettingsExclusion("showRoomStats",
                "Visual overlay (PlaySettings.cs:163); vanilla also binds it to ToggleRoomStatsDisplay. Overlay-exclusion rule."),
            new PlaySettingsExclusion("showRoofOverlay",
                "Visual overlay (PlaySettings.cs:166). Overlay-exclusion rule."),
            new PlaySettingsExclusion("showFertilityOverlay",
                "Visual overlay (PlaySettings.cs:167). Overlay-exclusion rule."),
            new PlaySettingsExclusion("showTerrainAffordanceOverlay",
                "Visual overlay (PlaySettings.cs:168). Overlay-exclusion rule."),
            new PlaySettingsExclusion("showTemperatureOverlay",
                "Visual overlay (PlaySettings.cs:171). Overlay-exclusion rule."),
            new PlaySettingsExclusion("showPollutionOverlay",
                "Visual overlay, Biotech-gated (PlaySettings.cs:179-182). Overlay-exclusion rule."),
            new PlaySettingsExclusion("showVacuumOverlay",
                "Visual overlay, Odyssey and in-vacuum gated (PlaySettings.cs:183-190). Overlay-exclusion rule."),

            // Map-row toggles excluded for their OWN reasons, not the overlay ruling.
            new PlaySettingsExclusion("showColonistBar",
                "Deliberately not offered, and NOT merely visual: switching it off empties ColonistBar.Entries "
                + "(RimWorld/ColonistBar.cs:227), which is the list this mod's own colonist navigation reads "
                + "(ColonistBarState.cs:145/:234). Offering it would put a switch that disables keyboard colonist "
                + "navigation inside a keyboard menu. Deliberately flagged rather than silently included."),
            new PlaySettingsExclusion("showLearningHelper",
                "Controls only whether vanilla's LearningReadout panel draws when no concept is active "
                + "(RimWorld/LearningReadout.cs:120). This mod's learning helper is a separate scope with its own "
                + "opener (LearningHelperOpenerClaims), unaffected either way."),

            // World-view row (PlaySettings.DoWorldViewControls): not the row this menu mirrors.
            new PlaySettingsExclusion("lockNorthUp",
                "World-view row (PlaySettings.cs:132); camera orientation only. This menu mirrors the MAP row."),
            new PlaySettingsExclusion("usePlanetDayNightSystem",
                "World-view row (PlaySettings.cs:143); planet shading only. This menu mirrors the MAP row."),
            new PlaySettingsExclusion("showImportantExpandingIcons",
                "World-view row (PlaySettings.cs:137); which world icons render. This menu mirrors the MAP row."),
            new PlaySettingsExclusion("showBasesExpandingIcons",
                "World-view row (PlaySettings.cs:138); which world icons render. This menu mirrors the MAP row."),
            new PlaySettingsExclusion("showExpandingLandmarks",
                "World-view row, Odyssey-gated (PlaySettings.cs:139-142); which world icons render. This menu mirrors the MAP row."),
            new PlaySettingsExclusion("showWorldFeatures",
                "World-view row (PlaySettings.cs:144); world feature labels only. This menu mirrors the MAP row."),

            // A PlaySettings field vanilla draws nowhere near this row.
            new PlaySettingsExclusion("useWorkPriorities",
                "Not a play-settings row control at all: vanilla draws it as the Work tab's ManualPriorities checkbox "
                + "(RimWorld/MainTabWindow_Work.cs:42), and this mod toggles it there (WorkMenuState.cs:755, "
                + "WorkTableScope.Game.cs:884)."),

            // Row controls that own no PlaySettings field.
            new PlaySettingsExclusion("",
                "Categorized resource readout: the row's toggle writes Prefs.ResourceReadoutCategorized, not a "
                + "PlaySettings field (PlaySettings.cs:172-178). It organizes a visual readout this mod does not read "
                + "(zero references to ResourceReadout in src/), so it carries no information a keyboard user loses."),
            new PlaySettingsExclusion("",
                "Search button (PlaySettings.cs:193-197): deliberately superseded. ScannerSearchPatch.cs blocks "
                + "vanilla's OpenMapSearch binding outright so the mod's own accessible scanner search handles it."),
        };
    }

    /// <summary>
    /// The play-settings menu (pause-menu item "Play settings") as a
    /// <see cref="WindowlessFloatMenuState"/> menu, which makes it visible through the FloatMenuTwin
    /// and takes navigation, typeahead, activation and Escape from that state plus
    /// <see cref="RimWorldAccess.Shell.FloatMenuOverlayScope"/>. The option list is projected from
    /// <see cref="PlaySettingsCatalog"/>, whose entries name the vanilla fields they mirror.
    /// </summary>
    public static class PlaySettingsMenuState
    {
        // The exact list instance handed to WindowlessFloatMenuState.Open, so IsActive can tell our
        // menu apart from any other menu riding the same state.
        private static List<FloatMenuOption> ourOptions;

        public static bool IsActive =>
            WindowlessFloatMenuState.IsActive && ReferenceEquals(WindowlessFloatMenuState.CurrentOptions, ourOptions);

        public static void Open()
        {
            OpenAt(0, announceFirst: true, playOpenSound: true);
        }

        /// <summary>
        /// Builds a fresh option list and (re)opens the menu at <paramref name="index"/>:
        /// <see cref="Open"/> uses index 0 with the open sound, while a toggle entry reopens
        /// silently at its OWN row so the cursor stays put across the relabel — its Activate()
        /// already spoke the state change. <c>announceSelection: false</c> throughout, since every
        /// entry speaks for itself and the generic "Selected" wording is never wanted.
        /// </summary>
        private static void OpenAt(int index, bool announceFirst, bool playOpenSound)
        {
            List<FloatMenuOption> options = BuildMenuOptions();
            ourOptions = options;
            WindowlessFloatMenuState.Open(
                options,
                colonistOrders: false,
                startIndex: index,
                announceSelection: false,
                playOpenSound: playOpenSound,
                onClose: HandleClose,
                announceFirst: announceFirst);
        }

        /// <summary>
        /// Runs on every close, chosen option or Escape. Only Escape returns to the real pause menu,
        /// matching how Save/Load/Options/Review Scenario behave when escaped rather than dropping
        /// the player out of the pause menu; the in-list Cancel row closes silently.
        /// </summary>
        private static void HandleClose(bool cancelled)
        {
            ourOptions = null;
            if (!cancelled)
                return;
            if (Current.ProgramState == ProgramState.Playing)
            {
                PauseMenuScope.OpenRealMenu();
            }
        }

        /// <summary>
        /// Silent hygiene close for the reset registry: closes the shared menu only while it is
        /// still ours, and never runs the on-close callback, which would reopen a stale pause menu.
        /// </summary>
        public static void Close()
        {
            if (IsActive)
            {
                WindowlessFloatMenuState.Close();
            }
            ourOptions = null;
        }

        /// <summary>
        /// Vanilla's own visibility gate for the Entity Codex button (RimWorld/PlaySettings.cs:198).
        /// The null test adds no condition: <c>Find.Anomaly</c> is a game component, null only
        /// outside a running game, where vanilla's row does not draw either.
        /// </summary>
        internal static bool EntityCodexAvailable()
        {
            return ModsConfig.AnomalyActive
                && Find.Anomaly != null
                && Find.Anomaly.AnomalyStudyEnabled;
        }

        /// <summary>Opens the entity codex, the one control on this row that opens a window rather than flipping a field.</summary>
        internal static void OpenEntityCodex()
        {
            // Stand the windowless menu down BEFORE a real window opens over it. Unlike Cancel this
            // does not reopen the pause menu: the codex is the destination, and closing it returns
            // focus beneath exactly as it does for a codex opened from its gizmo.
            Close();
            // Vehicle A: vanilla's own opener line (RimWorld/PlaySettings.cs:203).
            Find.WindowStack.Add(new Dialog_EntityCodex());
        }

        /// <summary>Projects <see cref="PlaySettingsCatalog.Offered"/> into menu options, skipping entries whose vanilla gate is closed this frame.</summary>
        private static List<FloatMenuOption> BuildMenuOptions()
        {
            var options = new List<FloatMenuOption>();

            for (int i = 0; i < PlaySettingsCatalog.Offered.Count; i++)
            {
                PlaySettingsEntry entry = PlaySettingsCatalog.Offered[i];
                if (entry.Available != null && !entry.Available())
                    continue;

                int capturedIndex = options.Count;
                options.Add(new FloatMenuOption(entry.Label(), delegate
                {
                    entry.Activate();
                    // Reopen at the SAME row with refreshed labels so a toggled entry reads its new
                    // state on the next navigation. Skipped when the action already closed the menu
                    // itself, after which ourOptions no longer points at this closure's own list.
                    if (ReferenceEquals(ourOptions, options))
                    {
                        OpenAt(capturedIndex, announceFirst: false, playOpenSound: false);
                    }
                }));
            }

            // Cancel, under vanilla's own key.
            options.Add(new FloatMenuOption("CancelButton".Translate(), Close));

            return options;
        }
    }
}
