using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Mutation and data logic for one typed precept list (roles, rituals, buildings, relics,
    /// weapons, venerated animals, preferred xenotypes, apparel), opened from the custom-creation
    /// hub, the in-game reform dialog and the Archonexus reform screen. Presentation and keyboard
    /// routing live in <see cref="RimWorldAccess.Shell.IdeoTypedPreceptScreenScope"/>; this class
    /// owns <see cref="BuildTree"/>, the per-kind predicates, the Add-precept reflection call into
    /// <c>IdeoUIUtility.AddPrecept</c> (redirected by <see cref="DialogInterceptionPatch"/>), the
    /// edit-actions menu built from vanilla's own precept-box options plus Regenerate, the shared
    /// post-edit housekeeping mirroring Dialog_EditPrecept.ApplyChanges, and vanilla's name-rule
    /// spec that validates the dialog scope's text sessions.
    /// </summary>
    public static class IdeoTypedPreceptState
    {
        public static bool IsActive { get; private set; }

        /// <summary>The ideo currently being edited. Null while inactive.</summary>
        public static Ideo Ideo { get; private set; }

        /// <summary>Which typed list this instance is editing. Only meaningful while <see cref="IsActive"/>.</summary>
        public static IdeoBuilderHelper.SectionKind Kind { get; private set; }

        /// <summary>
        /// The mod-added precept class this instance is editing, null for every vanilla section.
        /// While set it decides both filters on its own and <see cref="Kind"/> carries no meaning.
        /// </summary>
        public static System.Type PreceptClass { get; private set; }

        /// <summary>The title derived for <see cref="PreceptClass"/>, carried in from the hub so naming this screen sweeps no defs.</summary>
        private static string preceptClassLabel;

        /// <summary>This screen's section title, from whichever of the two halves above applies.</summary>
        public static string SectionLabel
        {
            get
            {
                return PreceptClass != null
                    ? preceptClassLabel
                    : IdeoBuilderHelper.GetLocalizedSectionLabel(Kind);
            }
        }

        private static readonly System.Reflection.MethodInfo AddPreceptMethod =
            AccessTools.Method(typeof(IdeoUIUtility), "AddPrecept");

        // Scenario.playerFaction and ScenPart_PlayerFaction.factionDef are both internal, so the
        // faction argument vanilla's own Regenerate option passes needs reflection.
        private static readonly System.Reflection.FieldInfo ScenarioPlayerFactionField =
            AccessTools.Field(typeof(Scenario), "playerFaction");
        private static readonly System.Reflection.FieldInfo ScenPartFactionDefField =
            AccessTools.Field(typeof(ScenPart_PlayerFaction), "factionDef");

        /// <summary>The scenario's player FactionDef, or null when it has no player-faction scen part; Regenerate accepts null, which is what vanilla passes once a World exists.</summary>
        private static FactionDef PlayerScenarioFactionDef()
        {
            Scenario scenario = Find.Scenario;
            if (scenario == null || ScenarioPlayerFactionField == null || ScenPartFactionDefField == null)
            {
                return null;
            }
            object scenPart = ScenarioPlayerFactionField.GetValue(scenario);
            return scenPart != null ? (FactionDef)ScenPartFactionDefField.GetValue(scenPart) : null;
        }

        public static void Open(Ideo targetIdeo, IdeoBuilderHelper.SectionKind sectionKind,
            System.Type preceptClass = null, string preceptClassSectionLabel = null)
        {
            if (targetIdeo == null) return;
            Ideo = targetIdeo;
            Kind = sectionKind;
            PreceptClass = preceptClass;
            preceptClassLabel = preceptClassSectionLabel;
            IsActive = true;
        }

        public static void Close()
        {
            IsActive = false;
            Ideo = null;
            PreceptClass = null;
            preceptClassLabel = null;
        }

        #region Predicate / filter per kind

        private static Func<Precept, bool> CurrentPreceptPredicate(IdeoBuilderHelper.SectionKind k, System.Type preceptClass)
        {
            if (preceptClass != null) return p => preceptClass.IsInstanceOfType(p) && p.def.visible;
            switch (k)
            {
                // MUTATION-C: mirrors IdeoUIUtility.DoPreceptsInt's own row-gathering loop
                // (decompiled RimWorld/IdeoUIUtility.cs:1436) — vanilla filters every precept
                // category's rows by "showAll || def.visible" (showAll is a DEV-only debug
                // checkbox, defaulting off and out of scope here), applied uniformly across all
                // eight typed-precept categories, not just Rituals.
                case IdeoBuilderHelper.SectionKind.Roles: return p => p is Precept_Role && p.def.visible;
                case IdeoBuilderHelper.SectionKind.Rituals: return p => p.def.preceptClass == typeof(Precept_Ritual) && p.def.visible;
                case IdeoBuilderHelper.SectionKind.Buildings: return p => (p is Precept_Building || p is Precept_RitualSeat) && p.def.visible;
                case IdeoBuilderHelper.SectionKind.Relics: return p => p is Precept_Relic && p.def.visible;
                case IdeoBuilderHelper.SectionKind.Weapons: return p => p is Precept_Weapon && p.def.visible;
                case IdeoBuilderHelper.SectionKind.VeneratedAnimals: return p => p is Precept_Animal && p.def.visible;
                case IdeoBuilderHelper.SectionKind.PreferredXenotypes: return p => p is Precept_Xenotype && p.def.visible;
                case IdeoBuilderHelper.SectionKind.Apparel: return p => p is Precept_Apparel && p.def.visible;
                default: return p => false;
            }
        }

        private static Func<PreceptDef, bool> AddFilter(IdeoBuilderHelper.SectionKind k, System.Type preceptClass)
        {
            if (preceptClass != null) return p => p.preceptClass == preceptClass;
            switch (k)
            {
                case IdeoBuilderHelper.SectionKind.Roles: return p => typeof(Precept_Role).IsAssignableFrom(p.preceptClass);
                case IdeoBuilderHelper.SectionKind.Rituals: return p => p.preceptClass == typeof(Precept_Ritual);
                case IdeoBuilderHelper.SectionKind.Buildings: return p => p.preceptClass == typeof(Precept_Building) || p.preceptClass == typeof(Precept_RitualSeat);
                case IdeoBuilderHelper.SectionKind.Relics: return p => p.preceptClass == typeof(Precept_Relic);
                case IdeoBuilderHelper.SectionKind.Weapons: return p => p.preceptClass == typeof(Precept_Weapon);
                case IdeoBuilderHelper.SectionKind.VeneratedAnimals: return p => p.preceptClass == typeof(Precept_Animal);
                case IdeoBuilderHelper.SectionKind.PreferredXenotypes: return p => p.preceptClass == typeof(Precept_Xenotype);
                case IdeoBuilderHelper.SectionKind.Apparel: return p => p.preceptClass == typeof(Precept_Apparel);
                default: return p => false;
            }
        }

        /// <summary>The ideo's current precepts of this instance's Kind, in ideo order — the scope's row source.</summary>
        public static List<Precept> CurrentPrecepts()
        {
            return Ideo.PreceptsListForReading.Where(CurrentPreceptPredicate(Kind, PreceptClass)).ToList();
        }

        /// <summary>
        /// The one Def a precept carries that vanilla actually gives an info card, for Alt+I; null
        /// where none exists. ThingDef and XenotypeDef are both plainly carded by vanilla, so
        /// Precept_ThingDef's ThingDef, Precept_Apparel's apparelDef and Precept_Xenotype's xenotype
        /// qualify. Its <c>customXenotype</c> sibling does not — that is data, not a Def.
        /// Precept_Weapon and Precept_Ritual carry no single citable Def, so their rows correctly
        /// yield no Alt+I target.
        /// </summary>
        public static Def LinkedDefFor(Precept precept)
        {
            if (precept is Precept_ThingDef ptd && ptd.ThingDef != null) return ptd.ThingDef;
            if (precept is Precept_Xenotype px && px.xenotype != null) return px.xenotype;
            if (precept is Precept_Apparel pa && pa.apparelDef != null) return pa.apparelDef;
            return null;
        }

        #endregion

        #region Detail lines (for the scope's expandable detail rows)

        /// <summary>
        /// One detail line, whether it heads a section, and the section it belongs to. SectionTitle
        /// is null before the first header, on header lines themselves, and on lines vanilla renders
        /// outside every section's colour (see <see cref="IsHintLine"/>).
        /// </summary>
        public struct DetailLine
        {
            public readonly string Text;
            public readonly bool IsHeader;
            public readonly string SectionTitle;
            public DetailLine(string text, bool isHeader, string sectionTitle)
            {
                Text = text;
                IsHeader = isHeader;
                SectionTitle = sectionTitle;
            }
        }

        // The opening tags vanilla wraps section titles and its trailing gray hint line in, built
        // through the same Colorize extension so neither hardcodes a hex, and matched against the raw
        // GetTip() text before CleanGameText strips the markup. No Precept_*.GetTip override other
        // than the hint colorizes a line gray, so this is a generic colour signal, not a special case.
        private static readonly string SectionTitlePrefix = BuildColorPrefix(ColoredText.TipSectionTitleColor);
        private static readonly string HintLinePrefix = BuildColorPrefix(Color.gray);

        private static string BuildColorPrefix(Color color)
        {
            try
            {
                string sample = "x".Colorize(color);
                int gt = sample.IndexOf('>');
                return gt > 0 ? sample.Substring(0, gt + 1) : null;
            }
            catch { return null; }
        }

        private static bool HasColorPrefix(string rawLine, string prefix)
        {
            if (string.IsNullOrEmpty(prefix) || string.IsNullOrEmpty(rawLine)) return false;
            return rawLine.TrimStart().StartsWith(prefix, StringComparison.Ordinal);
        }

        private static bool IsSectionTitleLine(string rawLine) => HasColorPrefix(rawLine, SectionTitlePrefix);

        /// <summary>
        /// Vanilla's gray trailing hint: textually the last line after every section's body, but not
        /// part of the section preceding it, so it must not inherit that section's title.
        /// </summary>
        private static bool IsHintLine(string rawLine) => HasColorPrefix(rawLine, HintLinePrefix);

        /// <summary>
        /// The detail lines for a precept, taken from vanilla's own GetTip, cleaned of markup and
        /// unresolved grammar tokens, with section-title lines flagged and every other line stamped
        /// with its section so the scope can announce a section on crossing into it and Page Up/Down
        /// between sections. For precepts granting abilities, each ability's description is injected
        /// inline through <c>EnhanceWithAbilityDescriptions</c>, since vanilla's tip lists names only.
        /// </summary>
        public static List<DetailLine> BuildPreceptDetailLines(Precept precept)
        {
            var lines = new List<DetailLine>();

            string tip = precept.GetTip();
            if (precept.def != null && !precept.def.grantedAbilities.NullOrEmpty())
                tip = IdeologyHelper.EnhanceWithAbilityDescriptions(precept.def.grantedAbilities, precept.ideo, tip);

            if (!string.IsNullOrEmpty(tip))
            {
                string currentSection = null;
                foreach (var raw in tip.Split('\n'))
                {
                    bool isHeader = IsSectionTitleLine(raw);
                    bool isHint = IsHintLine(raw);
                    string line = IdeoBuilderHelper.CleanGameText(raw);
                    if (string.IsNullOrEmpty(line)) continue;

                    if (isHint)
                    {
                        // Closes out any section context: vanilla renders this line outside them all.
                        lines.Add(new DetailLine(line, false, null));
                        currentSection = null;
                    }
                    else if (isHeader)
                    {
                        currentSection = line;
                        lines.Add(new DetailLine(line, true, null));
                    }
                    else
                    {
                        lines.Add(new DetailLine(line, false, currentSection));
                    }
                }
            }

            return lines;
        }

        #endregion

        #region Tree building

        /// <summary>
        /// One node per current precept at indent 0, each holding a read-only row per non-header
        /// detail line, stamped with <see cref="InspectionTreeItem.SectionTitle"/>. Header lines are
        /// NOT emitted as nodes — they exist only to stamp that title on the rows below, so an
        /// expanded precept does not read its section titles as extra counted rows. The "Add" row is
        /// not a node either; the scope carries it as a prefix row so it never masquerades as a tree
        /// level. Labels carry no live state: the scope folds the detail set into Extras while
        /// collapsed, and the child rows speak it when expanded.
        /// </summary>
        public static InspectionTreeItem BuildTree()
        {
            var root = new InspectionTreeItem
            {
                Label = "Root",
                IndentLevel = -1,
                IsExpandable = true,
                IsExpanded = true,
                Type = InspectionTreeItem.ItemType.Category,
            };
            if (Ideo == null) return root;

            foreach (var precept in CurrentPrecepts())
            {
                var detailLines = BuildPreceptDetailLines(precept);
                // Headers emit no row, so expandability must count only the children that will
                // exist; an all-header tip would otherwise claim to expand into nothing.
                bool hasRealRows = detailLines.Any(l => !l.IsHeader);
                var node = new InspectionTreeItem
                {
                    Label = IdeoBuilderHelper.PreceptLabel(precept),
                    IndentLevel = 0,
                    IsExpandable = hasRealRows,
                    IsExpanded = false,
                    Type = InspectionTreeItem.ItemType.SubCategory,
                    Data = precept,
                    // Not precept.def: vanilla gives no card for a PreceptDef itself.
                    LinkedDef = LinkedDefFor(precept),
                    Parent = root,
                };
                foreach (var line in detailLines)
                {
                    if (line.IsHeader) continue;
                    node.Children.Add(new InspectionTreeItem
                    {
                        Label = line.Text,
                        SectionTitle = line.SectionTitle,
                        IndentLevel = 1,
                        IsExpandable = false,
                        Type = InspectionTreeItem.ItemType.DetailText,
                        Parent = node,
                    });
                }
                root.Children.Add(node);
            }

            return root;
        }

        #endregion

        #region Delete

        /// <summary>
        /// Removes the precept behind vanilla's own removal guard: some precepts cannot be removed
        /// in the UI, and one required by a meme cannot be removed at all. Speaks the outcome and
        /// returns true only on a real removal, so the scope knows to move its cursor off the row.
        /// </summary>
        public static bool TryDeletePrecept(Precept precept)
        {
            if (precept == null) return false;

            if (!precept.def.canRemoveInUI || precept.def.issue.HasDefaultPrecept)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.SpeakData((string)"CannotRemove".Translate() + ": " + IdeoBuilderHelper.PreceptLabel(precept), SpeechPriority.High);
                return false;
            }
            var requiringMeme = Ideo.GetMemeThatRequiresPrecept(precept.def);
            if (requiringMeme != null)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.SpeakData((string)"CannotRemove".Translate() + ": " + (string)"RequiredByMeme".Translate(requiringMeme.label), SpeechPriority.High);
                return false;
            }

            string removedName = IdeoBuilderHelper.PreceptLabel(precept);
            Ideo.RemovePrecept(precept);
            Ideo.anyPreceptEdited = true;
            Ideo.RegenerateDescription();
            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            TolkHelper.SpeakData($"{removedName}, {(string)"RimWorldAccess.Ideology.Builder.Status.Removed".Translate()}");
            return true;
        }

        #endregion

        #region Add precept (reflection into vanilla)

        public static void InvokeAddPrecept()
        {
            if (AddPreceptMethod == null)
            {
                Log.Error("[RimWorld Access] Could not find IdeoUIUtility.AddPrecept");
                return;
            }
            try
            {
                bool group = PreceptClass == null && Kind == IdeoBuilderHelper.SectionKind.Precepts;
                // Vanilla adds a FloatMenu to the WindowStack, which DialogInterceptionPatch converts
                // to a WindowlessFloatMenuState. Mods transpile that construction — one swaps in its
                // own searchable Window past 30 options, which reaches the keyboard only under the
                // guard.
                RimWorldAccess.Shell.ScopeDelegateGuard.Run(delegate
                {
                    AddPreceptMethod.Invoke(null, new object[]
                    {
                        Ideo, IdeoEditMode.GameStart, AddFilter(Kind, PreceptClass), group
                    });
                });
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorld Access] Error invoking AddPrecept: {ex}");
            }
        }

        // Frame on which an edit last ran its silent housekeeping or armed a dialog about to open.
        // Frame-scoped so a stale flag can never suppress a later genuine return.
        private static int suppressReturnReannounceFrame = -1;

        /// <summary>
        /// Arms the suppression window from an entry about to open a window of its own in the same
        /// frame the edit menu pops back, whose dialog is the only voice that should follow.
        /// </summary>
        public static void SuppressNextReturnReannounce()
        {
            suppressReturnReannounceFrame = Time.frameCount;
        }

        /// <summary>
        /// Whether the scope's OnFocus should re-announce the current row when a sub-picker pops back
        /// to it. False when an edit that just ran, or a dialog about to open, already claimed this
        /// frame's voice; true for a genuine return with nothing else said.
        /// </summary>
        public static bool ShouldReannounceOnReturn()
        {
            bool suppress = suppressReturnReannounceFrame >= 0
                && Time.frameCount - suppressReturnReannounceFrame <= 1;
            suppressReturnReannounceFrame = -1;
            return !suppress;
        }

        #endregion

        #region Edit precept (] context menu)

        // Vanilla's precept-name rules: letters, digits, space, apostrophe, hyphen; max 32 chars.
        private static readonly Regex ValidPreceptNameRegex = new Regex("^[\\p{L}0-9 '\\-]*$");
        private const int MaxPreceptNameLength = 32;

        public static TextFieldSpec PreceptNameSpec(string labelKey) =>
            new TextFieldSpec(labelKey, maxLength: MaxPreceptNameLength, minLength: 1, allowedChars: ValidPreceptNameRegex);

        /// <summary>
        /// The edit-actions menu for the focused precept, built option for option out of vanilla's
        /// own precept-box options plus Regenerate. Roles, relics, buildings and rituals yield one
        /// "Edit..." opening the real <c>Dialog_EditPrecept</c>; weapons, apparel and ritual seats
        /// yield inline edits that mutate the precept without a dialog; animals and xenotypes yield
        /// none. Removal is not here — it is the scope's Delete key behind the same vanilla guards.
        ///
        /// An inline edit never closes the menu, since vanilla's precept editor is one dialog whose
        /// fields cannot dismiss it. Each such entry ends by calling <paramref name="reopenAt"/> with
        /// its index and chosen label, and that re-opened landing is the action's one utterance. The
        /// label rides back because the inline family's ENTRY SET changes with the value chosen —
        /// vanilla omits the option matching the current state — so the index alone cannot be
        /// trusted. The "Edit..." entry instead arms <see cref="SuppressNextReturnReannounce"/>, so
        /// the dialog it opens is what speaks next.
        /// </summary>
        public static List<FloatMenuOption> BuildEditOptions(Precept precept, Action<int, string> reopenAt)
        {
            var options = new List<FloatMenuOption>();

            // Each option is wrapped, never replaced, so it keeps every field vanilla set.
            var vanilla = precept.EditFloatMenuOptions();
            if (vanilla != null)
            {
                bool inline = HasInlineEditOptions(precept);
                foreach (var opt in vanilla)
                {
                    int entryIndex = options.Count;
                    Action vanillaAction = opt.action;
                    string chosenLabel = opt.Label;
                    if (vanillaAction != null)
                    {
                        opt.action = inline
                            ? (Action)(() => { vanillaAction(); reopenAt(entryIndex, chosenLabel); })
                            : (Action)(() => { SuppressNextReturnReannounce(); vanillaAction(); });
                    }
                    options.Add(opt);
                }
            }

            // Rides vanilla's Regenerate behind its own CanRegenerate twin, with the same faction
            // argument vanilla passes: null once a World exists, the scenario's faction in worldgen.
            if (precept.CanRegenerate)
            {
                int regenerateIndex = options.Count;
                options.Add(new FloatMenuOption("Regenerate".Translate().CapitalizeFirst(), () =>
                {
                    precept.Regenerate(Ideo, Find.World == null ? PlayerScenarioFactionDef() : null);
                    AfterPreceptEditSilent();
                    // The one entry whose label cannot state what changed, so the regenerated
                    // precept's new label rides in as the re-opened menu's heading.
                    reopenAt(regenerateIndex, IdeoBuilderHelper.PreceptLabel(precept));
                }));
            }

            return options;
        }

        // Types whose EditFloatMenuOptions mutate the precept inline (no dialog).
        private static bool HasInlineEditOptions(Precept p) =>
            p is Precept_Weapon || p is Precept_Apparel || p is Precept_RitualSeat;

        #endregion

        /// <summary>
        /// Post-edit refresh for every edit reached from the edit menu, mirroring
        /// Dialog_EditPrecept.ApplyChanges. Silent: the menu re-opens on the edited entry whose label
        /// carries the new value, so this arms the return-re-announce off and leaves that landing as
        /// the action's single utterance.
        /// </summary>
        public static void AfterPreceptEditSilent()
        {
            ApplyPreceptEditHousekeeping();
            suppressReturnReannounceFrame = Time.frameCount;
        }

        /// <summary>The non-speaking half of <see cref="AfterPreceptEditSilent"/>.</summary>
        private static void ApplyPreceptEditHousekeeping()
        {
            foreach (var p in Ideo.PreceptsListForReading)
                p.ClearTipCache();
            Ideo.anyPreceptEdited = true;
            Ideo.RegenerateDescription();
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
        }
    }
}
