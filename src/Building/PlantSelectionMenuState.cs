using System.Collections.Generic;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages a windowless plant selection menu for growing zones.
    /// Provides keyboard navigation through available plants with detailed information.
    ///
    /// Map-controls migration: cursor/typeahead arithmetic (selectedIndex, the private
    /// TypeaheadSearchHelper, SelectNext/Previous/JumpToFirst/Last, HandleTypeahead/HandleBackspace,
    /// AnnounceCurrentSelection/AnnounceWithSearch) moved to PlantSelectionScope (ScreenScope + the
    /// shared typeahead engine, table-model T4). This state shrinks to the plant list DATA model (still
    /// read live from PlantUtility/IsPlantAvailable/GetPlantListPriority, unchanged) and the
    /// selection/info-card/warning mutation methods. The retired class doc comment's
    /// "TypeaheadDispatcher registry 2.9" CJK/IME channel was already dead code (the dispatcher itself
    /// was long retired; only a stale comment referenced it) — no separate migration needed,
    /// ScreenScope.Typeahead's shared ICharSink already covers every input layout uniformly.
    /// </summary>
    public static class PlantSelectionMenuState
    {
        private static List<PlantOption> availablePlants = null;
        private static bool isActive = false;
        private static IPlantToGrowSettable currentSettable = null;
        private static int initialIndex = 0;

        private class PlantOption
        {
            public ThingDef plantDef;
            public string displayText;

            public PlantOption(ThingDef def)
            {
                plantDef = def;

                // Build display text with skill requirements
                displayText = def.LabelCap;
                if (def.plant.sowMinSkill > 0)
                {
                    displayText += "RimWorldAccess.Building.Plant.MinSkillSuffix".Translate(def.plant.sowMinSkill);
                }
            }
        }

        /// <summary>
        /// Gets whether the plant selection menu is currently active.
        /// </summary>
        public static bool IsActive => isActive;

        /// <summary>Row count for PlantSelectionScope's one content region.</summary>
        public static int Count => availablePlants?.Count ?? 0;

        /// <summary>
        /// The index Open() pre-positioned the cursor on (the currently-set
        /// plant, or 0) — read once by PlantSelectionScope on push.
        /// </summary>
        public static int InitialIndex => initialIndex;

        public static string DisplayTextOf(int index) => availablePlants[index].displayText;

        public static ThingDef PlantDefOf(int index) => availablePlants[index].plantDef;

        /// <summary>
        /// Computed live at announce time (not cached at Open()) so a roof built/removed while this
        /// menu is open is reflected immediately — deliberate: reading live state is what the doctrine
        /// prefers, and it fixes the old stale-roof-warning quirk (QA note: audible delta from the
        /// precompute-once-at-open behavior this replaces).
        /// </summary>
        public static string DetailedInfoOf(int index)
        {
            ThingDef def = availablePlants[index].plantDef;
            List<string> details = new List<string>();

            if (!string.IsNullOrEmpty(def.description))
            {
                details.Add(def.description);
            }

            if (def.plant.sowMinSkill > 0)
            {
                details.Add("RimWorldAccess.Building.Plant.RequiresPlantsSkill".Translate(def.plant.sowMinSkill));
            }

            float growDays = def.plant.growDays;
            if (growDays > 0)
            {
                details.Add("RimWorldAccess.Building.Plant.GrowsInDays".Translate(growDays.ToString("F1")));
            }

            if (def.plant.harvestedThingDef != null)
            {
                string yieldInfo = "RimWorldAccess.Building.Plant.Yields".Translate(def.plant.harvestedThingDef.LabelCap);
                if (def.plant.harvestYield > 0)
                {
                    yieldInfo += "RimWorldAccess.Building.Plant.YieldMultiplier".Translate(def.plant.harvestYield);
                }
                details.Add(yieldInfo);
            }

            string purpose;
            switch (def.plant.purpose)
            {
                case PlantPurpose.Food:
                    purpose = "RimWorldAccess.Building.Plant.PurposeFood".Translate();
                    break;
                case PlantPurpose.Health:
                    purpose = "RimWorldAccess.Building.Plant.PurposeHealth".Translate();
                    break;
                case PlantPurpose.Beauty:
                    purpose = "RimWorldAccess.Building.Plant.PurposeBeauty".Translate();
                    break;
                case PlantPurpose.Misc:
                    purpose = "RimWorldAccess.Building.Plant.PurposeMisc".Translate();
                    break;
                default:
                    purpose = "RimWorldAccess.Building.Plant.PurposeUnknown".Translate();
                    break;
            }
            details.Add(purpose);

            if (def.plant.interferesWithRoof && currentSettable != null)
            {
                bool hasRoof = false;
                foreach (IntVec3 cell in currentSettable.Cells)
                {
                    if (cell.Roofed(currentSettable.Map))
                    {
                        hasRoof = true;
                        break;
                    }
                }
                if (hasRoof)
                {
                    details.Add("RimWorldAccess.Building.Plant.RoofWarning".Translate());
                }
            }

            if (def.plant.cavePlant)
            {
                details.Add("RimWorldAccess.Building.Plant.CavePlant".Translate());
            }

            return string.Join(". ", details);
        }

        /// <summary>
        /// Opens the plant selection menu for the given growing zone.
        /// </summary>
        public static void Open(IPlantToGrowSettable settable)
        {
            if (settable == null)
            {
                Log.Error("Cannot open plant selection menu: settable is null");
                return;
            }

            currentSettable = settable;
            availablePlants = new List<PlantOption>();
            initialIndex = 0;
            isActive = true;

            // Get list of available plants
            List<IPlantToGrowSettable> settables = new List<IPlantToGrowSettable> { settable };
            List<ThingDef> validPlants = new List<ThingDef>();

            foreach (ThingDef plantDef in PlantUtility.ValidPlantTypesForGrowers(settables))
            {
                if (IsPlantAvailable(plantDef, settable.Map))
                {
                    validPlants.Add(plantDef);
                }
            }

            // Sort plants by priority (Food > Health > Beauty > Misc), then alphabetically
            validPlants.SortBy(
                (ThingDef x) => 0f - GetPlantListPriority(x),
                (ThingDef x) => x.label
            );

            // Build plant options
            foreach (ThingDef plantDef in validPlants)
            {
                availablePlants.Add(new PlantOption(plantDef));
            }

            // Find currently selected plant
            ThingDef currentPlant = settable.GetPlantDefToGrow();
            string currentPlantName = currentPlant != null
                ? (string)currentPlant.LabelCap
                : (string)"RimWorldAccess.Building.PlantSelect.NoneSelected".Translate();
            if (currentPlant != null)
            {
                for (int i = 0; i < availablePlants.Count; i++)
                {
                    if (availablePlants[i].plantDef == currentPlant)
                    {
                        initialIndex = i;
                        break;
                    }
                }
            }

            // Announce menu opening with current crop
            TolkHelper.Speak("RimWorldAccess.Building.PlantSelect.OpenPrompt".Loc(currentPlantName));

        }

        /// <summary>
        /// Closes the plant selection menu.
        /// </summary>
        public static void Close()
        {
            availablePlants = null;
            initialIndex = 0;
            isActive = false;
            currentSettable = null;
        }

        /// <summary>
        /// Sets the plant at the given row as the zone's crop.
        ///
        /// MUTATION-C: mirrors Command_SetPlantToGrow.ProcessInput's
        /// SetPlantDefToGrow call (decompiled Verse/Command_SetPlantToGrow.cs:78-96);
        /// vehicle A is impossible because the vanilla path's only entry opens a
        /// mouse FloatMenu, which is precisely the modal this windowless menu
        /// exists to avoid. TutorSystem.AllowAction's gate is omitted
        /// deliberately (tutorial-system-only, no gameplay effect).
        /// WarnAsAppropriate is reimplemented as CheckAndWarnAboutPlant below for
        /// announcement-based warning UX (a modal Dialog_MessageBox popup is
        /// worse for keyboard-only play than an inline announcement) — a
        /// deliberate, reasoned accessibility deviation, not an oversight.
        /// </summary>
        public static void ConfirmSelection(int index)
        {
            if (availablePlants == null || index < 0 || index >= availablePlants.Count)
            {
                Close();
                return;
            }

            PlantOption selected = availablePlants[index];
            ThingDef plantDef = selected.plantDef;

            currentSettable.SetPlantDefToGrow(plantDef);

            CheckAndWarnAboutPlant(plantDef);

            TolkHelper.Speak("RimWorldAccess.Building.PlantSelect.Selected".Loc(selected.displayText));

            Close();
        }

        /// <summary>
        /// Opens an info card for the plant at the given row.
        /// </summary>
        public static void OpenInfoCard(int index)
        {
            ThingDef plantDef = null;
            if (availablePlants != null && index >= 0 && index < availablePlants.Count)
            {
                plantDef = availablePlants[index].plantDef;
            }
            InfoCardState.TryOpenInfoCardForDef(plantDef);
        }

        private static bool IsPlantAvailable(ThingDef plantDef, Map map)
        {
            // Check research prerequisites
            List<ResearchProjectDef> sowResearchPrerequisites = plantDef.plant.sowResearchPrerequisites;
            if (sowResearchPrerequisites != null)
            {
                for (int i = 0; i < sowResearchPrerequisites.Count; i++)
                {
                    if (!sowResearchPrerequisites[i].IsFinished)
                    {
                        return false;
                    }
                }
            }

            // Check if requires permanent darkness
            if (plantDef.plant.mustBePermanentDarknessToSow && !map.gameConditionManager.IsAlwaysDarkOutside)
            {
                return false;
            }

            // Check if must be wild
            if (plantDef.plant.mustBeWildToSow && !map.wildPlantSpawner.AllWildPlants.Contains(plantDef))
            {
                return false;
            }

            return true;
        }

        private static float GetPlantListPriority(ThingDef plantDef)
        {
            if (plantDef.plant.IsTree)
            {
                return 1f;
            }

            switch (plantDef.plant.purpose)
            {
                case PlantPurpose.Food:
                    return 4f;
                case PlantPurpose.Health:
                    return 3f;
                case PlantPurpose.Beauty:
                    return 2f;
                case PlantPurpose.Misc:
                    return 0f;
                default:
                    return 0f;
            }
        }

        private static void CheckAndWarnAboutPlant(ThingDef plantDef)
        {
            // Check if any colonist can plant it
            if (plantDef.plant.sowMinSkill > 0)
            {
                bool hasSkilled = false;
                foreach (Pawn colonist in currentSettable.Map.mapPawns.FreeColonistsSpawned)
                {
                    if (colonist.skills.GetSkill(SkillDefOf.Plants).Level >= plantDef.plant.sowMinSkill
                        && !colonist.Downed
                        && colonist.workSettings.WorkIsActive(WorkTypeDefOf.Growing))
                    {
                        hasSkilled = true;
                        break;
                    }
                }

                if (!hasSkilled)
                {
                    // Check for mechanoids if Biotech is active
                    bool hasMech = false;
                    if (ModsConfig.BiotechActive)
                    {
                        hasMech = MechanitorUtility.AnyPlayerMechCanDoWork(WorkTypeDefOf.Growing, plantDef.plant.sowMinSkill, out var _);
                    }

                    if (!hasMech)
                    {
                        TolkHelper.Speak("RimWorldAccess.Building.PlantSelect.NoColonistCanPlant".Loc(plantDef.label, plantDef.plant.sowMinSkill));
                    }
                }
            }

            // Check for roof/light warnings for cave plants
            if (plantDef.plant.cavePlant || plantDef.plant.diesToLight)
            {
                IntVec3 problemCell = IntVec3.Invalid;
                bool isAlwaysDark = currentSettable.Map.gameConditionManager.IsAlwaysDarkOutside;

                foreach (IntVec3 cell in currentSettable.Cells)
                {
                    bool isRoofed = !isAlwaysDark || cell.Roofed(currentSettable.Map);
                    bool isDark = currentSettable.Map.glowGrid.GroundGlowAt(cell, ignoreCavePlants: true) <= 0f;

                    if (!isRoofed || !isDark)
                    {
                        problemCell = cell;
                        break;
                    }
                }

                if (problemCell.IsValid)
                {
                    TolkHelper.Speak("RimWorldAccess.Building.PlantSelect.CavePlantExposed".Loc(plantDef.LabelCap));
                }
            }
        }
    }
}
