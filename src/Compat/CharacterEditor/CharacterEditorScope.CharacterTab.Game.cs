using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    internal sealed partial class CharacterEditorScope
    {
        // Character section: Name, Age, Backstory, Incapable of, Traits, Skills. Only what the
        // edited pawn supports is built, gated exactly as the mod's own draw guards are.

        /// <summary>Digit bound for the mod's own age fields. Neither clamps an upper value on this path; only the step buttons do.</summary>
        private const int BiologicalAgeDigits = 4;
        private const int ChronologicalAgeDigits = 5;

        private void BuildCharacterSection(InspectionTreeItem section, Pawn pawn)
        {
            BuildNameSubsection(section, pawn);
            BuildAgeSubsection(section, pawn);
            if (pawn.story != null)
            {
                BuildBackstorySubsection(section, pawn);
                BuildIncapableSubsection(section, pawn);
                BuildTraitsSubsection(section, pawn);
            }
            if (pawn.skills != null)
            {
                BuildSkillsSubsection(section, pawn);
            }
            if (pawn.abilities != null)
            {
                BuildAbilitiesSubsection(section, pawn);
            }
            if (pawn.HasPsylink)
            {
                BuildPsycastsSubsection(section, pawn);
            }
            BuildIdentitySubsection(section, pawn);
            // The mod's own training gate. Humanlikes never satisfy RaceProps.Animal, so this
            // cannot collide with the subsections above.
            if (pawn.RaceProps.Animal && pawn.Faction != null && pawn.training != null)
            {
                BuildTrainingSubsection(section, pawn);
            }
        }

        private static InspectionTreeItem AddSubsection(InspectionTreeItem parent, string label)
        {
            var sub = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.SubCategory,
                Label = label,
                IndentLevel = parent.IndentLevel + 1,
                IsExpandable = true,
                IsExpanded = false,
                AutoExpandForSearch = true,
                Parent = parent,
            };
            parent.Children.Add(sub);
            return sub;
        }

        // ---- Name. ----

        private void BuildNameSubsection(InspectionTreeItem section, Pawn pawn)
        {
            InspectionTreeItem sub = AddSubsection(section, "RimWorldAccess.CharEd.Character.NameSection".Translate());
            if (pawn.Name is NameTriple triple)
            {
                AddRow(sub, CharRowKind.NameFirst, "FirstName".Translate().CapitalizeFirst());
                AddRow(sub, CharRowKind.NameNick, "NickName".Translate().CapitalizeFirst());
                AddRow(sub, CharRowKind.NameLast, "LastName".Translate().CapitalizeFirst());
            }
            else
            {
                AddRow(sub, CharRowKind.NameSingle, "RimWorldAccess.CharEd.Character.NameSection".Translate());
            }
            AddRow(sub, CharRowKind.NameRandomize, "RimWorldAccess.CharEd.Character.RandomizeName".Translate());
            AddRow(sub, CharRowKind.NameRandomizeFull, "RimWorldAccess.CharEd.Character.RandomizeNameFull".Translate());
            // The mod draws its race button here beside name and age, not in the Identity strip.
            AddRow(sub, CharRowKind.NameChangeRace, "RimWorldAccess.CharEd.Character.ChangeRace".Translate());

            RoyalTitle title = CurrentRoyalTitle(pawn);
            if (title != null)
            {
                AddRow(sub, CharRowKind.NameRoyalTitle, title.def.GetLabelCapFor(pawn));
            }
        }

        /// <summary>The pawn's most senior royal title, as the mod's own name block reads it.</summary>
        private static RoyalTitle CurrentRoyalTitle(Pawn pawn)
        {
            if (pawn?.royalty == null)
            {
                return null;
            }
            var titles = pawn.royalty.AllTitlesForReading;
            return titles != null && titles.Count > 0 ? titles[0] : null;
        }

        private void BeginNameFieldEdit(Pawn pawn, bool isTriple, int component)
        {
            if (pawn == null)
            {
                return;
            }
            string current;
            string label;
            if (isTriple)
            {
                var name = pawn.Name as NameTriple ?? new NameTriple("", "", "");
                current = component == 0 ? name.First : component == 1 ? name.Nick : name.Last;
                label = (component == 0 ? "FirstName".Translate() : component == 1 ? "NickName".Translate() : "LastName".Translate())
                    .ToString().CapitalizeFirst();
            }
            else
            {
                current = (pawn.Name as NameSingle)?.Name ?? "";
                label = "RimWorldAccess.CharEd.Character.NameSection".Translate().ToString();
            }
            var spec = new TextFieldSpec(labelKey: null, maxLength: isTriple ? 17 : 64, minLength: 0,
                allowedChars: CharacterCardUtility.ValidNameRegex);
            editSession.EnterEdit(current, spec, label,
                apply: value => ApplyNameComponent(pawn, isTriple, component, value),
                onExit: () => AnnounceNameChanged(pawn));
        }

        /// <summary>
        /// MUTATION-C: mirrors BlockBio.DrawNameTripe/DrawNameSingle's own commit
        /// (CEditor.cs:1706-1775) -- the mod has no discrete "commit name" method; its timed
        /// inline field reassigns <c>Pawn.Name</c> directly the instant any component differs,
        /// under the same <c>CharacterCardUtility.ValidNameRegex</c>/length validation the
        /// vanilla <c>Widgets.TextField</c> call enforces inline. This session-based commit is
        /// the accessible equivalent: one write on session exit instead of one write per
        /// differing draw frame.
        /// </summary>
        private static void ApplyNameComponent(Pawn pawn, bool isTriple, int component, string value)
        {
            if (isTriple)
            {
                var name = pawn.Name as NameTriple ?? new NameTriple("", "", "");
                string first = component == 0 ? value : name.First;
                string nick = component == 1 ? value : name.Nick;
                string last = component == 2 ? value : name.Last;
                pawn.Name = new NameTriple(first ?? "", nick ?? "", last ?? "");
            }
            else
            {
                pawn.Name = new NameSingle(value ?? "");
            }
        }

        private void AnnounceNameChanged(Pawn pawn)
        {
            RefreshModel();
            string full = pawn?.Name != null ? pawn.Name.ToStringFull : (pawn?.LabelShortCap ?? "");
            TolkHelper.SpeakData("RimWorldAccess.CharEd.Character.NameChanged".Translate(full).ToString());
        }

        // ---- Age. ----

        private void BuildAgeSubsection(InspectionTreeItem section, Pawn pawn)
        {
            if (pawn.ageTracker == null)
            {
                return;
            }
            InspectionTreeItem sub = AddSubsection(section, "RimWorldAccess.CharEd.Character.AgeSection".Translate());
            AddRow(sub, CharRowKind.AgeBiological, "RimWorldAccess.CharEd.Character.AgeBiological".Translate());
            AddRow(sub, CharRowKind.AgeChronological, "RimWorldAccess.CharEd.Character.AgeChronological".Translate());
            AddRow(sub, CharRowKind.AgeBirthday, "RimWorldAccess.CharEd.Character.Birthday".Translate());
        }

        private void OpenExactAgeEntry(Pawn pawn, bool isChrono)
        {
            if (pawn?.ageTracker == null)
            {
                return;
            }
            int current = isChrono ? pawn.ageTracker.AgeChronologicalYears : pawn.ageTracker.AgeBiologicalYears;
            string label = (isChrono
                ? "RimWorldAccess.CharEd.Character.AgeChronological"
                : "RimWorldAccess.CharEd.Character.AgeBiological").Translate();
            // Digit cap comes from the mod's own field: this path has no upper range.
            var spec = new TextFieldSpec(labelKey: null,
                maxLength: isChrono ? ChronologicalAgeDigits : BiologicalAgeDigits,
                minLength: 0, allowedChars: CharEdNumericEntry.DigitsOnly);
            CharEdNumericEntry.OpenInt(editSession, label, current, 1, int.MaxValue,
                years => ApplyExactAge(pawn, isChrono, years),
                onExit: () => { RefreshModel(); AnnounceCurrentItem(); },
                policy: CharEdNumericEntry.OutOfRange.Drop, spec: spec);
        }

        /// <summary>
        /// MUTATION-C: mirrors BlockBio.DrawBiologicalAge/DrawChronologicalAge's own numeric-field
        /// commit (CEditor.cs:1618-1657 -- `if (int.TryParse(...) &amp;&amp; iAge != result &amp;&amp; result &gt; 0)`).
        /// A non-positive or unparseable value is a no-op (the Drop policy on a floor of 1), exactly
        /// matching the mod's own `result &gt; 0` guard -- not clamped to zero. Deliberately NO upper
        /// clamp here either: the mod's exact-entry path has none (only its step buttons do -- see
        /// <see cref="CharEditorCompat.StepBiologicalAge"/>/<see cref="CharEditorCompat.StepChronologicalAge"/>),
        /// a mod quirk preserved for parity.
        /// </summary>
        private static void ApplyExactAge(Pawn pawn, bool isChrono, int years)
        {
            if (isChrono)
            {
                CharEditorCompat.SetChronologicalAge(pawn, years);
            }
            else
            {
                CharEditorCompat.SetBiologicalAge(pawn, years);
            }
        }

        // ---- Backstory. ----

        private void BuildBackstorySubsection(InspectionTreeItem section, Pawn pawn)
        {
            InspectionTreeItem sub = AddSubsection(section, "RimWorldAccess.CharEd.Character.BackstorySection".Translate());
            AddRow(sub, CharRowKind.BackstoryChildhood, "Childhood".Translate());
            AddRow(sub, CharRowKind.BackstoryRandomChildhood, "RimWorldAccess.CharEd.Character.RandomizeChildhood".Translate());
            AddRow(sub, CharRowKind.BackstoryAdulthood, "Adulthood".Translate());
            AddRow(sub, CharRowKind.BackstoryRandomAdulthood, "RimWorldAccess.CharEd.Character.RandomizeAdulthood".Translate());
        }

        private static string BackstoryValueLabel(Pawn pawn, bool isChildhood)
        {
            BackstoryDef def = pawn?.story == null ? null : (isChildhood ? pawn.story.Childhood : pawn.story.Adulthood);
            return def != null ? def.TitleCapFor(pawn.gender) : "None".Translate().ToString();
        }

        /// <summary>Names the new backstory after a reroll: the sibling ComboBox row's fresh state, not the dice row under the cursor.</summary>
        private void AnnounceBackstoryChanged(InspectionTreeItem diceRow, bool isChildhood)
        {
            RefreshModel();
            InspectionTreeItem comboRow = FindSiblingCharRow(diceRow, isChildhood ? CharRowKind.BackstoryChildhood : CharRowKind.BackstoryAdulthood);
            if (comboRow?.Data is RegionRow<CharRowKind> row)
            {
                var d = new ElementDescription();
                DescribeCharRow(row, d);
                TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(d, TranslatedShellVocabulary.Instance));
            }
            else
            {
                AnnounceCurrentItem();
            }
        }

        private static InspectionTreeItem FindSiblingCharRow(InspectionTreeItem item, CharRowKind kind)
        {
            if (item?.Parent == null)
            {
                return null;
            }
            foreach (InspectionTreeItem child in item.Parent.Children)
            {
                if (child.Data is RegionRow<CharRowKind> row && row.Kind == kind)
                {
                    return child;
                }
            }
            return null;
        }

        // ---- Incapable of. ----

        private static void BuildIncapableSubsection(InspectionTreeItem section, Pawn pawn)
        {
            InspectionTreeItem sub = AddSubsection(section, "IncapableOf".Translate(pawn).ToString());
            WorkTags disabled = pawn.CombinedDisabledWorkTags;
            if (disabled == WorkTags.None)
            {
                InspectNodeFactory.DetailLine(sub, "None".Translate());
                return;
            }
            foreach (WorkTags tag in disabled.GetAllSelectedItems<WorkTags>())
            {
                if (tag == WorkTags.None)
                {
                    continue;
                }
                InspectionTreeItem row = AddRow(sub, CharRowKind.IncapableEntry, tag.LabelTranslated().CapitalizeFirst(), tag);
                row.Description = IncapableCause(pawn, tag);
            }
        }

        /// <summary>
        /// The character card's own per-tag cause text — the exact tooltip a sighted player
        /// reads — from vanilla's private <c>GetWorkTypeDisabledCausedBy</c>.
        /// </summary>
        private static string IncapableCause(Pawn pawn, WorkTags tag)
        {
            try
            {
                var method = VanillaAccess.GetMethod(typeof(CharacterCardUtility), "GetWorkTypeDisabledCausedBy");
                return method?.Invoke(null, new object[] { pawn, tag }) as string;
            }
            catch
            {
                return null;
            }
        }

        // ---- Traits. ----

        /// <summary>
        /// The pawn's traits and abilities as they stood when the Add dialog opened, and the
        /// pawn they belonged to; non-null only while that dialog is open. Reference identity is
        /// what matters: the mod builds a fresh object for an add and an in-place replace alike.
        /// </summary>
        private List<Trait> traitsBeforeAddDialog;
        private List<Ability> abilitiesBeforeAddDialog;
        private Pawn objectsBeforeAddDialogPawn;

        private void BuildTraitsSubsection(InspectionTreeItem section, Pawn pawn)
        {
            InspectionTreeItem sub = AddSubsection(section, "Traits".Translate());
            sub.Data = GroupSummaryKind.Traits;
            PopulateTraitsRows(sub, pawn);
        }

        private void PopulateTraitsRows(InspectionTreeItem sub, Pawn pawn)
        {
            AddRow(sub, CharRowKind.TraitAdd, "RimWorldAccess.CharEd.Character.AddTrait".Translate());
            AddRow(sub, CharRowKind.TraitCopy, "Copy".Translate());
            AddRow(sub, CharRowKind.TraitPaste, "Paste".Translate());
            if (CharEditorCompat.CreationMode)
            {
                AddRow(sub, CharRowKind.TraitRandomize, "Randomize".Translate());
            }
            if (pawn?.story?.traits != null)
            {
                foreach (Trait trait in pawn.story.traits.allTraits)
                {
                    AddRow(sub, CharRowKind.TraitEntry, trait.LabelCap, trait);
                }
            }
        }

        private void RememberTraitsBeforeAddDialog()
        {
            Pawn pawn = CharEditorCompat.CurrentPawn;
            objectsBeforeAddDialogPawn = pawn;
            abilitiesBeforeAddDialog = null;
            traitsBeforeAddDialog = pawn?.story?.traits?.allTraits == null
                ? null
                : new List<Trait>(pawn.story.traits.allTraits);
        }

        private void RememberAbilitiesBeforeAddDialog()
        {
            Pawn pawn = CharEditorCompat.CurrentPawn;
            objectsBeforeAddDialogPawn = pawn;
            traitsBeforeAddDialog = null;
            abilitiesBeforeAddDialog = pawn?.abilities?.abilities == null
                ? null
                : new List<Ability>(pawn.abilities.abilities);
        }

        /// <summary>
        /// Rebuilds the Traits subsection after a structural change, since any Trait reference
        /// held in a row payload may now be stale. The cursor keeps its flattened position rather
        /// than chasing "the same" trait: exact for a replace, close enough for the rest.
        /// </summary>
        private void RebuildTraitsAfterMutation(InspectionTreeItem anyTraitsRowItem)
        {
            InspectionTreeItem sub = anyTraitsRowItem?.Parent;
            if (sub == null)
            {
                return;
            }
            Pawn pawn = CharEditorCompat.CurrentPawn;
            int cursorIndex = IndexOfVisible(Tree.Visible, anyTraitsRowItem);
            sub.Children.Clear();
            PopulateTraitsRows(sub, pawn);
            Tree.Reflatten();
            ResetBoundaryTracking();
            RefreshModel();
            if (Tree.Visible.Count == 0)
            {
                return;
            }
            int clamped = Math.Min(Math.Max(cursorIndex, 0), Tree.Visible.Count - 1);
            Tree.SetSelectedIndex(clamped);
            SyncRegionFromCurrentTree();
            InspectionTreeItem landed = Tree.Visible[clamped];
            if (landed.Data is RegionRow<CharRowKind> row)
            {
                var d = new ElementDescription();
                DescribeCharRow(row, d);
                TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(d, TranslatedShellVocabulary.Instance));
            }
            else
            {
                AnnounceCurrentItem();
            }
        }

        // ---- Skills. ----

        private void BuildSkillsSubsection(InspectionTreeItem section, Pawn pawn)
        {
            InspectionTreeItem sub = AddSubsection(section, "Skills".Translate());
            sub.Data = GroupSummaryKind.Skills;
            AddRow(sub, CharRowKind.SkillCopy, "Copy".Translate());
            AddRow(sub, CharRowKind.SkillPaste, "Paste".Translate());
            if (CharEditorCompat.CreationMode)
            {
                AddRow(sub, CharRowKind.SkillRandomize, "Randomize".Translate());
            }
            // Vanilla SkillUI display order; pawn.skills.skills is not guaranteed to be in it.
            foreach (SkillRecord skill in pawn.skills.skills.OrderByDescending(s => s.def.listOrder))
            {
                AddRow(sub, CharRowKind.SkillEntry, skill.def.LabelCap.ToString(), skill);
            }
        }

        private static void DescribeSkillEntry(SkillRecord skill, Pawn pawn, ElementDescription d)
        {
            if (skill == null)
            {
                return;
            }
            d.Label = skill.def.LabelCap.ToString();
            if (skill.TotallyDisabled)
            {
                d.ReadOnly = true;
                d.Value = "RimWorldAccess.CharEd.Character.SkillDisabled".Translate().ToString();
                return;
            }
            d.Role = ElementRole.Stepper;
            d.Value = skill.levelInt.ToString();
            // No AtMinimum/AtMaximum: the mod's own plus/minus handlers are unbounded.

            var extras = new List<string>();
            string passionLabel = PawnSkillsTableHelper.PassionLabel(skill.passion);
            if (!passionLabel.NullOrEmpty())
            {
                extras.Add(passionLabel);
            }
            int offset = ComputeBackgroundOffset(pawn, skill);
            if (offset != 0)
            {
                extras.Add((offset > 0
                    ? "RimWorldAccess.CharEd.Character.PlusFromBackground"
                    : "RimWorldAccess.CharEd.Character.MinusFromBackground").Translate(Math.Abs(offset)).ToString());
            }
            if (extras.Count > 0)
            {
                d.Extras = string.Join(". ", extras.ToArray());
            }
        }

        /// <summary>
        /// The colored background bar's own formula: adulthood plus childhood backstory gain,
        /// trait offset, aptitude. The backstory and aptitude terms come straight off public
        /// vanilla data; the trait term rides
        /// <see cref="CharEditorCompat.TraitOffsetForSkill"/> because the mod's own arithmetic
        /// there is not independently confirmed.
        /// </summary>
        private static int ComputeBackgroundOffset(Pawn pawn, SkillRecord skill)
        {
            if (pawn?.story == null)
            {
                return 0;
            }
            int adult = SkillGainFor(pawn.story.GetBackstory(BackstorySlot.Adulthood), skill.def);
            int child = SkillGainFor(pawn.story.GetBackstory(BackstorySlot.Childhood), skill.def);
            int traitOffset = CharEditorCompat.TraitOffsetForSkill(pawn, skill.def);
            int aptitude = skill.Aptitude;
            return adult + child + traitOffset + aptitude;
        }

        private static int SkillGainFor(BackstoryDef def, SkillDef skillDef)
        {
            if (def == null)
            {
                return 0;
            }
            foreach (SkillGain gain in def.skillGains)
            {
                if (gain.skill == skillDef)
                {
                    return gain.amount;
                }
            }
            return 0;
        }

        // ---- Abilities. ----

        private void BuildAbilitiesSubsection(InspectionTreeItem section, Pawn pawn)
        {
            InspectionTreeItem sub = AddSubsection(section, "RimWorldAccess.CharEd.Character.AbilitiesSection".Translate());
            sub.Data = GroupSummaryKind.Abilities;
            PopulateAbilitiesRows(sub, pawn);
        }

        private void PopulateAbilitiesRows(InspectionTreeItem sub, Pawn pawn)
        {
            AddRow(sub, CharRowKind.AbilityAdd, "RimWorldAccess.CharEd.Character.AddAbility".Translate());
            AddRow(sub, CharRowKind.AbilityCopy, "Copy".Translate());
            AddRow(sub, CharRowKind.AbilityPaste, "Paste".Translate());
            if (CharEditorCompat.CreationMode)
            {
                AddRow(sub, CharRowKind.AbilityRandomize, "Randomize".Translate());
            }
            if (pawn?.abilities?.AllAbilitiesForReading != null)
            {
                foreach (Ability ability in pawn.abilities.AllAbilitiesForReading)
                {
                    AddRow(sub, CharRowKind.AbilityEntry, ability.def.LabelCap, ability);
                }
            }
        }

        /// <summary>Rebuilds the Abilities subsection in place after add/remove/paste/randomize.</summary>
        private void RebuildAbilitiesAfterMutation(InspectionTreeItem anyAbilityRowItem)
        {
            InspectionTreeItem sub = anyAbilityRowItem?.Parent;
            if (sub == null)
            {
                return;
            }
            Pawn pawn = CharEditorCompat.CurrentPawn;
            int cursorIndex = IndexOfVisible(Tree.Visible, anyAbilityRowItem);
            sub.Children.Clear();
            PopulateAbilitiesRows(sub, pawn);
            Tree.Reflatten();
            ResetBoundaryTracking();
            RefreshModel();
            if (Tree.Visible.Count == 0)
            {
                return;
            }
            int clamped = Math.Min(Math.Max(cursorIndex, 0), Tree.Visible.Count - 1);
            Tree.SetSelectedIndex(clamped);
            SyncRegionFromCurrentTree();
            AnnounceCurrentItem();
        }

        // ---- Psycasts. ----

        /// <summary>Step granularity: the mod's sliders have no discrete step, so this divisor is ours.</summary>
        private const float PsycastStepDivisor = 20f;

        private void BuildPsycastsSubsection(InspectionTreeItem section, Pawn pawn)
        {
            InspectionTreeItem sub = AddSubsection(section, "RimWorldAccess.CharEd.Character.PsycastsSection".Translate());
            AddRow(sub, CharRowKind.PsycastEntropy, "RimWorldAccess.CharEd.Character.NeuralHeat".Translate());
            AddRow(sub, CharRowKind.PsycastPsyfocus, "RimWorldAccess.CharEd.Character.Psyfocus".Translate());
        }

        // ---- Identity. ----

        private void BuildIdentitySubsection(InspectionTreeItem section, Pawn pawn)
        {
            InspectionTreeItem sub = AddSubsection(section, "RimWorldAccess.CharEd.Character.IdentitySection".Translate());
            // The mod draws gender as a utility icon by the portrait, but it changes identity, so
            // it reads here beside faction and ideoligion. The mod special-cases a genderless race
            // inside the mutation step; gating the whole row on hasGenders is clearer.
            if (pawn.RaceProps.hasGenders)
            {
                AddRow(sub, ActionRowKind.UtilityGender, "RimWorldAccess.CharEd.Actions.Gender".Translate());
            }
            AddRow(sub, CharRowKind.IdentityFaction, "Faction".Translate().CapitalizeFirst());
            if (ModsConfig.IdeologyActive && pawn.ideo != null)
            {
                AddRow(sub, CharRowKind.IdentityIdeo, "StatsReport_Ideoligion".Translate());
            }
            if (ModsConfig.BiotechActive && pawn.genes != null)
            {
                AddRow(sub, CharRowKind.IdentityXenoView, "ViewGenes".Translate());
                AddRow(sub, CharRowKind.IdentityXenoEdit, "XenotypeEditor".Translate());
            }
            if (pawn.story != null)
            {
                AddRow(sub, CharRowKind.IdentityFavoriteColor, "RimWorldAccess.CharEd.Character.FavoriteColor".Translate());
            }
            if (ModsConfig.AnomalyActive)
            {
                AddRow(sub, CharRowKind.IdentityMutant, "RimWorldAccess.CharEd.Character.Mutant".Translate());
            }
            if (ModsConfig.RoyaltyActive && pawn.royalty != null)
            {
                AddRow(sub, CharRowKind.IdentityRoyalTitle, "RimWorldAccess.CharEd.Character.RoyalTitleRow".Translate());
            }
            // The mod's own gate, coarser than "is a prisoner": recruit and enslave here bypass
            // vanilla's resistance and timer flow entirely.
            if (pawn.Faction != Faction.OfPlayer && pawn.Faction != Faction.OfMechanoids && !pawn.Dead)
            {
                AddRow(sub, CharRowKind.IdentityRecruit, "RimWorldAccess.CharEd.Character.Recruit".Translate());
                AddRow(sub, CharRowKind.IdentityEnslave, "RimWorldAccess.CharEd.Character.Enslave".Translate());
            }
        }

        /// <summary>The pawn's ideoligion name, or "none".</summary>
        private static string DescribeIdeoValue(Pawn pawn)
        {
            Ideo ideo = pawn?.Ideo;
            return ideo != null ? ideo.name : "None".Translate().ToString();
        }

        private static string DescribeIdeoExtras(Pawn pawn)
        {
            Ideo ideo = pawn?.Ideo;
            if (ideo == null)
            {
                return null;
            }
            var parts = new List<string>();
            Precept_Role role = ideo.GetRole(pawn);
            if (role != null)
            {
                parts.Add(role.LabelForPawn(pawn));
            }
            parts.Add("RimWorldAccess.CharEd.Character.Certainty".Translate(pawn.ideo.Certainty.ToStringPercent()).ToString());
            return string.Join(". ", parts.ToArray());
        }

        private static string DescribeFavoriteColorValue(Pawn pawn)
        {
            ColorDef color = pawn?.story?.favoriteColor;
            return color != null ? ColorDefSpokenLabel(color) : "None".Translate().ToString();
        }

        private static string DescribeMutantValue(Pawn pawn)
        {
            return pawn?.mutant?.Def != null ? pawn.mutant.Def.LabelCap.ToString() : "None".Translate().ToString();
        }

        // ---- Training (trainable animals). ----

        private void BuildTrainingSubsection(InspectionTreeItem section, Pawn pawn)
        {
            InspectionTreeItem sub = AddSubsection(section, "RimWorldAccess.CharEd.Character.TrainingSection".Translate());
            if (pawn.training.HasLearned(TrainableDefOf.Obedience))
            {
                AddRow(sub, CharRowKind.TrainingMaster, "Master".Translate());
            }
            AddRow(sub, CharRowKind.TrainingTrainability, "Trainability".Translate());
            foreach (TrainableDef def in DefDatabase<TrainableDef>.AllDefsListForReading)
            {
                AddRow(sub, CharRowKind.TrainingEntry, def.LabelCap, def);
            }
        }

        private static string DescribeMasterValue(Pawn pawn)
        {
            Pawn master = pawn?.playerSettings?.Master;
            return master != null ? master.LabelShortCap : "None".Translate().ToString();
        }

        private void OpenMasterPicker(Pawn pawn, InspectionTreeItem item)
        {
            List<Pawn> candidates = CharEditorCompat.MasterCandidates(window);
            var options = new List<FloatMenuOption>();
            foreach (Pawn candidate in candidates)
            {
                if (candidate == null)
                {
                    continue;
                }
                Pawn captured = candidate;
                options.Add(new FloatMenuOption(DescribePawn(captured), delegate
                {
                    CharEditorCompat.SelectMaster(window, captured);
                    AnnounceAdjustedRow<CharRowKind>(item, DescribeCharRow);
                }));
            }
            WindowlessFloatMenuState.OpenTitled("Master".Translate(), options);
        }

        /// <summary>Vanilla's resolved trainability tier, falling back to the race default exactly as the stats report row does.</summary>
        private static string DescribeTrainabilityValue(Pawn pawn)
        {
            if (pawn?.RaceProps == null)
            {
                return "";
            }
            TrainabilityDef def = TrainableUtility.GetTrainability(pawn) ?? pawn.RaceProps.trainability;
            return def != null ? def.LabelCap.ToString() : "";
        }

        private void DescribeTrainingEntry(TrainableDef def, Pawn pawn, ElementDescription d)
        {
            d.Label = def.LabelCap;
            d.Role = ElementRole.Checkbox;
            bool wanted = pawn.training.GetWanted(def);
            d.Check = wanted ? CheckState.Checked : CheckState.Unchecked;
            AcceptanceReport canTrain = pawn.training.CanAssignToTrain(def);
            d.Disabled = !canTrain.Accepted;

            int steps = CharEditorCompat.TrainingSteps(window, def);
            var extras = new List<string> { "RimWorldAccess.CharEd.Character.TrainingSteps".Translate(steps, def.steps).ToString() };
            if (!canTrain.Accepted)
            {
                extras.Add(canTrain.Reason);
            }
            else if (!def.prerequisites.NullOrEmpty())
            {
                foreach (TrainableDef prereq in def.prerequisites)
                {
                    if (!pawn.training.HasLearned(prereq))
                    {
                        extras.Add("TrainingNeedsPrerequisite".Translate(prereq.LabelCap).ToString());
                    }
                }
            }
            d.Extras = string.Join(". ", extras.ToArray());
        }

        private void ToggleTrainable(TrainableDef def, InspectionTreeItem item)
        {
            Pawn pawn = CharEditorCompat.CurrentPawn;
            if (pawn?.training == null)
            {
                return;
            }
            AcceptanceReport canTrain = pawn.training.CanAssignToTrain(def);
            if (!canTrain.Accepted)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.SpeakData(canTrain.Reason);
                return;
            }
            bool wanted = pawn.training.GetWanted(def);
            // MUTATION-B: vanilla's own gated recursive setter (Pawn_TrainingTracker
            // .SetWantedRecursive) cascades prerequisite wanted-flags itself; no mod wrapper needed
            // (public vanilla member, called directly).
            pawn.training.SetWantedRecursive(def, !wanted);
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceAdjustedRow<CharRowKind>(item, DescribeCharRow);
        }

        private bool OnTrainingEntryRow()
        {
            RefreshModel();
            return CurrentTreeItem()?.Data is RegionRow<CharRowKind> row && row.Kind == CharRowKind.TrainingEntry;
        }

        private void PerformTrainStep()
        {
            InspectionTreeItem item = CurrentTreeItem();
            if (!(item?.Data is RegionRow<CharRowKind> row) || !(row.Payload is TrainableDef def))
            {
                return;
            }
            CharEditorCompat.RunWithMenuRedirect(() => CharEditorCompat.TrainOneStep(window, def));
            if (WindowlessFloatMenuState.IsActive)
            {
                return;
            }
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceAdjustedRow<CharRowKind>(item, DescribeCharRow);
        }


    }
}
