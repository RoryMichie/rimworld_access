using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>Builds the starting-pawn context menu: randomize, rename, edit filter, Wanderer
    /// add/remove, and the Biotech developmental-stage and xenotype pickers.</summary>
    public static class PawnContextMenuBuilder
    {
        public static List<FloatMenuOption> GetContextMenuOptions(int pawnIndex, Action rebuildCallback)
        {
            var options = new List<FloatMenuOption>();
            var pawns = Find.GameInitData.startingAndOptionalPawns;
            if (pawnIndex < 0 || pawnIndex >= pawns.Count) return options;

            var pawn = pawns[pawnIndex];

            var randomizeOption = new FloatMenuOption("Randomize".Translate(), () =>
            {
                StartingPawnState.RandomizePawnAt(pawnIndex);
            });
            randomizeOption.tooltip = new TipSignal("Alt+R");
            options.Add(randomizeOption);

            var renameOption = new FloatMenuOption("Rename".Translate(), () =>
            {
                StartingPawnState.RenamePawnAt(pawnIndex);
            });
            renameOption.tooltip = new TipSignal("Alt+N");
            options.Add(renameOption);

            var filterOption = new FloatMenuOption("RimWorldAccess.StartingPawn.EditPawnFilter".Translate().ToString(), () =>
            {
                PawnFilterState.Open();
            });
            filterOption.tooltip = new TipSignal("Alt+F");
            options.Add(filterOption);

            if (StartingPawnState.Context == PawnEditorContext.Wanderer)
            {
                if (pawns.Count < 6)
                {
                    var addOption = new FloatMenuOption("RimWorldAccess.StartingPawn.AddPawn".Translate().ToString(), () =>
                    {
                        WandererPatch.AddPawn();
                        rebuildCallback?.Invoke();
                    });
                    addOption.tooltip = new TipSignal("Alt+A");
                    options.Add(addOption);
                }
                if (pawns.Count > 1)
                {
                    var removeOption = new FloatMenuOption("RimWorldAccess.StartingPawn.RemovePawn".Translate().ToString(), () =>
                    {
                        WandererPatch.RemovePawn(pawnIndex);
                        rebuildCallback?.Invoke();
                    });
                    removeOption.tooltip = new TipSignal("Delete");
                    options.Add(removeOption);
                }
            }

            if (ModsConfig.BiotechActive)
            {
                string devStageLabel = pawn.DevelopmentalStage.ToString().Translate().CapitalizeFirst();
                var devStageOption = new FloatMenuOption(devStageLabel + "...", () =>
                {
                    // Vanilla's own gates before this picker opens (CharacterCardUtility.cs:483-527).
                    if (!TutorSystem.AllowAction("ChangeDevelopmentStage")) return;
                    if (!ScenarioUtility.AllowsChildSelection(Find.Scenario))
                    {
                        Messages.Message("MessageDevelopmentalStageSelectionDisabledByScenario".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                        return;
                    }
                    var stageOptions = BuildDevStageOptions(pawnIndex, rebuildCallback);
                    WindowlessFloatMenuState.Open(stageOptions, colonistOrders: false);
                });
                devStageOption.tooltip = new TipSignal("DevelopmentalAgeSelectionDesc".Translate());
                options.Add(devStageOption);

                string xenoLabel = pawn.genes != null ? pawn.genes.XenotypeLabelCap : (string)"Xenotype".Translate();
                var xenoOption = new FloatMenuOption("Xenotype".Translate() + ": " + xenoLabel + "...", () =>
                {
                    // Vanilla's own gate before this picker opens (CharacterCardUtility.cs:545).
                    if (!TutorSystem.AllowAction("ChangeXenotype")) return;
                    var xenoOptions = BuildXenotypeOptions(pawnIndex, rebuildCallback, out var xenoDefs);
                    // Each option speaks its own outcome; the menu's generic line would double it.
                    WindowlessFloatMenuState.Open(xenoOptions, colonistOrders: false, announceSelection: false, infoCardDefs: xenoDefs);
                });
                xenoOption.tooltip = new TipSignal("XenotypeSelectionDesc".Translate());
                options.Add(xenoOption);
            }

            return options;
        }

        private static List<FloatMenuOption> BuildDevStageOptions(int pawnIndex, Action rebuildCallback)
        {
            var options = new List<FloatMenuOption>();
            var request = StartingPawnUtility.GetGenerationRequest(pawnIndex);

            var stages = new[]
            {
                DevelopmentalStage.Adult,
                DevelopmentalStage.Child,
                DevelopmentalStage.Baby
            };

            string selectedSuffix = " (" + "StartingPawnsSelected".Translate().ToLower() + ")";

            foreach (var stage in stages)
            {
                var localStage = stage;
                bool isCurrent = request.AllowedDevelopmentalStages.Has(localStage);
                string label = localStage.ToString().Translate().CapitalizeFirst();

                if (isCurrent)
                {
                    label += selectedSuffix;
                    options.Add(new FloatMenuOption(label, () => { }));
                }
                else
                {
                    options.Add(new FloatMenuOption(label, () =>
                    {
                        var req = StartingPawnUtility.GetGenerationRequest(pawnIndex);
                        req.AllowedDevelopmentalStages = localStage;
                        StartingPawnUtility.SetGenerationRequest(pawnIndex, req);
                        StartingPawnUtility.RandomizePawn(pawnIndex);
                        TolkHelper.Speak("RimWorldAccess.StartingPawn.LabelRandomize".Loc(localStage.ToString().Translate().CapitalizeFirst(), "Randomize".Translate()));
                        rebuildCallback?.Invoke();
                    }));
                }
            }

            return options;
        }

        private static List<FloatMenuOption> BuildXenotypeOptions(int pawnIndex, Action rebuildCallback, out List<Def> infoCardDefs)
        {
            var options = new List<FloatMenuOption>();
            infoCardDefs = new List<Def>();

            // MUTATION-C: every selection option below rides
            // CharacterCardUtility.SetupGenerationRequest (private,
            // CharacterCardUtility.cs:639-668) via reflection instead of writing
            // PawnGenerationRequest.ForcedXenotype/ForcedCustomXenotype/AllowedXenotypes/
            // ForceBaselinerChance bare. That private method IS the vanilla vehicle: it
            // owns the "no-op if this is already selected" validator gate, the
            // once-per-session WarnChangingXenotypeWillRandomizePawn confirmation dialog
            // (CharacterCardUtility.cs:646-657), and the randomize:false path used only by
            // "AnyNonArchite" (updates the allowed pool without forcing an immediate
            // reroll). No public/gated wrapper exists, so the private method is called
            // directly rather than hand-copied; future changes to the warning dialog or
            // the no-op guard stay in sync automatically. The instance-level Get/SetGenerationRequest
            // calls the old bare-field code used are gone entirely -- SetupGenerationRequest
            // does both itself.
            var setupGenerationRequest = HarmonyLib.AccessTools.Method(typeof(CharacterCardUtility), "SetupGenerationRequest");

            void ApplyXenotype(XenotypeDef xenotype, CustomXenotype customXenotype, List<XenotypeDef> allowedXenotypes, float forceBaselinerChance, Func<PawnGenerationRequest, bool> validator, bool randomize)
            {
                Action randomizeCallback = () =>
                {
                    StartingPawnUtility.RandomizePawn(pawnIndex);
                    rebuildCallback?.Invoke();
                };
                setupGenerationRequest.Invoke(null, new object[] { pawnIndex, xenotype, customXenotype, allowedXenotypes, forceBaselinerChance, validator, randomizeCallback, randomize });
            }

            // MUTATION-C: mirrors CharacterCardUtility.LifestageAndXenotypeOptions's
            // "AnyNonArchite" branch (CharacterCardUtility.cs:552-556) exactly, including
            // its allowed-pool (non-Archite, non-Baseliner defs) and randomize:false --
            // vanilla only updates the generation request's allowed pool here, it does not
            // force an immediate reroll.
            options.Add(new FloatMenuOption("AnyNonArchite".Translate().CapitalizeFirst(), () =>
            {
                var allowedXenotypes = DefDatabase<XenotypeDef>.AllDefs
                    .Where(x => !x.Archite && x != XenotypeDefOf.Baseliner)
                    .ToList();
                ApplyXenotype(null, null, allowedXenotypes, 0.5f,
                    req => req.ForcedXenotype != null || req.ForcedCustomXenotype != null,
                    randomize: false);
                TolkHelper.Speak("RimWorldAccess.StartingPawn.XenotypePoolSet".Loc("AnyNonArchite".Translate().CapitalizeFirst()));
            }));
            infoCardDefs.Add(null);

            options.Add(new FloatMenuOption("XenotypeEditor".Translate() + "...", () =>
            {
                Find.WindowStack.Add(new Dialog_CreateXenotype(pawnIndex, () =>
                {
                    CharacterCardUtility.cachedCustomXenotypes = null;
                    StartingPawnUtility.RandomizePawn(pawnIndex);
                    rebuildCallback?.Invoke();
                }));
            }));
            infoCardDefs.Add(null);

            // MUTATION-C: CharacterCardUtility.LifestageAndXenotypeOptions orders by
            // `0f - xenotypeDef.displayPriority` (CharacterCardUtility.cs:566), i.e.
            // descending displayPriority -- OrderByDescending matches it exactly (the
            // ascending OrderBy this replaces put the list in reverse of vanilla's order).
            foreach (var xenotype in DefDatabase<XenotypeDef>.AllDefs.OrderByDescending(x => x.displayPriority))
            {
                var localXeno = xenotype;

                // MUTATION-C: mirrors CharacterCardUtility.LifestageAndXenotypeOptions's
                // local XenotypeValidator function (CharacterCardUtility.cs:628-636) -- no
                // invokable vehicle exists for a compiler-emitted local function, so the
                // tutorial-mode violence-lock check and the "already selected" no-op are
                // hand-copied here.
                bool Validator(PawnGenerationRequest req)
                {
                    if (TutorSystem.TutorialMode && req.MustBeCapableOfViolence
                        && localXeno.AllGenes.Any(g => g.disabledWorkTags.HasFlag(WorkTags.Violent)))
                    {
                        Messages.Message("MessageStartingPawnCapableOfViolence".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                        return false;
                    }
                    return req.ForcedXenotype != localXeno;
                }

                var xenoOption = new FloatMenuOption(localXeno.LabelCap, () =>
                {
                    ApplyXenotype(localXeno, null, null, 0f, Validator, randomize: true);
                    TolkHelper.Speak("RimWorldAccess.StartingPawn.LabelRandomize".Loc(localXeno.LabelCap, "Randomize".Translate()));
                });
                string desc = localXeno.descriptionShort ?? localXeno.description;
                if (!string.IsNullOrEmpty(desc))
                    xenoOption.tooltip = new TipSignal(desc);
                options.Add(xenoOption);
                infoCardDefs.Add(localXeno);
            }

            // Vanilla puts delete on an inline per-row button (CharacterCardUtility.cs:577-600); a
            // linear keyboard picker has none, so delete follows each custom xenotype as an option.
            foreach (var customXenotype in CharacterCardUtility.CustomXenotypesForReading)
            {
                var localCustom = customXenotype;
                string customLabel = localCustom.name.CapitalizeFirst() + " (" + "Custom".Translate() + ")";

                // MUTATION-C: mirrors CharacterCardUtility.LifestageAndXenotypeOptions's
                // local CustomXenotypeValidator function (CharacterCardUtility.cs:602-610)
                // -- same no-invokable-vehicle reasoning as the standard-xenotype Validator
                // above.
                bool CustomValidator(PawnGenerationRequest req)
                {
                    if (TutorSystem.TutorialMode && req.MustBeCapableOfViolence
                        && localCustom.genes.Any(g => g.disabledWorkTags.HasFlag(WorkTags.Violent)))
                    {
                        Messages.Message("MessageStartingPawnCapableOfViolence".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                        return false;
                    }
                    return req.ForcedCustomXenotype != localCustom;
                }

                options.Add(new FloatMenuOption(customLabel, () =>
                {
                    ApplyXenotype(null, localCustom, null, 0f, CustomValidator, randomize: true);
                    TolkHelper.Speak("RimWorldAccess.StartingPawn.LabelRandomize".Loc(customLabel, "Randomize".Translate()));
                }));
                infoCardDefs.Add(null); // Vanilla has no info-card button on custom-xenotype rows.

                // MUTATION-C: mirrors the inline delete button's click body
                // (CharacterCardUtility.cs:585-596) -- rides vanilla's own
                // Dialog_MessageBox.CreateConfirmation vehicle, then the same File.Delete +
                // cache-invalidation body; no gated "delete xenotype" method exists to call
                // instead.
                options.Add(new FloatMenuOption("RimWorldAccess.StartingPawn.DeleteCustomXenotype".Translate(localCustom.name.CapitalizeFirst()), () =>
                {
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        "ConfirmDelete".Translate(localCustom.name.CapitalizeFirst()),
                        () =>
                        {
                            string path = GenFilePaths.AbsFilePathForXenotype(localCustom.name);
                            if (File.Exists(path))
                            {
                                File.Delete(path);
                                CharacterCardUtility.cachedCustomXenotypes = null;
                            }
                            var refreshedOptions = BuildXenotypeOptions(pawnIndex, rebuildCallback, out var refreshedDefs);
                            WindowlessFloatMenuState.Open(refreshedOptions, colonistOrders: false, announceSelection: false, infoCardDefs: refreshedDefs);
                        },
                        destructive: true));
                }));
                infoCardDefs.Add(null);
            }

            return options;
        }

        // The Bio category's combo rows open these same pickers. Left/Right change nothing there:
        // a combo box is a dropdown, not a slider.

        /// <summary>Opens the dev-stage picker for the combo row, behind vanilla's gates.</summary>
        public static void OpenDevStagePicker(int pawnIndex, Action onMutated)
        {
            if (!GateDevStageChange()) return;
            var options = BuildDevStageOptions(pawnIndex, onMutated);
            WindowlessFloatMenuState.Open(options, colonistOrders: false);
        }

        private static bool GateDevStageChange()
        {
            // Vanilla's own button-click gate (CharacterCardUtility.cs:483-527).
            if (!TutorSystem.AllowAction("ChangeDevelopmentStage")) return false;
            if (!ScenarioUtility.AllowsChildSelection(Find.Scenario))
            {
                Messages.Message("MessageDevelopmentalStageSelectionDisabledByScenario".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                return false;
            }
            return true;
        }

        /// <summary>Opens the xenotype picker for the combo row, behind vanilla's gate.</summary>
        public static void OpenXenotypePicker(int pawnIndex, Action onMutated)
        {
            if (!TutorSystem.AllowAction("ChangeXenotype")) return;
            var options = BuildXenotypeOptions(pawnIndex, onMutated, out var defs);
            WindowlessFloatMenuState.Open(options, colonistOrders: false, announceSelection: false, infoCardDefs: defs);
        }

    }
}
