using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Reflection-only compatibility surface for Vanilla Skills Expanded's Expertise feature.
    /// Every public entry point is wrapped in try/catch with a graceful decline, so a VSE update
    /// can never break the Skills tab for players with or without VSE installed.
    ///
    /// Two surfaces: READ (owned expertises nested under their skill in the Skills inspection tab,
    /// via <see cref="PawnSkillsAdapter.RegisterDetailExtender"/>) and CHOOSE (a picker that
    /// grants a new expertise, added as the lead "Choose expertise" row via
    /// <see cref="PawnSkillsAdapter.RegisterLeadRowExtender"/>, mirroring where VSE itself places
    /// the trigger).
    ///
    /// Mutation vehicle B: <c>ExpertiseTracker.AddExpertise</c> is only ever invoked by
    /// <see cref="TryGrant"/> after its own Can-twin <c>ExpertiseDef.CanApplyOn</c> returns true.
    /// </summary>
    internal static class VseExpertiseCompat
    {
        private static readonly Type expertiseTrackerType;
        private static readonly Type expertiseTrackersType;
        private static readonly Type expertiseRecordType;
        private static readonly Type expertiseDefType;

        private static readonly MethodInfo expertiseForPawnMethod;
        private static readonly PropertyInfo allExpertiseProperty;
        private static readonly MethodInfo addExpertiseMethod;

        private static readonly FieldInfo recordDefField;
        private static readonly PropertyInfo recordLevelProperty;
        private static readonly PropertyInfo recordLevelDescriptorProperty;
        private static readonly MethodInfo recordFullDescriptionMethod;

        private static readonly FieldInfo defSkillField;
        private static readonly FieldInfo defHideField;
        private static readonly MethodInfo canApplyOnMethod;
        private static readonly MethodInfo effectsMethod;

        private static readonly PropertyInfo allDefsProperty;

        // The panel VSE toggles beside the Character tab is an ImmediateWindow, invisible to the
        // window-scope reader; its own surface, so a rename never costs the read/choose surfaces.
        private static readonly FieldInfo showExpertiseField;
        private static readonly MethodInfo doOpenExpertiseButtonMethod;
        private static readonly bool panelReady;

        private static readonly bool ready;

        public static bool Ready => ready;

        static VseExpertiseCompat()
        {
            var surface = new ReflectionSurface("VseExpertiseCompat");

            expertiseTrackerType = surface.Type("VSE.ExpertiseTracker");
            expertiseTrackersType = surface.Type("VSE.ExpertiseTrackers");
            expertiseRecordType = surface.Type("VSE.ExpertiseRecord");
            expertiseDefType = surface.Type("VSE.Expertise.ExpertiseDef");

            // Expertise(this Pawn) is one of two same-named extension overloads (the other takes a
            // Pawn_SkillTracker), so it must be resolved by parameter type.
            expertiseForPawnMethod = surface.Method(expertiseTrackersType, "Expertise", new[] { typeof(Pawn) });

            allExpertiseProperty = surface.Property(expertiseTrackerType, "AllExpertise");
            addExpertiseMethod = expertiseDefType != null
                ? surface.Method(expertiseTrackerType, "AddExpertise", new[] { expertiseDefType })
                : null;

            recordDefField = surface.Field(expertiseRecordType, "def");
            recordLevelProperty = surface.Property(expertiseRecordType, "Level");
            recordLevelDescriptorProperty = surface.Property(expertiseRecordType, "LevelDescriptor");
            recordFullDescriptionMethod = surface.Method(expertiseRecordType, "FullDescription", Type.EmptyTypes);

            defSkillField = surface.Field(expertiseDefType, "skill");
            defHideField = surface.Field(expertiseDefType, "hide");
            canApplyOnMethod = surface.Method(expertiseDefType, "CanApplyOn",
                new[] { typeof(Pawn), typeof(string).MakeByRefType() });
            effectsMethod = surface.Method(expertiseDefType, "Effects",
                new[] { typeof(int), typeof(string) });
            allDefsProperty = expertiseDefType != null
                ? typeof(DefDatabase<>).MakeGenericType(expertiseDefType)
                    .GetProperty("AllDefs", BindingFlags.Public | BindingFlags.Static)
                : null;

            ready = surface.Ready && allDefsProperty != null;

            var panelSurface = new ReflectionSurface("VseExpertiseCompat.Panel");
            Type uiType = panelSurface.Type("VSE.ExpertiseUIUtility");
            showExpertiseField = panelSurface.Field(uiType, "ShowExpertise");
            doOpenExpertiseButtonMethod = panelSurface.Method(uiType, "DoOpenExpertiseButton",
                new[] { typeof(Pawn), typeof(float).MakeByRefType() });
            panelReady = panelSurface.Ready && showExpertiseField.IsStatic;
        }

        /// <summary>A pawn's owned expertise, projected to reflection-free data.</summary>
        public readonly struct ExpertiseView
        {
            public readonly string Label;            // ExpertiseDef.LabelCap
            public readonly string LevelDescriptor;  // ExpertiseRecord.LevelDescriptor
            public readonly int Level;               // ExpertiseRecord.Level
            public readonly string FullDescription;  // ExpertiseRecord.FullDescription()
            public readonly SkillDef Skill;          // ExpertiseDef.skill

            public ExpertiseView(string label, string levelDescriptor, int level, string fullDescription, SkillDef skill)
            {
                Label = label;
                LevelDescriptor = levelDescriptor;
                Level = level;
                FullDescription = fullDescription;
                Skill = skill;
            }
        }

        /// <summary>A grantable (or locked) expertise option, projected to reflection-free data.</summary>
        public readonly struct ExpertiseChoice
        {
            public readonly string Label;       // ExpertiseDef.LabelCap
            public readonly string Description; // ExpertiseDef.description
            public readonly string Effects;     // ExpertiseDef.Effects(1) per-level bonuses, cleaned for speech
            public readonly bool CanApply;      // CanApplyOn result
            public readonly string Reason;      // CanApplyOn out reason (verbatim from VSE); null when CanApply
            public readonly SkillDef Skill;     // ExpertiseDef.skill
            public readonly Def Def;            // the ExpertiseDef, opaque handle passed back to TryGrant

            public ExpertiseChoice(string label, string description, string effects, bool canApply, string reason, SkillDef skill, Def def)
            {
                Label = label;
                Description = description;
                Effects = effects;
                CanApply = canApply;
                Reason = reason;
                Skill = skill;
                Def = def;
            }
        }

        /// <summary>True when the pawn can carry expertise (VSE ready and the pawn has a skill tracker).</summary>
        public static bool IsReady(Pawn pawn)
        {
            return ready && pawn != null && pawn.skills != null;
        }

        /// <summary>
        /// Reads a pawn's owned expertises, each projected to an <see cref="ExpertiseView"/>.
        /// Empty list if the pawn has none or VSE is unavailable.
        /// </summary>
        public static List<ExpertiseView> GetOwnedExpertise(Pawn pawn)
        {
            var result = new List<ExpertiseView>();
            try
            {
                if (!IsReady(pawn))
                    return result;

                object tracker = expertiseForPawnMethod.Invoke(null, new object[] { pawn });
                if (tracker == null)
                    return result;

                if (!(allExpertiseProperty.GetValue(tracker) is IList records))
                    return result;

                foreach (object record in records)
                {
                    if (record == null)
                        continue;
                    result.Add(ToView(record));
                }

                return result;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VseExpertiseCompat.GetOwnedExpertise failed: {ex.Message}");
                return result;
            }
        }

        /// <summary>
        /// True when at least one non-hidden expertise can currently be applied to the
        /// pawn (CanApplyOn == true). Gates whether the "Choose expertise" action appears.
        /// </summary>
        public static bool HasAnyEligible(Pawn pawn)
        {
            try
            {
                if (!IsReady(pawn))
                    return false;

                foreach (object def in AllExpertiseDefs())
                {
                    if (def == null || (bool)defHideField.GetValue(def))
                        continue;
                    if (CanApplyOn(def, pawn, out _))
                        return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VseExpertiseCompat.HasAnyEligible failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// All non-hidden expertise defs the pawn does not already own, each mapped to an
        /// <see cref="ExpertiseChoice"/> via CanApplyOn, sorted eligible-first then by skill
        /// label for stable grouping. Empty list on any failure.
        /// </summary>
        public static List<ExpertiseChoice> GetChoices(Pawn pawn)
        {
            var result = new List<ExpertiseChoice>();
            try
            {
                if (!IsReady(pawn))
                    return result;

                var ownedDefs = new HashSet<object>();
                foreach (object record in OwnedRecords(pawn))
                {
                    object def = recordDefField.GetValue(record);
                    if (def != null)
                        ownedDefs.Add(def);
                }

                foreach (object def in AllExpertiseDefs())
                {
                    if (def == null || (bool)defHideField.GetValue(def) || ownedDefs.Contains(def))
                        continue;

                    bool canApply = CanApplyOn(def, pawn, out string reason);
                    var skill = defSkillField.GetValue(def) as SkillDef;
                    string label = ((Def)def).LabelCap;
                    string description = ((Def)def).description;
                    string effects = EffectsText(def);

                    result.Add(new ExpertiseChoice(label, description, effects, canApply,
                        canApply ? null : reason, skill, (Def)def));
                }

                return result
                    .OrderByDescending(c => c.CanApply)
                    .ThenBy(c => c.Skill?.label ?? string.Empty)
                    .ThenBy(c => c.Label ?? string.Empty)
                    .ToList();
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VseExpertiseCompat.GetChoices failed: {ex.Message}");
                return new List<ExpertiseChoice>();
            }
        }

        /// <summary>
        /// Grants an expertise to a pawn. MUTATION rides VSE's own gate: CanApplyOn (its
        /// Can-twin) is re-checked live and must pass before AddExpertise is invoked.
        /// Returns false (no mutation) when the def is no longer applicable.
        /// </summary>
        public static bool TryGrant(Pawn pawn, Def expertiseDef)
        {
            try
            {
                if (!IsReady(pawn) || expertiseDef == null || !expertiseDefType.IsInstanceOfType(expertiseDef))
                    return false;

                if (!CanApplyOn(expertiseDef, pawn, out _))
                    return false;

                object tracker = expertiseForPawnMethod.Invoke(null, new object[] { pawn });
                if (tracker == null)
                    return false;

                addExpertiseMethod.Invoke(tracker, new object[] { expertiseDef });
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VseExpertiseCompat.TryGrant failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>No-ops unless Ready. Registers the read extender, the action adapter, and the category provider.</summary>
        public static void Register(Harmony harmony)
        {
            if (!ready)
                return;

            PawnSkillsAdapter.RegisterDetailExtender(BuildExpertiseChildren);
            InspectNodeRegistry.RegisterCategoryExtender("Skills", AddChooseExpertiseRow);

            if (panelReady)
            {
                harmony.Patch(doOpenExpertiseButtonMethod,
                    prefix: new HarmonyMethod(typeof(VseExpertiseCompat), nameof(OpenExpertiseButtonPrefix)),
                    postfix: new HarmonyMethod(typeof(VseExpertiseCompat), nameof(OpenExpertiseButtonPostfix)));
            }

        }

        private static bool ShowExpertise
        {
            get { return panelReady && (bool)showExpertiseField.GetValue(null); }
            // MUTATION-C: mirrors VSE.ExpertiseUIUtility.DoExpertisePanel's close button
            // (ShowExpertise = false); the toggle is a bare public static field with no setter.
            set { if (panelReady) showExpertiseField.SetValue(null, value); }
        }

        private static void OpenExpertiseButtonPrefix(ref float x, out (bool shown, float x) __state)
        {
            __state = (ShowExpertise, x);
        }

        /// <summary>Names the tooltip-less icon for the capture; on the toggle flipping open, presents the panel as the picker, whose close closes the panel.</summary>
        private static void OpenExpertiseButtonPostfix(Pawn pawn, ref float x, (bool shown, float x) __state)
        {
            if (x == __state.x)
                return; // Not a colonist: VSE drew nothing.
            TooltipHandler.TipRegion(new UnityEngine.Rect(__state.x, 0f, 30f, 30f), CompatText.ModText("VSE.Expertise"));
            if (__state.shown || !ShowExpertise)
                return;
            if (!OpenExpertisePicker(pawn, onClose: _ => ShowExpertise = false))
            {
                TolkHelper.Speak("RimWorldAccess.Compat.Vse.NoExpertiseAvailable".Loc());
                ShowExpertise = false;
            }
        }

        /// <summary>
        /// Read extender: nests the pawn's owned expertises for this skill under the skill
        /// item, each an expandable row with its level descriptor and full description.
        /// </summary>
        private static void BuildExpertiseChildren(InspectionTreeItem skillItem, Pawn pawn, SkillRecord skill)
        {
            if (!ready || skill == null)
                return;

            try
            {
                List<ExpertiseView> owned = GetOwnedExpertise(pawn);
                if (owned.Count == 0)
                    return;

                int childIndent = skillItem.IndentLevel + 1;
                foreach (ExpertiseView view in owned)
                {
                    if (view.Skill != skill.def)
                        continue;

                    string rowLabel = "RimWorldAccess.Compat.Vse.ExpertiseRow".Translate(view.Label, view.LevelDescriptor);
                    var item = new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.Item,
                        Label = rowLabel,
                        ExpandedLabel = rowLabel,
                        IndentLevel = childIndent,
                        IsExpandable = true,
                        IsExpanded = false
                    };

                    InspectNodeFactory.DetailLines(item, view.FullDescription, redundantWithLabel: view.Label);
                    if (item.Children.Count == 0)
                        item.IsExpandable = false;

                    InspectNodeFactory.Attach(skillItem, item);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VseExpertiseCompat.BuildExpertiseChildren failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Category extender (registered for "Skills"): prepends a "Choose expertise"
        /// action as the lead row for a player colonist who currently qualifies for a new
        /// expertise. Enter opens the picker, an overlay menu that owns its own announcement.
        /// </summary>
        private static void AddChooseExpertiseRow(InspectionTreeItem categoryItem, object obj)
        {
            if (!ready || !(obj is Pawn pawn) || !pawn.IsColonistPlayerControlled)
                return;
            if (!HasAnyEligible(pawn))
                return;

            string label = "RimWorldAccess.Compat.Vse.ChooseExpertise".Translate();
            var row = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Item,
                Label = label,
                ExpandedLabel = label,
                IndentLevel = categoryItem.IndentLevel + 1,
                IsExpandable = false,
                OnActivate = () => OpenExpertisePicker(pawn),
                OpensOverlayMenu = true
            };
            // Runs after the Skills adapter built its skill rows, so insert at the
            // front to lead the category.
            InspectNodeFactory.AttachFirst(categoryItem, row);
        }

        /// <summary>
        /// Opens the accessible expertise picker: every non-owned expertise as a float-menu
        /// option, eligible ones granting through <see cref="TryGrant"/>, locked ones shown
        /// disabled with VSE's own reason.
        /// </summary>
        private static bool OpenExpertisePicker(Pawn pawn, Action<bool> onClose = null)
        {
            List<ExpertiseChoice> choices = GetChoices(pawn);
            if (choices.Count == 0)
                return false;

            var options = new List<FloatMenuOption>();
            foreach (ExpertiseChoice choice in choices)
            {
                // Speak VSE's own description AND its per-level stat effects on each
                // option, matching what its picker shows inline and in the row tooltip.
                string desc = string.IsNullOrEmpty(choice.Description) ? "" : ". " + choice.Description.TrimEnd('.', ' ');
                string fx = string.IsNullOrEmpty(choice.Effects)
                    ? ""
                    : ". " + "RimWorldAccess.Compat.Vse.EffectsPerLevel".Translate(choice.Effects).ToString();
                if (choice.CanApply)
                {
                    Def def = choice.Def;
                    // Rebuild the inspection tree after a grant so the now-obsolete
                    // "Choose expertise" row drops and the new expertise appears under
                    // its skill (the established mutating-inspection-action pattern,
                    // e.g. PawnGearAdapter after a drop). Without this the tree is
                    // stale until the player re-enters it.
                    options.Add(new FloatMenuOption(choice.Label + desc + fx, () =>
                    {
                        if (TryGrant(pawn, def))
                            WindowlessInspectionState.RebuildTree();
                    }));
                }
                else
                {
                    string reason = string.IsNullOrEmpty(choice.Reason) ? "" : ", " + choice.Reason;
                    options.Add(new FloatMenuOption(choice.Label + reason + desc + fx, null) { Disabled = true });
                }
            }

            WindowlessFloatMenuState.Open(options, colonistOrders: false,
                titleText: "RimWorldAccess.Compat.Vse.ChooseExpertise".Translate(),
                infoCardDefs: choices.Select(c => c.Def).ToList(),
                onClose: onClose);
            return true;
        }

        private static ExpertiseView ToView(object record)
        {
            object def = recordDefField.GetValue(record);
            string label = def != null ? ((Def)def).LabelCap.ToString() : string.Empty;
            var skill = def != null ? defSkillField.GetValue(def) as SkillDef : null;
            string levelDescriptor = recordLevelDescriptorProperty.GetValue(record) as string;
            int level = (int)recordLevelProperty.GetValue(record);
            string fullDescription = recordFullDescriptionMethod.Invoke(record, null) as string;
            return new ExpertiseView(label, levelDescriptor, level, fullDescription, skill);
        }

        private static IEnumerable OwnedRecords(Pawn pawn)
        {
            object tracker = expertiseForPawnMethod.Invoke(null, new object[] { pawn });
            if (tracker == null)
                return Array.Empty<object>();
            return allExpertiseProperty.GetValue(tracker) as IEnumerable ?? Array.Empty<object>();
        }

        private static IEnumerable AllExpertiseDefs()
        {
            return allDefsProperty.GetValue(null) as IEnumerable ?? Array.Empty<object>();
        }

        private static bool CanApplyOn(object expertiseDef, Pawn pawn, out string reason)
        {
            object[] args = { pawn, null };
            bool ok = (bool)canApplyOnMethod.Invoke(expertiseDef, args);
            reason = args[1] as string;
            return ok;
        }

        /// <summary>
        /// VSE's per-level stat effects for an expertise (the content of the tooltip its
        /// picker shows on each row), cleaned for speech: leading newline trimmed, line
        /// breaks joined with commas. Empty when the expertise defines no effects.
        /// </summary>
        private static string EffectsText(object expertiseDef)
        {
            string raw = effectsMethod.Invoke(expertiseDef, new object[] { 1, "" }) as string;
            if (string.IsNullOrEmpty(raw))
                return "";
            return raw.Trim().Replace("\n", ", ");
        }
    }
}
