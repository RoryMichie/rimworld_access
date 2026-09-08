using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The keyboard focus scope for vanilla's <see cref="Dialog_EditPrecept"/>, shaped like
    /// <see cref="EditDeityDialogScope"/>: scratch-field writes, the captured Done row, Escape left
    /// to vanilla.
    /// The row set varies with the precept. Every row is gated by the condition
    /// <c>DoWindowContents</c> gates its drawing with (decompiled Dialog_EditPrecept.cs:163-537),
    /// read off the DIALOG's scratch state rather than the precept, so what is spoken is what is
    /// drawn. Two regions mirror vanilla's two medium-font headers (:168, :340-343); the apparel
    /// region is empty — and skipped by Tab — for any precept that is not a role.
    /// Pickers carry vanilla's delegate bodies verbatim, <c>UpdateWindowHeight</c> included, so the
    /// window resizes when a mouse player's would. Nothing here commits to the precept:
    /// <c>ApplyChanges</c> is the only writer, except vanilla's own name lock (:200-211) and relic
    /// material (:489-492), which its delegates write straight through. Randomize touches every
    /// scratch field at once (:503-533) and stays the captured window button.
    /// </summary>
    public sealed class EditPreceptDialogScope : ScreenScope
    {
        private enum RowKind
        {
            Name,
            NameLock,
            LeaderTitleMale,
            LeaderTitleFemale,
            StartingCondition,
            DateDay,
            DateQuadrum,
            Reward,
            BuildingStyle,
            RelicStuff,
            ApparelRequirement,
            ApparelAdd,
        }

        private struct Row
        {
            public RowKind Kind;
            /// <summary>The apparel requirement's position in the dialog's list; unused by every other kind.</summary>
            public int Index;
        }

        private const int FieldsRegion = 0;
        private const int ApparelRegion = 1;

        private static readonly AccessTools.FieldRef<Dialog_EditPrecept, Precept> PreceptRef =
            AccessTools.FieldRefAccess<Dialog_EditPrecept, Precept>("precept");
        private static readonly AccessTools.FieldRef<Dialog_EditPrecept, string> NewName =
            AccessTools.FieldRefAccess<Dialog_EditPrecept, string>("newPreceptName");
        private static readonly AccessTools.FieldRef<Dialog_EditPrecept, string> NewNameFemale =
            AccessTools.FieldRefAccess<Dialog_EditPrecept, string>("newPreceptNameFemale");
        private static readonly AccessTools.FieldRef<Dialog_EditPrecept, RitualObligationTrigger_Date> DateTrigger =
            AccessTools.FieldRefAccess<Dialog_EditPrecept, RitualObligationTrigger_Date>("dateTrigger");
        private static readonly AccessTools.FieldRef<Dialog_EditPrecept, bool> NewCanStartAnytime =
            AccessTools.FieldRefAccess<Dialog_EditPrecept, bool>("newCanStartAnytime");
        private static readonly AccessTools.FieldRef<Dialog_EditPrecept, int> NewTriggerDays =
            AccessTools.FieldRefAccess<Dialog_EditPrecept, int>("newTriggerDaysSinceStartOfYear");
        private static readonly AccessTools.FieldRef<Dialog_EditPrecept, Quadrum> SelectedQuadrum =
            AccessTools.FieldRefAccess<Dialog_EditPrecept, Quadrum>("quadrum");
        private static readonly AccessTools.FieldRef<Dialog_EditPrecept, int> SelectedDay =
            AccessTools.FieldRefAccess<Dialog_EditPrecept, int>("day");
        private static readonly AccessTools.FieldRef<Dialog_EditPrecept, StyleCategoryPair> SelectedStyle =
            AccessTools.FieldRefAccess<Dialog_EditPrecept, StyleCategoryPair>("selectedStyle");
        private static readonly AccessTools.FieldRef<Dialog_EditPrecept, RitualAttachableOutcomeEffectDef> SelectedReward =
            AccessTools.FieldRefAccess<Dialog_EditPrecept, RitualAttachableOutcomeEffectDef>("selectedReward");
        private static readonly AccessTools.FieldRef<Dialog_EditPrecept, List<PreceptApparelRequirement>> ApparelRequirements =
            AccessTools.FieldRefAccess<Dialog_EditPrecept, List<PreceptApparelRequirement>>("apparelRequirements");
        private static readonly AccessTools.FieldRef<Dialog_EditPrecept, List<RitualAttachableOutcomeEffectDef>> AttachableEffects =
            AccessTools.FieldRefAccess<Dialog_EditPrecept, List<RitualAttachableOutcomeEffectDef>>("attachableOutcomeEffects");

        private static readonly MethodInfo UpdateWindowHeightMethod =
            AccessTools.Method(typeof(Dialog_EditPrecept), "UpdateWindowHeight");
        private static readonly MethodInfo StylesForBuildingMethod =
            AccessTools.Method(typeof(Dialog_EditPrecept), "StylesForBuilding");

        private readonly Dialog_EditPrecept dialog;
        private readonly List<Row> fieldRows = new List<Row>();
        private readonly List<Row> apparelRows = new List<Row>();
        private readonly TextFieldEditSession editSession = new TextFieldEditSession();

        /// <summary>A fresh scope instance per window open (ScopeForWindow), so the opening header is pending exactly once.</summary>
        private bool pendingOpeningHeader = true;

        public EditPreceptDialogScope(Dialog_EditPrecept dialog)
        {
            this.dialog = dialog;
            // The shared overlay-editor Delete, claimed here for the one row family this window can
            // remove: an apparel requirement (vanilla's own per-row Remove button, :358-361).
            Claim("ideoOverlayEditor.delete", OnDelete);
            RegisterPopTeardown(editSession.CancelIfActive);
        }

        public override string Name
        {
            get { return "edit-precept-dialog"; }
        }

        /// <summary>Vanilla's own rows are all modelled below; anything else on this window is a mod's, and belongs in the extras net.</summary>
        protected override bool IncludeCapturedExtrasRegion
        {
            get { return true; }
        }

        internal bool Owns(Window window)
        {
            return ReferenceEquals(window, dialog);
        }

        internal static EditPreceptDialogScope OwningScope(Window window)
        {
            return TextDialogShared.ScopeOwning<EditPreceptDialogScope>(
                window, delegate(EditPreceptDialogScope s, Window w) { return s.Owns(w); });
        }

        private Precept Precept
        {
            get { return PreceptRef(dialog); }
        }

        // Row model: the dialog's own draw branches, in the dialog's own order.

        protected override void RefreshContent()
        {
            fieldRows.Clear();
            apparelRows.Clear();
            Precept precept = Precept;
            if (precept == null)
            {
                return;
            }
            if (precept.def.leaderRole)
            {
                fieldRows.Add(new Row { Kind = RowKind.LeaderTitleMale });
                fieldRows.Add(new Row { Kind = RowKind.LeaderTitleFemale });
            }
            else
            {
                fieldRows.Add(new Row { Kind = RowKind.Name });
                fieldRows.Add(new Row { Kind = RowKind.NameLock });
                var ritual = precept as Precept_Ritual;
                if (ritual != null)
                {
                    if (ritual.canBeAnytime && ritual.sourcePattern != null && !ritual.sourcePattern.alwaysStartAnytime)
                    {
                        fieldRows.Add(new Row { Kind = RowKind.StartingCondition });
                        if (DateTrigger(dialog) != null && !NewCanStartAnytime(dialog))
                        {
                            fieldRows.Add(new Row { Kind = RowKind.DateDay });
                            fieldRows.Add(new Row { Kind = RowKind.DateQuadrum });
                        }
                    }
                    if (ritual.SupportsAttachableOutcomeEffect)
                    {
                        fieldRows.Add(new Row { Kind = RowKind.Reward });
                    }
                }
            }
            var building = precept as Precept_Building;
            if (building != null && StylesForBuilding(building).Count > 1)
            {
                fieldRows.Add(new Row { Kind = RowKind.BuildingStyle });
            }
            var relic = precept as Precept_Relic;
            if (relic != null && relic.ThingDef != null && relic.ThingDef.MadeFromStuff)
            {
                fieldRows.Add(new Row { Kind = RowKind.RelicStuff });
            }
            List<PreceptApparelRequirement> requirements = ApparelRequirements(dialog);
            if (requirements != null)
            {
                for (int i = 0; i < requirements.Count; i++)
                {
                    apparelRows.Add(new Row { Kind = RowKind.ApparelRequirement, Index = i });
                }
                apparelRows.Add(new Row { Kind = RowKind.ApparelAdd });
            }
        }

        protected override int ContentRegionCount
        {
            get { return 2; }
        }

        protected override string ContentRegionName(int region)
        {
            return region == ApparelRegion
                ? (string)"EditApparelRequirement".Translate()
                : (string)"EditName".Translate();
        }

        protected override int ContentItemCount(int region)
        {
            return RowsOf(region).Count;
        }

        private List<Row> RowsOf(int region)
        {
            return region == ApparelRegion ? apparelRows : fieldRows;
        }

        /// <summary>False for the Buttons and extras regions too, whose own engines own their rows.</summary>
        private bool TryRow(int region, int index, out Row row)
        {
            row = default(Row);
            if (region != FieldsRegion && region != ApparelRegion)
            {
                return false;
            }
            List<Row> rows = RowsOf(region);
            if (index < 0 || index >= rows.Count)
            {
                return false;
            }
            row = rows[index];
            return true;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            if (!TryRow(region, index, out Row row))
            {
                return new ElementDescription();
            }
            switch (row.Kind)
            {
                case RowKind.Name:
                    return DescribeTextRow((string)"Name".Translate(), NewName(dialog));
                case RowKind.NameLock:
                    return DescribeNameLock();
                case RowKind.LeaderTitleMale:
                    return DescribeTextRow(LeaderTitleLabel(female: false), NewName(dialog));
                case RowKind.LeaderTitleFemale:
                    return DescribeTextRow(LeaderTitleLabel(female: true), NewNameFemale(dialog));
                case RowKind.StartingCondition:
                    return new ElementDescription
                    {
                        Label = (string)"StartingCondition".Translate(),
                        Role = ElementRole.ComboBox,
                        Value = StartingConditionLabel(NewCanStartAnytime(dialog)),
                    };
                case RowKind.DateDay:
                    return new ElementDescription
                    {
                        Label = (string)"Date".Translate(),
                        Role = ElementRole.ComboBox,
                        Value = DayLabel(SelectedDay(dialog)),
                    };
                case RowKind.DateQuadrum:
                    return new ElementDescription
                    {
                        Label = (string)"Date".Translate(),
                        Role = ElementRole.ComboBox,
                        Value = SelectedQuadrum(dialog).Label(),
                    };
                case RowKind.Reward:
                    return DescribeReward();
                case RowKind.BuildingStyle:
                    return new ElementDescription
                    {
                        Label = (string)"Appearance".Translate(),
                        Role = ElementRole.ComboBox,
                        Value = StyleCategoryLabel(SelectedStyle(dialog)),
                    };
                case RowKind.RelicStuff:
                    return new ElementDescription
                    {
                        Label = (string)"RelicStuff".Translate(),
                        Role = ElementRole.ComboBox,
                        Value = RelicStuffLabel(),
                    };
                case RowKind.ApparelRequirement:
                    return DescribeApparelRequirement(row.Index);
                case RowKind.ApparelAdd:
                    return new ElementDescription
                    {
                        Label = AddRowLabel(),
                        Role = ElementRole.Button,
                        // Vanilla prints this note under the section header (:346-350); it
                        // belongs with the row that acts on it.
                        Extras = (string)"NoteRoleApparelRequirementEffects".Translate(),
                    };
                default:
                    return new ElementDescription();
            }
        }

        private static ElementDescription DescribeTextRow(string label, string value)
        {
            var d = new ElementDescription { Label = label, Role = ElementRole.TextField };
            if (string.IsNullOrEmpty(value))
            {
                d.ValueBlank = true;
            }
            else
            {
                d.Value = value;
            }
            return d;
        }

        /// <summary>The lock button's own tooltip is the only name vanilla gives it (:215); the check state carries what the two lock textures show.</summary>
        private ElementDescription DescribeNameLock()
        {
            return new ElementDescription
            {
                Label = (string)"LockPreceptNameButtonDesc".Translate(),
                Role = ElementRole.Checkbox,
                Check = Precept.nameLocked ? CheckState.Checked : CheckState.Unchecked,
            };
        }

        private ElementDescription DescribeReward()
        {
            RitualAttachableOutcomeEffectDef reward = SelectedReward(dialog);
            var d = new ElementDescription
            {
                Label = (string)"RitualAttachedReward".Translate(),
                Role = ElementRole.ComboBox,
                Value = reward != null ? reward.LabelCap.ToString() : (string)"None".Translate(),
                // Vanilla greys the Choose button out when no effect def exists at all (:294).
                Disabled = !AttachableEffects(dialog).Any(),
            };
            var ritual = Precept as Precept_Ritual;
            if (reward != null && ritual != null)
            {
                var extras = new List<string>();
                if (ritual.outcomeEffect != null)
                {
                    string outcomes = reward.AppliesToOutcomesString(ritual.outcomeEffect.def);
                    extras.Add((reward.AppliesToSeveralOutcomes(ritual)
                        ? "RitualAttachedApplliesForOutcomes"
                        : "RitualAttachedApplliesForOutcome").Translate(outcomes));
                }
                extras.Add(reward.effectDesc.CapitalizeFirst());
                d.Extras = string.Join(". ", extras.ToArray());
            }
            return d;
        }

        /// <summary>
        /// One requirement row IS vanilla's Remove button (:357-361); the compatibility warning
        /// vanilla colours yellow beside it (:362-365, :375-378) rides in Extras.
        /// </summary>
        private ElementDescription DescribeApparelRequirement(int index)
        {
            List<PreceptApparelRequirement> requirements = ApparelRequirements(dialog);
            if (requirements == null || index < 0 || index >= requirements.Count)
            {
                return new ElementDescription();
            }
            PreceptApparelRequirement requirement = requirements[index];
            var d = new ElementDescription
            {
                Label = (string)"Remove".Translate() + ": " + ApparelLabel(requirement),
                Role = ElementRole.Button,
            };
            if (requirement.RequirementOverlapsOther(requirements, out string reason) && !reason.NullOrEmpty())
            {
                d.Extras = reason.CapitalizeFirst();
            }
            return d;
        }

        private static string ApparelLabel(PreceptApparelRequirement requirement)
        {
            return string.Join(", ", requirement.requirement.AllRequiredApparel()
                .Select(a => a.LabelCap.ToString()).ToArray());
        }

        private static string LeaderTitleLabel(bool female)
        {
            return (string)"LeaderTitle".Translate() + " (" + (female ? Gender.Female : Gender.Male).GetLabel() + ")";
        }

        private static string StartingConditionLabel(bool anytime)
        {
            return (anytime ? "StartingCondition_Anytime" : "StartingCondition_Date").Translate();
        }

        private static string DayLabel(int day)
        {
            return Find.ActiveLanguageWorker.OrdinalNumber(day + 1);
        }

        private static string StyleCategoryLabel(StyleCategoryPair pair)
        {
            return pair != null && pair.category != null
                ? pair.category.LabelCap.ToString()
                : (string)"Default".Translate();
        }

        private string RelicStuffLabel()
        {
            var relic = Precept as Precept_Relic;
            return relic != null && relic.stuff != null
                ? relic.stuff.LabelCap.ToString()
                : (string)"None".Translate();
        }

        private static string AddRowLabel()
        {
            return (string)"Add".Translate().CapitalizeFirst() + "...";
        }

        private static string ChooseRewardLabel()
        {
            return (string)"RitualAttachedRewardChoose".Translate() + "...";
        }

        private static string ChooseStuffLabel()
        {
            return (string)"ChooseStuffForRelic".Translate() + "...";
        }

        private List<StyleCategoryPair> StylesForBuilding(Precept_Building building)
        {
            // Vanilla's own list hands back a shared static buffer, so the copy is not optional.
            var styles = (List<StyleCategoryPair>)StylesForBuildingMethod.Invoke(dialog, new object[] { building });
            return styles != null ? new List<StyleCategoryPair>(styles) : new List<StyleCategoryPair>();
        }

        private void UpdateWindowHeight()
        {
            UpdateWindowHeightMethod.Invoke(dialog, null);
        }

        protected override void ActivateContentItem(int region, int index)
        {
            if (!TryRow(region, index, out Row row))
            {
                return;
            }
            switch (row.Kind)
            {
                case RowKind.Name:
                    BeginTextEdit((string)"Name".Translate(), "Name", NewName(dialog),
                        value => NewName(dialog) = value ?? "");
                    break;
                case RowKind.NameLock:
                    ToggleNameLock();
                    break;
                case RowKind.LeaderTitleMale:
                    BeginTextEdit(LeaderTitleLabel(female: false), "LeaderTitle", NewName(dialog),
                        value => NewName(dialog) = value ?? "");
                    break;
                case RowKind.LeaderTitleFemale:
                    BeginTextEdit(LeaderTitleLabel(female: true), "LeaderTitle", NewNameFemale(dialog),
                        value => NewNameFemale(dialog) = value ?? "");
                    break;
                case RowKind.StartingCondition:
                    OpenStartingConditionPicker();
                    break;
                case RowKind.DateDay:
                    OpenDayPicker();
                    break;
                case RowKind.DateQuadrum:
                    OpenQuadrumPicker();
                    break;
                case RowKind.Reward:
                    OpenRewardPicker();
                    break;
                case RowKind.BuildingStyle:
                    OpenBuildingStylePicker();
                    break;
                case RowKind.RelicStuff:
                    OpenRelicStuffPicker();
                    break;
                case RowKind.ApparelRequirement:
                    RemoveApparelRequirement(row.Index);
                    break;
                case RowKind.ApparelAdd:
                    OpenAddApparelRequirementPicker();
                    break;
            }
        }

        /// <summary>Delete on an apparel requirement row runs the same removal its Enter does; every other row ignores it.</summary>
        private void OnDelete(KeyEventSnapshot e)
        {
            RefreshModel();
            if (Model.RegionIndex != ApparelRegion)
            {
                return;
            }
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty || !TryRow(ApparelRegion, region.Index, out Row row)
                || row.Kind != RowKind.ApparelRequirement)
            {
                return;
            }
            RemoveApparelRequirement(row.Index);
        }

        /// <summary>
        /// The name rules vanilla enforces inside the field itself (:57/:59), so the session never
        /// produces the reject message the dialog fires on every keystroke past 32.
        /// </summary>
        private void BeginTextEdit(string displayLabel, string labelKey, string current, Action<string> apply)
        {
            editSession.EnterEdit(
                current ?? "",
                IdeoTypedPreceptState.PreceptNameSpec(labelKey),
                displayLabel,
                apply,
                AnnounceCurrentItem);
        }

        /// <summary>The lock button's own delegate body (:202-210), sounds included.</summary>
        private void ToggleNameLock()
        {
            Precept precept = Precept;
            precept.nameLocked = !precept.nameLocked;
            if (precept.nameLocked)
            {
                SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
            }
            else
            {
                SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();
            }
            AnnounceCurrentItem();
        }

        /// <summary>
        /// Vanilla draws the starting condition as two radio buttons that write back once, after
        /// both (:225-242); one picker here, same single write, same <c>UpdateWindowHeight</c>.
        /// </summary>
        private void OpenStartingConditionPicker()
        {
            var options = new List<FloatMenuOption>();
            foreach (bool anytime in new[] { true, false })
            {
                bool captured = anytime;
                options.Add(new FloatMenuOption(StartingConditionLabel(captured), delegate
                {
                    if (NewCanStartAnytime(dialog) != captured)
                    {
                        NewCanStartAnytime(dialog) = captured;
                        UpdateWindowHeight();
                    }
                }));
            }
            OpenPicker(options, (string)"StartingCondition".Translate());
        }

        /// <summary>The day list vanilla's own day button builds (:248-258).</summary>
        private void OpenDayPicker()
        {
            var options = new List<FloatMenuOption>();
            for (int i = 0; i < 15; i++)
            {
                int d = i;
                options.Add(new FloatMenuOption(DayLabel(d), delegate
                {
                    SelectedDay(dialog) = d;
                    NewTriggerDays(dialog) = (int)SelectedQuadrum(dialog) * 15 + SelectedDay(dialog);
                }));
            }
            OpenPicker(options, (string)"Date".Translate());
        }

        /// <summary>The quadrum list vanilla's own quadrum button builds (:262-271).</summary>
        private void OpenQuadrumPicker()
        {
            var options = new List<FloatMenuOption>();
            foreach (Quadrum quadrum in QuadrumUtility.QuadrumsInChronologicalOrder)
            {
                Quadrum q = quadrum;
                options.Add(new FloatMenuOption(q.Label(), delegate
                {
                    SelectedQuadrum(dialog) = q;
                    NewTriggerDays(dialog) = (int)SelectedQuadrum(dialog) * 15 + SelectedDay(dialog);
                }));
            }
            OpenPicker(options, (string)"Date".Translate());
        }

        /// <summary>
        /// The reward list vanilla's Choose button builds (:296-334), <c>CanAttachToRitual</c> gate
        /// included: a refused effect stays listed, disabled, with vanilla's reason appended.
        /// </summary>
        private void OpenRewardPicker()
        {
            var ritual = Precept as Precept_Ritual;
            if (ritual == null)
            {
                return;
            }
            var options = new List<FloatMenuOption>();
            if (SelectedReward(dialog) != null)
            {
                options.Add(new FloatMenuOption((string)"None".Translate(), delegate
                {
                    SelectedReward(dialog) = null;
                    UpdateWindowHeight();
                }));
            }
            foreach (RitualAttachableOutcomeEffectDef effect in AttachableEffects(dialog))
            {
                RitualAttachableOutcomeEffectDef eff = effect;
                if (eff == SelectedReward(dialog))
                {
                    continue;
                }
                AcceptanceReport report = eff.CanAttachToRitual(ritual);
                if (report.Accepted)
                {
                    options.Add(new FloatMenuOption(eff.LabelCap, delegate
                    {
                        SelectedReward(dialog) = eff;
                        UpdateWindowHeight();
                    }));
                }
                else
                {
                    options.Add(new FloatMenuOption(eff.LabelCap + " (" + report.Reason + ")", null));
                }
            }
            OpenPicker(options, (string)"RitualAttachedReward".Translate());
        }

        /// <summary>The style stack's own click body (:454-457): vanilla writes the pair and ticks.</summary>
        private void OpenBuildingStylePicker()
        {
            var building = Precept as Precept_Building;
            if (building == null)
            {
                return;
            }
            var options = new List<FloatMenuOption>();
            foreach (StyleCategoryPair pair in StylesForBuilding(building))
            {
                StyleCategoryPair captured = pair;
                options.Add(new FloatMenuOption(StyleCategoryLabel(captured), delegate
                {
                    SelectedStyle(dialog) = captured;
                    SoundDefOf.Tick_High.PlayOneShotOnCamera();
                }));
            }
            OpenPicker(options, (string)"Appearance".Translate());
        }

        /// <summary>
        /// The material list vanilla's Choose button builds (:485-494). Its delegate writes the
        /// relic directly, not a scratch field, so this edit stands whether or not the dialog is
        /// accepted — as it does for a mouse player.
        /// </summary>
        private void OpenRelicStuffPicker()
        {
            var relic = Precept as Precept_Relic;
            if (relic == null)
            {
                return;
            }
            var options = new List<FloatMenuOption>();
            foreach (ThingDef stuff in GenStuff.AllowedStuffsFor(relic.ThingDef))
            {
                ThingDef localStuff = stuff;
                options.Add(new FloatMenuOption(stuff.LabelCap, delegate
                {
                    relic.stuff = localStuff;
                }, stuff));
            }
            OpenPicker(options, (string)"RelicStuff".Translate());
        }

        /// <summary>Vanilla's removal body (:383-388).</summary>
        private void RemoveApparelRequirement(int index)
        {
            List<PreceptApparelRequirement> requirements = ApparelRequirements(dialog);
            if (requirements == null || index < 0 || index >= requirements.Count)
            {
                return;
            }
            requirements.RemoveAt(index);
            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            UpdateWindowHeight();
            RefreshModel();
            AnnounceCurrentItem();
        }

        /// <summary>
        /// The Add button's own body (:389-425): the meme veto first, then the requirement list,
        /// vanilla's <c>CanAddRequirement</c> gate deciding which options are disabled and why.
        /// </summary>
        private void OpenAddApparelRequirementPicker()
        {
            Precept precept = Precept;
            List<PreceptApparelRequirement> requirements = ApparelRequirements(dialog);
            if (precept == null || requirements == null)
            {
                return;
            }
            foreach (MemeDef meme in precept.ideo.memes)
            {
                if (meme.preventApparelRequirements)
                {
                    Messages.Message("CannotNotAddRoleApparelDueToMeme".Translate(meme.LabelCap.Named("MEME")),
                        MessageTypeDefOf.RejectInput, historical: false);
                    return;
                }
            }
            var options = new List<FloatMenuOption>();
            foreach (PreceptApparelRequirement possible in
                Precept_Role.AllPossibleRequirements(precept.ideo, precept.def, desperate: true))
            {
                PreceptApparelRequirement localReq = possible;
                List<ThingDef> apparel = possible.requirement.AllRequiredApparel().ToList();
                if (apparel.Count == 0)
                {
                    continue;
                }
                var option = new FloatMenuOption(
                    string.Join(", ", apparel.Select(ap => ap.LabelCap.ToString()).ToArray()),
                    delegate
                    {
                        requirements.Add(localReq);
                        UpdateWindowHeight();
                    },
                    apparel[0].uiIcon, apparel[0].uiIconColor);
                option.Disabled = !possible.CanAddRequirement(precept, requirements, out string cannotAddReason);
                if (option.Disabled)
                {
                    option.Label = option.Label + " (" + cannotAddReason + ")";
                }
                options.Add(option);
            }
            OpenPicker(options, (string)"EditApparelRequirement".Translate());
        }

        /// <summary>
        /// Every picker on this window, opened windowless (keyboard-navigable, visible in the twin)
        /// with no selection echo: the re-announcement when the menu closes is the confirmation.
        /// </summary>
        private void OpenPicker(List<FloatMenuOption> options, string title)
        {
            if (options.Count == 0)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }
            WindowlessFloatMenuState.Open(options, colonistOrders: false, announceSelection: false, titleText: title);
        }

        // Focus ring: each row's backing vanilla widget capture.

        protected internal override Rect FocusedContentRect()
        {
            RefreshModel();
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty
                || !TryRow(Model.RegionIndex, region.Index, out Row row)
                || !TryCapturedTarget(row, out WidgetKind kind, out string label, out int ordinal))
            {
                return default(Rect);
            }
            return FindCapturedWidgetRect(kind, label, ordinal);
        }

        /// <summary>
        /// The vanilla widget a row rides, as the capture stream records it: a label/field pair
        /// anchors on the field, a row with no widget of its own (the style stack, :452-454) on its
        /// caption. Doubles as the Buttons-region filter — every Button named here is a content row.
        /// </summary>
        private bool TryCapturedTarget(Row row, out WidgetKind kind, out string label, out int ordinal)
        {
            kind = WidgetKind.Button;
            label = "";
            ordinal = 0;
            switch (row.Kind)
            {
                // RecordTextField's contract: Label is always "", ordinal among text fields alone,
                // in draw order.
                case RowKind.Name:
                case RowKind.LeaderTitleMale:
                    kind = WidgetKind.TextField;
                    return true;
                case RowKind.LeaderTitleFemale:
                    kind = WidgetKind.TextField;
                    ordinal = 1;
                    return true;
                case RowKind.NameLock:
                    label = WidgetCapture.ImageButtonLabel(
                        Precept.nameLocked ? IdeoUIUtility.LockedTex : IdeoUIUtility.UnlockedTex, null);
                    return true;
                case RowKind.StartingCondition:
                    // Widgets.RadioButton draws a bare texture plus an unlabeled ButtonInvisible;
                    // ring the chosen one.
                    kind = WidgetKind.InvisibleButton;
                    ordinal = NewCanStartAnytime(dialog) ? 0 : 1;
                    return true;
                case RowKind.DateDay:
                    label = DayLabel(SelectedDay(dialog));
                    return true;
                case RowKind.DateQuadrum:
                    label = SelectedQuadrum(dialog).Label();
                    return true;
                case RowKind.Reward:
                    label = ChooseRewardLabel();
                    return true;
                case RowKind.BuildingStyle:
                    kind = WidgetKind.Label;
                    label = (string)"Appearance".Translate() + ":";
                    return true;
                case RowKind.RelicStuff:
                    label = ChooseStuffLabel();
                    return true;
                case RowKind.ApparelRequirement:
                    label = (string)"Remove".Translate();
                    ordinal = row.Index;
                    return true;
                case RowKind.ApparelAdd:
                    label = AddRowLabel();
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Only the three window buttons (:499-537) belong in the Buttons region; every other ButtonText is a content row above.</summary>
        protected override bool KeepCapturedButton(string rawLabel)
        {
            return !IsContentButton(fieldRows, rawLabel) && !IsContentButton(apparelRows, rawLabel);
        }

        private bool IsContentButton(List<Row> rows, string rawLabel)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (TryCapturedTarget(rows[i], out WidgetKind kind, out string label, out int _)
                    && kind == WidgetKind.Button && label == rawLabel)
                {
                    return true;
                }
            }
            return false;
        }

        // Per-GUI-pass work, from the dialog's own draw prefix.

        internal void OnDialogDrawPass()
        {
            // Keep the shell the sole owner of Unity keyboard focus so arrow keys reach the
            // dispatcher, then post the live edit buffer into the field vanilla is about to draw.
            ShellTextFocus.ReleaseNativeFocus();
            editSession.MirrorLive();
        }

        // Opening announcement: the window title, folded into the first landing.

        protected override string AnnouncePrefix(int region, int index)
        {
            string prefix = base.AnnouncePrefix(region, index);
            if (!pendingOpeningHeader)
            {
                return prefix;
            }
            pendingOpeningHeader = false;
            Precept precept = Precept;
            string header = (string)"EditName".Translate();
            if (precept != null)
            {
                header = header + ". " + IdeoBuilderHelper.PreceptLabel(precept);
            }
            return string.IsNullOrEmpty(prefix) ? header : header + ". " + prefix;
        }
    }

    /// <summary>
    /// Brackets the scope's per-pass work to the dialog's own draw, before vanilla reads its
    /// scratch fields into <c>Widgets.TextField</c>. No accept-poll mask is needed: the dialog
    /// polls no raw key, and its <c>OnAcceptKeyPressed</c> override is already gated by the
    /// shell's window-stack accept router.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_EditPrecept), "DoWindowContents")]
    public static class EditPreceptDrawPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Dialog_EditPrecept __instance)
        {
            try
            {
                EditPreceptDialogScope scope = EditPreceptDialogScope.OwningScope(__instance);
                if (scope != null)
                {
                    scope.OnDialogDrawPass();
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Edit precept dialog draw pass error", ex);
            }
        }
    }
}
