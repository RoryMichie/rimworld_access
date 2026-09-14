using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Main-menu (Entry) keyboard access. Captures the option lists vanilla's own
    /// DoMainMenuControls constructs — via <see cref="OptionListingCapturePatch"/> on
    /// OptionListingUtility.DrawOptionListing, its only two call sites being the menu's
    /// two columns (decompiled RimWorld/MainMenuDrawer.cs:265 and :306) — instead of
    /// re-deriving them line-by-line, so a new DLC button, a reordered item, or another
    /// mod's injected option is inherited automatically.
    ///
    /// Deliberate transforms applied to the captured lists:
    /// - The Tutorial option (identified by its vanilla InitLearnToPlay delegate, never
    ///   by label) is dropped from the keyboard menu — a documented keyboard-hostile
    ///   deviation; vanilla's own read-only draw is untouched, so the button still shows.
    /// - ListableOption_WebLink entries constructed with a url and no action get the
    ///   OpenURL fallback their own DrawOption applies on click (decompiled
    ///   Verse/ListableOption_WebLink.cs, null-action branch), so Enter works on them.
    /// - The mod's What's New, Website and Discord items are prepended to the links column.
    ///
    /// The Playing pause-menu tab is PauseMenuScope's; this patch owns Entry only.
    /// </summary>
    [HarmonyPatch(typeof(MainMenuDrawer), "DoMainMenuControls")]
    public static class MainMenuAccessibilityPatch
    {
        private static bool announcedMainMenu = false;
        private static ProgramState lastAnnouncedState = ProgramState.Entry;

        private static List<ListableOption> capturedColumn0;
        private static List<ListableOption> capturedColumn1;
        private static Rect capturedRect0;
        private static Rect capturedRect1;

        // Vanilla's Tutorial option is built as `new ListableOption(label, InitLearnToPlay)`
        // (decompiled RimWorld/MainMenuDrawer.cs:138, method at :465) — the delegate is the
        // stable identity signal for it across languages.
        private static readonly MethodInfo InitLearnToPlayMethod =
            AccessTools.Method(typeof(MainMenuDrawer), "InitLearnToPlay");

        internal static bool Capturing { get; private set; }
        private static int captureCallIndex;
        private static int lastPostfixFrame = int.MinValue;

        [HarmonyPrefix]
        public static void Prefix()
        {
            if (Current.ProgramState != ProgramState.Entry)
                return;

            Capturing = true;
            captureCallIndex = 0;
            capturedColumn0 = null;
            capturedColumn1 = null;
        }

        /// <summary>
        /// Called by <see cref="OptionListingCapturePatch"/> for each DrawOptionListing
        /// call while capturing. Vanilla draws the main column first, the links column
        /// second (MainMenuDrawer.cs:265, :306); further calls (another mod drawing its
        /// own listing mid-menu) are ignored rather than misfiled.
        /// </summary>
        internal static void RecordListing(Rect rect, List<ListableOption> options)
        {
            if (captureCallIndex == 0)
            {
                capturedRect0 = rect;
                capturedColumn0 = options;
            }
            else if (captureCallIndex == 1)
            {
                capturedRect1 = rect;
                capturedColumn1 = options;
            }
            captureCallIndex++;
        }

        [HarmonyPostfix]
        public static void Postfix(Rect rect, bool anyMapFiles)
        {
            if (Current.ProgramState != ProgramState.Entry)
                return;

            Capturing = false;

            // Mark main menu as active for the layout-aware typeahead dispatcher.
            // DoMainMenuControls runs every frame the main menu is visible, so
            // MenuNavigationState.IsActive (frame-recency check) stays true while drawn.
            MenuNavigationState.MarkRendered();

            // The full ordered checklist of static per-screen resets lives in
            // StateResetRegistry.OnReturnToMainMenu — see that file for the manifest.
            // Edge-triggered: the checklist clears SESSION leftovers (its entries close
            // any state active at Entry), so it must fire once on ARRIVAL at the menu,
            // not every frame — a per-frame run instantly closes legitimate menu-opened
            // overlays (the BuySoundtrack windowless float menu). Arrival = this postfix
            // did not run last frame (startup, quit-to-menu, or any surface that stops
            // the menu drawing).
            int frame = Time.frameCount;
            if (frame - lastPostfixFrame > 1)
                StateResetRegistry.RunOnReturnToMainMenu();
            lastPostfixFrame = frame;

            if (capturedColumn0 == null || capturedColumn1 == null)
                return;

            List<ListableOption> column0 = TransformMainColumn(capturedColumn0);
            List<ListableOption> column1 = TransformLinksColumn(capturedColumn1);

            // Cursor persistence across Entry arrivals is deliberate
            // (remember-my-place): the scope's ScreenModel is a singleton
            // that survives OnPush/OnPop, so — unlike the retired
            // once-ever Reset() call this replaced — no explicit reset is
            // needed even on the very first launch; a fresh ListModel
            // already starts at index 0.
            MenuNavigationState.Initialize(column0, column1);

            // Announce main menu when first appearing or when returning from a game
            if (!announcedMainMenu || lastAnnouncedState != ProgramState.Entry)
            {
                announcedMainMenu = true;
                lastAnnouncedState = ProgramState.Entry;
                TolkHelper.Speak("GameOverMainMenu".Loc(), SpeechPriority.Normal);

                // Surface the "What's New" message here (once per launch) if the mod updated
                // since the player last ran it, so it never interrupts a game.
                WhatsNewState.NotifyMainMenuReached();
            }

            DrawSelectionHighlight(rect);
        }

        private static List<ListableOption> TransformMainColumn(List<ListableOption> source)
        {
            var result = new List<ListableOption>(source.Count);
            foreach (ListableOption option in source)
            {
                if (option?.action != null && option.action.Method == InitLearnToPlayMethod)
                {
                    // Documented keyboard-hostile deviation: the tutorial's forced-mouse,
                    // exact-placement lessons can't be done by keyboard, so it is dropped
                    // from the keyboard menu rather than offered as a dead end.
                    continue;
                }
                result.Add(option);
            }
            return result;
        }

        private static List<ListableOption> TransformLinksColumn(List<ListableOption> source)
        {
            var result = new List<ListableOption>(source.Count + 3)
            {
                new ListableOption("RimWorldAccess.WhatsNew.MenuItem.WhatsNew".Translate(),
                    delegate { WhatsNewState.Open(); }),
                new ListableOption("RimWorldAccess.WhatsNew.MenuItem.Website".Translate(),
                    delegate { Application.OpenURL("https://rimworldaccess.com"); }),
                new ListableOption("RimWorldAccess.WhatsNew.MenuItem.Discord".Translate(),
                    delegate { Application.OpenURL("https://discord.rimworldaccess.com"); })
            };

            foreach (ListableOption option in source)
            {
                // Mirrors ListableOption_WebLink.DrawOption's own click fallback
                // (action null -> Application.OpenURL(url)) so keyboard activation
                // through MenuNavigationState.ActivateSelected behaves identically.
                if (option is ListableOption_WebLink webLink
                    && webLink.action == null
                    && !webLink.url.NullOrEmpty())
                {
                    string url = webLink.url;
                    webLink.action = delegate { Application.OpenURL(url); };
                }
                result.Add(option);
            }
            return result;
        }

        private static void DrawSelectionHighlight(Rect menuRect)
        {
            int column = MenuNavigationState.CurrentColumn;
            int selectedIndex = MenuNavigationState.SelectedIndex;

            List<ListableOption> currentList = MenuNavigationState.CurrentColumnOptions;
            if (currentList == null || selectedIndex < 0 || selectedIndex >= currentList.Count)
                return;

            Rect columnRect = (column == 0) ? capturedRect0 : capturedRect1;

            // Mirror the layout math of OptionListingUtility.DrawOptionListing (7f spacing)
            // and ListableOption.DrawOption / ListableOption_WebLink.DrawOption
            // (max(minHeight, CalcHeight)) so the highlight lands on the real row.
            Text.Font = GameFont.Small;
            float yOffset = 0f;
            for (int i = 0; i < selectedIndex; i++)
            {
                yOffset += OptionHeight(currentList[i], columnRect.width) + 7f;
            }
            float height = OptionHeight(currentList[selectedIndex], columnRect.width);

            Rect highlightRect = new Rect(
                menuRect.x + columnRect.x,
                menuRect.y + columnRect.y + yOffset + 17f,
                columnRect.width,
                height);

            Widgets.DrawHighlight(highlightRect);
        }

        private static float OptionHeight(ListableOption option, float columnWidth)
        {
            float labelWidth = option is ListableOption_WebLink webLink && webLink.image != null
                ? columnWidth - 24f - 3f
                : columnWidth;
            return Mathf.Max(option.minHeight, Text.CalcHeight(option.label, labelWidth));
        }
    }

    /// <summary>
    /// Capture tap for <see cref="MainMenuAccessibilityPatch"/>: records the exact
    /// (rect, list) pairs vanilla's DoMainMenuControls passes to its two
    /// OptionListingUtility.DrawOptionListing calls. Read-only — never alters drawing.
    /// </summary>
    [HarmonyPatch(typeof(OptionListingUtility), nameof(OptionListingUtility.DrawOptionListing))]
    public static class OptionListingCapturePatch
    {
        [HarmonyPrefix]
        public static void Prefix(Rect rect, List<ListableOption> optList)
        {
            if (MainMenuAccessibilityPatch.Capturing)
                MainMenuAccessibilityPatch.RecordListing(rect, optList);
        }
    }
}
