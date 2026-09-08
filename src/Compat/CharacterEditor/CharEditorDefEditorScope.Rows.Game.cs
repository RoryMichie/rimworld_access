using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    internal sealed partial class CharEditorDefEditorScope : TreeRegionScope
    {
        private List<DefRow> BuildGeneBiostatRows()
        {
            return new List<DefRow>
            {
                IntRow(CharEditorDefEditorCompat.Label("COMPLEXITY"), () => gene.biostatCpx, v => gene.biostatCpx = v, 0, 9),
                IntRow(CharEditorDefEditorCompat.Label("METABOLICEFFICIENCY"), () => gene.biostatMet, v => gene.biostatMet = v, -9, 9),
                IntRow(CharEditorDefEditorCompat.Label("ARCHITECAPSULES"), () => gene.biostatArc, v => gene.biostatArc = v, 0, 5),
            };
        }

        private List<DefRow> BuildGeneAppearanceRows()
        {
            var rows = new List<DefRow>
            {
                FloatRow(CharEditorDefEditorCompat.Label("RANDOMBRIGHTNESSFACTOR"), () => gene.randomBrightnessFactor, v => gene.randomBrightnessFactor = v, 0f, 2f),
                ColorRow(CharEditorDefEditorCompat.Label("HAIRCOLOROVERRIDE"), CharEditorColorPickerCompat.Mode.GeneColorHair),
                ColorRow(CharEditorDefEditorCompat.Label("SKINCOLORBASE"), CharEditorColorPickerCompat.Mode.GeneColorSkinBase),
                ColorRow(CharEditorDefEditorCompat.Label("SKINCOLOROVERRIDE"), CharEditorColorPickerCompat.Mode.GeneColorSkinOverride),
            };
            return rows;
        }

        private DefRow ColorRow(string label, CharEditorColorPickerCompat.Mode mode)
        {
            return new DefRow
            {
                Label = label.NullOrEmpty() ? mode.ToString() : label,
                Kind = RowKind.Combo,
                RenderValue = () => "RimWorldAccess.CharEd.DefEditor.OpensColorPicker".Translate().ToString(),
                Activate = () => CharEditorColorPickerCompat.Open(mode, gene),
            };
        }

        private List<DefRow> BuildGeneLifeStageRows()
        {
            LifeStageDef ls = CharEditorDefEditorCompat.GeneGetLifeStageDef(gene);
            if (ls == null) return new List<DefRow>();
            return new List<DefRow>
            {
                FloatRow(CharEditorDefEditorCompat.Label("BODYSIZEFACTOR"), () => ls.bodySizeFactor, v => ls.bodySizeFactor = v, 0.1f, 5f),
                NullableFloatRow(CharEditorDefEditorCompat.Label("BODYWIDTH"), () => ls.bodyWidth, v => ls.bodyWidth = v, 0.1f, 5f),
                NullableFloatRow(CharEditorDefEditorCompat.Label("HEADSIZEFACTOR"), () => ls.headSizeFactor, v => ls.headSizeFactor = v, 0.1f, 5f),
                FloatRow(CharEditorDefEditorCompat.Label("HEALTHSCALEFACTOR"), () => ls.healthScaleFactor, v => ls.healthScaleFactor = v, 0.1f, 5f),
                FloatRow(CharEditorDefEditorCompat.Label("HUNGERRATEFACTOR"), () => ls.hungerRateFactor, v => ls.hungerRateFactor = v, 0f, 5f),
                FloatRow(CharEditorDefEditorCompat.Label("FOODMAXFACTOR"), () => ls.foodMaxFactor, v => ls.foodMaxFactor = v, 0f, 5f),
                FloatRow(CharEditorDefEditorCompat.Label("MELEEDAMAGEFACTOR"), () => ls.meleeDamageFactor, v => ls.meleeDamageFactor = v, 0f, 5f),
                NullableFloatRow(CharEditorDefEditorCompat.Label("EYESIZEFACTOR"), () => ls.eyeSizeFactor, v => ls.eyeSizeFactor = v, 0f, 5f),
                FloatRow(CharEditorDefEditorCompat.Label("MARKETFACTOR"), () => ls.marketValueFactor, v => ls.marketValueFactor = v, 0f, 5f),
                FloatRow(CharEditorDefEditorCompat.Label("VOICEPITCH"), () => ls.voxPitch, v => ls.voxPitch = v, 0.1f, 5f),
                FloatRow(CharEditorDefEditorCompat.Label("VOICEVOLUME"), () => ls.voxVolume, v => ls.voxVolume = v, 0.1f, 5f),
                BoolRow(CharEditorDefEditorCompat.Label("REPRODUCTIVE"), () => ls.reproductive, v => ls.reproductive = v),
                BoolRow(CharEditorDefEditorCompat.Label("CARAVANRIDEABLE"), () => ls.caravanRideable, v => ls.caravanRideable = v),
            };
        }

        private List<DefRow> BuildGeneChemicalHairRows()
        {
            var chems = DefDatabase<ChemicalDef>.AllDefsListForReading.Cast<ChemicalDef>().ToList();
            var hairs = DefDatabase<HairDef>.AllDefsListForReading.Cast<HairDef>().ToList();
            return new List<DefRow>
            {
                new DefRow
                {
                    Label = "Chemical".Translate().ToString(),
                    Kind = RowKind.Combo,
                    RenderValue = () => gene.chemical != null ? gene.chemical.LabelCap.ToString() : "RimWorldAccess.CharEd.Browser.AllOption".Translate().ToString(),
                    Activate = () => OpenDefCombo(chems, d => d != null ? d.LabelCap.ToString() : "RimWorldAccess.CharEd.Browser.AllOption".Translate().ToString(),
                        d => CharEditorDefEditorCompat.GeneSetChemicalDef(gene, d), "Chemical".Translate().ToString(), allowNull: true),
                },
                new DefRow
                {
                    Label = "Hair".Translate().CapitalizeFirst(),
                    Kind = RowKind.Combo,
                    RenderValue = () => gene.forcedHair != null ? gene.forcedHair.LabelCap.ToString() : "RimWorldAccess.CharEd.Browser.AllOption".Translate().ToString(),
                    Activate = () => OpenDefCombo(hairs, d => d != null ? d.LabelCap.ToString() : "RimWorldAccess.CharEd.Browser.AllOption".Translate().ToString(),
                        d => CharEditorDefEditorCompat.GeneSetForcedHairDef(gene, d), "Hair".Translate().CapitalizeFirst(), allowNull: true),
                },
            };
        }

        private void OpenDefCombo<T>(List<T> candidates, Func<T, string> labelFn, Action<T> onSelect, string title, bool allowNull) where T : Def
        {
            var options = new List<FloatMenuOption>();
            if (allowNull)
            {
                options.Add(new FloatMenuOption(labelFn(null), delegate { onSelect(null); RefreshModel(); AnnounceCurrentItem(); }));
            }
            foreach (T c in candidates)
            {
                T captured = c;
                options.Add(new FloatMenuOption(labelFn(c), delegate { onSelect(captured); RefreshModel(); AnnounceCurrentItem(); }));
            }
            WindowlessFloatMenuState.OpenTitled(title, options);
        }

        /// <summary>The same picker shape as <see cref="OpenDefCombo{T}"/> but over a value type rather than a <c>Def</c>.</summary>
        private void OpenValueCombo<T>(IEnumerable<T> candidates, Func<T, string> labelFn, Action<T> onSelect, string title)
        {
            var options = new List<FloatMenuOption>();
            if (candidates != null)
            {
                foreach (T c in candidates)
                {
                    T captured = c;
                    options.Add(new FloatMenuOption(labelFn(c), delegate { onSelect(captured); RefreshModel(); AnnounceCurrentItem(); }));
                }
            }
            WindowlessFloatMenuState.OpenTitled(title, options);
        }

        private List<DefRow> BuildGenePassionRows()
        {
            var skills = DefDatabase<SkillDef>.AllDefsListForReading.Cast<SkillDef>().ToList();
            return new List<DefRow>
            {
                new DefRow
                {
                    Label = CharEditorDefEditorCompat.Label("PASSIONMODADD"),
                    Kind = RowKind.Combo,
                    RenderValue = () => (gene.passionMod != null && gene.passionMod.modType == PassionMod.PassionModType.AddOneLevel && gene.passionMod.skill != null)
                        ? gene.passionMod.skill.LabelCap.ToString() : "RimWorldAccess.CharEd.Browser.AllOption".Translate().ToString(),
                    Activate = () => OpenDefCombo(skills, d => d != null ? d.LabelCap.ToString() : "RimWorldAccess.CharEd.Browser.AllOption".Translate().ToString(),
                        d => CharEditorDefEditorCompat.GeneSetPassionMod(gene, d, PassionMod.PassionModType.AddOneLevel), CharEditorDefEditorCompat.Label("PASSIONMODADD"), allowNull: false),
                },
                new DefRow
                {
                    Label = CharEditorDefEditorCompat.Label("PASSIONMODSUB"),
                    Kind = RowKind.Combo,
                    RenderValue = () => (gene.passionMod != null && gene.passionMod.modType == PassionMod.PassionModType.DropAll && gene.passionMod.skill != null)
                        ? gene.passionMod.skill.LabelCap.ToString() : "RimWorldAccess.CharEd.Browser.AllOption".Translate().ToString(),
                    Activate = () => OpenDefCombo(skills, d => d != null ? d.LabelCap.ToString() : "RimWorldAccess.CharEd.Browser.AllOption".Translate().ToString(),
                        d => CharEditorDefEditorCompat.GeneSetPassionMod(gene, d, PassionMod.PassionModType.DropAll), CharEditorDefEditorCompat.Label("PASSIONMODSUB"), allowNull: false),
                },
            };
        }

        // Gene list-relational sections.

        private List<DefRow> BuildGeneStatFactorRows() => BuildStatModifierRows(gene.statFactors,
            CharEditorDefEditorCompat.GeneFreeStatFactors, CharEditorDefEditorCompat.GeneSetStatFactor, CharEditorDefEditorCompat.GeneRemoveStatFactor, CharEditorDefEditorCompat.Label("STAT_FACTORS"));

        private List<DefRow> BuildGeneStatOffsetRows() => BuildStatModifierRows(gene.statOffsets,
            CharEditorDefEditorCompat.GeneFreeStatOffsets, CharEditorDefEditorCompat.GeneSetStatOffset, CharEditorDefEditorCompat.GeneRemoveStatOffset, CharEditorDefEditorCompat.Label("STAT_OFFSETS"));

        private List<DefRow> BuildStatModifierRows(List<StatModifier> current, Func<HashSet<StatDef>> free,
            Action<GeneDef, StatDef, float> setter, Action<GeneDef, StatDef> remover, string addLabel)
        {
            var rows = new List<DefRow>();
            if (current != null)
            {
                foreach (StatModifier sm in current.ToList())
                {
                    StatModifier captured = sm;
                    float min = captured.stat != null ? captured.stat.minValue : -999f;
                    float max = captured.stat != null ? captured.stat.maxValue : 999f;
                    rows.Add(new DefRow
                    {
                        Label = captured.stat != null ? captured.stat.LabelCap.ToString() : "?",
                        Kind = RowKind.ValueElement,
                        RenderValue = () => F2(captured.value),
                        Adjust = dir => { float v = Mathf.Clamp(captured.value + dir * StepFor(min, max), min, max); captured.value = v; setter(gene, captured.stat, v); },
                        Activate = () => CharEdNumericEntry.OpenFloat(editSession, captured.stat.LabelCap.ToString(),
                            F2(captured.value), FloatSpec, min, max,
                            v => { captured.value = v; setter(gene, captured.stat, v); }, RefreshAndAnnounce),
                        RemoveSelf = () => remover(gene, captured.stat),
                    });
                }
            }
            rows.Add(new DefRow { Label = addLabel + "...", Kind = RowKind.AddAction, Activate = () => OpenAddMenu(free(), d => d.LabelCap.ToString(), d => setter(gene, d, 0f), addLabel) });
            return rows;
        }

        private List<DefRow> BuildGeneAptitudeRows()
        {
            var rows = new List<DefRow>();
            if (gene.aptitudes != null)
            {
                foreach (Aptitude a in gene.aptitudes.ToList())
                {
                    Aptitude captured = a;
                    rows.Add(new DefRow
                    {
                        Label = captured.skill != null ? captured.skill.LabelCap.ToString() : "?",
                        Kind = RowKind.ValueElement,
                        RenderValue = () => captured.level.ToString(),
                        Adjust = dir => { int v = Mathf.Clamp(captured.level + dir, -999, 999); captured.level = v; CharEditorDefEditorCompat.GeneSetAptitude(gene, captured.skill, v); },
                        Activate = () => BeginExactEntry(captured.skill.LabelCap.ToString(), captured.level.ToString(), IntSpec,
                            s => { if (int.TryParse(s, out int v) && captured.skill != null) { captured.level = v; CharEditorDefEditorCompat.GeneSetAptitude(gene, captured.skill, v); } }),
                        RemoveSelf = () => CharEditorDefEditorCompat.GeneRemoveAptitude(gene, captured.skill),
                    });
                }
            }
            string label = CharEditorDefEditorCompat.Label("APTITUDE");
            rows.Add(new DefRow { Label = label + "...", Kind = RowKind.AddAction, Activate = () => OpenAddMenu(CharEditorDefEditorCompat.GeneFreeAptitudes(), d => d.LabelCap.ToString(), d => CharEditorDefEditorCompat.GeneSetAptitude(gene, d, 0), label) });
            return rows;
        }

        private List<DefRow> BuildGeneCapacityRows()
        {
            var rows = new List<DefRow>();
            if (gene.capMods != null)
            {
                foreach (PawnCapacityModifier c in gene.capMods.ToList())
                {
                    PawnCapacityModifier captured = c;
                    rows.Add(new DefRow
                    {
                        Label = captured.capacity != null ? captured.capacity.LabelCap.ToString() : "?",
                        Kind = RowKind.ValueElement,
                        RenderValue = () => "RimWorldAccess.CharEd.DefEditor.OffsetFactor".Translate(F2(captured.offset), F2(captured.postFactor)).ToString(),
                        Adjust = dir => { float v = captured.offset + dir * 0.05f; captured.offset = v; CharEditorDefEditorCompat.GeneSetCapacity(gene, captured.capacity, v, captured.postFactor); },
                        Activate = () => BeginExactEntry(captured.capacity.LabelCap.ToString(), F2(captured.offset), FloatSpec,
                            s => { if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) { captured.offset = v; CharEditorDefEditorCompat.GeneSetCapacity(gene, captured.capacity, v, captured.postFactor); } }),
                        RemoveSelf = () => CharEditorDefEditorCompat.GeneRemoveCapacity(gene, captured.capacity),
                    });
                }
            }
            string label = CharEditorDefEditorCompat.Label("CAPACITIES");
            rows.Add(new DefRow { Label = label + "...", Kind = RowKind.AddAction, Activate = () => OpenAddMenu(CharEditorDefEditorCompat.GeneFreeCapacities(), d => d.LabelCap.ToString(), d => CharEditorDefEditorCompat.GeneSetCapacity(gene, d, 0f, 0f), label) });
            return rows;
        }

        private List<DefRow> BuildGeneAbilityRows() => BuildMembershipRows(gene.abilities, CharEditorDefEditorCompat.GeneFreeAbilities,
            d => d.LabelCap.ToString(), d => CharEditorDefEditorCompat.GeneSetAbility(gene, d), d => CharEditorDefEditorCompat.GeneRemoveAbility(gene, d), CharEditorDefEditorCompat.Label("ABILITIES") + "...");

        private static string GeneticTraitLabel(GeneticTraitData gtd)
        {
            if (gtd.def == null) return "?";
            TraitDegreeData data = gtd.def.DataAtDegree(gtd.degree);
            return data != null && !data.label.NullOrEmpty() ? data.label.CapitalizeFirst() : gtd.def.LabelCap.ToString();
        }

        private List<DefRow> BuildGeneForcedTraitRows() => BuildMembershipRows(gene.forcedTraits, CharEditorDefEditorCompat.GeneFreeForcedTraits,
            GeneticTraitLabel, gtd => CharEditorDefEditorCompat.GeneSetForcedTrait(gene, gtd), gtd => CharEditorDefEditorCompat.GeneRemoveForcedTrait(gene, gtd), CharEditorDefEditorCompat.Label("FORCEDTRAITS") + "...");

        private List<DefRow> BuildGeneSuppressedTraitRows() => BuildMembershipRows(gene.suppressedTraits, CharEditorDefEditorCompat.GeneFreeSuppressedTraits,
            GeneticTraitLabel, gtd => CharEditorDefEditorCompat.GeneSetSuppressedTrait(gene, gtd), gtd => CharEditorDefEditorCompat.GeneRemoveSuppressedTrait(gene, gtd), CharEditorDefEditorCompat.Label("SUPPRESSEDTRAITS") + "...");

        private List<DefRow> BuildGeneImmunityRows() => BuildMembershipRows(gene.makeImmuneTo, CharEditorDefEditorCompat.GeneFreeImmunities,
            d => d.LabelCap.ToString(), d => CharEditorDefEditorCompat.GeneSetImmunity(gene, d), d => CharEditorDefEditorCompat.GeneRemoveImmunity(gene, d), CharEditorDefEditorCompat.Label("IMMUNETO") + "...");

        private List<DefRow> BuildGeneProtectionRows() => BuildMembershipRows(gene.hediffGiversCannotGive, CharEditorDefEditorCompat.GeneFreeProtections,
            d => d.LabelCap.ToString(), d => CharEditorDefEditorCompat.GeneSetProtection(gene, d), d => CharEditorDefEditorCompat.GeneRemoveProtection(gene, d), CharEditorDefEditorCompat.Label("FULLYPROTECTEDFROM") + "...");

        private List<DefRow> BuildGeneDamageFactorRows()
        {
            var rows = new List<DefRow>();
            if (gene.damageFactors != null)
            {
                foreach (DamageFactor df in gene.damageFactors.ToList())
                {
                    DamageFactor captured = df;
                    rows.Add(new DefRow
                    {
                        Label = captured.damageDef != null ? captured.damageDef.LabelCap.ToString() : "?",
                        Kind = RowKind.ValueElement,
                        RenderValue = () => F2(captured.factor),
                        Adjust = dir => { float v = Mathf.Clamp(captured.factor + dir * 0.1f, -999f, 999f); captured.factor = v; CharEditorDefEditorCompat.GeneSetDamageFactor(gene, captured.damageDef, v); },
                        Activate = () => BeginExactEntry(captured.damageDef.LabelCap.ToString(), F2(captured.factor), FloatSpec,
                            s => { if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) { captured.factor = v; CharEditorDefEditorCompat.GeneSetDamageFactor(gene, captured.damageDef, v); } }),
                        RemoveSelf = () => CharEditorDefEditorCompat.GeneRemoveDamageFactor(gene, captured.damageDef),
                    });
                }
            }
            string label = CharEditorDefEditorCompat.Label("DAMAGEFACTOR");
            rows.Add(new DefRow { Label = label + "...", Kind = RowKind.AddAction, Activate = () => OpenAddMenu(CharEditorDefEditorCompat.GeneFreeDamageFactors(), d => d.LabelCap.ToString(), d => CharEditorDefEditorCompat.GeneSetDamageFactor(gene, d, 0f), label) });
            return rows;
        }

        private List<DefRow> BuildGeneDisabledNeedRows() => BuildMembershipRows(gene.disablesNeeds, CharEditorDefEditorCompat.GeneFreeNeeds,
            d => d.LabelCap.ToString(), d => CharEditorDefEditorCompat.GeneSetDisabledNeed(gene, d), d => CharEditorDefEditorCompat.GeneRemoveDisabledNeed(gene, d), CharEditorDefEditorCompat.Label("DISABLEDNEEDS") + "...");

        private List<DefRow> BuildGeneForcedHeadTypeRows() => BuildMembershipRows(gene.forcedHeadTypes, CharEditorDefEditorCompat.GeneFreeForcedHeadTypes,
            d => d.LabelCap.ToString(), d => CharEditorDefEditorCompat.GeneSetForcedHeadType(gene, d), d => CharEditorDefEditorCompat.GeneRemoveForcedHeadType(gene, d), CharEditorDefEditorCompat.Label("FORCEDHEADTYPES") + "...");

        private List<DefRow> BuildGeneDisabledWorkTagRows()
        {
            var current = new List<WorkTags>();
            foreach (WorkTags tag in Enum.GetValues(typeof(WorkTags)))
            {
                if (tag != WorkTags.None && (gene.disabledWorkTags & tag) == tag && IsSingleFlag(tag))
                    current.Add(tag);
            }
            return BuildMembershipRows(current, CharEditorDefEditorCompat.GeneFreeWorkTags,
                t => t.LabelTranslated().ToString(),
                t => CharEditorDefEditorCompat.GeneSetDisabledWorkTags(gene, t),
                t => CharEditorDefEditorCompat.GeneRemoveDisabledWorkTags(gene, t),
                CharEditorDefEditorCompat.Label("DISABLEDWORKTAGS") + "...");
        }

        private static bool IsSingleFlag(WorkTags tag) => tag != 0 && (tag & (tag - 1)) == 0;

        // GeneDef: general, chances, behavior, sounds, tag lists and gizmo thresholds, via
        // ApplyGeneParam.

        private List<DefRow> BuildGeneGeneralRows()
        {
            var rows = new List<DefRow>
            {
                // The mod passes no label text, so this row's caption is a minted key.
                GeneTextParamRow("RimWorldAccess.CharEd.DefEditor.LabelField".Translate().ToString(), "P01_label"),
                GeneTextParamRow(CharEditorDefEditorCompat.Label("RESOURCELABEL"), "P65_resourceLabel"),
                GeneTextParamRow(CharEditorDefEditorCompat.Label("RESOURCEDESC"), "P66_resourceDescription"),
                GeneTextParamRow(CharEditorDefEditorCompat.Label("LABELADJ"), "P63_labelAdj"),
                GeneTextParamRow(CharEditorDefEditorCompat.Label("ICONPATH"), "P64_iconPath"),
                // LAllGeneCategories is the dialog's own private instance field; falls back to the full GeneCategoryDef DefDatabase, same precedent as this scope's existing Chemical/ForcedHair combos.
                GeneDefComboRow(CharEditorDefEditorCompat.Label("GENECATEGORY"), "P67_geneCategory",
                    () => DefDatabase<GeneCategoryDef>.AllDefsListForReading.ToList(), d => d != null ? d.LabelCap.ToString() : "RimWorldAccess.CharEd.Browser.AllOption".Translate().ToString(), allowNull: true),
                GeneDefComboRow(CharEditorDefEditorCompat.Label("PREREQUISITE"), "P62_prerequisiteDef",
                    () => DefDatabase<GeneDef>.AllDefsListForReading.ToList(), d => d != null ? d.LabelCap.ToString() : "RimWorldAccess.CharEd.Browser.AllOption".Translate().ToString(), allowNull: true),
                GeneDefComboRow(CharEditorDefEditorCompat.Label("HISTORYEVENTONDEATH"), "P61_historyEventDef",
                    () => DefDatabase<HistoryEventDef>.AllDefsListForReading.ToList(), d => d != null ? d.LabelCap.ToString() : "RimWorldAccess.CharEd.Browser.AllOption".Translate().ToString(), allowNull: true),
            };
            // Capture stores (int)endogeneCategory; candidate source is the dialog's own private field, falls back to Enum.GetValues.
            rows.Add(new DefRow
            {
                Label = CharEditorDefEditorCompat.Label("ENDOGENECATEGORY"),
                Kind = RowKind.Combo,
                RenderValue = () =>
                {
                    string raw = CharEditorDefEditorCompat.ReadGeneParam(gene, "P68_endogeneCategory");
                    return int.TryParse(raw, out int ev) ? ((EndogeneCategory)ev).ToString() : raw;
                },
                Activate = () => OpenValueCombo((EndogeneCategory[])Enum.GetValues(typeof(EndogeneCategory)), e => e.ToString(),
                    e => { if (!CharEditorDefEditorCompat.ApplyGeneParam(gene, "P68_endogeneCategory", ((int)e).ToString())) SpeakWriteFailed(); },
                    CharEditorDefEditorCompat.Label("ENDOGENECATEGORY")),
            });
            // Capture stores the enum NAME, not an int, when set, and "" when null.
            rows.Add(new DefRow
            {
                Label = CharEditorDefEditorCompat.Label("GENETICBODYTYPE"),
                Kind = RowKind.Combo,
                RenderValue = () =>
                {
                    string raw = CharEditorDefEditorCompat.ReadGeneParam(gene, "P70_geneticBodyType");
                    return raw.NullOrEmpty() ? "RimWorldAccess.CharEd.Browser.AllOption".Translate().ToString() : raw;
                },
                Activate = delegate
                {
                    var candidates = new List<GeneticBodyType?> { null };
                    foreach (GeneticBodyType b in Enum.GetValues(typeof(GeneticBodyType))) candidates.Add(b);
                    OpenValueCombo(candidates, b => b.HasValue ? b.Value.ToString() : "RimWorldAccess.CharEd.Browser.AllOption".Translate().ToString(),
                        b => { if (!CharEditorDefEditorCompat.ApplyGeneParam(gene, "P70_geneticBodyType", b.HasValue ? b.Value.ToString() : "")) SpeakWriteFailed(); },
                        CharEditorDefEditorCompat.Label("GENETICBODYTYPE"));
                },
            });
            // A write here would silently have no effect on the live def, so the row is read-only.
            rows.Add(new DefRow
            {
                Label = "RimWorldAccess.CharEd.DefEditor.CausedNeed".Translate().ToString(),
                Kind = RowKind.ReadOnly,
                RenderValue = () => gene.enablesNeeds.NullOrEmpty() ? "" : gene.enablesNeeds[0]?.LabelCap.ToString(),
            });
            return rows;
        }

        private List<DefRow> BuildGeneChancesRows()
        {
            return new List<DefRow>
            {
                GeneFloatParamRow(CharEditorDefEditorCompat.Label("ADDICTIONCHANCEFACTOR"), "P23_addictionChanceFactor", 0f, 50f),
                GeneFloatParamRow(CharEditorDefEditorCompat.Label("OVERDOSECHANCEFACTOR"), "P52_overdoseChanceFactor", 0f, 50f),
                GeneFloatParamRow(CharEditorDefEditorCompat.Label("TOLERANCEBUILDUPFACTOR"), "P53_toleranceBuildupFactor", 0f, 50f),
                GeneFloatParamRow("Stat_Hediff_FoodPoisoningChanceFactor_Name".Translate().ToString(), "P25_foodPoisioningChanceFactor", -1f, 10f),
                GeneFloatParamRow("RimWorldAccess.CharEd.DefEditor.PainFactor".Translate().ToString(), "P32_painFactor", 0f, 100f),
                GeneFloatParamRow("RimWorldAccess.CharEd.DefEditor.PainOffset".Translate().ToString(), "P33_painOffset", 0f, 100f),
                GeneFloatParamRow(CharEditorDefEditorCompat.Label("MENTALBREAKMTBDAYS"), "P28_mentalBreakMtbDays", 0f, 100f),
                GeneFloatParamRow(CharEditorDefEditorCompat.Label("MENTALBREAKCHANCEFACTOR"), "P24_mentalBreakChanceFactor", 0f, 100f),
                GeneFloatParamRow(CharEditorDefEditorCompat.Label("SOCIALFIGHTCHANCEFACTOR"), "P39_socialFightChanceFactor", 0f, 100f),
                GeneFloatParamRow(CharEditorDefEditorCompat.Label("PRISONBREAKINTERVAL"), "P34_prisonBreakMtbFactor", 0f, 100f),
                GeneFloatParamRow(CharEditorDefEditorCompat.Label("MARKETVALUEFACTOR"), "P27_marketValueFactor", 0f, 100f),
                GeneFloatParamRow(CharEditorDefEditorCompat.Label("STARTSATAGE"), "P29_minAgeActive", 0f, 500f),
                // minMelanin has no widget in the mod at all, so this is exact-entry only with a
                // minted label. Vanilla's default of -1 means race default, so the range spans it.
                GeneFloatParamRow("RimWorldAccess.CharEd.DefEditor.MinMelanin".Translate().ToString(), "P30_minMelanin", -1f, 1f),
                GeneFloatParamRow(CharEditorDefEditorCompat.Label("SELECTIONWEIGHT"), "P37_selectionWeight", 0f, 2f),
                GeneFloatParamRow(CharEditorDefEditorCompat.Label("SELECTIONWEIGHTDARKSKIN"), "P38_selectionWeightFactorDarkSkin", 0f, 2f),
                GeneFloatParamRow(CharEditorDefEditorCompat.Label("DISPLAYODERINCATEGORY"), "P54_displayOrderInCategory", -100f, 10000f),
                GeneFloatParamRow(CharEditorDefEditorCompat.Label("LOVINMTBFACTOR"), "P26_lovinMTBFactor", 0f, 10f),
                GeneFloatParamRow(CharEditorDefEditorCompat.Label("RESOURCELOSSPERDAY"), "P36_resourceLossPerDay", 0f, 100f),
                GeneFloatParamRow("RimWorldAccess.CharEd.DefEditor.MissingRomanceChance".Translate().ToString(), "P31_missingGeneRomanceChanceFactor", 0f, 100f),
            };
        }

        private List<DefRow> BuildGeneBehaviorRows()
        {
            return new List<DefRow>
            {
                GeneBoolParamRow(CharEditorDefEditorCompat.Label("UNAFFECTEDBYDARK"), "P43_ignoreDarkness"),
                GeneBoolParamRow(CharEditorDefEditorCompat.Label("CANGENERATEINGENESET"), "P40_canGenerateInGeneSet"),
                GeneBoolParamRow(CharEditorDefEditorCompat.Label("REMOVEONREDRESS"), "P48_removeOnRedress"),
                GeneBoolParamRow(CharEditorDefEditorCompat.Label("PASSONDIRECTLY"), "P55_passOnDirectly"),
                GeneBoolParamRow(CharEditorDefEditorCompat.Label("RANDOMCHOSEN"), "P47_randomChosen"),
                GeneBoolParamRow(CharEditorDefEditorCompat.Label("STERILIZE"), "P50_sterilize"),
                GeneBoolParamRow(CharEditorDefEditorCompat.Label("DISLIKESSUNLIGHT"), "P41_dislikesSunLight"),
                GeneBoolParamRow(CharEditorDefEditorCompat.Label("DONTMINDRAWFOOD"), "P42_dontMindRawFood"),
                GeneBoolParamRow(CharEditorDefEditorCompat.Label("IMMUNETOTOXGASEXPOSURE"), "P44_immuneToToxGasExposure"),
                GeneBoolParamRow(CharEditorDefEditorCompat.Label("NEVERGRAYHAIR"), "P45_neverGrayHair"),
                GeneBoolParamRow(CharEditorDefEditorCompat.Label("WOMENCANHAVEBEARDS"), "P51_womenCanHaveBeards"),
                GeneBoolParamRow(CharEditorDefEditorCompat.Label("PREVENTPERMANENTWOUNDS"), "P46_prevenetPermanent_Wounds"),
                GeneBoolParamRow("RimWorldAccess.CharEd.DefEditor.ShowGizmoOnWorldView".Translate().ToString(), "P49_showGizmoOnWorldView"),
                GeneBoolParamRow("RimWorldAccess.CharEd.DefEditor.ShowGizmoWhenDrafted".Translate().ToString(), "P56_showGizmoWhenDrafted"),
                GeneBoolParamRow("RimWorldAccess.CharEd.DefEditor.ShowGizmoOnMultiSelect".Translate().ToString(), "P57_showGizmoOnMultiSelect"),
            };
        }

        private List<DefRow> BuildGeneSoundRows()
        {
            // The dialog's own sound list is a private field filtered to "Pawn_" prefixes; this
            // falls back to the full SoundDef DefDatabase, broader than the mod's filtered list.
            return new List<DefRow>
            {
                GeneDefComboRow(CharEditorDefEditorCompat.Label("SOUNDCALL"), "P58_soundCall",
                    () => DefDatabase<SoundDef>.AllDefsListForReading.ToList(), d => d != null ? d.defName : "RimWorldAccess.CharEd.Browser.AllOption".Translate().ToString(), allowNull: true),
                GeneDefComboRow(CharEditorDefEditorCompat.Label("SOUNDDEATH"), "P59_soundDeath",
                    () => DefDatabase<SoundDef>.AllDefsListForReading.ToList(), d => d != null ? d.defName : "RimWorldAccess.CharEd.Browser.AllOption".Translate().ToString(), allowNull: true),
                GeneDefComboRow(CharEditorDefEditorCompat.Label("SOUNDWOUNDED"), "P60_soundWounded",
                    () => DefDatabase<SoundDef>.AllDefsListForReading.ToList(), d => d != null ? d.defName : "RimWorldAccess.CharEd.Browser.AllOption".Translate().ToString(), allowNull: true),
            };
        }

        // Plain pipe-joined string lists with no Set/Remove vehicle; candidates are harvested live
        // across DefDatabase<GeneDef>.
        private List<DefRow> BuildGeneCustomEffectDescriptionRows() => BuildStringListParamRows(
            () => CharEditorDefEditorCompat.ReadGeneParam(gene, "P71_customEffectDescriptions"),
            v => CharEditorDefEditorCompat.ApplyGeneParam(gene, "P71_customEffectDescriptions", v),
            () => HarvestGeneTagValues(d => d.customEffectDescriptions),
            CharEditorDefEditorCompat.Label("CUSTOMEFFECTDESCRIPTIONS") + "...");

        private List<DefRow> BuildGeneExclusionTagRows() => BuildStringListParamRows(
            () => CharEditorDefEditorCompat.ReadGeneParam(gene, "P74_exclusionTags"),
            v => CharEditorDefEditorCompat.ApplyGeneParam(gene, "P74_exclusionTags", v),
            () => HarvestGeneTagValues(d => d.exclusionTags),
            CharEditorDefEditorCompat.Label("EXCLUSIONTAGS") + "...");

        private List<DefRow> BuildGeneHairTagRows() => BuildStringListParamRows(
            () => CharEditorDefEditorCompat.ReadGeneParam(gene, "P75_hairTagFilter"),
            v => CharEditorDefEditorCompat.ApplyGeneParam(gene, "P75_hairTagFilter", v),
            () => HarvestGeneTagValues(d => d.hairTagFilter?.tags),
            CharEditorDefEditorCompat.Label("HAIRTAGS") + "...");

        private List<DefRow> BuildGeneBeardTagRows() => BuildStringListParamRows(
            () => CharEditorDefEditorCompat.ReadGeneParam(gene, "P76_beardTagFilter"),
            v => CharEditorDefEditorCompat.ApplyGeneParam(gene, "P76_beardTagFilter", v),
            () => HarvestGeneTagValues(d => d.beardTagFilter?.tags),
            CharEditorDefEditorCompat.Label("BEARDTAGS") + "...");

        // A plain pipe-joined List<float>, no per-element key; Add/Remove operate by index rather than by def.
        private List<DefRow> BuildGeneGizmoThresholdRows()
        {
            var rows = new List<DefRow>();
            List<float> current = SplitPipeFloats(CharEditorDefEditorCompat.ReadGeneParam(gene, "P72_resourceGizmoThresholds"));
            for (int i = 0; i < current.Count; i++)
            {
                int idx = i;
                rows.Add(new DefRow
                {
                    Label = F2(current[i]),
                    Kind = RowKind.ValueElement,
                    RenderValue = () =>
                    {
                        List<float> live = SplitPipeFloats(CharEditorDefEditorCompat.ReadGeneParam(gene, "P72_resourceGizmoThresholds"));
                        return idx < live.Count ? F2(live[idx]) : "";
                    },
                    Adjust = dir =>
                    {
                        List<float> list = SplitPipeFloats(CharEditorDefEditorCompat.ReadGeneParam(gene, "P72_resourceGizmoThresholds"));
                        if (idx >= list.Count) return;
                        list[idx] = list[idx] + dir * 0.05f;
                        if (!CharEditorDefEditorCompat.ApplyGeneParam(gene, "P72_resourceGizmoThresholds", JoinPipeFloats(list))) SpeakWriteFailed();
                    },
                    Activate = () => BeginExactEntry(CharEditorDefEditorCompat.Label("RESOURCEGIZMOTHRESHOLD"), F2(current[idx]), FloatSpec, s =>
                    {
                        if (!float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) return;
                        List<float> list = SplitPipeFloats(CharEditorDefEditorCompat.ReadGeneParam(gene, "P72_resourceGizmoThresholds"));
                        if (idx >= list.Count) return;
                        list[idx] = v;
                        if (!CharEditorDefEditorCompat.ApplyGeneParam(gene, "P72_resourceGizmoThresholds", JoinPipeFloats(list))) SpeakWriteFailed();
                    }),
                    RemoveSelf = () =>
                    {
                        List<float> list = SplitPipeFloats(CharEditorDefEditorCompat.ReadGeneParam(gene, "P72_resourceGizmoThresholds"));
                        if (idx >= list.Count) return;
                        list.RemoveAt(idx);
                        if (!CharEditorDefEditorCompat.ApplyGeneParam(gene, "P72_resourceGizmoThresholds", JoinPipeFloats(list))) SpeakWriteFailed();
                    },
                });
            }
            string label = CharEditorDefEditorCompat.Label("RESOURCEGIZMOTHRESHOLD");
            rows.Add(new DefRow
            {
                Label = label + "...",
                Kind = RowKind.AddAction,
                Activate = () =>
                {
                    InspectionTreeItem item = CurrentTreeItem();
                    InspectionTreeItem sectionItem = item != null && item.Data is DefRow ? item.Parent : item;
                    editSession.EnterEdit("", FloatSpec, label, s =>
                    {
                        if (!float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) return;
                        List<float> list = SplitPipeFloats(CharEditorDefEditorCompat.ReadGeneParam(gene, "P72_resourceGizmoThresholds"));
                        list.Add(v);
                        if (CharEditorDefEditorCompat.ApplyGeneParam(gene, "P72_resourceGizmoThresholds", JoinPipeFloats(list)))
                        {
                            RebuildSection(sectionItem);
                            RefreshModel();
                            SyncRegionFromCurrentTree();
                        }
                        else SpeakWriteFailed();
                    }, onExit: RefreshAndAnnounce);
                },
            });
            return rows;
        }

        // ThingDef sections.

        private List<DefSection> BuildObjectSections()
        {
            var list = new List<DefSection>();
            if (thing == null) return list;

            list.Add(new DefSection { Label = "RimWorldAccess.CharEd.DefEditor.GeneralSection".Translate(), Build = BuildThingGeneralRows });
            list.Add(new DefSection { Label = CharEditorDefEditorCompat.Label("TRADETAGS"), Build = BuildThingTradeTagRows });
            list.Add(new DefSection { Label = CharEditorDefEditorCompat.Label("WEAPONTAGS"), Build = BuildThingWeaponTagRows });
            if (thing.apparel != null)
            {
                list.Add(new DefSection { Label = CharEditorDefEditorCompat.Label("APPARELTAGS"), Build = BuildThingApparelTagRows });
                list.Add(new DefSection { Label = CharEditorDefEditorCompat.Label("OUTFITTAGS"), Build = BuildThingOutfitTagRows });
            }
            if (ThingHasVerb())
            {
                list.Add(new DefSection { Label = "RimWorldAccess.CharEd.DefEditor.BallisticsSection".Translate(), Build = BuildThingBallisticsRows });
            }
            list.Add(new DefSection { Label = CharEditorDefEditorCompat.Label("STAT_FACTORS"), Build = BuildThingStatFactorRows });
            list.Add(new DefSection { Label = CharEditorDefEditorCompat.Label("STAT_OFFSETS"), Build = BuildThingStatOffsetRows });
            if (thing.race == null)
            {
                list.Add(new DefSection { Label = CharEditorDefEditorCompat.Label("STUFFPROPS"), Build = BuildThingStuffCategoryRows });
                list.Add(new DefSection { Label = CharEditorDefEditorCompat.Label("COSTS"), Build = BuildThingCostRows });
            }
            if (thing.costListForDifficulty != null)
            {
                list.Add(new DefSection { Label = CharEditorDefEditorCompat.Label("COSTS") + CharEditorDefEditorCompat.Label("FORDIFFICULTY"), Build = BuildThingCostsDiffRows });
            }
            if (thing.apparel != null)
            {
                list.Add(new DefSection { Label = CharEditorDefEditorCompat.Label("APPARELLAYER"), Build = BuildThingApparelLayerRows });
                list.Add(new DefSection { Label = CharEditorDefEditorCompat.Label("BODYPARTGROUPS"), Build = BuildThingBodyPartGroupRows });
            }
            if (thing.building != null)
            {
                list.Add(new DefSection { Label = CharEditorDefEditorCompat.Label("BUILDINGPREREQUISITES"), Build = BuildThingResearchPrereqRows });
            }
            if (thing.recipeMaker != null)
            {
                list.Add(new DefSection { Label = "RimWorldAccess.CharEd.DefEditor.RecipeResearchSection".Translate(), Build = BuildThingRecipeResearchRows });
            }
            if (CharEditorDefEditorCompat.ThingSelectedTempThing()?.TryGetComp<CompBladelinkWeapon>() != null)
            {
                list.Add(new DefSection { Label = CharEditorDefEditorCompat.Label("BLADELINKTRAITS"), Build = BuildThingWeaponTraitRows });
            }
            return list;
        }

        private List<DefRow> BuildThingStatFactorRows() => BuildStatModifierRowsThing(thing.statBases,
            CharEditorDefEditorCompat.ThingFreeStatFactors, CharEditorDefEditorCompat.ThingSetStatFactor, CharEditorDefEditorCompat.ThingRemoveStatFactor, CharEditorDefEditorCompat.Label("STAT_FACTORS"));

        private List<DefRow> BuildThingStatOffsetRows() => BuildStatModifierRowsThing(thing.equippedStatOffsets,
            CharEditorDefEditorCompat.ThingFreeStatOffsets, CharEditorDefEditorCompat.ThingSetStatOffset, CharEditorDefEditorCompat.ThingRemoveStatOffset, CharEditorDefEditorCompat.Label("STAT_OFFSETS"));

        private List<DefRow> BuildStatModifierRowsThing(List<StatModifier> current, Func<HashSet<StatDef>> free,
            Action<ThingDef, StatDef, float> setter, Action<ThingDef, StatDef> remover, string addLabel)
        {
            var rows = new List<DefRow>();
            if (current != null)
            {
                foreach (StatModifier sm in current.ToList())
                {
                    StatModifier captured = sm;
                    float min = captured.stat != null ? captured.stat.minValue : -999f;
                    float max = captured.stat != null ? captured.stat.maxValue : 999f;
                    rows.Add(new DefRow
                    {
                        Label = captured.stat != null ? captured.stat.LabelCap.ToString() : "?",
                        Kind = RowKind.ValueElement,
                        RenderValue = () => F2(captured.value),
                        Adjust = dir => { float v = Mathf.Clamp(captured.value + dir * StepFor(min, max), min, max); captured.value = v; setter(thing, captured.stat, v); },
                        Activate = () => CharEdNumericEntry.OpenFloat(editSession, captured.stat.LabelCap.ToString(),
                            F2(captured.value), FloatSpec, min, max,
                            v => { captured.value = v; setter(thing, captured.stat, v); }, RefreshAndAnnounce),
                        RemoveSelf = () => remover(thing, captured.stat),
                    });
                }
            }
            rows.Add(new DefRow { Label = addLabel + "...", Kind = RowKind.AddAction, Activate = () => OpenAddMenu(free(), d => d.LabelCap.ToString(), d => setter(thing, d, 0f), addLabel) });
            return rows;
        }

        private List<DefRow> BuildThingStuffCategoryRows() => BuildMembershipRows(thing.stuffCategories, CharEditorDefEditorCompat.ThingFreeStuffCategories,
            d => d.LabelCap.ToString(), d => CharEditorDefEditorCompat.ThingSetStuffCategorie(thing, d), d => CharEditorDefEditorCompat.ThingRemoveStuffCategorie(thing, d), CharEditorDefEditorCompat.Label("STUFFPROPS") + "...");

        private List<DefRow> BuildThingCostRows()
        {
            var rows = new List<DefRow>();
            if (thing.costList != null)
            {
                foreach (ThingDefCountClass c in thing.costList.ToList())
                {
                    ThingDefCountClass captured = c;
                    rows.Add(new DefRow
                    {
                        Label = captured.thingDef != null ? captured.thingDef.LabelCap.ToString() : "?",
                        Kind = RowKind.ValueElement,
                        RenderValue = () => captured.count.ToString(),
                        Adjust = dir => { int v = Mathf.Clamp(captured.count + dir, 0, 99999); captured.count = v; CharEditorDefEditorCompat.ThingSetCosts(thing, captured.thingDef, v); },
                        Activate = () => BeginExactEntry(captured.thingDef.LabelCap.ToString(), captured.count.ToString(), IntSpec,
                            s => { if (int.TryParse(s, out int v)) { captured.count = v; CharEditorDefEditorCompat.ThingSetCosts(thing, captured.thingDef, v); } }),
                        RemoveSelf = () => CharEditorDefEditorCompat.ThingRemoveCosts(thing, captured.thingDef),
                    });
                }
            }
            string label = CharEditorDefEditorCompat.Label("COSTS");
            rows.Add(new DefRow { Label = label + "...", Kind = RowKind.AddAction, Activate = () => OpenAddMenu(CharEditorDefEditorCompat.ThingFreeCosts(), d => d.LabelCap.ToString(), d => CharEditorDefEditorCompat.ThingSetCosts(thing, d, 0), label) });
            return rows;
        }

        private List<DefRow> BuildThingCostsDiffRows()
        {
            var rows = new List<DefRow>();
            List<ThingDefCountClass> costList = thing.costListForDifficulty?.costList;
            if (costList != null)
            {
                foreach (ThingDefCountClass c in costList.ToList())
                {
                    ThingDefCountClass captured = c;
                    rows.Add(new DefRow
                    {
                        Label = captured.thingDef != null ? captured.thingDef.LabelCap.ToString() : "?",
                        Kind = RowKind.ValueElement,
                        RenderValue = () => captured.count.ToString(),
                        Adjust = dir => { int v = Mathf.Clamp(captured.count + dir, 0, 99999); captured.count = v; CharEditorDefEditorCompat.ThingSetCostsDiff(thing, captured.thingDef, v); },
                        Activate = () => BeginExactEntry(captured.thingDef.LabelCap.ToString(), captured.count.ToString(), IntSpec,
                            s => { if (int.TryParse(s, out int v)) { captured.count = v; CharEditorDefEditorCompat.ThingSetCostsDiff(thing, captured.thingDef, v); } }),
                        RemoveSelf = () => CharEditorDefEditorCompat.ThingRemoveCostsDiff(thing, captured.thingDef),
                    });
                }
            }
            string label = CharEditorDefEditorCompat.Label("COSTS") + CharEditorDefEditorCompat.Label("FORDIFFICULTY");
            rows.Add(new DefRow { Label = label + "...", Kind = RowKind.AddAction, Activate = () => OpenAddMenu(CharEditorDefEditorCompat.ThingFreeCostsDiff(), d => d.LabelCap.ToString(), d => CharEditorDefEditorCompat.ThingSetCostsDiff(thing, d, 0), label) });
            return rows;
        }

        private List<DefRow> BuildThingApparelLayerRows() => BuildMembershipRows(thing.apparel?.layers, CharEditorDefEditorCompat.ThingFreeApparelLayer,
            d => d.LabelCap.ToString(), d => CharEditorDefEditorCompat.ThingSetApparelLayer(thing, d), d => CharEditorDefEditorCompat.ThingRemoveApparelLayer(thing, d), CharEditorDefEditorCompat.Label("APPARELLAYER") + "...");

        private List<DefRow> BuildThingBodyPartGroupRows() => BuildMembershipRows(thing.apparel?.bodyPartGroups, CharEditorDefEditorCompat.ThingFreeBodyPartGroup,
            d => d.LabelCap.ToString(), d => CharEditorDefEditorCompat.ThingSetBodyPartGroup(thing, d), d => CharEditorDefEditorCompat.ThingRemoveBodyPartGroup(thing, d), CharEditorDefEditorCompat.Label("BODYPARTGROUPS") + "...");

        private List<DefRow> BuildThingResearchPrereqRows() => BuildMembershipRows(thing.researchPrerequisites, CharEditorDefEditorCompat.ThingFreePrerequisites,
            d => d.LabelCap.ToString(), d => CharEditorDefEditorCompat.ThingSetPrerequisite(thing, d), d => CharEditorDefEditorCompat.ThingRemovePrerequisite(thing, d), CharEditorDefEditorCompat.Label("BUILDINGPREREQUISITES") + "...");

        private List<DefRow> BuildThingRecipeResearchRows()
        {
            var candidates = DefDatabase<ResearchProjectDef>.AllDefsListForReading.Cast<ResearchProjectDef>().ToList();
            return new List<DefRow>
            {
                new DefRow
                {
                    Label = "RimWorldAccess.CharEd.DefEditor.RecipeResearchSection".Translate(),
                    Kind = RowKind.Combo,
                    RenderValue = () => thing.recipeMaker.researchPrerequisite != null ? thing.recipeMaker.researchPrerequisite.LabelCap.ToString() : "RimWorldAccess.CharEd.Browser.AllOption".Translate().ToString(),
                    Activate = () => OpenDefCombo(candidates, d => d != null ? d.LabelCap.ToString() : "RimWorldAccess.CharEd.Browser.AllOption".Translate().ToString(),
                        d => CharEditorDefEditorCompat.ThingSetResearchPrerequisite(thing, d), "RimWorldAccess.CharEd.DefEditor.RecipeResearchSection".Translate().ToString(), allowNull: true),
                },
            };
        }

        private List<DefRow> BuildThingWeaponTraitRows()
        {
            Thing tempThing = CharEditorDefEditorCompat.ThingSelectedTempThing();
            CompBladelinkWeapon comp = tempThing?.TryGetComp<CompBladelinkWeapon>();
            var rows = new List<DefRow>();
            if (comp != null)
            {
                foreach (WeaponTraitDef t in comp.TraitsListForReading.ToList())
                {
                    WeaponTraitDef captured = t;
                    rows.Add(new DefRow
                    {
                        Label = captured.LabelCap.ToString(),
                        Kind = RowKind.MembershipElement,
                        RemoveSelf = () => CharEditorDefEditorCompat.ThingRemoveBladeLinkTrait(tempThing, captured),
                    });
                }
                string label = CharEditorDefEditorCompat.Label("BLADELINKTRAITS");
                rows.Add(new DefRow { Label = label + "...", Kind = RowKind.AddAction, Activate = () => OpenAddMenu(CharEditorDefEditorCompat.ThingAllWeaponTraitDef(), d => d.LabelCap.ToString(), d => CharEditorDefEditorCompat.ThingSetBladeLinkTrait(tempThing, d), label) });
                // MUTATION-C: mirrors DialogObjects.DrawWeaponTraits' own code/uncode branch
                // (DialogObjects.cs:1343-1350) -- "tempPawn" there is the dialog's own captured
                // pawn; the editor's currently loaded pawn (CharEditorCompat.CurrentPawn) is the
                // equivalent reference for a keyboard-driven open of this same browser.
                rows.Add(BoolRow(CharEditorDefEditorCompat.Label("BIOCODED"), () => comp.CodedPawn != null, v =>
                {
                    if (!v) comp.UnCode();
                    else
                    {
                        Pawn p = CharEditorCompat.CurrentPawn;
                        if (p != null && comp.CodedPawn != p) comp.CodeFor(p);
                    }
                }));
            }
            return rows;
        }

        // ThingDef: general, tags and ballistics, via ApplyObjectParam.

        private bool ThingHasVerb() => thing.Verbs != null && thing.Verbs.Count > 0;
        private bool ThingHasProjectile() => ThingHasVerb() && thing.Verbs[0].defaultProjectile != null && thing.Verbs[0].defaultProjectile.projectile != null;
        /// <summary>Approximates the mod's inaccessible IsTurret from public vanilla state; display and row gating only, never a write gate.</summary>
        private bool ThingIsTurretApprox() => thing.building != null && thing.building.turretGunDef != null;

        private List<DefRow> BuildThingGeneralRows()
        {
            var rows = new List<DefRow>
            {
                // The mod passes no label text at all, so this row's caption is a minted key.
                ThingTextParamRow("RimWorldAccess.CharEd.DefEditor.LabelField".Translate().ToString(), "P01_label"),
                ThingBoolParamRow(CharEditorDefEditorCompat.Label("STEALABLE"), "P04_stealable"),
            };
            // Capture stores (int)techLevel.
            rows.Add(new DefRow
            {
                Label = CharEditorDefEditorCompat.Label("TECHLEVEL"),
                Kind = RowKind.Combo,
                RenderValue = () => thing.techLevel.ToStringHuman(),
                Activate = () => OpenValueCombo(CharEditorDefEditorCompat.ThingAllTechLevels(), t => t.ToStringHuman(),
                    t => { if (!CharEditorDefEditorCompat.ApplyObjectParam(thing, "P02_techLevel", ((int)t).ToString())) SpeakWriteFailed(); },
                    CharEditorDefEditorCompat.Label("TECHLEVEL")),
            });
            // Capture stores (int)tradeability; vanilla has no localized label for this enum.
            rows.Add(new DefRow
            {
                Label = CharEditorDefEditorCompat.Label("TRADEABILITY"),
                Kind = RowKind.Combo,
                RenderValue = () => thing.tradeability.ToString(),
                Activate = () => OpenValueCombo(CharEditorDefEditorCompat.ThingAllTradeabilities(), t => t.ToString(),
                    t => { if (!CharEditorDefEditorCompat.ApplyObjectParam(thing, "P03_tradeability", ((int)t).ToString())) SpeakWriteFailed(); },
                    CharEditorDefEditorCompat.Label("TRADEABILITY")),
            });
            if (thing.race == null)
            {
                rows.Add(ThingIntParamRow(CharEditorDefEditorCompat.Label("COSTSTUFFCOUNT"), "P05_costStuffCount", 0, 99999));
            }
            rows.Add(ThingIntParamRow(CharEditorDefEditorCompat.Label("O_STACKLIMIT"), "P77_stackLimit", 1, 2000));
            return rows;
        }

        // A plain pipe-joined List<string> with no Set/Remove vehicle; candidates are harvested
        // live across DefDatabase<ThingDef>, the mod's own source being a private per-language list.
        private List<DefRow> BuildThingTradeTagRows() => BuildStringListParamRows(
            () => CharEditorDefEditorCompat.ReadObjectParam(thing, "P11_tradeTags"),
            v => CharEditorDefEditorCompat.ApplyObjectParam(thing, "P11_tradeTags", v),
            () => HarvestThingTagValues(d => d.tradeTags),
            CharEditorDefEditorCompat.Label("TRADETAGS") + "...");

        private List<DefRow> BuildThingWeaponTagRows() => BuildStringListParamRows(
            () => CharEditorDefEditorCompat.ReadObjectParam(thing, "P12_weaponTags"),
            v => CharEditorDefEditorCompat.ApplyObjectParam(thing, "P12_weaponTags", v),
            () => HarvestThingTagValues(d => d.weaponTags),
            CharEditorDefEditorCompat.Label("WEAPONTAGS") + "...");

        private List<DefRow> BuildThingApparelTagRows() => BuildStringListParamRows(
            () => CharEditorDefEditorCompat.ReadObjectParam(thing, "P75_apparelTags"),
            v => CharEditorDefEditorCompat.ApplyObjectParam(thing, "P75_apparelTags", v),
            () => HarvestThingTagValues(d => d.apparel?.tags),
            CharEditorDefEditorCompat.Label("APPARELTAGS") + "...");

        private List<DefRow> BuildThingOutfitTagRows() => BuildStringListParamRows(
            () => CharEditorDefEditorCompat.ReadObjectParam(thing, "P76_outfitTags"),
            v => CharEditorDefEditorCompat.ApplyObjectParam(thing, "P76_outfitTags", v),
            () => HarvestThingTagValues(d => d.apparel?.defaultOutfitTags),
            CharEditorDefEditorCompat.Label("OUTFITTAGS") + "...");

        /// <summary>
        /// The ballistics, ammo and sound cluster. Every row is gated on the same structural
        /// precondition the mod's own draw code requires (HasVerb/HasBullet/turret): a def without a
        /// Verb has no <c>Verbs[0]</c> to read or write.
        /// </summary>
        private List<DefRow> BuildThingBallisticsRows()
        {
            var rows = new List<DefRow>();
            if (!ThingHasVerb()) return rows;
            bool hasBullet = ThingHasProjectile();

            if (hasBullet)
            {
                // The mod passes no label text ("") for this picker, so this row's caption is a minted key.
                rows.Add(ThingDefComboRow("RimWorldAccess.CharEd.DefEditor.DefaultProjectile".Translate().ToString(),
                    "P13_bulletDefName", CharEditorDefEditorCompat.ThingAllBullets, d => d != null ? d.LabelCap.ToString() : "RimWorldAccess.CharEd.Browser.AllOption".Translate().ToString(), allowNull: true));
                rows.Add(ThingDefComboRow(CharEditorDefEditorCompat.Label("DAMAGEDEF"), "P14_bulletDamageDef",
                    CharEditorDefEditorCompat.ThingAllDamageDefs, d => d != null ? d.LabelCap.ToString() : "RimWorldAccess.CharEd.Browser.AllOption".Translate().ToString(), allowNull: true));
                // Private ProjectileProperties.damageAmountBase field; read via the mod's own capture (ReadObjectParam), never our own reflection.
                rows.Add(ThingIntParamRow(CharEditorDefEditorCompat.Label("DAMAGEAMOUNTBASE"), "P15_bulletDamageAmountBase", 0, 100));
                rows.Add(ThingFloatParamRow("ArmorPenetration".Translate().ToString(), "P18_bulletArmorPenetrationBase", 0f, 100f));
                rows.Add(ThingFloatParamRow(CharEditorDefEditorCompat.Label("BULLET_SPEED"), "P16_bulletSpeed", 0f, 150f));
                rows.Add(ThingFloatParamRow("StoppingPower".Translate().ToString(), "P17_bulletStoppingPower", 0f, 10f));
                rows.Add(ThingFloatParamRow(CharEditorDefEditorCompat.Label("EXPL_RADIUS"), "P19_bulletExplosionRadius", 0f, 50f));
                rows.Add(ThingIntParamRow(CharEditorDefEditorCompat.Label("EXPLOSIONDELAY"), "P20_bulletExplosionDelay", 0, 10));
                rows.Add(ThingIntParamRow(CharEditorDefEditorCompat.Label("NUMEXTRAHITCELLS"), "P21_bulletNumExtraHitCells", 0, 8));
                // The mod's own source is a per-frame category-filtered subset of dialog-instance
                // state; this falls back to the full ThingDef DefDatabase.
                rows.Add(ThingDefComboRow(CharEditorDefEditorCompat.Label("PREEXPLOSIONSPAWNTHING"), "P22_bulletPreExplosionSpawnThingDef",
                    () => new HashSet<ThingDef>(DefDatabase<ThingDef>.AllDefsListForReading), d => d != null ? d.LabelCap.ToString() : "RimWorldAccess.CharEd.Browser.AllOption".Translate().ToString(), allowNull: true));
                rows.Add(ThingIntParamRow(CharEditorDefEditorCompat.Label("PREEXPLOSIONSPAWNTHINGCOUNT"), "P23_bulletPreExplosionSpawnThingCount", 0, 20));
                rows.Add(ThingFloatParamRow(CharEditorDefEditorCompat.Label("PREEXPLOSIONSPAWNCHANCE"), "P24_bulletPreExplosionSpawnChance", 0f, 100f));
                rows.Add(ThingDefComboRow(CharEditorDefEditorCompat.Label("POSTEXPLOSIONSPAWNTHING"), "P25_bulletPostExplosionSpawnThingDef",
                    () => new HashSet<ThingDef>(DefDatabase<ThingDef>.AllDefsListForReading), d => d != null ? d.LabelCap.ToString() : "RimWorldAccess.CharEd.Browser.AllOption".Translate().ToString(), allowNull: true));
                rows.Add(ThingIntParamRow(CharEditorDefEditorCompat.Label("POSTEXPLOSIONSPAWNTHINGCOUNT"), "P26_bulletPostExplosionSpawnThingCount", 0, 20));
                rows.Add(ThingFloatParamRow(CharEditorDefEditorCompat.Label("POSTEXPLOSIONSPAWNCHANCE"), "P27_bulletPostExplosionSpawnChance", 0f, 100f));
                // Capture stores (int)value or "" for null.
                rows.Add(new DefRow
                {
                    Label = CharEditorDefEditorCompat.Label("GASTYPE"),
                    Kind = RowKind.Combo,
                    RenderValue = () =>
                    {
                        string raw = CharEditorDefEditorCompat.ReadObjectParam(thing, "P28_bulletPostExplosionGasType");
                        return int.TryParse(raw, out int gv) ? ((GasType)gv).ToString() : "RimWorldAccess.CharEd.Browser.AllOption".Translate().ToString();
                    },
                    Activate = () => OpenValueCombo(CharEditorDefEditorCompat.ThingAllGasTypes(), g => g.HasValue ? g.Value.ToString() : "RimWorldAccess.CharEd.Browser.AllOption".Translate().ToString(),
                        g => { if (!CharEditorDefEditorCompat.ApplyObjectParam(thing, "P28_bulletPostExplosionGasType", g.HasValue ? ((int)g.Value).ToString() : "")) SpeakWriteFailed(); },
                        CharEditorDefEditorCompat.Label("GASTYPE")),
                });
                rows.Add(ThingDefComboRow(CharEditorDefEditorCompat.Label("EXPLOSIONEFFECT"), "P29_bulletExplosionEffect",
                    CharEditorDefEditorCompat.ThingAllEffecterDefs, d => d != null ? d.LabelCap.ToString() : "RimWorldAccess.CharEd.Browser.AllOption".Translate().ToString(), allowNull: true));
                rows.Add(ThingDefComboRow(CharEditorDefEditorCompat.Label("LANDEDEFFECT"), "P30_bulletLandedEffect",
                    CharEditorDefEditorCompat.ThingAllEffecterDefs, d => d != null ? d.LabelCap.ToString() : "RimWorldAccess.CharEd.Browser.AllOption".Translate().ToString(), allowNull: true));
                rows.Add(ThingBoolParamRow(CharEditorDefEditorCompat.Label("FLYOVERHEAD"), "P35_bulletFlyOverhead"));
                rows.Add(ThingDefComboRow(CharEditorDefEditorCompat.Label("SOUNDEXPLODE"), "P36_bulletSoundExplode",
                    CharEditorDefEditorCompat.ThingAllGunRelatedSounds, d => d != null ? d.defName : "RimWorldAccess.CharEd.Browser.AllOption".Translate().ToString(), allowNull: true));
                rows.Add(ThingDefComboRow(CharEditorDefEditorCompat.Label("SOUNDIMPACT"), "P37_bulletSoundImpact",
                    CharEditorDefEditorCompat.ThingAllGunRelatedSounds, d => d != null ? d.defName : "RimWorldAccess.CharEd.Browser.AllOption".Translate().ToString(), allowNull: true));
                rows.Add(ThingBoolParamRow(CharEditorDefEditorCompat.Label("APPLYDAMAGETOEXPLOSIONCELLNEIGHBORS"), "P38_bulletDamageToCellsNeighbors"));
            }

            rows.Add(ThingBoolParamRow(CharEditorDefEditorCompat.Label("BEAMTARGETGROUND"), "P39_beamTargetsGround"));
            rows.Add(ThingIntParamRow("BurstShotFireRate".Translate().ToString(), "P40_ticksBetweenBurstShots", 0, 100));
            rows.Add(ThingIntParamRow("BurstShotCount".Translate().ToString(), "P41_burstShotCount", 0, 150));
            rows.Add(ThingFloatParamRow("Range".Translate().ToString(), "P42_weaponRange", 0f, 100f));
            rows.Add(ThingFloatParamRow(CharEditorDefEditorCompat.Label("MIN") + "Range".Translate(), "P43_weaponMinRange", 0f, 50f));
            // Private VerbProperties.forcedMissRadius field; read via ReadObjectParam.
            rows.Add(ThingFloatParamRow(CharEditorDefEditorCompat.Label("SPRAYING"), "P44_forcedMissRadius", 0f, 100f));
            rows.Add(ThingFloatParamRow("CooldownTime".Translate().ToString(), "P45_defaultCooldownTime", 0f, 100f));
            rows.Add(ThingFloatParamRow(CharEditorDefEditorCompat.Label("WARMUP"), "P46_warmupTime", 0f, 30f));
            rows.Add(ThingFloatParamRow("Base".Translate() + " " + StatDefOf.AccuracyTouch.label, "P47_baseAccuracyTouch", 0f, 100f));
            rows.Add(ThingFloatParamRow("Base".Translate() + " " + StatDefOf.AccuracyShort.label, "P48_baseAccuracyShort", 0f, 100f));
            rows.Add(ThingFloatParamRow("Base".Translate() + " " + StatDefOf.AccuracyMedium.label, "P49_baseAccuracyMedium", 0f, 100f));
            rows.Add(ThingFloatParamRow("Base".Translate() + " " + StatDefOf.AccuracyLong.label, "P50_baseAccuracyLong", 0f, 100f));
            rows.Add(ThingBoolParamRow(CharEditorDefEditorCompat.Label("REQUIRELINEOFSIGHT"), "P52_requireLineOfSight"));
            if (thing.Verbs[0].targetParams != null)
            {
                rows.Add(ThingBoolParamRow(CharEditorDefEditorCompat.Label("TARGETGROUND"), "P53_targetGround"));
            }
            rows.Add(ThingFloatParamRow(CharEditorDefEditorCompat.Label("BEAMSTREUUNG"), "P54_beamWidth", 0f, 10f));
            rows.Add(ThingFloatParamRow(CharEditorDefEditorCompat.Label("BEAMFULLWIDTHRANGE"), "P55_beamFullWidthRange", 0f, 10f));
            rows.Add(ThingDefComboRow(CharEditorDefEditorCompat.Label("BEAMDAMAGEDEF"), "P56_beamDamageDef",
                CharEditorDefEditorCompat.ThingAllDamageDefs, d => d != null ? d.LabelCap.ToString() : "RimWorldAccess.CharEd.Browser.AllOption".Translate().ToString(), allowNull: true));
            rows.Add(ThingFloatParamRow(CharEditorDefEditorCompat.Label("CONSUMEFUELPERSHOT"), "P57_consumeFuelPerShot", 0f, 100f));
            rows.Add(ThingFloatParamRow(CharEditorDefEditorCompat.Label("CONSUMEFUELPERBURST"), "P58_consumeFuelPerBurst", 0f, 100f));
            rows.Add(ThingDefComboRow(CharEditorDefEditorCompat.Label("SOUNDCAST"), "P61_soundCast",
                CharEditorDefEditorCompat.ThingAllGunShotSounds, d => d != null ? d.defName : "RimWorldAccess.CharEd.Browser.AllOption".Translate().ToString(), allowNull: true));
            rows.Add(ThingDefComboRow(CharEditorDefEditorCompat.Label("SOUNDAIMING"), "P62_soundAiming",
                CharEditorDefEditorCompat.ThingAllGunRelatedSounds, d => d != null ? d.defName : "RimWorldAccess.CharEd.Browser.AllOption".Translate().ToString(), allowNull: true));
            rows.Add(ThingDefComboRow(CharEditorDefEditorCompat.Label("SOUNDCASTBEAM"), "P63_soundCastBeam",
                CharEditorDefEditorCompat.ThingAllGunRelatedSounds, d => d != null ? d.defName : "RimWorldAccess.CharEd.Browser.AllOption".Translate().ToString(), allowNull: true));
            rows.Add(ThingDefComboRow(CharEditorDefEditorCompat.Label("SOUNDCASTTAIL"), "P64_soundCastTail",
                CharEditorDefEditorCompat.ThingAllGunRelatedSounds, d => d != null ? d.defName : "RimWorldAccess.CharEd.Browser.AllOption".Translate().ToString(), allowNull: true));
            rows.Add(ThingDefComboRow(CharEditorDefEditorCompat.Label("SOUNDLANDING"), "P65_soundLanding",
                CharEditorDefEditorCompat.ThingAllGunRelatedSounds, d => d != null ? d.defName : "RimWorldAccess.CharEd.Browser.AllOption".Translate().ToString(), allowNull: true));

            if (ThingIsTurretApprox())
            {
                rows.Add(ThingFloatParamRow(CharEditorDefEditorCompat.Label("WARMUP"), "P66_turretWarmupTimeMin", 0f, 30f));
                rows.Add(ThingFloatParamRow(CharEditorDefEditorCompat.Label("WARMUPMAX"), "P67_turretWarmupTimeMax", 0f, 30f));
                rows.Add(ThingFloatParamRow(StatDefOf.RangedWeapon_Cooldown.LabelCap.ToString(), "P68_turretBurstCooldownTime", 0f, 30f));
                // Gated on turret status alone: a non-mortar turret's edit is a harmless no-op,
                // FromDictionary applying it only when its own isMortar check passes.
                rows.Add(ThingFloatParamRow(CharEditorDefEditorCompat.Label("SPRAYING") + CharEditorDefEditorCompat.Label("CLASSICMORTAR"), "P51_forcedMissRadiusMortar", 0f, 10f));
            }
            if (CharEditorDefEditorCompat.IsCombatExtendedActive() || ThingIsTurretApprox())
            {
                // CE-specific; the mod's own capture and FromDictionary do the comp work.
                rows.Add(ThingIntParamRow(CharEditorDefEditorCompat.Label("MAGAZIN"), "P71_CEfuelCapacity", 0, 500));
            }
            if (CharEditorDefEditorCompat.IsCombatExtendedActive())
            {
                rows.Add(ThingFloatParamRow(CharEditorDefEditorCompat.Label("RELOADTIME"), "P72_CEreloadTime", 0f, 30f));
            }
            return rows;
        }

        // Scalar row factories.

        private DefRow IntRow(string label, Func<int> get, Action<int> set, int min, int max)
        {
            return new DefRow
            {
                Label = label,
                Kind = RowKind.IntStepper,
                RenderValue = () => get().ToString(),
                Adjust = dir => set(Mathf.Clamp(get() + dir, min, max)),
                Activate = () => CharEdNumericEntry.OpenInt(editSession, label, get(), min, max, set,
                    RefreshAndAnnounce, spec: IntSpec),
            };
        }

        private DefRow FloatRow(string label, Func<float> get, Action<float> set, float min, float max)
        {
            return new DefRow
            {
                Label = label,
                Kind = RowKind.FloatStepper,
                RenderValue = () => F2(get()),
                Adjust = dir => set(Mathf.Clamp(get() + dir * StepFor(min, max), min, max)),
                Activate = () => CharEdNumericEntry.OpenFloat(editSession, label, F2(get()), FloatSpec,
                    min, max, set, RefreshAndAnnounce),
            };
        }

        private DefRow NullableFloatRow(string label, Func<float?> get, Action<float?> set, float min, float max)
        {
            return new DefRow
            {
                Label = label,
                Kind = RowKind.FloatStepper,
                RenderValue = () => get().HasValue ? F2(get().Value) : "RimWorldAccess.CharEd.DefEditor.NotSet".Translate().ToString(),
                Adjust = dir => set(Mathf.Clamp((get() ?? 1f) + dir * StepFor(min, max), min, max)),
                // An unset field opens on an empty buffer, never on a stand-in figure.
                Activate = () => CharEdNumericEntry.OpenFloat(editSession, label,
                    get().HasValue ? F2(get().Value) : "", FloatSpec, min, max, v => set(v), RefreshAndAnnounce),
            };
        }

        private DefRow BoolRow(string label, Func<bool> get, Action<bool> set)
        {
            return new DefRow
            {
                Label = label,
                Kind = RowKind.Bool,
                GetBool = get,
                Activate = () => set(!get()),
            };
        }

        // Param-vehicle row factories. Every row reads through a fresh mod-side capture, so it
        // always matches what the mod's own FromDictionary would parse back, and writes by capturing
        // the def into a fresh preset, overwriting one dictionary entry, invoking FromDictionary and
        // reporting its bool. A false return is spoken and the value is left as FromDictionary left
        // it, never assumed.

        private void SpeakWriteFailed()
        {
            SoundDefOf.ClickReject.PlayOneShotOnCamera();
            TolkHelper.SpeakData("RimWorldAccess.CharEd.DefEditor.WriteFailed".Translate().ToString());
        }

        private static int ParseIntParam(string s) { int.TryParse(s, out int v); return v; }
        private static float ParseFloatParam(string s) { float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v); return v; }
        /// <summary>Mirrors the mod's own AsBool: writes always use bool.ToString(), which it accepts alongside "1".</summary>
        private static bool ParseBoolParam(string s) => s == "True" || s == "1";

        private DefRow ThingIntParamRow(string label, string paramName, int min, int max)
        {
            Func<int> get = () => ParseIntParam(CharEditorDefEditorCompat.ReadObjectParam(thing, paramName));
            Action<int> set = v =>
            {
                if (!CharEditorDefEditorCompat.ApplyObjectParam(thing, paramName, Mathf.Clamp(v, min, max).ToString(CultureInfo.InvariantCulture)))
                    SpeakWriteFailed();
            };
            return new DefRow
            {
                Label = label,
                Kind = RowKind.IntStepper,
                RenderValue = () => get().ToString(),
                Adjust = dir => set(get() + dir),
                Activate = () => CharEdNumericEntry.OpenInt(editSession, label, get(), min, max, set,
                    RefreshAndAnnounce, spec: IntSpec),
            };
        }

        private DefRow ThingFloatParamRow(string label, string paramName, float min, float max)
        {
            Func<float> get = () => ParseFloatParam(CharEditorDefEditorCompat.ReadObjectParam(thing, paramName));
            Action<float> set = v =>
            {
                if (!CharEditorDefEditorCompat.ApplyObjectParam(thing, paramName, Mathf.Clamp(v, min, max).ToString(CultureInfo.InvariantCulture)))
                    SpeakWriteFailed();
            };
            return new DefRow
            {
                Label = label,
                Kind = RowKind.FloatStepper,
                RenderValue = () => F2(get()),
                Adjust = dir => set(get() + dir * StepFor(min, max)),
                Activate = () => CharEdNumericEntry.OpenFloat(editSession, label, F2(get()), FloatSpec,
                    min, max, set, RefreshAndAnnounce),
            };
        }

        private DefRow ThingBoolParamRow(string label, string paramName)
        {
            Func<bool> get = () => ParseBoolParam(CharEditorDefEditorCompat.ReadObjectParam(thing, paramName));
            return new DefRow
            {
                Label = label,
                Kind = RowKind.Bool,
                GetBool = get,
                Activate = () =>
                {
                    if (!CharEditorDefEditorCompat.ApplyObjectParam(thing, paramName, (!get()).ToString()))
                        SpeakWriteFailed();
                },
            };
        }

        private DefRow ThingTextParamRow(string label, string paramName)
        {
            Func<string> get = () => CharEditorDefEditorCompat.ReadObjectParam(thing, paramName);
            return new DefRow
            {
                Label = label,
                Kind = RowKind.TextField,
                RenderValue = get,
                Activate = () => BeginExactEntry(label, get(), FreeTextSpec,
                    s => { if (!CharEditorDefEditorCompat.ApplyObjectParam(thing, paramName, s)) SpeakWriteFailed(); }),
            };
        }

        private DefRow ThingDefComboRow<T>(string label, string paramName, Func<HashSet<T>> candidatesFn, Func<T, string> labelFn, bool allowNull) where T : Def
        {
            Func<string> get = () =>
            {
                string defName = CharEditorDefEditorCompat.ReadObjectParam(thing, paramName);
                if (defName.NullOrEmpty()) return labelFn(null);
                T d = DefDatabase<T>.GetNamedSilentFail(defName);
                return d != null ? labelFn(d) : defName;
            };
            return new DefRow
            {
                Label = label,
                Kind = RowKind.Combo,
                RenderValue = get,
                Activate = () => OpenDefCombo((candidatesFn() ?? new HashSet<T>()).ToList(), labelFn,
                    d => { if (!CharEditorDefEditorCompat.ApplyObjectParam(thing, paramName, d?.defName ?? "")) SpeakWriteFailed(); }, label, allowNull),
            };
        }

        private DefRow GeneIntParamRow(string label, string paramName, int min, int max)
        {
            Func<int> get = () => ParseIntParam(CharEditorDefEditorCompat.ReadGeneParam(gene, paramName));
            Action<int> set = v =>
            {
                if (!CharEditorDefEditorCompat.ApplyGeneParam(gene, paramName, Mathf.Clamp(v, min, max).ToString(CultureInfo.InvariantCulture)))
                    SpeakWriteFailed();
            };
            return new DefRow
            {
                Label = label,
                Kind = RowKind.IntStepper,
                RenderValue = () => get().ToString(),
                Adjust = dir => set(get() + dir),
                Activate = () => CharEdNumericEntry.OpenInt(editSession, label, get(), min, max, set,
                    RefreshAndAnnounce, spec: IntSpec),
            };
        }

        private DefRow GeneFloatParamRow(string label, string paramName, float min, float max)
        {
            Func<float> get = () => ParseFloatParam(CharEditorDefEditorCompat.ReadGeneParam(gene, paramName));
            Action<float> set = v =>
            {
                if (!CharEditorDefEditorCompat.ApplyGeneParam(gene, paramName, Mathf.Clamp(v, min, max).ToString(CultureInfo.InvariantCulture)))
                    SpeakWriteFailed();
            };
            return new DefRow
            {
                Label = label,
                Kind = RowKind.FloatStepper,
                RenderValue = () => F2(get()),
                Adjust = dir => set(get() + dir * StepFor(min, max)),
                Activate = () => CharEdNumericEntry.OpenFloat(editSession, label, F2(get()), FloatSpec,
                    min, max, set, RefreshAndAnnounce),
            };
        }

        private DefRow GeneBoolParamRow(string label, string paramName)
        {
            Func<bool> get = () => ParseBoolParam(CharEditorDefEditorCompat.ReadGeneParam(gene, paramName));
            return new DefRow
            {
                Label = label,
                Kind = RowKind.Bool,
                GetBool = get,
                Activate = () =>
                {
                    if (!CharEditorDefEditorCompat.ApplyGeneParam(gene, paramName, (!get()).ToString()))
                        SpeakWriteFailed();
                },
            };
        }

        private DefRow GeneTextParamRow(string label, string paramName)
        {
            Func<string> get = () => CharEditorDefEditorCompat.ReadGeneParam(gene, paramName);
            return new DefRow
            {
                Label = label,
                Kind = RowKind.TextField,
                RenderValue = get,
                Activate = () => BeginExactEntry(label, get(), FreeTextSpec,
                    s => { if (!CharEditorDefEditorCompat.ApplyGeneParam(gene, paramName, s)) SpeakWriteFailed(); }),
            };
        }

        private DefRow GeneDefComboRow<T>(string label, string paramName, Func<List<T>> candidatesFn, Func<T, string> labelFn, bool allowNull) where T : Def
        {
            Func<string> get = () =>
            {
                string defName = CharEditorDefEditorCompat.ReadGeneParam(gene, paramName);
                if (defName.NullOrEmpty()) return labelFn(null);
                T d = DefDatabase<T>.GetNamedSilentFail(defName);
                return d != null ? labelFn(d) : defName;
            };
            return new DefRow
            {
                Label = label,
                Kind = RowKind.Combo,
                RenderValue = get,
                Activate = () => OpenDefCombo(candidatesFn() ?? new List<T>(), labelFn,
                    d => { if (!CharEditorDefEditorCompat.ApplyGeneParam(gene, paramName, d?.defName ?? "")) SpeakWriteFailed(); }, label, allowNull),
            };
        }

        // Pipe-list param helpers: "|" is the mod's own separator for every plain string, def-name
        // and float list, one element per entry with the trailing pipe trimmed.

        private static List<string> SplitPipeList(string s)
        {
            var list = new List<string>();
            if (s.NullOrEmpty()) return list;
            foreach (string part in s.Split('|'))
                if (!part.NullOrEmpty()) list.Add(part);
            return list;
        }

        private static string JoinPipeList(List<string> list) => string.Join("|", list.ToArray());

        private static List<float> SplitPipeFloats(string s)
        {
            var list = new List<float>();
            if (s.NullOrEmpty()) return list;
            foreach (string part in s.Split('|'))
                if (!part.NullOrEmpty() && float.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) list.Add(v);
            return list;
        }

        private static string JoinPipeFloats(List<float> list)
        {
            var parts = new string[list.Count];
            for (int i = 0; i < list.Count; i++) parts[i] = list[i].ToString(CultureInfo.InvariantCulture);
            return string.Join("|", parts);
        }

        private static HashSet<string> HarvestThingTagValues(Func<ThingDef, List<string>> selector)
        {
            var set = new HashSet<string>();
            foreach (ThingDef d in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                List<string> vals = selector(d);
                if (vals == null) continue;
                foreach (string v in vals) if (!v.NullOrEmpty()) set.Add(v);
            }
            return set;
        }

        private static HashSet<string> HarvestGeneTagValues(Func<GeneDef, List<string>> selector)
        {
            var set = new HashSet<string>();
            foreach (GeneDef d in DefDatabase<GeneDef>.AllDefsListForReading)
            {
                List<string> vals = selector(d);
                if (vals == null) continue;
                foreach (string v in vals) if (!v.NullOrEmpty()) set.Add(v);
            }
            return set;
        }

        /// <summary>Section for a plain pipe-joined string-list param: one MembershipElement row per current entry, one AddAction row offering harvested candidates plus free-text entry.</summary>
        private List<DefRow> BuildStringListParamRows(Func<string> readCsv, Func<string, bool> applyCsv, Func<HashSet<string>> candidatesFn, string addLabel)
        {
            var rows = new List<DefRow>();
            foreach (string s in SplitPipeList(readCsv()))
            {
                string captured = s;
                rows.Add(new DefRow
                {
                    Label = captured,
                    Kind = RowKind.MembershipElement,
                    RemoveSelf = () =>
                    {
                        List<string> list = SplitPipeList(readCsv());
                        list.RemoveAll(x => x == captured);
                        if (!applyCsv(JoinPipeList(list))) SpeakWriteFailed();
                    },
                });
            }
            rows.Add(new DefRow
            {
                Label = addLabel,
                Kind = RowKind.AddAction,
                Activate = () => OpenStringAddMenu(candidatesFn?.Invoke(), s =>
                {
                    List<string> list = SplitPipeList(readCsv());
                    if (!list.Contains(s)) list.Add(s);
                    if (!applyCsv(JoinPipeList(list))) SpeakWriteFailed();
                }, addLabel),
            });
            return rows;
        }

        /// <summary>Free-value add menu for string-list params: a leading "enter new tag" text-entry option, then the harvested candidate values, matching <see cref="OpenAddMenu{T}"/>'s rebuild sequence.</summary>
        private void OpenStringAddMenu(HashSet<string> candidates, Action<string> onAdd, string title)
        {
            InspectionTreeItem currentItem = CurrentTreeItem();
            InspectionTreeItem sectionItem = currentItem != null && currentItem.Data is DefRow ? currentItem.Parent : currentItem;
            Action<string> addAndRebuild = s =>
            {
                if (s.NullOrEmpty()) return;
                onAdd(s);
                RebuildSection(sectionItem);
                RefreshModel();
                SyncRegionFromCurrentTree();
                AnnounceCurrentItem();
            };
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("RimWorldAccess.CharEd.DefEditor.EnterNewTag".Translate().ToString(), delegate
                {
                    editSession.EnterEdit("", FreeTextSpec, title, addAndRebuild, onExit: RefreshAndAnnounce);
                }),
            };
            if (candidates != null)
            {
                foreach (string c in candidates)
                {
                    string captured = c;
                    options.Add(new FloatMenuOption(c, delegate { addAndRebuild(captured); }));
                }
            }
            WindowlessFloatMenuState.OpenTitled(title, options);
        }
    }
}
