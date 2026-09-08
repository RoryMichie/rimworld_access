using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Focus scope for the Character Editor mod's editor window
    /// (<c>CharacterEditor.CEditor+EditorUI</c>). All reflection lives in
    /// <see cref="RimWorldAccess.CharEditorCompat"/>; this scope only calls its typed methods.
    /// Region 0 is the pawn-selection strip, region 1 is one sectioned tree over the mod's eight
    /// tabs (hence <see cref="TreeRegionScope"/>; <see cref="TreeRegionIndex"/> points the base's
    /// tree arithmetic at it), and the Buttons region carries the mod's icon-strip actions.
    /// Rows whose facade bindings are missing are omitted rather than announced as broken.
    /// The mod pushes the live map selection into the editor every frame
    /// (<c>BlockPawnList.CheckPawnFromSelector</c>), so <see cref="RefreshContent"/> reconciles on
    /// pawn identity, silently; the switch paths announce for themselves.
    /// Section names read whichever source the mod's own tab strip uses, so they match the
    /// sighted tab labels in every language.
    /// <c>EditorUI</c> never sets <c>absorbInputAroundWindow</c>, so a vanilla Escape opens the
    /// pause menu OVER it: <see cref="OwnsCancel"/> is true and closes through the window's own
    /// <c>Close</c> (casket ejection, window-position save).
    /// </summary>
    internal sealed partial class CharacterEditorScope : TreeRegionScope
    {
        /// <summary>The scope's two typed content components, mirroring the mod's own two upper zones.</summary>
        private enum Region
        {
            PawnSelection = 0,
            Editor = 1,
        }

        /// <summary>A pawn-selection row's identity; the live list omits rows whose bindings are missing.</summary>
        private enum PrefixKind
        {
            EditedPawn,
            ListSource,
            OnMapFilter,
            CreationMode,
            StartingCount,
        }

        /// <summary>Which of the mod's tabs a tree section presents. Carried as a section node's Data.</summary>
        private enum SectionKind
        {
            Character,
            Appearance,
            Inventory,
            Health,
            Needs,
            Social,
            Log,
            Info,
            Records,
        }

        /// <summary>The mod's <c>TabType</c> member name each section mirrors, for the visual tab sync.</summary>
        private const string CharacterTabName = "BlockBio";
        private const string InventoryTabName = "BlockInventory";
        private const string HealthTabName = "BlockHealth";
        private const string NeedsTabName = "BlockNeeds";
        private const string SocialTabName = "BlockSocial";
        private const string LogTabName = "BlockLog";
        private const string InfoTabName = "BlockInfo";
        private const string RecordsTabName = "BlockRecords";

        /// <summary>A Character-section leaf's identity, carried as a leaf node's <c>Data</c>.</summary>
        private enum CharRowKind
        {
            NameFirst,
            NameNick,
            NameLast,
            NameSingle,
            NameRandomize,
            NameRandomizeFull,
            NameRoyalTitle,
            AgeBiological,
            AgeChronological,
            BackstoryChildhood,
            BackstoryAdulthood,
            BackstoryRandomChildhood,
            BackstoryRandomAdulthood,
            IncapableEntry,
            TraitAdd,
            TraitCopy,
            TraitPaste,
            TraitRandomize,
            TraitEntry,
            SkillCopy,
            SkillPaste,
            SkillRandomize,
            SkillEntry,
            AgeBirthday,
            AbilityAdd,
            AbilityCopy,
            AbilityPaste,
            AbilityRandomize,
            AbilityEntry,
            PsycastEntropy,
            PsycastPsyfocus,
            IdentityFaction,
            IdentityIdeo,
            IdentityXenoView,
            IdentityXenoEdit,
            IdentityFavoriteColor,
            IdentityMutant,
            IdentityRoyalTitle,
            IdentityRecruit,
            IdentityEnslave,
            TrainingMaster,
            TrainingTrainability,
            TrainingEntry,
            NameChangeRace,
        }

        /// <summary>A leaf's identity: which control of its region, plus the control's subject. Carried as the node's Data.</summary>
        private sealed class RegionRow<TKind> where TKind : struct
        {
            public TKind Kind;
            public object Payload;

            public RegionRow(TKind kind, object payload = null)
            {
                Kind = kind;
                Payload = payload;
            }
        }

        private static InspectionTreeItem AddRow<TKind>(InspectionTreeItem parent, TKind kind, string label, object payload = null) where TKind : struct
        {
            var item = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Item,
                Label = label,
                IndentLevel = parent.IndentLevel + 1,
                IsExpandable = false,
                Data = new RegionRow<TKind>(kind, payload),
                Parent = parent,
            };
            parent.Children.Add(item);
            return item;
        }

        /// <summary>One state-change utterance for the row under the cursor after a Left/Right, Space or picker mutation.</summary>
        private void AnnounceAdjustedRow<TKind>(InspectionTreeItem item, Action<RegionRow<TKind>, ElementDescription> describe) where TKind : struct
        {
            RefreshModel();
            if (item?.Data is RegionRow<TKind> row)
            {
                var d = new ElementDescription();
                describe(row, d);
                TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(d, TranslatedShellVocabulary.Instance));
                return;
            }
            AnnounceCurrentItem();
        }

        /// <summary>
        /// An Appearance-section leaf's identity, carried as a leaf node's <c>Data</c>.
        /// <see cref="FacialHead"/> through <see cref="FacialSkin"/> are the Facial Stuff morph
        /// controllers, built only when <see cref="CharEditorCompat.HasFacialHeadController"/> gates
        /// them on; <see cref="HeadVanilla"/> is their mutually-exclusive sibling.
        /// </summary>
        private enum AppearanceRowKind
        {
            HairStyle,
            Beard,
            HairColorA,
            HairColorB,
            GradientMask,
            FaceTattoo,
            HeadVanilla,
            FacialHead,
            FacialEye,
            EyeColor1,
            EyeColor2,
            FacialLid,
            FacialBrow,
            FacialMouth,
            FacialSkin,
            Body,
            SkinColorSingle,
            SkinColorA,
            SkinColorB,
            BodyTattoo,
            ApparelEntry,
            WeaponEntry,
            HeadAddons,
        }

        /// <summary>
        /// The identity of a leaf built from one of the mod's icon-strip controls. Only the four
        /// that describe a specific piece of content are tree rows; everything else the strips draw
        /// acts on the pawn or roster as a whole and lives on the toolbar
        /// (<see cref="DeclaredActions"/>).
        /// </summary>
        private enum ActionRowKind
        {
            UtilityGender,
            UtilityNude,
            UtilityHats,
            UtilityRotate,
        }

        /// <summary>
        /// One Records-section leaf's identity. Built from <c>DefDatabase&lt;RecordDef&gt;</c>, not
        /// from <c>InfoCardDataExtractor</c>'s composed strings, so the edit path has a live
        /// <see cref="RecordDef"/> to target.
        /// </summary>
        private sealed class RecordRow
        {
            public RecordDef Def;

            public RecordRow(RecordDef def)
            {
                Def = def;
            }
        }

        /// <summary>
        /// A Health-section leaf's identity. <see cref="ConditionEntry"/>'s payload is the FULL
        /// <c>List&lt;Hediff&gt;</c> for one <c>UIGroupKey</c> group, so its "x2"-style count label
        /// recomputes on every describe; Enter/Delete act on the group's first hediff, matching
        /// BlockHealth's own row-label button and Delete icon.
        /// </summary>
        private enum HealthRowKind
        {
            ConditionEntry,
            ActionAddCondition,
            ActionCopyHealth,
            ActionPasteHealth,
            ActionRandomHealth,
            ActionShowHidden,
            ActionFullHeal,
            ActionInstantFullHeal,
            ActionMedicate,
            ActionAnaesthetize,
            ActionHurt,
            ActionDamageUntilDeath,
            ActionResurrect,
        }

        /// <summary>The Needs tree section's row identity.</summary>
        private enum NeedsRowKind
        {
            NeedEntry,
            MemoryEntry,
            ActionAddThought,
            ActionFillNeeds,
            ActionClearMemories,
        }

        /// <summary>A Memories-subsection row's payload: one thought, or the mod's "AteNon" aggregate (Count &gt; 0, Example is the thought BlockNeeds.LabelHelper resolves the shared row from).</summary>
        private sealed class NeedsMemoryEntry
        {
            public Thought Example;
            public int Count;
        }

        /// <summary>The Social tree section's row identity. Relation-builder slot rows carry the slot number (1-4) as Payload; the four slots share one Describe/Activate/Adjust shape.</summary>
        private enum SocialRowKind
        {
            StateMentalState,
            StateInspiration,
            RelationChoice,
            RelationSlotPawn,
            RelationSlotGender,
            RelationAdd,
            ActionAddSocialThought,
            DirectRelationEntry,
            IndirectRelationEntry,
            OpinionEntry,
        }

        /// <summary>An Indirect-relations row's payload: one (pawn, implied PawnRelationDef) pair, matching BlockSocial.DrawIndirect's per-relation rows.</summary>
        private sealed class IndirectRelationEntry
        {
            public Pawn OtherPawn;
            public PawnRelationDef Relation;
        }

        /// <summary>
        /// The Inventory tree section's row identity. <see cref="ThingEntry"/> covers every
        /// Equipment/Apparel/Inventory row alike; its payload carries the <c>DialogType</c> mode
        /// alongside the <c>Thing</c>, since a bare <c>Thing</c> cannot tell an inventory item from
        /// an equipped weapon of the same def. <see cref="ActionUndressDrop"/> and
        /// <see cref="ActionUndressMoveToInventory"/> are mutually exclusive by
        /// <see cref="CharEditorCompat.InStartingScreen"/>, the same flag BlockInventory's
        /// <c>AUndress</c> selects on; exactly one is ever built.
        /// </summary>
        private enum InventoryRowKind
        {
            ThingEntry,
            ActionCopyEquipment,
            ActionPasteEquipment,
            ActionCopyApparel,
            ActionPasteApparel,
            ActionCopyInventory,
            ActionPasteInventory,
            ActionCopyAllGear,
            ActionPasteAllGear,
            ActionUndressDrop,
            ActionUndressMoveToInventory,
            ActionUndressDestroy,
            ActionRedress,
            ActionReequip,
            ActionReinvent,
            ActionAddEquipment,
            ActionAddApparel,
            ActionAddItem,
        }

        /// <summary>A thing row's payload: the thing itself plus which DialogObjects mode (Weapon/Apparel/Object) a row-click edit or per-row randomize should open it in.</summary>
        private sealed class InventoryThingEntry
        {
            public Thing Thing;
            public CharEditorObjectsCompat.ObjectsMode Mode;

            public InventoryThingEntry(Thing thing, CharEditorObjectsCompat.ObjectsMode mode)
            {
                Thing = thing;
                Mode = mode;
            }
        }

        /// <summary>The line budget the mod's own Log tab passes to vanilla's generator.</summary>
        private const int LogLineBudget = 1000;

        private readonly Window window;
        private readonly List<PrefixKind> prefixRows = new List<PrefixKind>();
        private readonly List<ScreenAction> actions = new List<ScreenAction>();

        /// <summary>The pawn the tree currently describes; drives the rebuild check in RefreshContent.</summary>
        private Pawn treePawn;

        /// <summary>
        /// Sections whose cached children predate a mutation. In-place refreshes skip collapsed
        /// sections (rebuilding would discard expansion state), and lazy population builds children
        /// only ONCE, so without this flag a skipped section serves pre-mutation rows forever.
        /// <see cref="OnBeforeExpandNode"/> consumes it by dropping the cached children;
        /// <see cref="BuildTree"/> clears the set.
        /// </summary>
        private readonly HashSet<SectionKind> staleSections = new HashSet<SectionKind>();

        private bool announcedOpen;

        /// <summary>Shared text-edit session for the Name fields, Skills' exact-level entry and Records' exact-value entry; only one can be live at a time.</summary>
        private readonly TextFieldEditSession editSession = new TextFieldEditSession();

        public CharacterEditorScope(Window w)
        {
            window = w;

            // The wrapper root carries no content of its own; the sections are the top level.
            Tree.SkipRoot = true;

            BuildPawnRows();

            Claim("tree.jumpToPreviousSection", e => PerformJumpToAdjacentSection(false));
            Claim("tree.jumpToNextSection", e => PerformJumpToAdjacentSection(true));
            Claim(SharedMenuGrammar.Cancel, e => PerformCancel(), when: () => !TypeaheadHasActiveSearch);

            Claim("charEditor.cyclePassion", e => PerformCyclePassion(), when: OnSkillEntryRow);
            // No confirm: the mod's own remove handlers carry none either.
            Claim("charEditor.removeTrait", e => PerformRemoveTraitOrAbility(), when: OnTraitOrAbilityEntryRow);
            // Disjoint from cyclePassion's Space claim: a row is never both a Skills and a Training row.
            Claim("charEditor.trainStep", e => PerformTrainStep(), when: OnTrainingEntryRow);

            Claim(SharedMenuGrammar.Info, e => PerformAppearanceDrillIn(), when: OnAppearanceEntryRow);

            // Toolbar chords, each running its ScreenAction's handler. The creation-strip chords go
            // through RunCreationAction so an Alt press refuses in the same words the disabled row
            // speaks; jump-to-pawn is claimed only where the mod itself draws it.
            Claim("charEditor.previousPawn", e => PerformPreviousPawn());
            Claim("charEditor.nextPawn", e => PerformNextPawn());
            Claim("charEditor.addPawn",
                e => RunCreationAction("RimWorldAccess.CharEd.Actions.AddPawn", PerformAddPawn));
            Claim("charEditor.deletePawn",
                e => RunCreationAction("RimWorldAccess.CharEd.Actions.DeletePawn", PerformDeletePawn));
            Claim("charEditor.clonePawn",
                e => RunCreationAction("RimWorldAccess.CharEd.Actions.ClonePawn", PerformClonePawn));
            Claim("charEditor.randomizePawn",
                e => RunCreationAction("RimWorldAccess.CharEd.Actions.RandomizePawn", PerformRandomizePawn));
            Claim("charEditor.findPawn",
                e => RunCreationAction("RimWorldAccess.CharEd.Actions.FindPawn", PerformFindPawn));
            Claim("charEditor.saveToSlot", e => OpenSaveToSlotPicker());
            Claim("charEditor.loadFromSlot", e => OpenLoadFromSlotPicker());
            Claim("charEditor.jumpToPawn", e => PerformJumpToPawn(),
                when: () => !CharEditorCompat.InStartingScreen);

            RegisterPopTeardown(editSession.CancelIfActive);
            RegisterPopTeardown(() =>
            {
                traitsBeforeAddDialog = null;
                abilitiesBeforeAddDialog = null;
                objectsBeforeAddDialogPawn = null;
            });
        }

        public override string Name => "character-editor";

        protected internal override Window OwnedWindow => window;

        /// <summary>Pawn names, list sources and section content are all worth searching by name.</summary>
        protected override bool EnableTypeahead => true;

        /// <summary>See the class remarks: a coexisting window's vanilla Escape opens the pause menu over it.</summary>
        public override bool OwnsCancel => true;

        /// <summary>
        /// The mod draws its tab buttons and most of its toolbar through
        /// <c>SZWidgets.ButtonTextVar</c>, a wrapper over <c>Widgets.ButtonText</c>, so blanket
        /// capture would fill the Buttons region with the tab strip this scope replaces;
        /// <see cref="DeclaredActions"/> enumerates the real actions instead.
        /// </summary>
        protected override bool CaptureWindowButtons => false;

        protected override string TreeRegionLabel =>
            "RimWorldAccess.CharEd.EditorRegion".Translate().ToString();

        // ------------------------------------------------------------------
        // Lifecycle.
        // ------------------------------------------------------------------

        public override void OnPush()
        {
            base.OnPush();
            BuildTree(preserveCursor: false);
        }

        public override void OnFocus()
        {
            base.OnFocus();

            // Announce only on the first focus. Every later focus returns from a picker or child
            // dialog that announced its own result but may have mutated the pawn out from under
            // this scope's cached rows, so each affected section refreshes silently.
            if (announcedOpen)
            {
                RefreshApparelOrWeaponRowAfterChildDialog();
                RefreshCharacterSectionAfterChildDialog();
                FocusObjectAddedByChildDialog();
                RefreshHealthSectionInPlace(silent: true);
                RefreshInventorySectionInPlace(silent: true);
                RefreshNeedsSectionInPlace(silent: true);
                RefreshSocialSectionInPlace(silent: true);
                return;
            }
            announcedOpen = true;
            string opening = "RimWorldAccess.CharEd.Opened".Translate(DescribePawnValue()).ToString();
            string tabCount = TabCountFragment();
            TolkHelper.SpeakData(string.IsNullOrEmpty(tabCount) ? opening : tabCount + ". " + opening);
            AnnounceCurrentItem();
        }

        // ------------------------------------------------------------------
        // Region 0: the pawn-selection component (the mod's own BlockPawnList zone).
        // ------------------------------------------------------------------

        /// <summary>
        /// Decides which pawn-selection rows exist for this window. The on-map filter is absent in
        /// world generation exactly as the mod's own globe button is. Computed once per window: the
        /// bindings are fixed for the session and the starting-screen flag cannot flip while the
        /// editor is open.
        /// </summary>
        private void BuildPawnRows()
        {
            prefixRows.Clear();
            if (!CharEditorCompat.Ready)
            {
                return;
            }
            prefixRows.Add(PrefixKind.EditedPawn);
            prefixRows.Add(PrefixKind.ListSource);
            if (!CharEditorCompat.InStartingScreen)
            {
                prefixRows.Add(PrefixKind.OnMapFilter);
            }
            prefixRows.Add(PrefixKind.CreationMode);
            // Mirrors BlockPawnList.DrawListCount's gate: the count is an editable stepper only for
            // the player's starting roster, so elsewhere the row is absent rather than disabled.
            if (CharEditorCompat.InStartingScreen && CharEditorCompat.IsPlayerFactionList
                && !Find.GameInitData.startingAndOptionalPawns.NullOrEmpty())
            {
                prefixRows.Add(PrefixKind.StartingCount);
            }
        }

        private ElementDescription DescribePawnRow(int index)
        {
            var d = new ElementDescription();
            if (index < 0 || index >= prefixRows.Count)
            {
                return d;
            }
            switch (prefixRows[index])
            {
                case PrefixKind.EditedPawn:
                    d.Label = "RimWorldAccess.CharEd.EditingPawn".Translate();
                    d.Role = ElementRole.ComboBox;
                    d.Value = DescribePawnValue();
                    break;
                case PrefixKind.ListSource:
                    d.Label = "RimWorldAccess.CharEd.ListSource".Translate();
                    d.Role = ElementRole.ComboBox;
                    d.Value = CharEditorCompat.ListSourceName;
                    break;
                case PrefixKind.OnMapFilter:
                    d.Label = "RimWorldAccess.CharEd.OnMapFilter".Translate();
                    d.Role = ElementRole.Checkbox;
                    d.Check = CharEditorCompat.OnMapFilter ? CheckState.Checked : CheckState.Unchecked;
                    break;
                case PrefixKind.CreationMode:
                    d.Label = "RimWorldAccess.CharEd.CreationMode".Translate();
                    d.Role = ElementRole.Checkbox;
                    d.Check = CharEditorCompat.CreationMode ? CheckState.Checked : CheckState.Unchecked;
                    break;
                case PrefixKind.StartingCount:
                {
                    int max = StartingCountMax();
                    int current = Find.GameInitData.startingPawnCount;
                    d.Label = "RimWorldAccess.CharEd.StartingCount".Translate();
                    d.Role = ElementRole.Stepper;
                    d.Value = "RimWorldAccess.CharEd.Character.ValueOfMax".Translate(current, max).ToString();
                    d.AtMinimum = current <= 1;
                    d.AtMaximum = current >= max;
                    int reserveCount = max - current;
                    if (reserveCount > 0)
                    {
                        d.Extras = "RimWorldAccess.CharEd.StartingCountReserveHint".Translate(reserveCount).ToString();
                    }
                    break;
                }
            }
            return d;
        }

        /// <summary>The mod's own stepper ceiling (BlockPawnList.DrawListCount, CEditor.cs:6815): the current starting-and-optional roster's size.</summary>
        private static int StartingCountMax()
        {
            List<Pawn> roster = Find.GameInitData.startingAndOptionalPawns;
            return roster != null ? roster.Count : 1;
        }

        /// <summary>The edited pawn as one spoken value, or the empty-state phrase when there is none.</summary>
        private string DescribePawnValue()
        {
            Pawn pawn = CharEditorCompat.CurrentPawn;
            if (pawn == null)
            {
                return "RimWorldAccess.CharEd.NoPawn".Translate().ToString();
            }
            return DescribePawn(pawn, IsReservePawn(pawn, CharEditorCompat.PawnList()));
        }

        /// <summary>One pawn as a spoken phrase: name, kind, faction, and the dead marker — the four facts the mod's own pawn rows carry.</summary>
        private static string DescribePawn(Pawn pawn)
        {
            return DescribePawn(pawn, reserve: false);
        }

        /// <summary>Appends the mod's "reserve" concept: a starting pawn beyond the count renders white instead of its faction color.</summary>
        private static string DescribePawn(Pawn pawn, bool reserve)
        {
            var parts = new List<string> { pawn.LabelShortCap };
            if (!pawn.KindLabel.NullOrEmpty())
            {
                parts.Add(pawn.KindLabel);
            }
            if (pawn.Faction != null)
            {
                parts.Add(pawn.Faction.Name);
            }
            if (pawn.Dead)
            {
                parts.Add("Dead".Translate());
            }
            if (reserve)
            {
                parts.Add("RimWorldAccess.CharEd.ReserveTag".Translate());
            }
            return string.Join(", ", parts.ToArray());
        }

        /// <summary>
        /// Mirrors BlockPawnList.DrawList's reserve render (CEditor.cs:6869-6874): a player-faction
        /// pawn whose 1-based rank in <paramref name="list"/> exceeds <c>startingPawnCount</c>.
        /// <paramref name="list"/> must be the same list and index space that render walks.
        /// </summary>
        private static bool IsReservePawn(Pawn pawn, List<Pawn> list)
        {
            if (pawn == null || !CharEditorCompat.InStartingScreen || !CharEditorCompat.IsPlayerFactionList)
            {
                return false;
            }
            int rank = list.IndexOf(pawn) + 1;
            return rank > 0 && rank > Find.GameInitData.startingPawnCount;
        }

        private bool CanAdjustPawnRow(int index)
        {
            if (index < 0 || index >= prefixRows.Count)
            {
                return false;
            }
            // Only the starting-count stepper adjusts in place; the combo and checkbox rows act on
            // Enter/Space instead.
            return prefixRows[index] == PrefixKind.StartingCount;
        }

        private void AdjustPawnRow(int index, int direction)
        {
            if (index < 0 || index >= prefixRows.Count)
            {
                return;
            }
            switch (prefixRows[index])
            {
                case PrefixKind.StartingCount:
                    StepStartingCount(direction);
                    AnnouncePawnRowState(index);
                    break;
            }
        }

        /// <summary>
        /// MUTATION-C: mirrors BlockPawnList.DrawListCount's own <c>Listing_X.AddIntSection</c>
        /// clamp exactly (CEditor.cs:6815: floor 1, ceiling <c>startingAndOptionalPawns.Count</c>)
        /// -- a bare `int` field write (`Find.GameInitData.startingPawnCount`), not a wrapped
        /// setter, is the mod's OWN vehicle here; there is no `Set*` method to invoke instead
        /// (`AddIntSection` writes the `ref int` it was handed directly).
        /// </summary>
        private static void StepStartingCount(int direction)
        {
            int max = StartingCountMax();
            int next = Find.GameInitData.startingPawnCount + (direction > 0 ? 1 : -1);
            if (next < 1) next = 1;
            if (next > max) next = max;
            Find.GameInitData.startingPawnCount = next;
        }

        private void ActivatePawnRow(int index)
        {
            if (index < 0 || index >= prefixRows.Count)
            {
                return;
            }
            switch (prefixRows[index])
            {
                case PrefixKind.EditedPawn:
                    OpenPawnPicker(index);
                    break;
                case PrefixKind.ListSource:
                    OpenListSourcePicker(index);
                    break;
                case PrefixKind.OnMapFilter:
                    // Vehicle A: the globe button's own handler, which also reloads the list.
                    CharEditorCompat.ToggleOnMapFilter(window);
                    AnnouncePawnRowState(index);
                    break;
                case PrefixKind.CreationMode:
                    // Vehicle A: the dice toggle's own handler.
                    CharEditorCompat.ToggleCreationMode(window);
                    // Creation mode gates the Character and Health sections' Randomize rows, so
                    // refresh both in place; each marks its section stale while collapsed.
                    RefreshCharacterSectionAfterChildDialog();
                    RefreshHealthSectionInPlace(silent: true);
                    AnnouncePawnRowState(index);
                    break;
                case PrefixKind.StartingCount:
                    OpenExactStartingCountEntry(index);
                    break;
            }
        }

        /// <summary>Exact entry for the starting-count stepper.</summary>
        private void OpenExactStartingCountEntry(int rowIndex)
        {
            int max = StartingCountMax();
            int current = Find.GameInitData.startingPawnCount;
            string label = "RimWorldAccess.CharEd.StartingCount".Translate();
            // MUTATION-C: see StepStartingCount's remarks -- the exact-entry path clamps to the
            // SAME floor 1/ceiling max the mod's own stepper enforces, then writes through the same
            // bare field.
            CharEdNumericEntry.OpenInt(editSession, label, current, 1, max,
                count => { Find.GameInitData.startingPawnCount = count; },
                onExit: () => AnnouncePawnRowState(rowIndex));
        }

        /// <summary>The pawn picker over the editor's current list; picking goes through the mod's own selection path, so map selection syncs with it.</summary>
        private void OpenPawnPicker(int rowIndex)
        {
            List<Pawn> pawns = CharEditorCompat.PawnList();
            var options = new List<FloatMenuOption>();
            foreach (Pawn pawn in pawns)
            {
                if (pawn == null)
                {
                    continue;
                }
                Pawn captured = pawn;
                options.Add(new FloatMenuOption(DescribePawn(captured, IsReservePawn(captured, pawns)), delegate
                {
                    CharEditorCompat.SelectPawn(captured);
                    RefreshModel();
                    AnnouncePawnRowState(rowIndex);
                }));
            }
            WindowlessFloatMenuState.OpenTitled("RimWorldAccess.CharEd.EditingPawn".Translate(), options,
                "RimWorldAccess.CharEd.NoPawnsInList");
        }

        private void OpenListSourcePicker(int rowIndex)
        {
            List<string> sources = CharEditorCompat.ListSources();
            var options = new List<FloatMenuOption>();
            foreach (string source in sources)
            {
                string captured = source;
                options.Add(new FloatMenuOption(captured, delegate
                {
                    CharEditorCompat.ChangeListSource(window, captured);
                    RefreshModel();
                    AnnouncePawnRowState(rowIndex);
                }));
            }
            WindowlessFloatMenuState.OpenTitled("RimWorldAccess.CharEd.ListSource".Translate(), options);
        }

        /// <summary>One state-change utterance for a pawn-selection row. Refreshes first so a pawn switch that rebuilt the tree is reconciled before the row is re-described.</summary>
        private void AnnouncePawnRowState(int index)
        {
            RefreshModel();
            TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(
                DescribePawnRow(index), TranslatedShellVocabulary.Instance));
        }

        // ------------------------------------------------------------------
        // Content model. Region-1 members pass the REAL region to the base: it maps the region onto
        // a panel (PanelFor), so a hardcoded 0 would land on no panel and read an empty tree.
        // ------------------------------------------------------------------

        protected override int ContentRegionCount => 2;

        /// <summary>The tree is region 1, so the base's cursor helpers must not read a region-0 row as a tree row.</summary>
        protected override int TreeRegionIndex => (int)Region.Editor;

        protected override string ContentRegionName(int region)
        {
            return (Region)region == Region.PawnSelection
                ? "RimWorldAccess.CharEd.SelectionRegion".Translate().ToString()
                : base.ContentRegionName(region);
        }

        protected override int ContentItemCount(int region)
        {
            return (Region)region == Region.PawnSelection ? prefixRows.Count : base.ContentItemCount(region);
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            return (Region)region == Region.PawnSelection
                ? (DescribePawnRow(index) ?? new ElementDescription())
                : base.DescribeContentItem(region, index);
        }

        protected override void ActivateContentItem(int region, int index)
        {
            if ((Region)region == Region.PawnSelection)
            {
                ActivatePawnRow(index);
                return;
            }
            base.ActivateContentItem(region, index);
        }

        protected override bool CanAdjustContentItem(int region, int index)
        {
            return (Region)region == Region.PawnSelection
                ? CanAdjustPawnRow(index)
                : base.CanAdjustContentItem(region, index);
        }

        // ------------------------------------------------------------------
        // Typeahead haystack.
        // ------------------------------------------------------------------

        /// <summary>
        /// Bare identity text rather than the spoken label: a collapsed section's hidden-row count
        /// must not be typeable, and a combo row keeps its identity in the label and its current
        /// value in the value, so both must join the haystack.
        /// </summary>
        protected override string ContentRowSearchText(int region, int row)
        {
            if ((Region)region == Region.PawnSelection)
            {
                if (row < 0 || row >= prefixRows.Count)
                {
                    return base.ContentRowSearchText(region, row);
                }
                ElementDescription d = DescribePawnRow(row);
                return d.Value.NullOrEmpty() ? d.Label : d.Label + " " + d.Value;
            }
            if (row >= 0 && row < Tree.Count)
            {
                InspectionTreeItem item = Tree.Visible[row];
                return item.ExpandedLabel.NullOrEmpty() ? (item.Label ?? "") : item.ExpandedLabel;
            }
            return base.ContentRowSearchText(region, row);
        }

        // ------------------------------------------------------------------
        // The tree: one section per wired surface, children built on first expand.
        // ------------------------------------------------------------------

        /// <summary>
        /// Rebuilds the whole tree for the edited pawn. Sections start collapsed and empty; content
        /// is generated on first expand. <paramref name="preserveCursor"/> false is the OnPush
        /// shape (fresh tree, cursor at row 0); true re-lands the cursor on the same logical row and
        /// re-expands surviving sections through <see cref="OnBeforeExpandNode"/>. The region's item
        /// count is STALE here (this can run outside RefreshContent) and
        /// <see cref="ListModel.MoveTo"/> throws past that count, so the move is skipped when the
        /// restored index falls outside it — <c>Tree.SelectedIndex</c> is already correct and the
        /// next sync or navigation corrects the region cursor. Always silent.
        /// </summary>
        private void BuildTree(bool preserveCursor)
        {
            ListModel regionModel = preserveCursor ? Model.Region(TreeRegionIndex) : null;
            if (preserveCursor && (regionModel == null || regionModel.IsEmpty))
            {
                preserveCursor = false;
            }
            int before = preserveCursor ? regionModel.Index : 0;

            treePawn = CharEditorCompat.CurrentPawn;
            staleSections.Clear();

            var root = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Category,
                Label = TreeRegionLabel,
                IsExpandable = true,
                IndentLevel = 0,
            };

            if (treePawn != null)
            {
                // The tree holds only the mod's TABS; its fixed BlockPerson/BlockPawnList controls
                // are toolbar entries (DeclaredActions), never sections.
                AddSection(root, SectionKind.Character, "TabCharacter".Translate());
                // BlockPerson and BlockInventory draw no zone label of their own (fixed columns,
                // never tab buttons), so these two names are minted; the rest reuse the keys the
                // mod's own tab strip resolves.
                AddSection(root, SectionKind.Appearance, "RimWorldAccess.CharEd.Appearance".Translate());
                AddSection(root, SectionKind.Inventory, "RimWorldAccess.CharEd.Inventory".Translate());
                AddSection(root, SectionKind.Health, "Health".Translate());
                AddSection(root, SectionKind.Needs, "TabNeeds".Translate());
                AddSection(root, SectionKind.Social, "TabSocial".Translate());
                AddSection(root, SectionKind.Log, "TabLog".Translate());
                AddSection(root, SectionKind.Info, CharEditorCompat.InfoTabLabel);
                AddSection(root, SectionKind.Records, "TabRecords".Translate());
            }

            if (preserveCursor)
            {
                int restored = SetTreeRootPreservingState(root, before);
                if (restored >= 0 && restored < regionModel.Count)
                {
                    regionModel.MoveTo(restored);
                }
            }
            else
            {
                SetTreeRoot(root);
            }
        }


        // ------------------------------------------------------------------
        // Tree announcements and activation.
        // ------------------------------------------------------------------

        protected override ElementDescription DescribeTreeNode(InspectionTreeItem item)
        {
            var d = new ElementDescription();
            if (item == null)
            {
                return d;
            }
            if (item.Data is RegionRow<CharRowKind> row)
            {
                DescribeCharRow(row, d);
                return d;
            }
            if (item.Data is RegionRow<AppearanceRowKind> appearanceRow)
            {
                DescribeAppearanceRow(appearanceRow, d);
                return d;
            }
            if (item.Data is RegionRow<ActionRowKind> actionRow)
            {
                DescribeActionRow(actionRow, d);
                return d;
            }
            if (item.Data is RegionRow<HealthRowKind> healthRow)
            {
                DescribeHealthRow(healthRow, d);
                return d;
            }
            if (item.Data is RegionRow<InventoryRowKind> inventoryRow)
            {
                DescribeInventoryRow(inventoryRow, d);
                return d;
            }
            if (item.Data is RegionRow<NeedsRowKind> needsRow)
            {
                DescribeNeedsRow(needsRow, d);
                return d;
            }
            if (item.Data is RegionRow<SocialRowKind> socialRow)
            {
                DescribeSocialRow(socialRow, d);
                return d;
            }
            if (item.Data is RecordRow recordRow)
            {
                DescribeRecordRow(recordRow, CharEditorCompat.CurrentPawn, d);
                return d;
            }
            d.Label = item.Label;
            if (item.IsExpandable)
            {
                d.Role = ElementRole.TreeItem;
                d.Expanded = item.IsExpanded;
                // A group marked with GroupSummaryKind speaks its content while collapsed.
                if (!item.IsExpanded && item.Data is GroupSummaryKind summaryKind)
                {
                    d.Extras = ComputeGroupSummary(item, summaryKind);
                }
            }
            else
            {
                // Every Log/Info/Records leaf is a readout; saying so keeps a navigable read-only
                // row from passing for something Enter changes.
                d.ReadOnly = true;
            }
            return d;
        }

        protected override void ActivateTreeNode(InspectionTreeItem item)
        {
            if (item == null)
            {
                return;
            }
            if (item.Data is RegionRow<CharRowKind> row)
            {
                ActivateCharRow(row, item);
                return;
            }
            if (item.Data is RegionRow<AppearanceRowKind> appearanceRow)
            {
                ActivateAppearanceRow(appearanceRow, item);
                return;
            }
            if (item.Data is RegionRow<ActionRowKind> actionRow)
            {
                ActivateActionRow(actionRow, item);
                return;
            }
            if (item.Data is RegionRow<HealthRowKind> healthRow)
            {
                ActivateHealthRow(healthRow, item);
                return;
            }
            if (item.Data is RegionRow<InventoryRowKind> inventoryRow)
            {
                ActivateInventoryRow(inventoryRow, item);
                return;
            }
            if (item.Data is RegionRow<NeedsRowKind> needsRow)
            {
                ActivateNeedsRow(needsRow, item);
                return;
            }
            if (item.Data is RegionRow<SocialRowKind> socialRow)
            {
                ActivateSocialRow(socialRow, item);
                return;
            }
            if (item.Data is RecordRow recordRow)
            {
                BeginRecordEdit(recordRow, item);
                return;
            }
            if (item.IsExpandable)
            {
                ToggleExpandable(item);
                return;
            }
            // A read-only row: Enter re-reads it.
            AnnounceCurrentItem();
        }

        /// <summary>
        /// Right/Left step the rows holding an arrow-movable value (steppers and sliders) via the
        /// per-section Adjust helpers. ComboBox rows are excluded deliberately — they open pickers
        /// on Enter/Space — and everything else falls through to the base's tree expand/collapse.
        /// </summary>
        protected override void AdjustContentItem(int region, int index, int direction)
        {
            if ((Region)region == Region.PawnSelection)
            {
                AdjustPawnRow(index, direction);
                return;
            }
            InspectionTreeItem item = TreeItemAtContentIndex(index);
            if (item?.Data is RegionRow<CharRowKind> row && AdjustCharRow(row, item, direction))
            {
                return;
            }
            if (item?.Data is RegionRow<HealthRowKind> healthRow && AdjustHealthRow(healthRow, item, direction))
            {
                return;
            }
            if (item?.Data is RegionRow<InventoryRowKind> inventoryRow && AdjustInventoryRow(inventoryRow, item, direction))
            {
                return;
            }
            if (item?.Data is RegionRow<NeedsRowKind> needsRow && AdjustNeedsRow(needsRow, item, direction))
            {
                return;
            }
            if (item?.Data is RecordRow recordRow && AdjustRecordRow(recordRow, item, direction))
            {
                return;
            }
            base.AdjustContentItem(region, index, direction);
        }

        /// <summary>Maps a region-1 index onto the flattened, visible tree row it names (region 1 is pure tree, so the index IS the tree index).</summary>
        private InspectionTreeItem TreeItemAtContentIndex(int index)
        {
            if (index < 0 || index >= Tree.Count)
            {
                return null;
            }
            return Tree.Visible[index];
        }

        /// <summary>
        /// Enter on a section: expand-and-drill in submenu mode, plain expand/collapse otherwise.
        /// Local because the base's expand/collapse dispatch is private to its Left/Right path.
        /// </summary>
        private void ToggleExpandable(InspectionTreeItem item)
        {
            if (Tree.SubmenuMode)
            {
                TreeActionResult<InspectionTreeItem> result = Tree.ExpandOrDrillDown();
                switch (result.Kind)
                {
                    case TreeActionKind.Expanded:
                    case TreeActionKind.ExpandedSubmenu:
                        SoundDefOf.FloatMenu_Open.PlayOneShotOnCamera();
                        SyncAndAnnounce();
                        break;
                    case TreeActionKind.DrilledToChild:
                        SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                        SyncAndAnnounce();
                        break;
                        // Rejected/None: silent, matching the base's own PerformExpand.
                }
                return;
            }

            if (!item.IsExpanded)
            {
                OnBeforeExpandNode(item);
            }
            item.IsExpanded = !item.IsExpanded;
            Tree.Reflatten();
            (item.IsExpanded ? SoundDefOf.FloatMenu_Open : SoundDefOf.FloatMenu_Cancel).PlayOneShotOnCamera();
            SyncAndAnnounce();
        }

        private void SyncAndAnnounce()
        {
            RefreshModel();
            SyncRegionFromCurrentTree();
            AnnounceCurrentItem();
        }

        /// <summary>
        /// Reconciles the tree with the edited pawn on pawn identity, since the pawn can change
        /// without a keystroke of ours. Silent by contract: this runs inside every RefreshModel.
        /// </summary>
        protected override void RefreshContent()
        {
            base.RefreshContent();
            Pawn current = CharEditorCompat.CurrentPawn;
            if (!ReferenceEquals(current, treePawn))
            {
                BuildTree(preserveCursor: true);
            }
        }

        // ------------------------------------------------------------------
        // The toolbar (the automatic Buttons region).
        // ------------------------------------------------------------------

        /// <summary>
        /// The mod's icon strips in drawn order: pawn-list arrows, creation toolbar, preset slots,
        /// utility icons. Gating follows the mod's own buttons with one deviation: the mod hides
        /// the creation-strip buttons while creation mode is off, whereas these stay present and
        /// disabled with the reason spoken, so no entry's position shifts under the player. Entries
        /// the mod omits by context (move up/down, jump, teleport) stay absent here too.
        /// </summary>
        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                if (!CharEditorCompat.Ready)
                {
                    return actions;
                }
                actions.Add(new ScreenAction(
                    "RimWorldAccess.Shell.Action.CharEditor.PreviousPawn".Translate(),
                    PerformPreviousPawn, "charEditor.previousPawn"));
                actions.Add(new ScreenAction(
                    "RimWorldAccess.Shell.Action.CharEditor.NextPawn".Translate(),
                    PerformNextPawn, "charEditor.nextPawn"));
                if (!CharEditorCompat.ActionsReady)
                {
                    // No action handlers resolved; the pawn-list arrows bind separately and stay.
                    return actions;
                }

                // ---- The creation strip (the mod's IsRandom-gated toolbar). ----
                bool creation = CharEditorCompat.CreationMode;
                string offHint = "RimWorldAccess.CharEd.Actions.CreationModeOffHint".Translate();
                AddCreationAction("RimWorldAccess.CharEd.Actions.AddPawn", PerformAddPawn,
                    "charEditor.addPawn", creation, offHint);
                AddCreationAction("RimWorldAccess.CharEd.Actions.DeletePawn", PerformDeletePawn,
                    "charEditor.deletePawn", creation, offHint);
                AddCreationAction("RimWorldAccess.CharEd.Actions.ClonePawn", PerformClonePawn,
                    "charEditor.clonePawn", creation, offHint);
                AddCreationAction("RimWorldAccess.CharEd.Actions.RandomizePawn", PerformRandomizePawn,
                    "charEditor.randomizePawn", creation, offHint);
                AddCreationAction("RimWorldAccess.CharEd.Actions.RandomizeKeepRace", OpenRandomizeKeepRacePicker,
                    null, creation, offHint);
                AddCreationAction("RimWorldAccess.CharEd.Actions.RandomizeBodyParts", OpenRandomizeBodyPartsPicker,
                    null, creation, offHint);
                AddCreationAction("RimWorldAccess.CharEd.Actions.RandomizeEquipment", PerformRandomizeEquipment,
                    null, creation, offHint);
                AddCreationAction("RimWorldAccess.CharEd.Actions.RandomizeBio", PerformRandomizeBio,
                    null, creation, offHint);
                AddCreationAction("RimWorldAccess.CharEd.Actions.QuickRestore", OpenQuickRestorePicker,
                    null, creation, offHint);
                AddCreationAction("RimWorldAccess.CharEd.Actions.FindPawn", PerformFindPawn,
                    "charEditor.findPawn", creation, offHint);

                // ---- The preset-slot strip (drawn unconditionally, not creation-gated). ----
                actions.Add(new ScreenAction("RimWorldAccess.CharEd.Actions.SaveToSlot".Translate(),
                    OpenSaveToSlotPicker, "charEditor.saveToSlot"));
                actions.Add(new ScreenAction("RimWorldAccess.CharEd.Actions.LoadFromSlot".Translate(),
                    OpenLoadFromSlotPicker, "charEditor.loadFromSlot"));
                actions.Add(new ScreenAction("RimWorldAccess.CharEd.Actions.ClearSlot".Translate(),
                    OpenClearSlotPicker));

                // ---- The utility icons. Mirrors BlockPerson.DrawTop's InStartingScreen gate. ----
                if (CharEditorCompat.InStartingScreen)
                {
                    actions.Add(new ScreenAction("RimWorldAccess.CharEd.Actions.MoveUp".Translate(), PerformMoveUp));
                    actions.Add(new ScreenAction("RimWorldAccess.CharEd.Actions.MoveDown".Translate(), PerformMoveDown));
                }
                else
                {
                    actions.Add(new ScreenAction("RimWorldAccess.CharEd.Actions.JumpToPawn".Translate(),
                        PerformJumpToPawn, "charEditor.jumpToPawn"));
                    // Gated on PlacingReady independently of ActionsReady, so a rename in
                    // PlacingTool cannot take down the rest of the strip.
                    if (CharEditorCompat.PlacingReady)
                    {
                        Pawn pawn = CharEditorCompat.CurrentPawn;
                        actions.Add(new ScreenAction(
                            "RimWorldAccess.CharEd.Actions.TeleportPawn".Translate(pawn != null
                                ? pawn.LabelShortCap
                                : "RimWorldAccess.CharEd.NoPawn".Translate().ToString()),
                            PerformTeleportPawn));
                        actions.Add(new ScreenAction("RimWorldAccess.CharEd.Actions.TeleportAnyPawn".Translate(),
                            PerformTeleportAnyPawn));
                    }
                }
                // One entry in both branches: the mod splits this button between the Character tab
                // and Records, but OpenCapsule rides the same ungated vehicle either way.
                if (CharEditorCompat.CharacterTabReady)
                {
                    actions.Add(new ScreenAction("RimWorldAccess.CharEd.Actions.Capsule".Translate(),
                        PerformOpenCapsule));
                }
                return actions;
            }
        }

        /// <summary>
        /// One creation-strip entry: always present, disabled with the off-mode reason while
        /// creation mode is off. <paramref name="actionId"/> is null for entries carrying no chord.
        /// </summary>
        private void AddCreationAction(string labelKey, Action perform, string actionId,
            bool creationMode, string offHint)
        {
            actions.Add(new ScreenAction(labelKey.Translate(), perform, actionId,
                disabled: !creationMode, disabledReason: creationMode ? null : offHint));
        }

        /// <summary>Names the new pawn in full: the cursor is on a button, not on the pawn row, so a bare state change would not identify it.</summary>
        private void AnnouncePawnSwitch()
        {
            RefreshModel();
            TolkHelper.SpeakData("RimWorldAccess.CharEd.SwitchedPawn".Translate(DescribePawnValue()).ToString());
        }

        // ------------------------------------------------------------------
        // Escape.
        // ------------------------------------------------------------------

        /// <summary>
        /// Closes through the window's own Close, which ejects the cryptosleep casket the editor
        /// was opened from and persists the window position.
        /// </summary>
        private void PerformCancel()
        {
            ShellFrameStamps.MarkCancelConsumed();
            window.Close(true);
        }
    }
}
