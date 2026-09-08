using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using RimWorldAccess.Shell;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Inspection-tree adapter for RimWorld of Magic's golem tab
    /// (TorannMagic.Golems.ITab_GolemPawn / ITab_GolemWorkstation). Both tabs share the labelKey
    /// "TabGolem", which would make the two indistinguishable in the tree, so this adapter
    /// carries two fixed display keys instead (see <see cref="ResolveDisplayName"/>).
    ///
    /// One instance serves each of pawn/workstation: the two FillTab methods draw identical
    /// content off the same CompGolem/TMPawnGolem members, differing only in how the CompGolem is
    /// reached and in the workstation tab's extra "no associated pawn yet" branch.
    ///
    /// TM_GolemUpgradeDef derives Verse.Def, so its label and description are read off a Def cast
    /// rather than reflected member-by-member.
    /// </summary>
    internal sealed class RwomGolemTabAdapter : InspectNodeAdapter
    {
        private readonly bool isWorkstation;

        private readonly Type tabType;
        private readonly Type compGolemType;
        private readonly Type tmPawnGolemType;
        private readonly Type buildingGolemBaseType;
        private readonly Type tmGolemUpgradeType;
        private readonly Type tmGolemUpgradeDefType;
        private readonly Type golemNameWindowType;
        private readonly Type golemAbilitiesWindowType;
        private readonly Type calcType; // TorannMagic.TM_Calc

        private readonly FieldInfo followsMasterField;
        private readonly FieldInfo followsMasterDraftedField;
        private readonly FieldInfo checkThreatPathField;
        private readonly FieldInfo remainDormantWhenUpgradingField;
        private readonly FieldInfo useAbilitiesWhenDormantField;
        private readonly FieldInfo threatRangeField;
        private readonly FieldInfo minEnergyPctForAbilitiesField;
        private readonly FieldInfo energyPctShouldRestField;
        private readonly FieldInfo energyPctShouldAwakenField;
        private readonly FieldInfo pawnMasterField;
        private readonly PropertyInfo golemNameProperty;   // CompGolem.GolemName : Verse.Name
        private readonly PropertyInfo pawnGolemProperty;   // CompGolem.PawnGolem : TMPawnGolem
        private readonly PropertyInfo upgradesProperty;    // CompGolem.Upgrades : List<TM_GolemUpgrade>

        private readonly FieldInfo showDormantPositionField;

        private readonly PropertyInfo golemCompProperty; // Building_TMGolemBase.GolemComp

        private readonly FieldInfo upgradeCurrentLevelField;
        private readonly FieldInfo upgradeEnabledField;
        private readonly FieldInfo upgradeDefField;

        private readonly FieldInfo upgradeDefMaxLevelField;

        private readonly FieldInfo nameWindowCgField;
        private readonly FieldInfo nameWindowGolemNameField;

        private readonly FieldInfo abilitiesWindowCgField;

        private readonly MethodInfo golemancersInFactionMethod; // static List<Pawn> GolemancersInFaction(Faction)

        private readonly bool ready;
        private InspectTabBase sharedTab;

        public override bool Ready => ready;

        private RwomGolemTabAdapter(bool isWorkstation)
        {
            this.isWorkstation = isWorkstation;

            var surface = new ReflectionSurface($"RwomGolemTabAdapter ({(isWorkstation ? "Workstation" : "Pawn")})");
            tabType = surface.Type(isWorkstation
                ? "TorannMagic.Golems.ITab_GolemWorkstation"
                : "TorannMagic.Golems.ITab_GolemPawn");
            compGolemType = surface.Type("TorannMagic.Golems.CompGolem");
            tmPawnGolemType = surface.Type("TorannMagic.Golems.TMPawnGolem");
            buildingGolemBaseType = surface.Type("TorannMagic.Golems.Building_TMGolemBase");
            tmGolemUpgradeType = surface.Type("TorannMagic.Golems.TM_GolemUpgrade");
            tmGolemUpgradeDefType = surface.Type("TorannMagic.TMDefs.TM_GolemUpgradeDef");
            golemNameWindowType = surface.Type("TorannMagic.Golems.GolemNameWindow");
            golemAbilitiesWindowType = surface.Type("TorannMagic.Golems.GolemAbilitiesWindow");
            calcType = surface.Type("TorannMagic.TM_Calc");

            followsMasterField = surface.Field(compGolemType, "followsMaster");
            followsMasterDraftedField = surface.Field(compGolemType, "followsMasterDrafted");
            checkThreatPathField = surface.Field(compGolemType, "checkThreatPath");
            remainDormantWhenUpgradingField = surface.Field(compGolemType, "remainDormantWhenUpgrading");
            useAbilitiesWhenDormantField = surface.Field(compGolemType, "useAbilitiesWhenDormant");
            threatRangeField = surface.Field(compGolemType, "threatRange");
            minEnergyPctForAbilitiesField = surface.Field(compGolemType, "minEnergyPctForAbilities");
            energyPctShouldRestField = surface.Field(compGolemType, "energyPctShouldRest");
            energyPctShouldAwakenField = surface.Field(compGolemType, "energyPctShouldAwaken");
            pawnMasterField = surface.Field(compGolemType, "pawnMaster");
            golemNameProperty = surface.Property(compGolemType, "GolemName");
            pawnGolemProperty = surface.Property(compGolemType, "PawnGolem");
            upgradesProperty = surface.Property(compGolemType, "Upgrades");

            showDormantPositionField = surface.Field(tmPawnGolemType, "showDormantPosition");

            golemCompProperty = surface.Property(buildingGolemBaseType, "GolemComp");

            upgradeCurrentLevelField = surface.Field(tmGolemUpgradeType, "currentLevel");
            upgradeEnabledField = surface.Field(tmGolemUpgradeType, "enabled");
            upgradeDefField = surface.Field(tmGolemUpgradeType, "golemUpgradeDef");

            upgradeDefMaxLevelField = surface.Field(tmGolemUpgradeDefType, "maxLevel");

            nameWindowCgField = surface.Field(golemNameWindowType, "cg");
            nameWindowGolemNameField = surface.Field(golemNameWindowType, "golemName");

            abilitiesWindowCgField = surface.Field(golemAbilitiesWindowType, "cg");

            golemancersInFactionMethod = surface.Method(calcType, "GolemancersInFaction", new[] { typeof(Faction) });

            ready = surface.Ready;
            if (ready)
            {
                sharedTab = InspectTabManager.GetSharedInstance(tabType);
            }
        }

        /// <summary>
        /// Registers an adapter instance for each tab type; either may be absent or broken
        /// independently. A missing type declines silently, a resolution failure is already
        /// logged by the constructor, so registration never breaks startup.
        /// </summary>
        public static void TryRegister()
        {
            TryRegisterOne(false);
            TryRegisterOne(true);
        }

        private static void TryRegisterOne(bool isWorkstation)
        {
            string tabTypeName = isWorkstation
                ? "TorannMagic.Golems.ITab_GolemWorkstation"
                : "TorannMagic.Golems.ITab_GolemPawn";
            string logName = $"RimWorld of Magic golem {(isWorkstation ? "workstation" : "pawn")} tab compat";
            CompatRegistration.TabAdapter(tabTypeName, t => new RwomGolemTabAdapter(isWorkstation), logName);
        }

        // Stable English dispatch tokens, l10n-exempt: never displayed raw.
        public override string CategoryKey => isWorkstation ? "RwomGolemWorkstation" : "RwomGolemPawn";

        public override TabHandlerType Handler => TabHandlerType.RichNavigation;

        public override string DisplayName(InspectTabBase tab) => ResolveDisplayName();

        public override string CategoryDisplayName(object obj) => ResolveDisplayName();

        // Both tabs set labelKey = "TabGolem", so resolving the label from the tab itself would
        // give both instances the same word when a golem and its workstation are inspected
        // together. Fixed, disambiguated keys instead.
        private string ResolveDisplayName()
        {
            return (isWorkstation
                ? "RimWorldAccess.Compat.Rwom.TabGolemWorkstation"
                : "RimWorldAccess.Compat.Rwom.TabGolemPawn").Translate();
        }

        public override void BuildChildren(InspectionTreeItem categoryItem, object obj, InspectionMode mode)
        {
            if (categoryItem.Children.Count > 0)
                return;

            object compGolem = ResolveCompGolem(obj);
            if (compGolem == null)
            {
                InspectNodeFactory.DetailLine(categoryItem, "RimWorldAccess.Compat.Rwom.GolemNoData".Translate());
                return;
            }

            Pawn pawnGolem = pawnGolemProperty.GetValue(compGolem) as Pawn;

            // Mirrors ITab_GolemWorkstation.FillTab's own GolemPawn != null gate: a dormant
            // workstation with no associated pawn shows only the Upgrades section. On the pawn
            // tab the selected object is always a TMPawnGolem, so pawnGolem is never null.
            if (pawnGolem != null)
            {
                InspectNodeFactory.GuardedBuild("RwomGolemTabAdapter", () => BuildNameRow(categoryItem, compGolem, pawnGolem, obj, mode));
                InspectNodeFactory.GuardedBuild("RwomGolemTabAdapter", () => BuildMasterRow(categoryItem, compGolem, pawnGolem, obj, mode));
                InspectNodeFactory.GuardedBuild("RwomGolemTabAdapter", () => BuildAbilitiesRow(categoryItem, compGolem, mode));
                InspectNodeFactory.GuardedBuild("RwomGolemTabAdapter", () => BuildAreaRow(categoryItem, pawnGolem, obj, mode));
                InspectNodeFactory.GuardedBuild("RwomGolemTabAdapter", () => BuildToggleRows(categoryItem, compGolem, pawnGolem, obj, mode));
                InspectNodeFactory.GuardedBuild("RwomGolemTabAdapter", () => BuildSliderRows(categoryItem, compGolem, obj, mode));
            }
            InspectNodeFactory.GuardedBuild("RwomGolemTabAdapter", () => BuildUpgradesSection(categoryItem, compGolem));
        }

        /// <summary>
        /// Clears and rebuilds the whole category after a mutation: any one row can change
        /// enough of the tab's own state that a full rebuild is simplest and correct.
        /// </summary>
        private void RebuildCategory(InspectionTreeItem categoryItem, object obj, InspectionMode mode)
        {
            InspectionTreeBuilder.RebuildAdapterCategory(categoryItem, obj, mode, this, sharedTab);
        }

        private object ResolveCompGolem(object obj)
        {
            if (!isWorkstation)
            {
                Pawn pawn = InspectionTreeBuilder.GetPawnFromThing(obj);
                if (pawn?.AllComps == null)
                    return null;
                foreach (ThingComp comp in pawn.AllComps)
                {
                    if (compGolemType.IsInstanceOfType(comp))
                        return comp;
                }
                return null;
            }

            if (!buildingGolemBaseType.IsInstanceOfType(obj))
                return null;
            return golemCompProperty.GetValue(obj);
        }

        // Row 1: name. Mirrors both FillTab methods' name button + GolemNameWindow construction.

        private void BuildNameRow(InspectionTreeItem categoryItem, object compGolem, Pawn pawnGolem, object obj, InspectionMode mode)
        {
            string currentName = ((Name)golemNameProperty.GetValue(compGolem))?.ToStringFull ?? "";
            string rowText = "RimWorldAccess.Compat.Rwom.GolemNameRow".Translate(currentName);
            if (mode != InspectionMode.ReadOnly)
            {
                InspectNodeFactory.ActionRow(categoryItem, rowText, compGolem, () => OnOpenNameWindow(compGolem, pawnGolem));
            }
            else
            {
                InspectNodeFactory.DetailLine(categoryItem, rowText);
            }
        }

        // Vehicle A: constructs the vanilla GolemNameWindow and seeds its two fields exactly as
        // the source does — golemName is the pawn's own Label, not GolemName.ToStringFull,
        // mirrored rather than "corrected" since Apply writes back through cg.GolemName either
        // way. The window's own widgets are served by the generic reader, and its Apply handler
        // applies the rename itself, so nothing here rebuilds the category.
        private void OnOpenNameWindow(object compGolem, Pawn pawnGolem)
        {
            try
            {
                object window = Activator.CreateInstance(golemNameWindowType);
                nameWindowCgField.SetValue(window, compGolem);
                nameWindowGolemNameField.SetValue(window, pawnGolem?.Label ?? "");
                Find.WindowStack.Add((Window)window);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"RwomGolemTabAdapter name window open failed: {ex.Message}");
                TolkHelper.Speak("RimWorldAccess.Compat.Rwom.ActionFailed".Loc());
            }
        }

        // Row 2: master. Mirrors both FillTab methods' master button, which delegates entirely
        // to GolemUtility.MasterButton.

        private void BuildMasterRow(InspectionTreeItem categoryItem, object compGolem, Pawn pawnGolem, object obj, InspectionMode mode)
        {
            Pawn currentMaster = pawnMasterField.GetValue(compGolem) as Pawn;
            string masterLabel = currentMaster != null
                ? currentMaster.LabelShort
                : "RimWorldAccess.Compat.Rwom.GolemMasterNone".Translate().ToString();
            string rowText = "RimWorldAccess.Compat.Rwom.GolemMasterRow".Translate(masterLabel);
            if (mode != InspectionMode.ReadOnly)
            {
                InspectNodeFactory.ActionRow(categoryItem, rowText, compGolem,
                    () => OnOpenMasterMenu(categoryItem, compGolem, pawnGolem, obj, mode));
            }
            else
            {
                InspectNodeFactory.DetailLine(categoryItem, rowText);
            }
        }

        private void OnOpenMasterMenu(InspectionTreeItem categoryItem, object compGolem, Pawn pawnGolem, object obj, InspectionMode mode)
        {
            try
            {
                Pawn currentMaster = pawnMasterField.GetValue(compGolem) as Pawn;
                RwomGolemCompat.OpenMasterMenu(pawnGolem, currentMaster, golemancersInFactionMethod,
                    newMaster => SetMaster(categoryItem, compGolem, obj, mode, newMaster));
            }
            catch (Exception ex)
            {
                ModLogger.Error($"RwomGolemTabAdapter master menu failed: {ex.Message}");
                TolkHelper.Speak("RimWorldAccess.Compat.Rwom.ActionFailed".Loc());
            }
        }

        // MUTATION-C: mirrors GolemUtility.MasterButton's own FloatMenuOption
        // delegate (GolemUtility.cs:59-69) — a bare `cg.pawnMaster = ...` write;
        // CompGolem exposes no gated setter for pawnMaster. MasterButton itself
        // draws + reads a Widgets.ButtonText click and builds this same option
        // list only from inside that draw call, unreachable outside the IMGUI
        // pass, so the option list is rebuilt here rather than invoked.
        private void SetMaster(InspectionTreeItem categoryItem, object compGolem, object obj, InspectionMode mode, Pawn newMaster)
        {
            try
            {
                pawnMasterField.SetValue(compGolem, newMaster);
                SoundDefOf.Click.PlayOneShotOnCamera(null);
                string label = newMaster != null ? newMaster.LabelShort : "RimWorldAccess.Compat.Rwom.GolemMasterNone".Translate().ToString();
                RebuildCategory(categoryItem, obj, mode);
                TolkHelper.Speak("RimWorldAccess.Compat.Rwom.GolemMasterSet".Loc(label));
            }
            catch (Exception ex)
            {
                ModLogger.Error($"RwomGolemTabAdapter master set failed: {ex.Message}");
                TolkHelper.Speak("RimWorldAccess.Compat.Rwom.ActionFailed".Loc());
            }
        }

        // Row 3: abilities and work types. Mirrors both FillTab methods' abilities button +
        // GolemAbilitiesWindow construction.

        private void BuildAbilitiesRow(InspectionTreeItem categoryItem, object compGolem, InspectionMode mode)
        {
            string rowText = "RimWorldAccess.Compat.Rwom.GolemAbilitiesRow".Translate();
            if (mode != InspectionMode.ReadOnly)
            {
                InspectNodeFactory.ActionRow(categoryItem, rowText, compGolem, () => OnOpenAbilitiesWindow(compGolem));
            }
            else
            {
                InspectNodeFactory.DetailLine(categoryItem, rowText);
            }
        }

        // Vehicle A: constructs the vanilla GolemAbilitiesWindow and seeds its one field as the
        // source does. That window applies changes live and again in its own Close() override, so
        // it must be allowed to close normally; nothing here ever TryRemoves it.
        private void OnOpenAbilitiesWindow(object compGolem)
        {
            try
            {
                object window = Activator.CreateInstance(golemAbilitiesWindowType);
                abilitiesWindowCgField.SetValue(window, compGolem);
                Find.WindowStack.Add((Window)window);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"RwomGolemTabAdapter abilities window open failed: {ex.Message}");
                TolkHelper.Speak("RimWorldAccess.Compat.Rwom.ActionFailed".Loc());
            }
        }

        // Row 4: allowed area. Mirrors both FillTab methods'
        // AreaAllowedGUI.DoAllowedAreaSelectors call, riding AreaUtility's own generator.

        private void BuildAreaRow(InspectionTreeItem categoryItem, Pawn pawnGolem, object obj, InspectionMode mode)
        {
            if (pawnGolem.playerSettings == null)
                return;

            string areaLabel = AreaUtility.AreaAllowedLabel(pawnGolem);
            string rowText = "RimWorldAccess.Compat.Rwom.GolemAreaRow".Translate(areaLabel);
            if (mode != InspectionMode.ReadOnly && pawnGolem.MapHeld != null)
            {
                InspectNodeFactory.ActionRow(categoryItem, rowText, pawnGolem,
                    () => OnOpenAreaMenu(categoryItem, pawnGolem, obj, mode));
            }
            else
            {
                InspectNodeFactory.DetailLine(categoryItem, rowText);
            }
        }

        private void OnOpenAreaMenu(InspectionTreeItem categoryItem, Pawn pawnGolem, object obj, InspectionMode mode)
        {
            Map map = pawnGolem.MapHeld;
            if (map == null)
                return;

            // Category A: vanilla's own generator, which pushes a real FloatMenu onto the stack
            // synchronously, so the menu redirected below is guaranteed to be the one it built.
            // MUTATION-C: the selArea callback is the exact bare-field-write
            // shape vanilla's own callers pass to this generator
            // (InspectPaneFiller.cs:160, Designator_AreaAllowed.cs:45) —
            // AreaRestrictionInPawnCurrentMap has no gated setter.
            int windowCountBefore = Find.WindowStack.Windows.Count;
            AreaUtility.MakeAllowedAreaListFloatMenu(
                selArea =>
                {
                    pawnGolem.playerSettings.AreaRestrictionInPawnCurrentMap = selArea;
                    RebuildCategory(categoryItem, obj, mode);
                    string areaName = selArea != null ? selArea.Label : (string)"NoAreaAllowed".Translate();
                    TolkHelper.Speak("RimWorldAccess.Compat.Rwom.GolemAreaSet".Loc(areaName));
                },
                addNullAreaOption: true,
                addManageOption: true,
                map);

            if (Find.WindowStack.Windows.Count > windowCountBefore
                && Find.WindowStack.Windows[Find.WindowStack.Windows.Count - 1] is FloatMenu spawnedMenu)
            {
                List<FloatMenuOption> options = PawnColumnMutationHelper.ExtractFloatMenuOptions(spawnedMenu);
                Find.WindowStack.TryRemove(spawnedMenu, doCloseSound: false);
                if (options != null)
                {
                    // playOpenSound false: FloatMenu's constructor already played
                    // SoundDefOf.FloatMenu_Open.
                    WindowlessFloatMenuState.Open(options, spawnedMenu.givesColonistOrders, playOpenSound: false);
                }
            }
        }

        // Rows 5: behavior toggles, mirroring both FillTab methods' six
        // Widgets.CheckboxLabeled rows.

        private void BuildToggleRows(InspectionTreeItem categoryItem, object compGolem, Pawn pawnGolem, object obj, InspectionMode mode)
        {
            AddToggleRow(categoryItem, obj, mode, "RimWorldAccess.Compat.Rwom.GolemFollowsMaster", compGolem, followsMasterField);
            AddToggleRow(categoryItem, obj, mode, "RimWorldAccess.Compat.Rwom.GolemFollowsMasterDrafted", compGolem, followsMasterDraftedField);
            AddToggleRow(categoryItem, obj, mode, "RimWorldAccess.Compat.Rwom.GolemShowDormantPosition", pawnGolem, showDormantPositionField);
            AddToggleRow(categoryItem, obj, mode, "RimWorldAccess.Compat.Rwom.GolemCheckThreatPath", compGolem, checkThreatPathField);
            AddToggleRow(categoryItem, obj, mode, "RimWorldAccess.Compat.Rwom.GolemRemainDormantWhenUpgrading", compGolem, remainDormantWhenUpgradingField);
            AddToggleRow(categoryItem, obj, mode, "RimWorldAccess.Compat.Rwom.GolemUseAbilitiesWhenDormant", compGolem, useAbilitiesWhenDormantField);
        }

        private void AddToggleRow(InspectionTreeItem categoryItem, object obj, InspectionMode mode, string labelKey, object target, FieldInfo field)
        {
            bool value = (bool)field.GetValue(target);
            string label = labelKey.Translate();
            string rowText = $"{label}: {CheckStateWord(value)}";
            if (mode != InspectionMode.ReadOnly)
            {
                InspectNodeFactory.ActionRow(categoryItem, rowText, target,
                    () => OnToggle(categoryItem, obj, mode, label, target, field));
            }
            else
            {
                InspectNodeFactory.DetailLine(categoryItem, rowText);
            }
        }

        // MUTATION-C: mirrors ITab_GolemPawn.FillTab's six Widgets.
        // CheckboxLabeled bare-ref writes (:175-197, identical in
        // ITab_GolemWorkstation.FillTab:161-184) — CompGolem/TMPawnGolem expose
        // no gated setter for any of these bools.
        private void OnToggle(InspectionTreeItem categoryItem, object obj, InspectionMode mode, string label, object target, FieldInfo field)
        {
            try
            {
                bool newValue = !(bool)field.GetValue(target);
                field.SetValue(target, newValue);
                SoundDefOf.Click.PlayOneShotOnCamera(null);
                RebuildCategory(categoryItem, obj, mode);
                TolkHelper.Speak("RimWorldAccess.Compat.Rwom.GolemToggleAnnounce".Loc(label, CheckStateWord(newValue)));
            }
            catch (Exception ex)
            {
                ModLogger.Error($"RwomGolemTabAdapter toggle failed: {ex.Message}");
                TolkHelper.Speak("RimWorldAccess.Compat.Rwom.ActionFailed".Loc());
            }
        }

        // Matches PawnColumnHandlers.CheckStateWord, which is protected on an unrelated class.
        private static string CheckStateWord(bool value)
        {
            return (value ? "RimWorldAccess.Shell.State.Checked" : "RimWorldAccess.Shell.State.Unchecked").Loc().ToString();
        }

        // Rows 6: energy/threat sliders, mirroring both FillTab methods' four
        // Widgets.HorizontalSlider rows. RWoM's widget is a raw drag-only slider with no
        // Dialog_Slider call site to ride, so each row constructs a real Dialog_Slider;
        // SliderDialogPatch makes any Dialog_Slider keyboard-accessible once it is on the stack.

        private void BuildSliderRows(InspectionTreeItem categoryItem, object compGolem, object obj, InspectionMode mode)
        {
            AddSliderRow(categoryItem, obj, mode, "RimWorldAccess.Compat.Rwom.GolemThreatRange",
                compGolem, threatRangeField, 0, 100, 1f, v => v.ToString("N0"));

            AddSliderRow(categoryItem, obj, mode, "RimWorldAccess.Compat.Rwom.GolemMinEnergyForAbilities",
                compGolem, minEnergyPctForAbilitiesField, 0, 100, 100f, pct => (pct / 100f).ToString("P0"));

            AddSliderRow(categoryItem, obj, mode, "RimWorldAccess.Compat.Rwom.GolemRestAt",
                compGolem, energyPctShouldRestField, 0, 100, 100f, pct => (pct / 100f).ToString("P0"));

            AddSliderRow(categoryItem, obj, mode, "RimWorldAccess.Compat.Rwom.GolemWakeAt",
                compGolem, energyPctShouldAwakenField, 10, 100, 100f, pct => (pct / 100f).ToString("P0"));
        }

        private void AddSliderRow(InspectionTreeItem categoryItem, object obj, InspectionMode mode, string labelKey,
            object target, FieldInfo field, int minInt, int maxInt, float scale, Func<int, string> valueText)
        {
            string label = labelKey.Translate();
            float value = (float)field.GetValue(target);
            int startInt = Mathf.RoundToInt(value * scale);
            string rowText = $"{label}: {valueText(startInt)}";
            if (mode != InspectionMode.ReadOnly)
            {
                InspectNodeFactory.ActionRow(categoryItem, rowText, target,
                    () => OpenSlider(categoryItem, obj, mode, label, target, field, minInt, maxInt, startInt, scale, valueText));
            }
            else
            {
                InspectNodeFactory.DetailLine(categoryItem, rowText);
            }
        }

        // MUTATION-C: mirrors ITab_GolemPawn.FillTab's four Widgets.
        // HorizontalSlider bare-field writes (:202-217, identical in
        // ITab_GolemWorkstation.FillTab:188-203) — CompGolem exposes no gated
        // setter for any of these floats. The Dialog_Slider itself is the
        // unmodified vanilla widget; only its confirmAction closure is a
        // hand-copied write.
        private void OpenSlider(InspectionTreeItem categoryItem, object obj, InspectionMode mode, string label,
            object target, FieldInfo field, int minInt, int maxInt, int startInt, float scale, Func<int, string> valueText)
        {
            var dialog = new Dialog_Slider(
                v => label + ": " + valueText(v),
                minInt, maxInt,
                newValue =>
                {
                    field.SetValue(target, newValue / scale);
                    RebuildCategory(categoryItem, obj, mode);
                    TolkHelper.Speak("RimWorldAccess.Compat.Rwom.GolemSliderSet".Loc(label, valueText(newValue)));
                },
                startInt);
            Find.WindowStack.Add(dialog);
        }

        // Row 7: upgrades (read-only). Mirrors both FillTab methods' upgrade list, filtered to
        // currentLevel > 0 && golemUpgradeDef.maxLevel > 0 exactly as the source filters.

        private void BuildUpgradesSection(InspectionTreeItem categoryItem, object compGolem)
        {
            InspectNodeFactory.Section(categoryItem, "RimWorldAccess.Compat.Rwom.GolemUpgradesSection".Translate(), compGolem,
                secItem => BuildUpgradeRows(secItem, compGolem));
        }

        private void BuildUpgradeRows(InspectionTreeItem secItem, object compGolem)
        {
            IEnumerable upgrades = upgradesProperty.GetValue(compGolem) as IEnumerable;
            if (upgrades == null)
                return;

            foreach (object gu in upgrades)
            {
                int currentLevel = (int)upgradeCurrentLevelField.GetValue(gu);
                if (currentLevel <= 0)
                    continue;

                object defObj = upgradeDefField.GetValue(gu);
                if (!(defObj is Def def))
                    continue;

                int maxLevel = (int)upgradeDefMaxLevelField.GetValue(defObj);
                if (maxLevel <= 0)
                    continue;

                bool enabled = (bool)upgradeEnabledField.GetValue(gu);

                string rowText = "RimWorldAccess.Compat.Rwom.GolemUpgradeRow".Translate(def.LabelCap, currentLevel, maxLevel);
                if (currentLevel >= maxLevel)
                    rowText += " " + "RimWorldAccess.Compat.Rwom.GolemUpgradeMaxed".Translate();
                if (!enabled)
                    rowText += " " + "RimWorldAccess.Compat.Rwom.GolemUpgradeDisabled".Translate();

                InspectNodeFactory.DetailLine(secItem, rowText);

                string description = SpeechFlatten.ToSentences(def.description);
                if (!string.IsNullOrEmpty(description))
                    InspectNodeFactory.DetailLine(secItem, description);
            }
        }
    }
}
