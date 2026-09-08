using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The ideoligion preset page (<see cref="Page_ChooseIdeoPreset"/>, the initial
    /// Classic/Custom/Load/preset choice, distinct from the src/IdeoBuilder deep editor), as three
    /// always-present regions:
    /// <list type="number">
    /// <item>Options — the four category RadioButtons, each carrying its
    /// <see cref="IdeoPresetCategoryDef"/>'s description.</item>
    /// <item>Structure and styles — a Structure ComboBox, one ComboBox per style slot, and an
    /// "Add style" Button shown only while fewer than three slots are filled. Enter opens a
    /// <see cref="WindowlessFloatMenuState"/> picker mirroring the page's own FloatMenu bodies and
    /// is the only way either value changes; Left/Right never touch a combo box. All three are
    /// gated on the page's TutorSystem.AllowAction keys, as the vanilla ButtonInvisible calls
    /// are.</item>
    /// <item>Presets — one RadioButton per preset ideo, over the same
    /// <see cref="IdeoPresetCategoryDef"/> grid vanilla draws. Crossing into a new category folds
    /// the category name into the row's Extras, since ScreenScope has no grouping primitive.</item>
    /// </list>
    /// The Buttons region declares Back/Next itself, because the page content draws its own
    /// ButtonTexts; the captured-extras region surfaces anything else vanilla or a mod draws.
    ///
    /// Every Enter is scope-routed, so the page's deferred Accept poll — which for this page
    /// dispatches to one of four side-effectful flows — must never fire on an Enter this scope
    /// consumed: <see cref="IdeologySelectionPatch_CanDoNext"/> blocks it while this scope or one
    /// of its windowless pickers is live and Next has not set <see cref="AdvanceRequested"/>.
    /// Escape reaches vanilla's Back except while a typeahead search is active
    /// (<see cref="IdeologySelectionPatch_CanDoBack"/>).
    /// </summary>
    public sealed class IdeoPresetScreenScope : ScreenScope
    {
        private enum Region { Options = 0, StructureAndStyles = 1, Presets = 2 }

        private enum StructureStyleRowKind { Structure, StyleSlot, AddStyle }

        // Shared accessors for the page's private selection state. Reads happen here; the
        // MUTATION-C write helpers in IdeologySelectionPatch reuse these same refs.
        internal static readonly AccessTools.FieldRef<Page_ChooseIdeoPreset, IdeoPresetDef> SelectedIdeoField =
            AccessTools.FieldRefAccess<Page_ChooseIdeoPreset, IdeoPresetDef>("selectedIdeo");
        internal static readonly AccessTools.FieldRef<Page_ChooseIdeoPreset, MemeDef> SelectedStructureField =
            AccessTools.FieldRefAccess<Page_ChooseIdeoPreset, MemeDef>("selectedStructure");
        internal static readonly AccessTools.FieldRef<Page_ChooseIdeoPreset, List<StyleCategoryDef>> SelectedStylesField =
            AccessTools.FieldRefAccess<Page_ChooseIdeoPreset, List<StyleCategoryDef>>("selectedStyles");
        internal static readonly AccessTools.FieldRef<Page_ChooseIdeoPreset, List<ThingStyleCategoryWithPriority>> SelectedStylesWithPriorityField =
            AccessTools.FieldRefAccess<Page_ChooseIdeoPreset, List<ThingStyleCategoryWithPriority>>("selectedStylesWithPriority");

        /// <summary>The category list's scroll offset — the vehicle the focus follow rides.</summary>
        private static readonly AccessTools.FieldRef<Page_ChooseIdeoPreset, Vector2> LeftScrollField =
            AccessTools.FieldRefAccess<Page_ChooseIdeoPreset, Vector2>("leftScrollPosition");

        // presetSelection is a private nested enum, inaccessible by name here;
        // IdeologySelectionPatch carries the reflection accessors for it.

        // Page.CanDoNext/CanDoBack/DoBack are fetched from the DECLARING type, which this page
        // overrides for neither gate. DoNext IS overridden, but invoking the Page MethodInfo on the
        // real instance still virtual-dispatches to it.
        private static readonly MethodInfo canDoNextMethod = AccessTools.Method(typeof(Page), "CanDoNext");
        private static readonly MethodInfo doNextMethod = AccessTools.Method(typeof(Page), "DoNext");
        private static readonly MethodInfo canDoBackMethod = AccessTools.Method(typeof(Page), "CanDoBack");
        private static readonly MethodInfo doBackMethod = AccessTools.Method(typeof(Page), "DoBack");

        /// <summary>Add-style affordance icon, loaded directly rather than reflected out of the page's private static field.</summary>
        private static readonly Texture2D AddStyleIcon = ContentFinder<Texture2D>.Get("UI/Buttons/Plus");

        // Scenario.playerFaction and ScenPart_PlayerFaction.factionDef are `internal` to another
        // assembly, so reading them needs reflection.
        private static readonly FieldInfo scenarioPlayerFactionField = AccessTools.Field(typeof(Scenario), "playerFaction");
        private static readonly FieldInfo scenPartFactionDefField = AccessTools.Field(typeof(ScenPart_PlayerFaction), "factionDef");

        /// <summary>The scenario's player FactionDef, or null if the scenario has no player-faction scen part.</summary>
        private static FactionDef PlayerScenarioFactionDef()
        {
            Scenario scenario = Find.Scenario;
            if (scenario == null)
            {
                return null;
            }
            object scenPart = scenarioPlayerFactionField.GetValue(scenario);
            return scenPart != null ? (FactionDef)scenPartFactionDefField.GetValue(scenPart) : null;
        }

        /// <summary>
        /// IdeoUtility.IsMemeAllowedFor dereferences faction unconditionally and throws on a null
        /// FactionDef. <see cref="PlayerScenarioFactionDef"/> can legitimately be null, so this
        /// wrapper skips the vanilla filter then instead of crashing the Structure combo.
        /// </summary>
        private static bool MemeAllowedForScenarioPlayerFaction(MemeDef meme)
        {
            FactionDef factionDef = PlayerScenarioFactionDef();
            return factionDef == null || IdeoUtility.IsMemeAllowedFor(meme, factionDef);
        }

        /// <summary>
        /// True only while the Next action drives the vanilla gate-and-advance, so
        /// <see cref="IdeologySelectionPatch_CanDoNext"/> lets that one call through while still
        /// blocking the raw keyboard Accept poll.
        /// </summary>
        internal static bool AdvanceRequested;

        /// <summary>
        /// True only while this scope's Back invocation runs its vanilla gate, letting that one call
        /// through <see cref="IdeologySelectionPatch_CanDoBack"/>, which otherwise blocks the raw
        /// Cancel poll outright while the scope is live.
        /// </summary>
        internal static bool BackRequested;

        private readonly Page_ChooseIdeoPreset page;
        private readonly List<ScreenAction> actions = new List<ScreenAction>();
        private bool announcedOpen;

        // Rebuilt every RefreshContent so page state and the model never drift.
        private readonly List<OptionRow> optionRows = new List<OptionRow>(4);
        private readonly List<StructureStyleRow> structureStyleRows = new List<StructureStyleRow>();
        private readonly List<PresetRow> presetRows = new List<PresetRow>();

        public IdeoPresetScreenScope(Page_ChooseIdeoPreset page)
        {
            this.page = page;
            // Escape = Back as a dispatcher claim: the raw Cancel poll in Page.DoBottomButtons can
            // run either side of the dispatcher depending on IMGUI focus, so Escape must be claimed
            // rather than left to fall through. The base's search-clear claim wins during a search.
            Claim(SharedMenuGrammar.Cancel, e => EscapeBack(), when: () => !HasActiveTypeaheadSearch);
            // Alt+I drill-in, claimed unconditionally: rows with nothing inspectable answer "no
            // info card available" rather than the claim itself being gated.
            Claim(SharedMenuGrammar.Info, OnInfo);
            Claim("ideoPreset.next", e => NextAction());
        }

        public override string Name
        {
            get { return "ideo-preset-page"; }
        }

        /// <summary>Option/structure/style/preset names are all worth searching.</summary>
        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        protected internal override Window OwnedWindow
        {
            get { return page; }
        }

        /// <summary>True for the page this scope was pushed for — the draw recorder's gate.</summary>
        internal bool Owns(Window window)
        {
            return ReferenceEquals(window, page);
        }

        /// <summary>The page content draws its own ButtonTexts (Back/Next); declare them instead.</summary>
        protected override bool CaptureWindowButtons
        {
            get { return false; }
        }

        /// <summary>Surface anything vanilla or a mod draws that the typed regions do not present.</summary>
        protected override bool IncludeCapturedExtrasRegion
        {
            get { return true; }
        }

        /// <summary>Exposed for the CanDoBack guard's shape parity; that guard blocks unconditionally while live.</summary>
        internal bool HasActiveTypeaheadSearch
        {
            get { return TypeaheadHasActiveSearch; }
        }

        // Content model.

        private sealed class OptionRow
        {
            public string Label;
            public string Description;
        }

        private sealed class StructureStyleRow
        {
            public StructureStyleRowKind Kind;
            public int SlotIndex;
        }

        private sealed class PresetRow
        {
            public IdeoPresetCategoryDef Category;
            public IdeoPresetDef Preset;
        }

        protected override void RefreshContent()
        {
            BuildOptionRows();
            BuildStructureStyleRows();
            BuildPresetRows();
        }

        private void BuildOptionRows()
        {
            optionRows.Clear();
            // The custom-fluid/fixed translations carry embedded newlines; flattened for speech.
            optionRows.Add(new OptionRow
            {
                Label = (string)"PlayClassic".Translate(),
                Description = IdeoPresetCategoryDefOf.Classic.description,
            });
            optionRows.Add(new OptionRow
            {
                Label = ((string)"CreateCustomFluid".Translate()).Replace("\n", " "),
                Description = IdeoPresetCategoryDefOf.Fluid.description,
            });
            optionRows.Add(new OptionRow
            {
                Label = ((string)"CreateCustomFixed".Translate()).Replace("\n", " "),
                Description = IdeoPresetCategoryDefOf.Custom.description,
            });
            optionRows.Add(new OptionRow
            {
                // Vanilla draws "LoadSaved" plus "..." on its own button.
                Label = (string)"LoadSaved".Translate() + "...",
                Description = null,
            });
        }

        private void BuildStructureStyleRows()
        {
            structureStyleRows.Clear();
            structureStyleRows.Add(new StructureStyleRow { Kind = StructureStyleRowKind.Structure });
            List<StyleCategoryDef> styles = SelectedStylesField(page);
            for (int i = 0; i < styles.Count; i++)
            {
                structureStyleRows.Add(new StructureStyleRow { Kind = StructureStyleRowKind.StyleSlot, SlotIndex = i });
            }
            if (styles.Count < 3)
            {
                structureStyleRows.Add(new StructureStyleRow { Kind = StructureStyleRowKind.AddStyle });
            }
        }

        private void BuildPresetRows()
        {
            presetRows.Clear();
            // Mirrors DoWindowContents' category loop: every IdeoPresetCategoryDef except the
            // three drawn on the Options tab, in DefDatabase order. A category with no presets adds
            // no rows on its own, since the inner loop simply contributes nothing.
            foreach (IdeoPresetCategoryDef category in DefDatabase<IdeoPresetCategoryDef>.AllDefsListForReading)
            {
                if (category == IdeoPresetCategoryDefOf.Classic
                    || category == IdeoPresetCategoryDefOf.Custom
                    || category == IdeoPresetCategoryDefOf.Fluid)
                {
                    continue;
                }
                foreach (IdeoPresetDef preset in DefDatabase<IdeoPresetDef>.AllDefs)
                {
                    if (preset.categoryDef != category)
                    {
                        continue;
                    }
                    presetRows.Add(new PresetRow { Category = category, Preset = preset });
                }
            }
        }

        protected override int ContentRegionCount
        {
            get { return 3; }
        }

        protected override string ContentRegionName(int region)
        {
            switch ((Region)region)
            {
                case Region.Options: return "RimWorldAccess.Ideology.Preset.OptionsRegion".Translate();
                case Region.StructureAndStyles: return "RimWorldAccess.Ideology.Preset.StructureStylesRegion".Translate();
                default: return "RimWorldAccess.Ideology.Preset.PresetsRegion".Translate();
            }
        }

        protected override int ContentItemCount(int region)
        {
            switch ((Region)region)
            {
                case Region.Options: return optionRows.Count;
                case Region.StructureAndStyles: return structureStyleRows.Count;
                default: return presetRows.Count;
            }
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            switch ((Region)region)
            {
                case Region.Options: return DescribeOption(index);
                case Region.StructureAndStyles: return DescribeStructureStyle(index);
                default: return DescribePreset(index);
            }
        }

        // Options region.

        private ElementDescription DescribeOption(int index)
        {
            var d = new ElementDescription();
            if (index < 0 || index >= optionRows.Count)
            {
                return d;
            }
            OptionRow row = optionRows[index];
            d.Label = row.Label;
            d.Role = ElementRole.RadioButton;
            d.Selected = IdeologySelectionPatch.GetPresetSelectionValue(page) == index;
            if (!string.IsNullOrEmpty(row.Description))
            {
                d.Extras = row.Description.StripTags();
            }
            return d;
        }

        private void ActivateOption(int index)
        {
            if (index < 0 || index >= optionRows.Count)
            {
                return;
            }
            // Vanilla passes tutorAllows: true for all four option cards, so no gate here.
            IdeologySelectionPatch.SelectOption(page, index);
            RefreshModel();
            AnnounceStateChange((int)Region.Options, index);
        }

        // Structure and styles region.

        private ElementDescription DescribeStructureStyle(int index)
        {
            if (index < 0 || index >= structureStyleRows.Count)
            {
                return new ElementDescription();
            }
            StructureStyleRow row = structureStyleRows[index];
            switch (row.Kind)
            {
                case StructureStyleRowKind.Structure: return DescribeStructureRow();
                case StructureStyleRowKind.StyleSlot: return DescribeStyleSlotRow(row.SlotIndex);
                default: return DescribeAddStyleRow();
            }
        }

        private ElementDescription DescribeStructureRow()
        {
            MemeDef structure = SelectedStructureField(page);
            var d = new ElementDescription
            {
                Label = (string)"Structure".Translate(),
                Role = ElementRole.ComboBox,
                Value = structure != null ? structure.LabelCap.ToString() : (string)"Random".Translate(),
            };
            // Vanilla's own tooltip builder, never a hand transcription.
            string tip = structure != null
                ? IdeoUIUtility.StructureTooltip(structure, IdeoEditMode.None).Resolve()
                : (string)"RandomStructureTip".Translate();
            d.Extras = tip.StripTags();
            AppendInspectableHint(d, StructureInspectableDefs(structure));
            return d;
        }

        private ElementDescription DescribeStyleSlotRow(int slotIndex)
        {
            List<StyleCategoryDef> styles = SelectedStylesField(page);
            var d = new ElementDescription
            {
                Label = "RimWorldAccess.Ideology.Preset.StyleSlotLabel".Translate(slotIndex + 1),
                Role = ElementRole.ComboBox,
            };
            if (slotIndex < 0 || slotIndex >= styles.Count)
            {
                return d;
            }
            StyleCategoryDef style = styles[slotIndex];
            d.Value = style != null ? style.LabelCap.ToString() : (string)"Random".Translate();
            string tip = style != null
                ? IdeoUIUtility.StyleTooltip(style, IdeoEditMode.None, null, SelectedStylesWithPriorityField(page)).Resolve()
                : (string)"RandomStyleTip".Translate();
            d.Extras = tip.StripTags();
            AppendInspectableHint(d, StyleSlotInspectableDefs(style));
            return d;
        }

        private ElementDescription DescribeAddStyleRow()
        {
            return new ElementDescription
            {
                Label = (string)"AddStyleCategory".Translate(),
                Role = ElementRole.Button,
                Extras = "StyleCategoryDescriptionAbstract".Translate().Resolve().StripTags(),
            };
        }

        // Nothing in this region adjusts in place, so CanAdjustContentItem stays false and
        // Enter/Space open the pickers below.

        private void ActivateStructureStyle(int index)
        {
            if (index < 0 || index >= structureStyleRows.Count)
            {
                return;
            }
            StructureStyleRow row = structureStyleRows[index];
            switch (row.Kind)
            {
                case StructureStyleRowKind.Structure:
                    OpenStructurePicker();
                    break;
                case StructureStyleRowKind.StyleSlot:
                    OpenStyleSlotPicker(row.SlotIndex);
                    break;
                default:
                    OpenAddStylePicker();
                    break;
            }
        }

        /// <summary>The page's own tutorial gate: <c>!TutorSystem.TutorialMode || TutorSystem.AllowAction(key)</c>.</summary>
        private static bool StructureStyleEditAllowed(string tutorKey)
        {
            return !TutorSystem.TutorialMode || TutorSystem.AllowAction(tutorKey);
        }

        private void OpenStructurePicker()
        {
            if (!StructureStyleEditAllowed("IdeoPresetEditStructure"))
            {
                return;
            }
            // MUTATION-C read of vanilla's own FloatMenu body (decompiled :377-398), mirrored
            // exactly: Random only offered when a structure is already chosen, then every
            // allowed structure meme other than the current one.
            MemeDef current = SelectedStructureField(page);
            var options = new List<FloatMenuOption>();
            if (current != null)
            {
                options.Add(new FloatMenuOption((string)"Random".Translate(), delegate
                {
                    IdeologySelectionPatch.SetStructure(page, null);
                    RefreshModel();
                    AnnounceComboValue((string)"Random".Translate());
                }));
            }
            foreach (MemeDef meme in DefDatabase<MemeDef>.AllDefsListForReading)
            {
                if (meme == current || meme.category != MemeCategory.Structure)
                {
                    continue;
                }
                if (!MemeAllowedForScenarioPlayerFaction(meme))
                {
                    continue;
                }
                MemeDef captured = meme;
                options.Add(new FloatMenuOption(captured.LabelCap, delegate
                {
                    IdeologySelectionPatch.SetStructure(page, captured);
                    RefreshModel();
                    AnnounceComboValue(captured.LabelCap.ToString());
                }));
            }
            if (options.Count == 0)
            {
                return;
            }
            WindowlessFloatMenuState.Open(options, colonistOrders: false, announceSelection: false);
        }

        private void OpenStyleSlotPicker(int slotIndex)
        {
            if (!StructureStyleEditAllowed("IdeoPresetEditStyle"))
            {
                return;
            }
            List<StyleCategoryDef> styles = SelectedStylesField(page);
            if (slotIndex < 0 || slotIndex >= styles.Count)
            {
                return;
            }
            // MUTATION-C read of FillAllAvailableStyles(forIndex: slotIndex) (decompiled
            // :410-453), mirrored exactly.
            StyleCategoryDef current = styles[slotIndex];
            var options = new List<FloatMenuOption>();
            if (current != null)
            {
                options.Add(new FloatMenuOption((string)"Random".Translate(), delegate
                {
                    IdeologySelectionPatch.SetStyleSlot(page, slotIndex, null);
                    RefreshModel();
                    AnnounceComboValue((string)"Random".Translate());
                }));
            }
            foreach (StyleCategoryDef s in DefDatabase<StyleCategoryDef>.AllDefs)
            {
                if (s.fixedIdeoOnly || styles.Contains(s))
                {
                    continue;
                }
                StyleCategoryDef captured = s;
                options.Add(new FloatMenuOption(captured.LabelCap, delegate
                {
                    IdeologySelectionPatch.SetStyleSlot(page, slotIndex, captured);
                    RefreshModel();
                    AnnounceComboValue(captured.LabelCap.ToString());
                }));
            }
            if (styles.Count > 1)
            {
                options.Add(new FloatMenuOption((string)"Remove".Translate(), delegate
                {
                    IdeologySelectionPatch.RemoveStyleSlot(page, slotIndex);
                    RefreshModel();
                    AnnounceCurrentItem();
                }));
            }
            if (options.Count == 0)
            {
                return;
            }
            WindowlessFloatMenuState.Open(options, colonistOrders: false, announceSelection: false);
        }

        private void OpenAddStylePicker()
        {
            if (!StructureStyleEditAllowed("IdeoPresetEditStyle"))
            {
                return;
            }
            // MUTATION-C read of FillAllAvailableStyles(forIndex: -1) (decompiled :410-453):
            // Random is always offered when adding (unlike editing an existing slot).
            List<StyleCategoryDef> styles = SelectedStylesField(page);
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption((string)"Random".Translate(), delegate
                {
                    IdeologySelectionPatch.AddStyleSlot(page, null);
                    RefreshModel();
                    AnnounceCurrentItem();
                }),
            };
            foreach (StyleCategoryDef s in DefDatabase<StyleCategoryDef>.AllDefs)
            {
                if (s.fixedIdeoOnly || styles.Contains(s))
                {
                    continue;
                }
                StyleCategoryDef captured = s;
                options.Add(new FloatMenuOption(captured.LabelCap, delegate
                {
                    IdeologySelectionPatch.AddStyleSlot(page, captured);
                    RefreshModel();
                    AnnounceCurrentItem();
                }));
            }
            WindowlessFloatMenuState.Open(options, colonistOrders: false, announceSelection: false);
        }

        /// <summary>Value-only announcement after a combo picker commits its choice.</summary>
        private static void AnnounceComboValue(string value)
        {
            var d = new ElementDescription { Value = value };
            TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(d, TranslatedShellVocabulary.Instance));
        }

        /// <summary>
        /// Value-only announcement for a row whose state changed in place: the identity was already
        /// spoken on focus. A structural change (a slot added or removed) keeps the full
        /// re-announcement, because the row under the cursor is no longer the one that was spoken.
        /// </summary>
        private void AnnounceStateChange(int region, int index)
        {
            ElementDescription d = DescribeContentItem(region, index);
            if (d == null)
            {
                return;
            }
            TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(d, TranslatedShellVocabulary.Instance));
        }

        // Presets region.

        private ElementDescription DescribePreset(int index)
        {
            var d = new ElementDescription();
            if (index < 0 || index >= presetRows.Count)
            {
                return d;
            }
            PresetRow row = presetRows[index];
            IdeoPresetDef preset = row.Preset;
            d.Label = preset.LabelCap;
            d.Role = ElementRole.RadioButton;
            d.Selected = IdeologySelectionPatch.GetPresetSelectionValue(page) == IdeologySelectionPatch.PresetSelectionPreset
                && ReferenceEquals(SelectedIdeoField(page), preset);

            var extrasParts = new List<string>();
            // Crossing into a new category folds its name in as changed context; ScreenScope has
            // no grouping primitive of its own.
            if (index == 0 || presetRows[index - 1].Category != row.Category)
            {
                extrasParts.Add(row.Category.LabelCap);
                // Sighted players read the category description in its group header.
                if (!string.IsNullOrEmpty(row.Category.description))
                {
                    extrasParts.Add(row.Category.description.StripTags());
                }
            }
            if (preset.memes != null && preset.memes.Count > 0)
            {
                var memeNames = new List<string>(preset.memes.Count);
                foreach (MemeDef meme in preset.memes)
                {
                    memeNames.Add(meme.LabelCap.ToString());
                }
                extrasParts.Add("RimWorldAccess.Ideology.Preset.MemesList".Translate(string.Join(", ", memeNames)));
            }
            if (!string.IsNullOrEmpty(preset.description))
            {
                extrasParts.Add(preset.description.StripTags());
            }
            d.Extras = string.Join(". ", extrasParts);
            AppendInspectableHint(d, PresetInspectableDefs(preset));
            return d;
        }

        private void ActivatePreset(int index)
        {
            if (index < 0 || index >= presetRows.Count)
            {
                return;
            }
            if (!StructureStyleEditAllowed("IdeoPresetSelectIdeo"))
            {
                return;
            }
            PresetRow row = presetRows[index];
            IdeologySelectionPatch.SelectPreset(page, row.Preset);
            RefreshModel();
            AnnounceStateChange((int)Region.Presets, index);
        }

        // Enter dispatch.

        /// <summary>
        /// The radio-group contract for both radio groups on this page: the Options rows and the
        /// preset list are one logical group, sharing the page's <c>presetSelection</c>. Each rides
        /// the same private-field vehicle Enter uses, silently — no preview ideoligion is generated
        /// until Next. Tutorial mode is skipped outright, because vanilla's
        /// <c>TutorSystem.AllowAction</c> gate messages the player on refusal and must not fire from
        /// mere browsing; Enter still asks the gate there.
        /// </summary>
        protected override void OnCursorSettled(int region, int index)
        {
            ScrollFocusedIntoView(region, index);
            if (TutorSystem.TutorialMode)
            {
                return;
            }
            if ((Region)region == Region.Options)
            {
                if (index < 0 || index >= optionRows.Count)
                {
                    return;
                }
                if (IdeologySelectionPatch.GetPresetSelectionValue(page) == index)
                {
                    return;
                }
                IdeologySelectionPatch.SelectOption(page, index);
                return;
            }
            if ((Region)region != Region.Presets || index < 0 || index >= presetRows.Count)
            {
                return;
            }
            IdeoPresetDef preset = presetRows[index].Preset;
            if (IdeologySelectionPatch.GetPresetSelectionValue(page) == IdeologySelectionPatch.PresetSelectionPreset
                && ReferenceEquals(SelectedIdeoField(page), preset))
            {
                return;
            }
            IdeologySelectionPatch.SelectPreset(page, preset);
        }

        protected override void ActivateContentItem(int region, int index)
        {
            switch ((Region)region)
            {
                case Region.Options:
                    ActivateOption(index);
                    break;
                case Region.StructureAndStyles:
                    ActivateStructureStyle(index);
                    break;
                default:
                    ActivatePreset(index);
                    break;
            }
        }

        // Focus ring and scroll follow (geometry from IdeoPresetDrawPatch).

        protected internal override Rect FocusedContentRect()
        {
            ListModel list = Model.CurrentRegion;
            if (list == null || list.IsEmpty)
            {
                return default(Rect);
            }
            int index = list.Index;
            Rect screen, raw;
            switch ((Region)Model.RegionIndex)
            {
                case Region.Options:
                    return IdeoPresetDrawPatch.TryGetOptionRect(index, optionRows.Count, out screen, out raw)
                        ? screen
                        : default(Rect);
                case Region.StructureAndStyles:
                    return TryGetStructureStyleRect(index, out screen) ? screen : default(Rect);
                case Region.Presets:
                    return index >= 0 && index < presetRows.Count
                        && IdeoPresetDrawPatch.TryGetPresetRect(presetRows[index].Preset, out screen, out raw)
                        ? screen
                        : default(Rect);
                default:
                    return default(Rect);
            }
        }

        /// <summary>
        /// Maps a structure/styles row onto vanilla's draw order, which runs the other way round:
        /// the add-style button first and only while fewer than three slots exist, then the style
        /// slots last to first, then the structure box.
        /// </summary>
        private bool TryGetStructureStyleRect(int index, out Rect screen)
        {
            screen = default(Rect);
            if (index < 0 || index >= structureStyleRows.Count)
            {
                return false;
            }
            int slots = SelectedStylesField(page).Count;
            bool hasAddStyle = slots < 3;
            int expected = slots + 1 + (hasAddStyle ? 1 : 0);
            StructureStyleRow row = structureStyleRows[index];
            int ordinal;
            switch (row.Kind)
            {
                case StructureStyleRowKind.Structure:
                    ordinal = expected - 1;
                    break;
                case StructureStyleRowKind.StyleSlot:
                    if (row.SlotIndex < 0 || row.SlotIndex >= slots)
                    {
                        return false;
                    }
                    ordinal = (hasAddStyle ? 1 : 0) + (slots - 1 - row.SlotIndex);
                    break;
                default:
                    ordinal = 0;
                    break;
            }
            return IdeoPresetDrawPatch.TryGetStructureStyleRect(ordinal, expected, out screen);
        }

        /// <summary>
        /// Scrolls the focused card into the category list's visible band, one corrective write per
        /// settle. Only the option and preset cards live inside that scroll view; the structure/style
        /// widget draws above it. Skipping the first settle, before any pass has recorded the page,
        /// is self-healing: the next settle follows a pass that has drawn.
        /// </summary>
        private void ScrollFocusedIntoView(int region, int index)
        {
            Rect screen, raw, outRect, viewRect;
            switch ((Region)region)
            {
                case Region.Options:
                    if (!IdeoPresetDrawPatch.TryGetOptionRect(index, optionRows.Count, out screen, out raw))
                    {
                        return;
                    }
                    break;
                case Region.Presets:
                    if (index < 0 || index >= presetRows.Count
                        || !IdeoPresetDrawPatch.TryGetPresetRect(presetRows[index].Preset, out screen, out raw))
                    {
                        return;
                    }
                    break;
                default:
                    return;
            }
            if (!IdeoPresetDrawPatch.TryGetScrollBand(out outRect, out viewRect))
            {
                return;
            }
            Vector2 scroll = LeftScrollField(page);
            float top = raw.y - viewRect.y;
            float bottom = raw.yMax - viewRect.y;
            float y = scroll.y;
            if (top < y)
            {
                y = top;
            }
            else if (bottom > y + outRect.height)
            {
                y = bottom - outRect.height;
            }
            y = Mathf.Max(y, 0f);
            if (y == scroll.y)
            {
                return;
            }
            scroll.y = y;
            LeftScrollField(page) = scroll;
        }

        // Buttons region: Back and Next ride the vanilla page vehicle.

        /// <summary>Next is this page's proceed button.</summary>
        protected override string DefaultAcceptActionId
        {
            get { return "ideoPreset.next"; }
        }

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                actions.Add(new ScreenAction("Back".Translate(), BackAction, SharedMenuGrammar.Cancel));
                actions.Add(new ScreenAction("Next".Translate(), NextAction, "ideoPreset.next"));
                return actions;
            }
        }

        private void EscapeBack()
        {
            ShellFrameStamps.MarkCancelConsumed();
            BackAction();
        }

        private void BackAction()
        {
            // Page.DoBottomButtons' Back branch: gate, then DoBack.
            BackRequested = true;
            try
            {
                if ((bool)canDoBackMethod.Invoke(page, null))
                {
                    doBackMethod.Invoke(page, null);
                }
            }
            finally
            {
                BackRequested = false;
            }
        }

        private void NextAction()
        {
            // The Next branch: gate, then DoNext, whose virtual dispatch reaches the page's own
            // override switching on presetSelection.
            AdvanceRequested = true;
            try
            {
                if ((bool)canDoNextMethod.Invoke(page, null))
                {
                    doNextMethod.Invoke(page, null);
                }
            }
            finally
            {
                AdvanceRequested = false;
            }
        }

        // Alt+I drill-in: zero inspectable defs speaks "no info card available", one opens it
        // directly, and several open a WindowlessFloatMenuState picker of def labels.

        private void OnInfo(KeyEventSnapshot e)
        {
            RefreshModel();
            ShowInfoCardPicker(CurrentRowInspectableDefs());
        }

        /// <summary>The inspectable Defs for the focused row; empty outside the three content regions.</summary>
        private List<Def> CurrentRowInspectableDefs()
        {
            if (Model.RegionIndex < 0 || Model.RegionIndex >= ContentRegionCount)
            {
                return new List<Def>();
            }
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty)
            {
                return new List<Def>();
            }
            int index = region.Index;
            switch ((Region)Model.RegionIndex)
            {
                case Region.StructureAndStyles:
                    return StructureStyleRowInspectableDefs(index);
                case Region.Presets:
                    return index >= 0 && index < presetRows.Count
                        ? PresetInspectableDefs(presetRows[index].Preset)
                        : new List<Def>();
                default:
                    return new List<Def>();
            }
        }

        private List<Def> StructureStyleRowInspectableDefs(int index)
        {
            if (index < 0 || index >= structureStyleRows.Count)
            {
                return new List<Def>();
            }
            StructureStyleRow row = structureStyleRows[index];
            if (row.Kind == StructureStyleRowKind.Structure)
            {
                return StructureInspectableDefs(SelectedStructureField(page));
            }
            if (row.Kind == StructureStyleRowKind.StyleSlot)
            {
                List<StyleCategoryDef> styles = SelectedStylesField(page);
                StyleCategoryDef style = row.SlotIndex >= 0 && row.SlotIndex < styles.Count ? styles[row.SlotIndex] : null;
                return StyleSlotInspectableDefs(style);
            }
            return new List<Def>();
        }

        /// <summary>
        /// Never inspectable: vanilla opens no Dialog_InfoCard for a MemeDef and this page has no
        /// Dialog_InfoCard call at all, so offering one here would fabricate a card. The meme's full
        /// content is already read as this row's ValueSummary.
        /// </summary>
        private static List<Def> StructureInspectableDefs(MemeDef structure)
        {
            return new List<Def>();
        }

        /// <summary>Never inspectable: no vanilla call site opens a card for a StyleCategoryDef, only for the ThingDefs it styles.</summary>
        private static List<Def> StyleSlotInspectableDefs(StyleCategoryDef style)
        {
            return new List<Def>();
        }

        /// <summary>Never inspectable: a preset's memes carry the same no-real-card limitation as the live ones above.</summary>
        private static List<Def> PresetInspectableDefs(IdeoPresetDef preset)
        {
            return new List<Def>();
        }

        /// <summary>Folds the inspectable hint onto a row's Extras when it has at least one inspectable def.</summary>
        private static void AppendInspectableHint(ElementDescription d, List<Def> defs)
        {
            if (defs.Count > 0)
            {
                d.Extras = (d.Extras ?? "") + (string)"RimWorldAccess.InfoCard.Inspectable".Translate();
            }
        }

        private static void ShowInfoCardPicker(List<Def> defs)
        {
            if (defs.Count == 0)
            {
                InfoCardState.SpeakNoInfoCardAvailable();
                return;
            }
            if (defs.Count == 1)
            {
                InfoCardState.OpenInfoCardForDef(defs[0]);
                return;
            }
            var options = new List<FloatMenuOption>();
            foreach (Def def in defs)
            {
                Def captured = def;
                string label = def.label != null ? def.label.CapitalizeFirst() : def.defName;
                options.Add(new FloatMenuOption(label, delegate { InfoCardState.OpenInfoCardForDef(captured); }));
            }
            TolkHelper.Speak("RimWorldAccess.InfoCard.ChooseItemToInspect".Loc());
            WindowlessFloatMenuState.Open(options, false);
        }

        // Captured-extras suppression.

        /// <summary>
        /// Folds into the captured-extras diff the strings this scope presents another way: the
        /// intro paragraph, and the icon-only invisible buttons in the structure/style widget, whose
        /// generic label is the icon's texture name and whose state the ComboBox rows already speak.
        /// </summary>
        protected override IEnumerable<string> AdditionalPresentedTexts
        {
            get
            {
                // The page title and section headers are conveyed by the opening announcement and
                // the region names, the Structure/Styles labels by the combo rows.
                yield return (string)"ChooseYourIdeoligion".Translate();
                yield return (string)"ChooseYourIdeoligionDesc".Translate();
                yield return (string)"CustomIdeoligions".Translate();
                yield return (string)"Structure".Translate();
                yield return (string)"Styles".Translate();
                // The Presets region mirrors the whole card grid, but WidgetCapture fuses each
                // visual GRID ROW into one captured line no single presented row contains. Present
                // each category's header fields plus one composite line in draw order, so every
                // fused slice is a substring of a presented line.
                string lastGroup = null;
                foreach (var kv in PresentedCategoryComposites())
                {
                    if (kv != lastGroup)
                    {
                        yield return kv;
                        lastGroup = kv;
                    }
                }
                if (AddStyleIcon != null && !string.IsNullOrEmpty(AddStyleIcon.name))
                {
                    yield return AddStyleIcon.name;
                }
                if (page.RandomIcon != null && !string.IsNullOrEmpty(page.RandomIcon.name))
                {
                    yield return page.RandomIcon.name;
                }
                MemeDef structure = SelectedStructureField(page);
                if (structure != null && structure.Icon != null && !string.IsNullOrEmpty(structure.Icon.name))
                {
                    yield return structure.Icon.name;
                }
                foreach (StyleCategoryDef style in SelectedStylesField(page))
                {
                    if (style != null && style.Icon != null && !string.IsNullOrEmpty(style.Icon.name))
                    {
                        yield return style.Icon.name;
                    }
                }
            }
        }

        /// <summary>
        /// Haystack lines covering the preset card grid: per category, in presetRows' draw order,
        /// its group label, its own label, and a composite of description plus every preset name, so
        /// any fused grid-row slice normalizes to a substring of one line.
        /// </summary>
        private IEnumerable<string> PresentedCategoryComposites()
        {
            IdeoPresetCategoryDef current = null;
            var names = new List<string>();
            for (int i = 0; i <= presetRows.Count; i++)
            {
                IdeoPresetCategoryDef cat = i < presetRows.Count ? presetRows[i].Category : null;
                if (cat != current)
                {
                    if (current != null)
                    {
                        yield return current.groupLabel;
                        yield return (string)current.LabelCap;
                        string desc = string.IsNullOrEmpty(current.description)
                            ? ""
                            : current.description.StripTags() + " ";
                        yield return desc + string.Join(", ", names);
                    }
                    current = cat;
                    names.Clear();
                }
                if (i < presetRows.Count)
                {
                    names.Add(presetRows[i].Preset.LabelCap.ToString());
                }
            }
        }

        // Lifecycle.

        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen)
            {
                return;
            }
            announcedOpen = true;
            string tabCount = TabCountFragment();
            string title = (string)"ChooseYourIdeoligion".Translate();
            string desc = ((string)"ChooseYourIdeoligionDesc".Translate()).StripTags();
            string opening = string.IsNullOrEmpty(tabCount) ? title : tabCount + ". " + title;
            opening += ". " + desc;
            TolkHelper.SpeakData(opening);
            AnnounceCurrentItem();
        }
    }
}
