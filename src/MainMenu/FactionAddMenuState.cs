using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// The "Add Faction" overlay's option list (one entry per configurable FactionDef,
    /// enabled/disabled per CanAddFaction) plus the add mutation -- split out of
    /// FactionsNavigationState along the IsAddMenuOpen seam (the overlay never touches the
    /// faction LIST's own selectedIndex/typeahead, only the shared factions
    /// List&lt;FactionDef&gt; on WorldParamsPageBridge).
    ///
    /// Navigation, typeahead and per-row announcements belong to
    /// <see cref="RimWorldAccess.Shell.WorldParamsAddFactionScope"/>'s
    /// <see cref="RimWorldAccess.Shell.ScreenScope"/> chassis, which reads
    /// <see cref="Options"/> and calls <see cref="Confirm"/>. Each
    /// <see cref="AddMenuOption"/> carries the raw pieces (Count, IsDisabled/DisabledReason)
    /// so the scope's row description composes them itself.
    /// </summary>
    internal static class FactionAddMenuState
    {
        public static bool IsOpen { get; private set; }

        private static readonly List<AddMenuOption> options = new List<AddMenuOption>();

        /// <summary>The addable factions, in FactionGenerator's own order -- the scope's content region.</summary>
        internal static IReadOnlyList<AddMenuOption> Options => options;

        internal class AddMenuOption
        {
            public FactionDef Faction { get; set; }
            /// <summary>How many of this faction are already in the world's faction list.</summary>
            public int Count { get; set; }
            public bool IsDisabled { get; set; }
            public string DisabledReason { get; set; }
        }

        // ===== LIFECYCLE =====

        /// <summary>Full teardown (FactionsNavigationState.Reset's idiom) -- clears the option list too, unlike Deactivate below.</summary>
        public static void Reset()
        {
            IsOpen = false;
            options.Clear();
        }

        /// <summary>
        /// Silent teardown when the factions section itself is switched away from
        /// (FactionsNavigationState.Deactivate's idiom) -- leaves the option list alone,
        /// matching the original: only IsAddMenuOpen was touched there, never options.
        /// </summary>
        public static void Deactivate()
        {
            IsOpen = false;
        }

        // ===== OPEN / CLOSE =====

        public static void Open()
        {
            // Check tutorial
            if (!TutorSystem.AllowAction("ConfiguringWorldFactions"))
            {
                TolkHelper.Speak("RimWorldAccess.Factions.CannotModifyTutorial".Loc());
                return;
            }

            RefreshOptions();

            if (options.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Factions.NoneAvailableToAdd".Loc());
                return;
            }

            // The scope's own open announcement speaks the menu name and the first
            // option once the mirror pushes it (ScreenScope.ComposeOpenAnnouncement).
            IsOpen = true;
        }

        /// <summary>
        /// Add-menu-specific close: flag off, "closed" spoken.
        /// FactionsNavigationState.CloseAddMenu (the forwarder) additionally
        /// re-announces the current faction from the LIST after calling this --
        /// that part is list state, not this class's concern.
        /// </summary>
        public static void Close()
        {
            IsOpen = false;
            TolkHelper.Speak("RimWorldAccess.Factions.AddMenuClosed".Loc());
        }

        // ===== CONFIRM =====

        /// <summary>
        /// Enter on an option row: honors the vanilla-mirrored CanAddFaction gate and, when it
        /// passes, adds the faction to the page's own faction list, then re-reads the options so
        /// the counts and gates the next row description speaks are live again.
        /// </summary>
        public static void Confirm(int index)
        {
            if (index < 0 || index >= options.Count)
            {
                TolkHelper.Speak("RimWorldAccess.Factions.NoFactionSelected".Loc());
                return;
            }

            AddMenuOption option = options[index];

            if (option.IsDisabled)
            {
                TolkHelper.Speak("RimWorldAccess.Factions.CannotAddReason".Loc(option.Faction.LabelCap, option.DisabledReason));
                return;
            }

            // Add the faction
            var factions = WorldParamsPageBridge.Factions;
            factions.Add(option.Faction);

            int newCount = factions.Count(f => f == option.Faction);
            TolkHelper.Speak("RimWorldAccess.Factions.AddedNowInList".Loc(option.Faction.LabelCap, newCount));

            // Refresh the menu options (counts may have changed, some may now be disabled).
            // The menu stays open so the user can add more factions.
            RefreshOptions();
        }

        // ===== HELPERS =====

        // MUTATION-C: mirrors WorldFactionsUIUtility.DoWindowContents's CanAddFaction
        // local function (no invokable vehicle exists — it's a nested local function,
        // not a public method). Must stay byte-equivalent to vanilla's two checks.
        private static AcceptanceReport CanAddFaction(FactionDef f)
        {
            var factions = WorldParamsPageBridge.Factions;

            // Check total non-hidden limit (12)
            if (!f.hidden && factions.Count(x => !x.hidden) >= 12)
            {
                return (string)"RimWorldAccess.Factions.MaxAllowed".Translate(12);
            }

            // Check per-faction limit. maxConfigurableAtWorldCreation == 0 must block,
            // matching vanilla exactly (no extra >0 guard).
            if (factions.Count(x => x == f) >= f.maxConfigurableAtWorldCreation)
            {
                return (string)"RimWorldAccess.Factions.MaxOfType".Translate(f.maxConfigurableAtWorldCreation);
            }

            return true;
        }

        private static void RefreshOptions()
        {
            options.Clear();
            var currentFactions = WorldParamsPageBridge.Factions;

            foreach (FactionDef def in FactionGenerator.ConfigurableFactions)
            {
                if (!def.displayInFactionSelection) continue;

                var option = new AddMenuOption { Faction = def };
                option.Count = currentFactions.Count(x => x == def);

                AcceptanceReport canAdd = CanAddFaction(def);
                if (!canAdd)
                {
                    option.IsDisabled = true;
                    option.DisabledReason = canAdd.Reason;
                }

                options.Add(option);
            }
        }
    }
}
