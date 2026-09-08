using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Inspection-tree adapter for Vanilla Psycasts Expanded's psycaster progression tab: level and
    /// XP, unspent points, the stat upgrade, meditation-focus unlocks, psysets, and every psycast
    /// path with its ability tree. Everything derives from the model — the pawn's hediff, its
    /// CompAbilities and the defs — never from pixels, and the data that is genuinely vanilla is
    /// typed, including every ability def, since VEF's AbilityDef extends Verse.Def.
    ///
    /// Mutations mirror the ITab's own button bodies, which are inline IMGUI with no invocable
    /// vehicle. Every closure re-checks its drawn gate at click time, since points and unlock state
    /// can have moved since the row was built, which also makes a stale re-click a safe no-op.
    ///
    /// Deliberately not covered: the dev-only controls, the cosmetic background checkbox, and the
    /// smallMode hover popups, which are irrelevant when the model is read directly. Psysets are
    /// fully managed through <see cref="VpePsysetCompat"/>; when
    /// <see cref="VpePsysetCompat.Ready"/> is false because a VPE member moved, the subcategory
    /// falls back to read-only names.
    /// </summary>
    internal sealed class VpePsycastsTabAdapter : InspectNodeAdapter
    {
        private readonly Type hediffType;
        private readonly FieldInfo experienceField;
        private readonly FieldInfo pointsField;
        private readonly FieldInfo psysetsField;
        private readonly FieldInfo unlockedPathsField;
        private readonly MethodInfo spentPointsMethod;
        private readonly MethodInfo improveStatsMethod;
        private readonly MethodInfo unlockPathMethod;
        private readonly MethodInfo unlockMeditationFocusMethod;
        private readonly MethodInfo experienceRequiredForLevelMethod;

        private readonly Type pathDefType;
        private readonly FieldInfo tabField;
        private readonly FieldInfo orderField;
        private readonly FieldInfo tooltipField;
        private readonly FieldInfo lockedReasonField;
        private readonly FieldInfo abilityLevelsInOrderField;
        private readonly FieldInfo hasAbilitiesField;
        private readonly FieldInfo abilitiesField;
        private readonly FieldInfo blankField;
        private readonly MethodInfo canPawnUnlockMethod;

        private readonly Type extType;
        private readonly FieldInfo extLevelField;
        private readonly FieldInfo extPrerequisitesField;
        private readonly MethodInfo prereqsCompletedMethod;

        private readonly Type compAbilitiesType;
        private readonly MethodInfo hasAbilityMethod;
        private readonly MethodInfo giveAbilityMethod;

        private readonly Type psycastsModType;
        private readonly FieldInfo settingsField;
        private readonly FieldInfo maxLevelField;
        private readonly FieldInfo changeFocusGainField;

        private readonly Type medUtilType;
        private readonly MethodInfo canUnlockMethod;

        private readonly bool ready;

        public override bool Ready => ready;

        public VpePsycastsTabAdapter()
        {
            var surface = new ReflectionSurface("VpePsycastsTabAdapter");

            hediffType = surface.Type("VanillaPsycastsExpanded.Hediff_PsycastAbilities");
            pathDefType = surface.Type("VanillaPsycastsExpanded.PsycasterPathDef");
            extType = surface.Type("VanillaPsycastsExpanded.AbilityExtension_Psycast");
            compAbilitiesType = surface.Type("VEF.Abilities.CompAbilities");
            psycastsModType = surface.Type("VanillaPsycastsExpanded.PsycastsMod");

            // Current VPE source puts MeditationUtilities in a .Meditation sub-namespace while the
            // shipped Workshop assembly still has it one level up, so both names are tried, newest
            // first, and neither goes through the surface.
            medUtilType = AccessTools.TypeByName("VanillaPsycastsExpanded.Meditation.MeditationUtilities")
                ?? AccessTools.TypeByName("VanillaPsycastsExpanded.MeditationUtilities");

            experienceField = surface.Field(hediffType, "experience");
            pointsField = surface.Field(hediffType, "points");
            psysetsField = surface.Field(hediffType, "psysets");
            unlockedPathsField = surface.Field(hediffType, "unlockedPaths");
            spentPointsMethod = surface.Method(hediffType, "SpentPoints", new[] { typeof(int) });
            improveStatsMethod = surface.Method(hediffType, "ImproveStats", new[] { typeof(int) });
            unlockPathMethod = pathDefType != null
                ? surface.Method(hediffType, "UnlockPath", new[] { pathDefType })
                : null;
            unlockMeditationFocusMethod = surface.Method(hediffType, "UnlockMeditationFocus", new[] { typeof(MeditationFocusDef) });
            experienceRequiredForLevelMethod = surface.Method(hediffType, "ExperienceRequiredForLevel", new[] { typeof(int) });

            tabField = surface.Field(pathDefType, "tab");
            orderField = surface.Field(pathDefType, "order");
            tooltipField = surface.Field(pathDefType, "tooltip");
            lockedReasonField = surface.Field(pathDefType, "lockedReason");
            abilityLevelsInOrderField = surface.Field(pathDefType, "abilityLevelsInOrder");
            hasAbilitiesField = surface.Field(pathDefType, "HasAbilities");
            abilitiesField = surface.Field(pathDefType, "abilities");
            blankField = surface.Field(pathDefType, "Blank");
            canPawnUnlockMethod = surface.Method(pathDefType, "CanPawnUnlock", new[] { typeof(Pawn) });

            extLevelField = surface.Field(extType, "level");
            extPrerequisitesField = surface.Field(extType, "prerequisites");
            prereqsCompletedMethod = surface.Method(extType, "PrereqsCompleted", new[] { typeof(Pawn) });

            hasAbilityMethod = surface.Method(compAbilitiesType, "HasAbility");
            giveAbilityMethod = surface.Method(compAbilitiesType, "GiveAbility");

            settingsField = surface.Field(psycastsModType, "Settings");
            Type settingsType = settingsField?.FieldType;
            maxLevelField = surface.Field(settingsType, "maxLevel");
            changeFocusGainField = surface.Field(settingsType, "changeFocusGain");

            canUnlockMethod = surface.Method(medUtilType, "CanUnlock",
                new[] { typeof(MeditationFocusDef), typeof(Pawn), typeof(string).MakeByRefType() });

            ready = surface.Ready && medUtilType != null;
        }

        // A stable English dispatch token, never displayed raw.
        public override string CategoryKey => "VPE Psycasts";

        public override TabHandlerType Handler => TabHandlerType.RichNavigation;

        // VPE's own tab label, the word a sighted player reads, so this bypasses
        // InspectionCategoryLocalizer rather than adding an entry only VPE ever renders.
        public override string DisplayName(InspectTabBase tab) => "RimWorldAccess.Compat.Vpe.Psycasts".Translate();

        public override string CategoryDisplayName(object obj) => "RimWorldAccess.Compat.Vpe.Psycasts".Translate();

        public override bool CanExpand(object obj)
        {
            return ready && obj is Pawn pawn && GetHediff(pawn) != null && GetCompAbilities(pawn) != null;
        }

        public override void BuildChildren(InspectionTreeItem categoryItem, object obj, InspectionMode mode)
        {
            if (categoryItem.Children.Count > 0)
                return;
            if (!(obj is Pawn pawn))
                return;

            try
            {
                Hediff hediff = GetHediff(pawn);
                ThingComp compAbilities = GetCompAbilities(pawn);
                if (hediff == null || compAbilities == null)
                    return;

                BuildLevelRow(categoryItem, hediff);
                InspectionTreeItem pointsItem = BuildPointsRow(categoryItem, hediff);

                BuildStatsSubcategory(categoryItem, pawn, hediff, pointsItem);
                BuildFociSubcategory(categoryItem, pawn, hediff, pointsItem);
                BuildPsysetsSubcategory(categoryItem, hediff, pawn);
                BuildPathsSection(categoryItem, pawn, hediff, compAbilities, pointsItem);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VpePsycastsTabAdapter.BuildChildren failed: {ex.Message}");
            }
        }

        private Hediff GetHediff(Pawn pawn)
        {
            if (pawn?.health?.hediffSet?.hediffs == null)
                return null;
            foreach (Hediff h in pawn.health.hediffSet.hediffs)
                if (hediffType.IsInstanceOfType(h))
                    return h;
            return null;
        }

        private ThingComp GetCompAbilities(Pawn pawn)
        {
            if (pawn?.AllComps == null)
                return null;
            foreach (ThingComp c in pawn.AllComps)
                if (compAbilitiesType.IsInstanceOfType(c))
                    return c;
            return null;
        }

        private object GetPsycastExtension(Def abilityDef)
        {
            if (abilityDef?.modExtensions == null)
                return null;
            foreach (DefModExtension ext in abilityDef.modExtensions)
                if (extType.IsInstanceOfType(ext))
                    return ext;
            return null;
        }

        private int GetMaxLevel()
        {
            object settings = settingsField.GetValue(null);
            // With no settings instance, default to "no cap known" so the XP progress bar keeps
            // showing rather than silently disappearing.
            return settings == null ? int.MaxValue : (int)maxLevelField.GetValue(settings);
        }

        private bool GetChangeFocusGain()
        {
            object settings = settingsField.GetValue(null);
            return settings != null && (bool)changeFocusGainField.GetValue(settings);
        }

        /// <summary>
        /// Level and, below max level, the XP progress text plus VPE's own "earn XP" hint, which it
        /// draws under the bar that only exists below max level.
        /// </summary>
        private void BuildLevelRow(InspectionTreeItem parent, Hediff hediff)
        {
            int level = ((Hediff_Level)hediff).level;
            string label = "RimWorldAccess.Compat.Vpe.PsyLevel".Translate(level);

            int maxLevel = GetMaxLevel();
            bool belowMaxLevel = level < maxLevel;
            if (belowMaxLevel)
            {
                float experience = (float)experienceField.GetValue(hediff);
                int xpForNext = (int)experienceRequiredForLevelMethod.Invoke(null, new object[] { level + 1 });
                label += ". " + "RimWorldAccess.Compat.Vpe.XpProgress".Translate(
                    experience.ToStringByStyle(ToStringStyle.FloatOne), xpForNext);
            }

            InspectNodeFactory.DetailLine(parent, label);
            if (belowMaxLevel)
                InspectNodeFactory.DetailLine(parent, "RimWorldAccess.Compat.Vpe.EarnXP".Translate());
        }

        /// <summary>
        /// Unspent points and VPE's own hint line. Returns the points row so later closures can
        /// refresh its label in place.
        /// </summary>
        private InspectionTreeItem BuildPointsRow(InspectionTreeItem parent, Hediff hediff)
        {
            int points = (int)pointsField.GetValue(hediff);
            InspectionTreeItem item = InspectNodeFactory.DetailLine(parent, PointsLabel(points));
            InspectNodeFactory.DetailLine(parent, "RimWorldAccess.Compat.Vpe.SpendPoints".Translate());
            return item;
        }

        /// <summary>
        /// The psycaster-stats subcategory. VPE draws the upgrade button AS the stat block's header
        /// row, so the upgrade action lives inside this section rather than beside it.
        /// </summary>
        private void BuildStatsSubcategory(InspectionTreeItem parent, Pawn pawn, Hediff hediff,
            InspectionTreeItem pointsItem)
        {
            InspectNodeFactory.Section(parent, "RimWorldAccess.Compat.Vpe.PsycasterStats".Translate(), null, statsItem =>
            {
                var statRows = new List<(StatDef stat, InspectionTreeItem item)>();

                InspectionTreeItem upgradeItem = InspectNodeFactory.ActionRow(
                    statsItem, "RimWorldAccess.Compat.Vpe.UpgradeAction".Translate(), null, null);
                upgradeItem.OnActivate = () =>
                {
                    try
                    {
                        int points = (int)pointsField.GetValue(hediff);
                        if (points >= 1)
                        {
                            // MUTATION-C: mirrors ITab_Pawn_Psycasts.FillTab's
                            // ButtonTextLabeled("VPE.PsycasterStats", "VPE.Upgrade") non-dev
                            // branch; body is inline IMGUI, no invocable vehicle.
                            spentPointsMethod.Invoke(hediff, new object[] { 1 });
                            improveStatsMethod.Invoke(hediff, new object[] { 1 });
                            pointsItem.Label = PointsLabel((int)pointsField.GetValue(hediff));
                            foreach ((StatDef stat, InspectionTreeItem statItem) in statRows)
                                statItem.Label = StatDisplayLabel(stat, pawn);
                        }
                        else
                        {
                            Messages.Message("RimWorldAccess.Compat.Vpe.NotEnoughPoints".Translate(), MessageTypeDefOf.RejectInput, false);
                        }
                    }
                    catch (Exception ex)
                    {
                        ModLogger.Error($"VpePsycastsTabAdapter upgrade action failed: {ex.Message}");
                    }
                };

                AddStatRow(statsItem, StatDefOf.PsychicEntropyMax, pawn, statRows);
                AddStatRow(statsItem, StatDefOf.PsychicEntropyRecoveryRate, pawn, statRows);
                AddStatRow(statsItem, StatDefOf.PsychicSensitivity, pawn, statRows);
                if (GetChangeFocusGain())
                    AddStatRow(statsItem, StatDefOf.MeditationFocusGain, pawn, statRows);
                StatDef psyfocusCostFactor = DefDatabase<StatDef>.GetNamedSilentFail("VPE_PsyfocusCostFactor");
                if (psyfocusCostFactor != null)
                    AddStatRow(statsItem, psyfocusCostFactor, pawn, statRows);
            });
        }

        private static void AddStatRow(InspectionTreeItem parent, StatDef stat, Pawn pawn,
            List<(StatDef stat, InspectionTreeItem item)> statRows)
        {
            InspectionTreeItem item = InspectNodeFactory.DetailLine(parent, StatDisplayLabel(stat, pawn));
            statRows.Add((stat, item));
        }

        /// <summary>The meditation-foci subcategory.</summary>
        private void BuildFociSubcategory(InspectionTreeItem parent, Pawn pawn, Hediff hediff,
            InspectionTreeItem pointsItem)
        {
            InspectNodeFactory.Section(parent, "RimWorldAccess.Compat.Vpe.FocusTypes".Translate(), null, fociItem =>
            {
                foreach (MeditationFocusDef def in DefDatabase<MeditationFocusDef>.AllDefs
                    .OrderByDescending(d => d.modContentPack?.IsOfficialMod == true)
                    .ThenByDescending(d => d.label))
                {
                    BuildFocusRow(fociItem, def, pawn, hediff, pointsItem);
                }
            });
        }

        private void BuildFocusRow(InspectionTreeItem parent, MeditationFocusDef def, Pawn pawn, Hediff hediff,
            InspectionTreeItem pointsItem)
        {
            bool unlocked = def.CanPawnUse(pawn);

            object[] canUnlockArgs = { def, pawn, null };
            bool canUnlock = (bool)canUnlockMethod.Invoke(null, canUnlockArgs);
            string reason = (string)canUnlockArgs[2];

            if (unlocked)
            {
                string label = def.LabelCap + "RimWorldAccess.Compat.Vpe.StateUnlocked".Translate();
                if (string.IsNullOrEmpty(def.description))
                {
                    InspectNodeFactory.ItemRow(parent, label, def);
                    return;
                }
                InspectNodeFactory.Section(parent, label, def, focusItem =>
                    InspectNodeFactory.DetailLines(focusItem, def.description));
                return;
            }

            if (canUnlock)
            {
                // Mirrors the ITab's tooltip: description only, since the reason appears only once
                // the focus is actually locked.
                string canUnlockLabel = def.LabelCap + "RimWorldAccess.Compat.Vpe.StateCanUnlock".Translate();
                int points = (int)pointsField.GetValue(hediff);
                if (points >= 1)
                {
                    // With no description, a section would hold nothing but the unlock action, so
                    // the row itself IS the action.
                    if (string.IsNullOrEmpty(def.description))
                    {
                        InspectionTreeItem actionItem = InspectNodeFactory.ActionRow(parent, canUnlockLabel, def, null);
                        actionItem.OnActivate = () => UnlockFocus(def, pawn, hediff, actionItem, null, pointsItem);
                        return;
                    }

                    InspectNodeFactory.Section(parent, canUnlockLabel, def, focusItem =>
                    {
                        InspectNodeFactory.DetailLines(focusItem, def.description);
                        InspectionTreeItem unlockItem = InspectNodeFactory.ActionRow(
                            focusItem, "RimWorldAccess.Compat.Vpe.UnlockAction".Translate(), def, null);
                        unlockItem.OnActivate = () => UnlockFocus(def, pawn, hediff, focusItem, unlockItem, pointsItem);
                    });
                }
                else if (string.IsNullOrEmpty(def.description))
                {
                    InspectNodeFactory.ItemRow(parent, canUnlockLabel, def);
                }
                else
                {
                    InspectNodeFactory.Section(parent, canUnlockLabel, def, focusItem =>
                        InspectNodeFactory.DetailLines(focusItem, def.description));
                }
                return;
            }

            string lockedLabel = def.LabelCap + "RimWorldAccess.Compat.Vpe.StateLocked".Translate();
            if (string.IsNullOrEmpty(def.description) && string.IsNullOrEmpty(reason))
            {
                InspectNodeFactory.DetailLine(parent, lockedLabel);
                return;
            }
            InspectNodeFactory.Section(parent, lockedLabel, def, focusItem =>
            {
                InspectNodeFactory.DetailLines(focusItem, def.description);
                if (!string.IsNullOrEmpty(reason))
                    InspectNodeFactory.DetailLine(focusItem, reason);
            });
        }

        /// <summary>
        /// The focus-unlock body shared by both row shapes. <paramref name="labelItem"/> takes the
        /// unlocked label, and <paramref name="removeItem"/>, the section's action child, is removed
        /// from its parent when present.
        /// </summary>
        private void UnlockFocus(MeditationFocusDef def, Pawn pawn, Hediff hediff,
            InspectionTreeItem labelItem, InspectionTreeItem removeItem, InspectionTreeItem pointsItem)
        {
            try
            {
                object[] recheckArgs = { def, pawn, null };
                bool stillCanUnlock = (bool)canUnlockMethod.Invoke(null, recheckArgs);
                int p = (int)pointsField.GetValue(hediff);
                if (!def.CanPawnUse(pawn) && stillCanUnlock && p >= 1)
                {
                    // MUTATION-C: mirrors ITab_Pawn_Psycasts.DoFocus's "▲"
                    // ButtonText (non-dev branch); body is inline IMGUI, no
                    // invocable vehicle.
                    spentPointsMethod.Invoke(hediff, new object[] { 1 });
                    unlockMeditationFocusMethod.Invoke(hediff, new object[] { def });
                    labelItem.Label = def.LabelCap + "RimWorldAccess.Compat.Vpe.StateUnlocked".Translate();
                    if (removeItem != null)
                        labelItem.Children.Remove(removeItem);
                    else
                        labelItem.OnActivate = null;
                    pointsItem.Label = PointsLabel((int)pointsField.GetValue(hediff));
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VpePsycastsTabAdapter focus unlock failed: {ex.Message}");
            }
        }

        /// <summary>The psyset subcategory: full management through VpePsysetCompat when Ready, else read-only names.</summary>
        private void BuildPsysetsSubcategory(InspectionTreeItem parent, Hediff hediff, Pawn pawn)
        {
            InspectNodeFactory.Section(parent, "RimWorldAccess.Compat.Vpe.PsysetCustomize".Translate(), null, psysetsItem =>
            {
                InspectNodeFactory.DetailLine(psysetsItem, "RimWorldAccess.Compat.Vpe.PsysetDesc".Translate());

                IList psysets = (IList)psysetsField.GetValue(hediff);

                // A moved psyset member means read-only names rather than dead action rows.
                if (!VpePsysetCompat.Ready)
                {
                    if (psysets == null || psysets.Count == 0)
                    {
                        InspectNodeFactory.DetailLine(psysetsItem, "RimWorldAccess.Compat.Vpe.NoPsysets".Translate());
                        return;
                    }
                    foreach (object psyset in psysets)
                        InspectNodeFactory.DetailLine(psysetsItem, ((IRenameable)psyset).RenamableLabel);
                    return;
                }

                if (psysets != null)
                    foreach (object psyset in psysets.Cast<object>().ToList())
                        BuildPsysetRow(psysetsItem, psyset, hediff, pawn);

                if (psysets == null || psysets.Count == 0)
                    InspectNodeFactory.DetailLine(psysetsItem, "RimWorldAccess.Compat.Vpe.NoPsysets".Translate());

                InspectionTreeItem createItem = InspectNodeFactory.ActionRow(
                    psysetsItem, "RimWorldAccess.Compat.Vpe.PsysetCreateAction".Translate(), null, null);
                createItem.OpensOverlayMenu = true;
                createItem.OnActivate = () =>
                {
                    try
                    {
                        object created = VpePsysetCompat.CreatePsyset(hediff, "RimWorldAccess.Compat.Vpe.UntitledPsyset".Translate());
                        if (created == null)
                            return;
                        BuildPsysetRow(psysetsItem, created, hediff, pawn, createItem);
                        VpePsysetCompat.OpenEditor(created, pawn);
                    }
                    catch (Exception ex) { ModLogger.Error($"VpePsycastsTabAdapter create psyset failed: {ex.Message}"); }
                };
            });
        }

        /// <summary>One managed psyset row: contents preview, Edit, Rename and Remove.</summary>
        private void BuildPsysetRow(InspectionTreeItem parent, object psyset, Hediff hediff, Pawn pawn,
            InspectionTreeItem insertBefore = null)
        {
            string name = VpePsysetCompat.PsysetName(psyset);
            InspectionTreeItem section = null;
            section = InspectNodeFactory.Section(parent, name, null, rowItem =>
            {
                List<Def> setMembers = VpePsysetCompat.Members(psyset);
                InspectNodeFactory.DetailLine(rowItem, setMembers.Count == 0
                    ? "RimWorldAccess.Compat.Vpe.PsysetEmptyContents".Translate()
                    : "RimWorldAccess.Compat.Vpe.PsysetContents".Translate(
                        string.Join(", ", setMembers.Select(m => m.LabelCap.ToString()))));

                InspectionTreeItem editItem = InspectNodeFactory.ActionRow(
                    rowItem, "RimWorldAccess.Compat.Vpe.PsysetEditAction".Translate(), null, null);
                editItem.OpensOverlayMenu = true;
                editItem.OnActivate = () => VpePsysetCompat.OpenEditor(psyset, pawn);

                InspectionTreeItem renameItem = InspectNodeFactory.ActionRow(
                    rowItem, "RimWorldAccess.Compat.Vpe.PsysetRenameAction".Translate(), null, null);
                renameItem.OpensOverlayMenu = true;
                renameItem.OnActivate = () => VpePsysetCompat.OpenRename(psyset);

                InspectionTreeItem removeItem = InspectNodeFactory.ActionRow(
                    rowItem, "RimWorldAccess.Compat.Vpe.PsysetRemoveAction".Translate(), null, null);
                removeItem.OnActivate = () =>
                {
                    try
                    {
                        VpePsysetCompat.RemovePsyset(hediff, psyset);
                        parent.Children.Remove(section);
                        TolkHelper.SpeakData((string)"RimWorldAccess.Compat.Vpe.PsysetRemoved".Translate(name));
                    }
                    catch (Exception ex) { ModLogger.Error($"VpePsycastsTabAdapter remove psyset failed: {ex.Message}"); }
                };
            });

            // The Create action row stays last, so a freshly-created psyset's section moves above it.
            if (insertBefore != null)
            {
                parent.Children.Remove(section);
                int idx = parent.Children.IndexOf(insertBefore);
                if (idx < 0)
                    idx = parent.Children.Count;
                parent.Children.Insert(idx, section);
            }
        }

        /// <summary>Paths, grouped by tab in first-seen DefDatabase order.</summary>
        private void BuildPathsSection(InspectionTreeItem parent, Pawn pawn, Hediff hediff,
            ThingComp compAbilities, InspectionTreeItem pointsItem)
        {
            var groups = GenDefDatabase.GetAllDefsInDatabaseForDef(pathDefType)
                .GroupBy(def => (string)tabField.GetValue(def));

            foreach (var group in groups)
            {
                List<Def> pathDefs = group.ToList();
                InspectionTreeItem tabItem = InspectNodeFactory.Section(
                    parent, group.Key, null, groupItem =>
                {
                    IList unlockedPaths = (IList)unlockedPathsField.GetValue(hediff);
                    foreach (Def pathDef in pathDefs
                        .OrderByDescending(d => unlockedPaths.Contains(d))
                        .ThenBy(d => (int)orderField.GetValue(d))
                        .ThenBy(d => d.label))
                    {
                        BuildPathRow(groupItem, pathDef, pawn, hediff, compAbilities, unlockedPaths, pointsItem);
                    }
                });
                tabItem.IsSectionHeader = true;
            }
        }

        private void BuildPathRow(InspectionTreeItem parent, Def pathDef, Pawn pawn, Hediff hediff,
            ThingComp compAbilities, IList unlockedPaths, InspectionTreeItem pointsItem)
        {
            string tooltip = (string)tooltipField.GetValue(pathDef);

            if (unlockedPaths.Contains(pathDef))
            {
                string unlockedLabel = pathDef.LabelCap + "RimWorldAccess.Compat.Vpe.StateUnlocked".Translate();
                InspectNodeFactory.Section(parent, unlockedLabel, pathDef, subItem =>
                    BuildUnlockedPathChildren(subItem, tooltip, pathDef, pawn, hediff, compAbilities, pointsItem));
                return;
            }

            // Shared by every locked variant below, mirroring the tooltip on VPE's locked overlay.
            IList pathAbilities = (IList)abilitiesField.GetValue(pathDef);
            string abilitiesJoined = string.Join(", ", pathAbilities.Cast<Def>().Select(a => a.LabelCap.ToString()));

            bool canUnlock = (bool)canPawnUnlockMethod.Invoke(pathDef, new object[] { pawn });
            int points = (int)pointsField.GetValue(hediff);

            if (canUnlock && points >= 1)
            {
                string canUnlockLabel = pathDef.LabelCap + "RimWorldAccess.Compat.Vpe.StateCanUnlock".Translate();
                InspectNodeFactory.Section(parent, canUnlockLabel, pathDef, sectionItem =>
                {
                    BuildLockedPathChildren(sectionItem, tooltip, abilitiesJoined);
                    InspectionTreeItem unlockItem = InspectNodeFactory.ActionRow(
                        sectionItem, "RimWorldAccess.Compat.Vpe.UnlockAction".Translate(), pathDef, null);
                    unlockItem.OnActivate = () =>
                    {
                        try
                        {
                            IList unlockedNow = (IList)unlockedPathsField.GetValue(hediff);
                            bool stillCanUnlock = (bool)canPawnUnlockMethod.Invoke(pathDef, new object[] { pawn });
                            int p = (int)pointsField.GetValue(hediff);
                            if (!unlockedNow.Contains(pathDef) && stillCanUnlock && p >= 1)
                            {
                                // MUTATION-C: mirrors ITab_Pawn_Psycasts.DoPaths's "VPE.Unlock"
                                // ButtonText (non-dev branch); body is inline IMGUI, no
                                // invocable vehicle.
                                spentPointsMethod.Invoke(hediff, new object[] { 1 });
                                unlockPathMethod.Invoke(hediff, new object[] { pathDef });

                                sectionItem.Label = pathDef.LabelCap + "RimWorldAccess.Compat.Vpe.StateUnlocked".Translate();
                                InspectNodeFactory.RebuildChildren(sectionItem, s =>
                                    BuildUnlockedPathChildren(s, tooltip, pathDef, pawn, hediff, compAbilities, pointsItem));
                                pointsItem.Label = PointsLabel((int)pointsField.GetValue(hediff));
                            }
                        }
                        catch (Exception ex)
                        {
                            ModLogger.Error($"VpePsycastsTabAdapter path unlock failed: {ex.Message}");
                        }
                    };
                });
            }
            else if (canUnlock)
            {
                string canUnlockLabel = pathDef.LabelCap + "RimWorldAccess.Compat.Vpe.StateCanUnlock".Translate();
                InspectNodeFactory.Section(parent, canUnlockLabel, pathDef, sectionItem =>
                    BuildLockedPathChildren(sectionItem, tooltip, abilitiesJoined));
            }
            else
            {
                string lockedReason = (string)lockedReasonField.GetValue(pathDef);
                string label = pathDef.LabelCap + ": " + "RimWorldAccess.Compat.Vpe.Locked".Translate()
                    + (string.IsNullOrEmpty(lockedReason) ? "" : ": " + lockedReason);
                InspectNodeFactory.Section(parent, label, pathDef, sectionItem =>
                    BuildLockedPathChildren(sectionItem, tooltip, abilitiesJoined));
            }
        }

        /// <summary>Locked-path children: tooltip lines, then the included-psycasts line.</summary>
        private static void BuildLockedPathChildren(InspectionTreeItem sectionItem, string tooltip, string abilitiesJoined)
        {
            InspectNodeFactory.DetailLines(sectionItem, tooltip);
            InspectNodeFactory.DetailLine(sectionItem,
                "RimWorldAccess.Compat.Vpe.AbilitiesList".Translate() + ": " + abilitiesJoined);
        }

        /// <summary>Unlocked-path children: tooltip lines, then the ability nodes.</summary>
        private void BuildUnlockedPathChildren(InspectionTreeItem sectionItem, string tooltip, Def pathDef, Pawn pawn,
            Hediff hediff, ThingComp compAbilities, InspectionTreeItem pointsItem)
        {
            InspectNodeFactory.DetailLines(sectionItem, tooltip);
            BuildAbilityNodes(sectionItem, pathDef, pawn, hediff, compAbilities, pointsItem);
        }

        /// <summary>
        /// Ability nodes for an unlocked path, walked level by level. Must NOT re-check
        /// Children.Count: it runs after sibling tooltip lines already populated the same parent, and
        /// the caller's own lazy-build guard covers re-entry.
        /// </summary>
        private void BuildAbilityNodes(InspectionTreeItem parent, Def pathDef, Pawn pawn, Hediff hediff,
            ThingComp compAbilities, InspectionTreeItem pointsItem)
        {
            if (!(abilityLevelsInOrderField.GetValue(pathDef) is Array levels))
                return;

            object blankInstance = blankField.GetValue(null);

            for (int i = 0; i < levels.Length; i++)
            {
                if (!(levels.GetValue(i) is Array levelAbilities))
                    continue;
                for (int j = 0; j < levelAbilities.Length; j++)
                {
                    object abilityObj = levelAbilities.GetValue(j);
                    if (abilityObj == null || ReferenceEquals(abilityObj, blankInstance))
                        continue;
                    if (!(abilityObj is Def abilityDef))
                        continue;
                    BuildAbilityRow(parent, abilityDef, abilityObj, pawn, hediff, compAbilities, pointsItem);
                }
            }
        }

        private void BuildAbilityRow(InspectionTreeItem parent, Def abilityDef, object abilityObj, Pawn pawn,
            Hediff hediff, ThingComp compAbilities, InspectionTreeItem pointsItem)
        {
            object ext = GetPsycastExtension(abilityDef);
            bool has = (bool)hasAbilityMethod.Invoke(compAbilities, new object[] { abilityObj });
            bool prereqsMet = ext != null && (bool)prereqsCompletedMethod.Invoke(ext, new object[] { pawn });
            int points = (int)pointsField.GetValue(hediff);
            bool canLearn = !has && prereqsMet && points >= 1;

            string suffix = has ? "RimWorldAccess.Compat.Vpe.StateLearned".Translate()
                : canLearn ? "RimWorldAccess.Compat.Vpe.StateCanLearn".Translate()
                : "RimWorldAccess.Compat.Vpe.StateLocked".Translate();
            string label = abilityDef.LabelCap + suffix;

            InspectionTreeItem item = InspectNodeFactory.Section(parent, label, abilityDef, sectionItem =>
            {
                InspectNodeFactory.DetailLines(sectionItem, abilityDef.description);

                if (ext != null)
                {
                    int level = (int)extLevelField.GetValue(ext);
                    InspectNodeFactory.DetailLine(sectionItem, "RimWorldAccess.Compat.Vpe.PathLevel".Translate(level));

                    if (extPrerequisitesField.GetValue(ext) is IList prereqs && prereqs.Count > 0)
                    {
                        var parts = new List<string>();
                        foreach (object prereqObj in prereqs)
                        {
                            if (!(prereqObj is Def prereqDef))
                                continue;
                            bool prereqHas = (bool)hasAbilityMethod.Invoke(compAbilities, new object[] { prereqObj });
                            parts.Add((prereqHas
                                ? "RimWorldAccess.Compat.Vpe.PrereqLearned"
                                : "RimWorldAccess.Compat.Vpe.PrereqNotLearned").Translate(prereqDef.LabelCap));
                        }
                        InspectNodeFactory.DetailLine(sectionItem,
                            "RimWorldAccess.Compat.Vpe.Requires".Translate(string.Join(", ", parts)));
                    }
                }

                if (canLearn)
                {
                    InspectionTreeItem learnItem = InspectNodeFactory.ActionRow(
                        sectionItem, "RimWorldAccess.Compat.Vpe.LearnAction".Translate(), abilityDef, null);
                    learnItem.OnActivate = () =>
                    {
                        try
                        {
                            bool hasNow = (bool)hasAbilityMethod.Invoke(compAbilities, new object[] { abilityObj });
                            bool prereqsMetNow = ext != null && (bool)prereqsCompletedMethod.Invoke(ext, new object[] { pawn });
                            int p = (int)pointsField.GetValue(hediff);
                            if (!hasNow && prereqsMetNow && p >= 1)
                            {
                                // MUTATION-C: mirrors ITab_Pawn_Psycasts.DoAbility's
                                // ButtonInvisible (non-dev branch); body is inline IMGUI, no
                                // invocable vehicle.
                                spentPointsMethod.Invoke(hediff, new object[] { 1 });
                                giveAbilityMethod.Invoke(compAbilities, new object[] { abilityObj });
                                sectionItem.Label = abilityDef.LabelCap + "RimWorldAccess.Compat.Vpe.StateLearned".Translate();
                                sectionItem.Children.Remove(learnItem);
                                pointsItem.Label = PointsLabel((int)pointsField.GetValue(hediff));
                            }
                        }
                        catch (Exception ex)
                        {
                            ModLogger.Error($"VpePsycastsTabAdapter ability learn failed: {ex.Message}");
                        }
                    };
                }
            });
            item.LinkedDef = abilityDef;
        }

        private static string PointsLabel(int points) => "RimWorldAccess.Compat.Vpe.PointsAvailable".Translate(points);

        // Verbatim from PsycastsUIUtility.StatDisplay's own formula.
        private static string StatDisplayLabel(StatDef stat, Pawn pawn) =>
            stat.LabelCap + ": " + stat.Worker.GetStatDrawEntryLabel(
                stat, pawn.GetStatValue(stat), stat.toStringNumberSense, StatRequest.For(pawn));
    }
}
