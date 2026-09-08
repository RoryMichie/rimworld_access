using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The keyboard screen for the real <see cref="Dialog_EntityCodex"/> window (Anomaly's
    /// discovered-entity browser), registered through <see cref="ScopeForWindow"/>, with
    /// <see cref="RimWorldAccess.EntityCodexState"/> as its data facade.
    ///
    /// The codex looks like a master/detail dialog but is not: vanilla paints a category rail
    /// and a detail pane, and the accessible surface is ONE flat list of
    /// <see cref="ElementRole.MenuItem"/> rows whose per-row announcement folds the detail in
    /// as <see cref="ElementDescription.Extras"/>. There is no second cursor to model. Enter
    /// re-announces (nothing here is activatable), Alt+I opens the linked-thing/research
    /// drill-in picker, RightBracket opens the dev context menu.
    ///
    /// No Buttons region: the dialog's only bottom button is Close, which Escape provides, and
    /// its content draws <c>Widgets.ButtonText</c> per research hyperlink and for
    /// "DEV: Discover", the over-capture case <see cref="CaptureWindowButtons"/> warns about.
    ///
    /// DELIBERATE DEVIATION: <see cref="IsLive"/> excludes
    /// <see cref="WindowlessResearchMenuState.IsActive"/>, so a research menu opened from the
    /// codex's own drill-in picker replaces the codex rather than being starved by it. The term
    /// stays explicit even though that state is now a modal <see cref="ResearchMenuScope"/>
    /// mirror: this scope is window-attached, so relying on scope order would depend on
    /// mirror-Reconcile timing relative to the ScopeForWindow attach, with no guarantee between
    /// the two mechanisms. IsLive false makes this scope a non-masking shadow, so keys fall
    /// through and the codex resumes when the research menu closes.
    /// </summary>
    public sealed class EntityCodexScope : ScreenScope
    {
        private readonly Dialog_EntityCodex dialog;
        private bool announcedOpen;

        public EntityCodexScope(Dialog_EntityCodex dialog)
        {
            this.dialog = dialog;

            // The base's typeahead claim, registered first, clears an active search; with none
            // this closes the window. EntityCodexPatch blocks vanilla's own Escape router for
            // this dialog, so the close has to be explicit.
            Claim(SharedMenuGrammar.Cancel, delegate { CloseDialog(); });

            Claim("entityCodex.drillIn", delegate { OpenDrillInPicker(); });
            // Both the debug actions and the menu itself require dev mode plus god mode,
            // exactly as vanilla gates them, so a non-dev RightBracket falls through.
            Claim("entityCodex.contextMenu",
                delegate { OpenDevContextMenu(); },
                when: () => Prefs.DevMode && DebugSettings.godMode);
        }

        public override string Name
        {
            get { return "entity-codex"; }
        }

        public override bool IsLive
        {
            get { return EntityCodexState.IsActive && !WindowlessResearchMenuState.IsActive; }
        }

        /// <summary>
        /// Dialog_EntityCodex overrides neither accept/cancel handler and leaves
        /// closeOnAccept/closeOnCancel at Window's defaults, so the base Window-level routers
        /// see every call. Deliberately redundant with EntityCodexPatch's own blocking prefixes.
        /// </summary>
        public override bool OwnsAccept
        {
            get { return true; }
        }

        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        /// <summary>The dialog's content draws its own buttons, so capture would over-capture.</summary>
        protected override bool IncludeActionsRegion
        {
            get { return false; }
        }

        private protected override void CollectRouteCandidates(
            List<PointerHitCandidate> candidates, List<RouteTarget> targets)
        {
            IReadOnlyList<EntityCodexEntryDef> entries = EntityCodexState.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                EntityCodexEntryRingPatch.rowGeometry.AddCandidate(entries[i], 0, i, candidates, targets);
            }
        }

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        /// <summary>Vanilla's own codex title.</summary>
        protected override string ContentRegionName(int region)
        {
            return (string)"EntityCodex".Translate();
        }

        protected override int ContentItemCount(int region)
        {
            return EntityCodexState.Entries.Count;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            IReadOnlyList<EntityCodexEntryDef> entries = EntityCodexState.Entries;
            if (index < 0 || index >= entries.Count)
                return new ElementDescription();

            EntityCodexEntryDef entry = entries[index];
            bool revealed = EntityCodexState.IsRevealed(entry);
            return new ElementDescription
            {
                // An undiscovered entry speaks vanilla's placeholder name, not its real one,
                // and typeahead matches that same text since the base matches on this Label.
                Label = revealed
                    ? entry.LabelCap.Resolve()
                    : (string)"UndiscoveredEntity".Translate().Resolve(),
                Role = ElementRole.MenuItem,
                Extras = BuildEntryDetail(entry, revealed),
            };
        }

        /// <summary>
        /// Enter re-announces: a pure browse list has nothing to activate. During a search the
        /// base settles that first.
        /// </summary>
        protected override void ActivateContentItem(int region, int index)
        {
            AnnounceCurrentItem();
        }

        public override void OnPush()
        {
            base.OnPush();
            TypeaheadReset();
            announcedOpen = false;
        }

        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen)
                return;
            announcedOpen = true;

            // Land on the entry the dialog itself had selected before speaking.
            ListModel region = Model.CurrentRegion;
            int initial = EntityCodexState.InitialIndex;
            if (region != null && initial > 0 && initial < region.Count)
            {
                region.MoveTo(initial);
            }

            // Three-part open announcement: title with count, codex blurb, landing row.
            string title = "EntityCodex".Translate().Resolve();
            TolkHelper.SpeakData(
                (string)"RimWorldAccess.Anomaly.Codex.Opened".Translate(title, EntityCodexState.Entries.Count));

            string desc = "EntityCodexDesc".Translate().Resolve();
            if (!string.IsNullOrEmpty(desc))
            {
                TolkHelper.SpeakData(SanitizeText(desc));
            }

            if (EntityCodexState.Entries.Count > 0)
            {
                AnnounceCurrentItem();
            }
        }

        /// <summary>
        /// The verbose tail: category, description, linked things (each named only when
        /// revealed), then research unlocks. An unrevealed entry gets vanilla's placeholder
        /// blurb instead of any real content.
        /// </summary>
        private static string BuildEntryDetail(EntityCodexEntryDef entry, bool revealed)
        {
            var parts = new List<string>();

            string category = entry.category?.LabelCap.Resolve();
            if (!string.IsNullOrEmpty(category))
            {
                parts.Add(category);
            }

            if (!revealed)
            {
                parts.Add(SanitizeText("UndiscoveredEntityDesc".Translate().Resolve()));
                return JoinSentences(parts);
            }

            parts.Add(SanitizeText(entry.Description));

            if (entry.linkedThings != null && entry.linkedThings.Count > 0)
            {
                string undiscovered = "Undiscovered".Translate().Resolve();
                var names = new List<string>(entry.linkedThings.Count);
                for (int i = 0; i < entry.linkedThings.Count; i++)
                {
                    ThingDef linked = entry.linkedThings[i];
                    names.Add(EntityCodexState.IsThingRevealed(linked)
                        ? linked.LabelCap.ToString()
                        : undiscovered);
                }
                parts.Add(string.Join(", ", names.ToArray()));
            }

            if (entry.discoveredResearchProjects != null && entry.discoveredResearchProjects.Count > 0)
            {
                var names = new List<string>(entry.discoveredResearchProjects.Count);
                for (int i = 0; i < entry.discoveredResearchProjects.Count; i++)
                {
                    names.Add(entry.discoveredResearchProjects[i].LabelCap.ToString());
                }
                parts.Add((string)"ResearchUnlocks".Translate().Resolve() + ": " + string.Join(", ", names.ToArray()));
            }

            return JoinSentences(parts);
        }

        /// <summary>The entry under the keyboard cursor, for <see cref="EntityCodexEntryRingPatch"/>. With one content region and nothing else, the cursor is always on an entry.</summary>
        internal EntityCodexEntryDef FocusedEntry
        {
            get { return CurrentEntry(); }
        }

        private EntityCodexEntryDef CurrentEntry()
        {
            IReadOnlyList<EntityCodexEntryDef> entries = EntityCodexState.Entries;
            ListModel region = Model.CurrentRegion;
            if (region == null || entries.Count == 0)
                return null;
            int index = region.Index;
            return index >= 0 && index < entries.Count ? entries[index] : null;
        }

        private void CloseDialog()
        {
            if (dialog != null)
            {
                dialog.Close();
            }
        }

        /// <summary>
        /// Alt+I drill-in picker: a float menu of the entry's revealed linked things and its
        /// research unlocks; an undiscovered entry refuses.
        /// </summary>
        private void OpenDrillInPicker()
        {
            EntityCodexEntryDef entry = CurrentEntry();
            if (entry == null)
                return;

            if (!EntityCodexState.IsRevealed(entry))
            {
                TolkHelper.Speak("UndiscoveredEntityDesc".Loc());
                return;
            }

            var options = new List<FloatMenuOption>();

            if (entry.linkedThings != null)
            {
                for (int i = 0; i < entry.linkedThings.Count; i++)
                {
                    ThingDef linked = entry.linkedThings[i];
                    if (linked == null || !EntityCodexState.IsThingRevealed(linked))
                        continue;
                    ThingDef captured = linked;
                    options.Add(new FloatMenuOption(
                        captured.LabelCap.ToString(),
                        () => Find.WindowStack.Add(new Dialog_InfoCard(captured))));
                }
            }

            if (entry.discoveredResearchProjects != null)
            {
                string researchPrefix = "ResearchUnlocks".Translate().Resolve();
                for (int i = 0; i < entry.discoveredResearchProjects.Count; i++)
                {
                    ResearchProjectDef project = entry.discoveredResearchProjects[i];
                    if (project == null)
                        continue;
                    ResearchProjectDef captured = project;
                    options.Add(new FloatMenuOption(
                        $"{researchPrefix}: {captured.LabelCap}",
                        () => WindowlessResearchMenuState.OpenAndSelectProject(captured)));
                }
            }

            if (options.Count == 0)
            {
                TolkHelper.Speak("None".Loc());
                return;
            }

            WindowlessFloatMenuState.Open(options, colonistOrders: false);
        }

        /// <summary>
        /// DEV context menu mirroring Dialog_EntityCodex's own god-mode-gated debug controls.
        /// Both require <see cref="Prefs.DevMode"/> and <see cref="DebugSettings.godMode"/>,
        /// exactly like vanilla; labels are vanilla's dev-tool literals verbatim.
        /// </summary>
        private void OpenDevContextMenu()
        {
            if (!Prefs.DevMode || !DebugSettings.godMode)
                return;

            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("DEV: Show all", delegate
                {
                    EntityCodexState.ToggleDevShowAll();
                    var toggled = new ElementDescription
                    {
                        Role = ElementRole.Checkbox,
                        Check = EntityCodexState.DevShowAll ? CheckState.Checked : CheckState.Unchecked,
                    };
                    // The float menu closes on selection, so the bare state word would have no
                    // context; name the control.
                    TolkHelper.SpeakData("DEV: Show all. " // l10n-exempt: verbatim vanilla dev label (Dialog_EntityCodex.cs:83), itself unlocalized
                        + AnnouncementComposer.ComposeStateChange(toggled, TranslatedShellVocabulary.Instance));
                    AnnounceCurrentItem();
                })
            };

            EntityCodexEntryDef entry = CurrentEntry();
            if (entry != null && !entry.Discovered)
            {
                EntityCodexEntryDef captured = entry;
                options.Add(new FloatMenuOption("DEV: Discover", delegate
                {
                    EntityCodexState.SetDiscovered(captured);
                    TolkHelper.Speak("RimWorldAccess.Dev.CodexDiscovered".Loc(captured.LabelCap.Resolve()));
                    AnnounceCurrentItem();
                }));
            }

            WindowlessFloatMenuState.Open(options, colonistOrders: false);
        }

        /// <summary>
        /// Joins detail fragments the way <see cref="AnnouncementComposer"/> joins its own,
        /// skipping the separating period after a fragment that already ends a sentence.
        /// </summary>
        private static string JoinSentences(List<string> parts)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < parts.Count; i++)
            {
                string fragment = parts[i] == null ? "" : parts[i].Trim();
                if (fragment.Length == 0)
                    continue;
                if (sb.Length > 0)
                {
                    char last = sb[sb.Length - 1];
                    if (last != '.' && last != '!' && last != '?' && last != ':')
                        sb.Append('.');
                    sb.Append(' ');
                }
                sb.Append(fragment);
            }
            return sb.ToString();
        }

        /// <summary>Strips the paragraph newlines vanilla embeds in game text.</summary>
        private static string SanitizeText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";
            return text.Replace("\n\n", ". ").Replace("\n", " ").Trim();
        }
    }

    /// <summary>
    /// Paints the shared focus ring on vanilla's own codex tile and scrolls a focused tile back
    /// into view. Both halves use vanilla's geometry — the rect <c>DrawEntry</c> was handed and
    /// the band <c>RightRect</c> scrolls inside — so no tile position is derived from a row
    /// height or an index. Identity is the <see cref="EntityCodexEntryDef"/> vanilla hands its
    /// painter, matched by reference; vanilla's own selected-entry background stays untouched.
    ///
    /// The scroll half is viable here because this grid does not cull: every entry's rect is
    /// computed and drawn. It is change-gated on the focused entry, so the view is positioned
    /// once per move rather than pinned every frame.
    /// </summary>
    internal static class EntityCodexEntryRingPatch
    {
        private static readonly AccessTools.FieldRef<Dialog_EntityCodex, Vector2> rightScrollPos =
            AccessTools.FieldRefAccess<Dialog_EntityCodex, Vector2>("rightScrollPos");

        private static EntityCodexEntryDef lastScrolledTo;
        private static Rect focusedRect;

        /// <summary>Every entry's own rect, so Alt+Shift+J can route to the tile the mouse is on.</summary>
        internal static readonly RowGeometryCache rowGeometry = new RowGeometryCache();
        private static bool focusedDrawn;

        [HarmonyPatch(typeof(Dialog_EntityCodex), "DrawEntry")]
        internal static class DrawEntryPatch
        {
            [HarmonyPostfix]
            public static void Postfix(Rect rect, EntityCodexEntryDef entry)
            {
                try
                {
                    EntityCodexScope scope = FocusStackLookup.TopmostOfType<EntityCodexScope>();
                    if (scope == null || entry == null)
                    {
                        return;
                    }
                    EntityCodexEntryRingPatch.rowGeometry.Record(entry, rect);
                    if (entry != scope.FocusedEntry)
                    {
                        return;
                    }
                    focusedRect = rect;
                    focusedDrawn = true;
                    if (Event.current.type == EventType.Repaint)
                    {
                        FocusRing.Draw(rect.ContractedBy(1f));
                    }
                }
                catch (Exception ex)
                {
                    ModLogger.LimitedError("Entity codex ring error", ex);
                }
            }
        }

        /// <summary>Applies the scroll once the whole grid has been walked, so the focused tile's own rect is in hand.</summary>
        [HarmonyPatch(typeof(Dialog_EntityCodex), "RightRect")]
        internal static class RightRectPatch
        {
            [HarmonyPostfix]
            public static void Postfix(Dialog_EntityCodex __instance, Rect rect)
            {
                try
                {
                    EntityCodexScope scope = FocusStackLookup.TopmostOfType<EntityCodexScope>();
                    EntityCodexEntryDef focused = scope == null ? null : scope.FocusedEntry;
                    bool drawn = focusedDrawn;
                    focusedDrawn = false;
                    if (focused == null || !drawn)
                    {
                        lastScrolledTo = focused;
                        return;
                    }
                    if (focused == lastScrolledTo)
                    {
                        return;
                    }
                    lastScrolledTo = focused;

                    Vector2 scroll = rightScrollPos(__instance);
                    if (focusedRect.yMin < scroll.y)
                    {
                        scroll.y = focusedRect.yMin;
                    }
                    else if (focusedRect.yMax > scroll.y + rect.height)
                    {
                        scroll.y = focusedRect.yMax - rect.height;
                    }
                    else
                    {
                        return;
                    }
                    rightScrollPos(__instance) = new Vector2(scroll.x, Mathf.Max(0f, scroll.y));
                }
                catch (Exception ex)
                {
                    ModLogger.LimitedError("Entity codex scroll error", ex);
                }
            }
        }
    }
}
