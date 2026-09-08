using System.Collections;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Simple Sidearms' Gizmo_SidearmsList is an invisible-button toolbar (preference selector,
    /// carried rows, missing-weapon ghosts, unarmed slot) whose ProcessInput is a no-op without a
    /// mouse-set interaction. Presents it as status plus description, and as one options menu
    /// firing the mod's own handleInteraction; options mirror its branch conditions exactly, so a
    /// no-op click, or a blocked weapon (no click target drawn), gets no option, and drafted state
    /// swaps the set. Enter opens the same menu.
    /// </summary>
    internal sealed class SidearmsListGizmoHandler : GizmoHandlerBase
    {
        private const int LeftClick = 0;
        private const int RightClick = 1;

        private sealed class WeaponEntry
        {
            public ThingWithComps Weapon;
            public object Pair;
            public bool Memorised;
            public bool Duplicate;
            public bool Blocked;
            public string BlockedReason;
            public bool Offhand;
        }

        private sealed class MemoryEntry
        {
            public object Pair;
            public int Count;
        }

        private sealed class Model
        {
            public Pawn Pawn;
            public object Comp;
            public bool Drafted;
            public readonly List<WeaponEntry> Ranged = new List<WeaponEntry>();
            public readonly List<WeaponEntry> Melee = new List<WeaponEntry>();
            public readonly List<MemoryEntry> MissingRanged = new List<MemoryEntry>();
            public readonly List<MemoryEntry> MissingMelee = new List<MemoryEntry>();
            public object Forced;
            public object ForcedDrafted;
            public object DefaultRanged;
            public object PreferredMelee;
            public bool PreferredUnarmed;
            public bool ForcedUnarmed;
            public bool ForcedUnarmedDrafted;
            public string Mode;
        }

        private static Model BuildModel(Gizmo gizmo)
        {
            Pawn pawn = SidearmsReflection.GizmoParent.GetValue(gizmo) as Pawn;
            if (pawn == null)
            {
                return null;
            }
            object comp = SidearmsReflection.GetMemoryComp(pawn);
            if (comp == null)
            {
                return null;
            }
            Model m = new Model
            {
                Pawn = pawn,
                Comp = comp,
                Drafted = pawn.Drafted,
                Forced = SidearmsReflection.CompForcedWeapon.GetValue(comp),
                ForcedDrafted = SidearmsReflection.CompForcedWeaponWhileDrafted.GetValue(comp),
                DefaultRanged = SidearmsReflection.CompDefaultRangedWeapon.GetValue(comp),
                PreferredMelee = SidearmsReflection.CompPreferredMeleeWeapon.GetValue(comp),
                PreferredUnarmed = (bool)SidearmsReflection.CompPreferredUnarmed.GetValue(comp),
                ForcedUnarmed = (bool)SidearmsReflection.CompForcedUnarmed.GetValue(comp),
                ForcedUnarmedDrafted = (bool)SidearmsReflection.CompForcedUnarmedWhileDrafted.GetValue(comp),
                Mode = SidearmsReflection.CompPrimaryWeaponMode.GetValue(comp)?.ToString() ?? "",
            };
            BuildRow(gizmo, pawn, SidearmsReflection.GizmoCarriedRanged, SidearmsReflection.GizmoRangedMemories,
                m.Ranged, m.MissingRanged);
            BuildRow(gizmo, pawn, SidearmsReflection.GizmoCarriedMelee, SidearmsReflection.GizmoMeleeMemories,
                m.Melee, m.MissingMelee);
            return m;
        }

        /// <summary>DrawRangedList/DrawMeleeList's walk: each carried weapon consumes one matching
        /// memory (memorised vs loose), leftovers group into missing entries, repeats are duplicates.</summary>
        private static void BuildRow(Gizmo gizmo, Pawn pawn, System.Reflection.FieldInfo carriedField,
            System.Reflection.FieldInfo memoriesField, List<WeaponEntry> carried, List<MemoryEntry> missing)
        {
            bool allowBlocked = (bool)SidearmsReflection.SettingsAllowBlockedWeaponUse.GetValue(SidearmsReflection.Settings);
            List<object> unsatisfied = SidearmsReflection.SnapshotPairs(memoriesField.GetValue(gizmo));
            List<object> seen = new List<object>();
            if (carriedField.GetValue(gizmo) is IEnumerable weapons)
            {
                foreach (object item in weapons)
                {
                    if (!(item is ThingWithComps weapon))
                    {
                        continue;
                    }
                    object pair = SidearmsReflection.ToPair(weapon);
                    WeaponEntry entry = new WeaponEntry
                    {
                        Weapon = weapon,
                        Pair = pair,
                        Duplicate = ContainsPair(seen, pair),
                        Offhand = SidearmsReflection.IsOffHand(weapon),
                    };
                    seen.Add(pair);
                    int memoryIndex = IndexOfPair(unsatisfied, pair);
                    if (memoryIndex >= 0)
                    {
                        unsatisfied.RemoveAt(memoryIndex);
                        entry.Memorised = true;
                    }
                    object[] args = { weapon, pawn, null };
                    bool canUse = (bool)SidearmsReflection.StatCanUseSidearmInstance.Invoke(null, args);
                    entry.Blocked = !canUse && !allowBlocked;
                    entry.BlockedReason = args[2] as string;
                    carried.Add(entry);
                }
            }
            for (int i = 0; i < unsatisfied.Count; i++)
            {
                object pair = unsatisfied[i];
                int existing = -1;
                for (int j = 0; j < missing.Count; j++)
                {
                    if (SidearmsReflection.PairEquals(missing[j].Pair, pair))
                    {
                        existing = j;
                        break;
                    }
                }
                if (existing >= 0)
                {
                    missing[existing].Count++;
                }
                else
                {
                    missing.Add(new MemoryEntry { Pair = pair, Count = 1 });
                }
            }
        }

        private static bool ContainsPair(List<object> pairs, object pair)
        {
            return IndexOfPair(pairs, pair) >= 0;
        }

        private static int IndexOfPair(List<object> pairs, object pair)
        {
            for (int i = 0; i < pairs.Count; i++)
            {
                if (SidearmsReflection.PairEquals(pairs[i], pair))
                {
                    return i;
                }
            }
            return -1;
        }

        // ----- facets -----

        public override bool TryGetLabel(Gizmo gizmo, out string label)
        {
            label = null;
            Command command = gizmo as Command;
            Pawn pawn = SidearmsReflection.GizmoParent.GetValue(gizmo) as Pawn;
            if (command == null || pawn == null)
            {
                return false;
            }
            // The mod appends the marker untranslated (DrawGizmoLabel), so parity keeps it verbatim.
            label = pawn.IsColonistPlayerControlled ? command.Label : command.Label + " (godmode)";
            return true;
        }

        public override bool TryGetStatus(Gizmo gizmo, out string status)
        {
            status = null;
            Model m = BuildModel(gizmo);
            if (m == null)
            {
                return false;
            }
            string modeKey = ModeStatusKey(m);
            if (modeKey == null)
            {
                return false;
            }
            status = modeKey.Translate().ToString();
            return true;
        }

        private static string ModeStatusKey(Model m)
        {
            switch (m.Mode)
            {
                case "Ranged":
                    return "RimWorldAccess.Compat.SimpleSidearms.StatusPrefRanged";
                case "Melee":
                    return "RimWorldAccess.Compat.SimpleSidearms.StatusPrefMelee";
                case "ByGenerated":
                    return "RimWorldAccess.Compat.SimpleSidearms.StatusPrefGenerated";
                case "BySkill":
                    object resolved = SidearmsReflection.ExtSkillWeaponPreference?.Invoke(null, new object[] { m.Pawn });
                    return resolved?.ToString() == "Melee"
                        ? "RimWorldAccess.Compat.SimpleSidearms.StatusPrefSkillMelee"
                        : "RimWorldAccess.Compat.SimpleSidearms.StatusPrefSkillRanged";
                default:
                    return null;
            }
        }

        public override bool TryGetDescription(Gizmo gizmo, out string description)
        {
            description = null;
            Model m = BuildModel(gizmo);
            if (m == null)
            {
                return false;
            }
            List<string> sentences = new List<string>();
            Command command = gizmo as Command;
            // The ctor's Desc key is undefined in the mod's English (dev mode speaks it decorated).
            if (!string.IsNullOrEmpty(command?.Desc) && "DrawSidearm_gizmoTooltip".CanTranslate())
            {
                sentences.Add(command.Desc.TrimEnd('.'));
            }
            AppendRowSentences(sentences, m, m.Ranged, m.MissingRanged,
                "RimWorldAccess.Compat.SimpleSidearms.DescCarriedRanged");
            AppendRowSentences(sentences, m, m.Melee, m.MissingMelee,
                "RimWorldAccess.Compat.SimpleSidearms.DescCarriedMelee");
            string unarmed = UnarmedStateKey(m);
            if (unarmed != null)
            {
                sentences.Add(unarmed.Translate().ToString());
            }
            if (sentences.Count == 0)
            {
                return false;
            }
            description = string.Join(". ", sentences) + ".";
            return true;
        }

        private static string UnarmedStateKey(Model m)
        {
            if (m.ForcedUnarmedDrafted && m.Drafted)
            {
                return "RimWorldAccess.Compat.SimpleSidearms.DescUnarmedForcedDrafted";
            }
            if (m.ForcedUnarmed)
            {
                return "RimWorldAccess.Compat.SimpleSidearms.DescUnarmedForced";
            }
            if (m.PreferredUnarmed)
            {
                return "RimWorldAccess.Compat.SimpleSidearms.DescUnarmedPreferred";
            }
            return null;
        }

        private static void AppendRowSentences(List<string> sentences, Model m,
            List<WeaponEntry> carried, List<MemoryEntry> missing, string groupKey)
        {
            if (carried.Count > 0)
            {
                List<string> entries = new List<string>();
                for (int i = 0; i < carried.Count; i++)
                {
                    entries.Add(DescribeWeapon(m, carried[i]));
                }
                sentences.Add(groupKey.Translate(string.Join("; ", entries)).ToString());
            }
            if (missing.Count > 0)
            {
                List<string> entries = new List<string>();
                for (int i = 0; i < missing.Count; i++)
                {
                    string label = SidearmsReflection.PairLabelCap(missing[i].Pair);
                    entries.Add(missing[i].Count > 1
                        ? "RimWorldAccess.Compat.SimpleSidearms.MissingWithCount".Translate(label, missing[i].Count).ToString()
                        : label);
                }
                sentences.Add("RimWorldAccess.Compat.SimpleSidearms.DescMissing".Translate(string.Join("; ", entries)).ToString());
            }
        }

        private static string DescribeWeapon(Model m, WeaponEntry e)
        {
            StringBuilder sb = new StringBuilder(e.Weapon.LabelCap);
            bool primary = m.Pawn.equipment?.Primary == e.Weapon;
            if (primary)
            {
                AppendMark(sb, "RimWorldAccess.Compat.SimpleSidearms.MarkEquipped");
            }
            else if (e.Offhand)
            {
                AppendMark(sb, "RimWorldAccess.Compat.SimpleSidearms.MarkOffhand");
            }
            if (primary && SidearmsReflection.PairEquals(m.ForcedDrafted, e.Pair))
            {
                AppendMark(sb, "RimWorldAccess.Compat.SimpleSidearms.MarkForcedDrafted");
            }
            if (primary && SidearmsReflection.PairEquals(m.Forced, e.Pair))
            {
                AppendMark(sb, "RimWorldAccess.Compat.SimpleSidearms.MarkForced");
            }
            if (!e.Duplicate)
            {
                if (e.Weapon.def.IsRangedWeapon && SidearmsReflection.PairEquals(m.DefaultRanged, e.Pair))
                {
                    AppendMark(sb, "RimWorldAccess.Compat.SimpleSidearms.MarkDefaultRanged");
                }
                else if (SidearmsReflection.PairEquals(m.PreferredMelee, e.Pair))
                {
                    AppendMark(sb, "RimWorldAccess.Compat.SimpleSidearms.MarkPreferredMelee");
                }
            }
            if (SidearmsReflection.IsToolNotWeapon(e.Pair))
            {
                AppendMark(sb, "RimWorldAccess.Compat.SimpleSidearms.MarkTool");
            }
            if ((bool)SidearmsReflection.FilterIsManualUse.Invoke(null, new object[] { e.Weapon }))
            {
                AppendMark(sb, "RimWorldAccess.Compat.SimpleSidearms.MarkManualUse");
            }
            if ((bool)SidearmsReflection.FilterIsEmpWeapon.Invoke(null, new object[] { e.Weapon }))
            {
                AppendMark(sb, "RimWorldAccess.Compat.SimpleSidearms.MarkEmp");
            }
            if ((bool)SidearmsReflection.FilterIsDangerousWeapon.Invoke(null, new object[] { e.Weapon }))
            {
                AppendMark(sb, "RimWorldAccess.Compat.SimpleSidearms.MarkDangerous");
            }
            if (e.Blocked)
            {
                sb.Append(", ");
                sb.Append("RimWorldAccess.Compat.SimpleSidearms.MarkBlocked".Translate(e.BlockedReason ?? "").ToString());
            }
            return sb.ToString();
        }

        private static void AppendMark(StringBuilder sb, string key)
        {
            sb.Append(", ").Append(key.Translate().ToString());
        }

        // ----- actions -----

        public override bool TryExecute(Gizmo gizmo, GizmoHandlerContext ctx, out bool runEpilogue)
        {
            runEpilogue = false;
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            if (!TryGetExtraOptions(gizmo, options) || options.Count == 0)
            {
                return false;
            }
            WindowlessFloatMenuState.OpenTitled((gizmo as Command)?.Label, options);
            return true;
        }

        public override bool TryGetExtraOptions(Gizmo gizmo, List<FloatMenuOption> options)
        {
            Model m = BuildModel(gizmo);
            if (m == null)
            {
                return false;
            }
            int before = options.Count;
            AddPreferenceOptions(gizmo, m, options);
            AddCarriedOptions(gizmo, m, m.Ranged, options);
            AddMemoryOptions(gizmo, m, m.MissingRanged, ranged: true, options);
            AddCarriedOptions(gizmo, m, m.Melee, options);
            AddMemoryOptions(gizmo, m, m.MissingMelee, ranged: false, options);
            AddUnarmedOptions(gizmo, m, options);
            AddSettingsOption(options);
            return options.Count > before;
        }

        private static void AddPreferenceOptions(Gizmo gizmo, Model m, List<FloatMenuOption> options)
        {
            AddPreference(gizmo, m, options, "Ranged", "SelectorRanged",
                "RimWorldAccess.Compat.SimpleSidearms.OptPreferRanged");
            if (m.Pawn.skills != null)
            {
                AddPreference(gizmo, m, options, "BySkill", "SelectorSkill",
                    "RimWorldAccess.Compat.SimpleSidearms.OptPreferSkill");
            }
            AddPreference(gizmo, m, options, "Melee", "SelectorMelee",
                "RimWorldAccess.Compat.SimpleSidearms.OptPreferMelee");
        }

        private static void AddPreference(Gizmo gizmo, Model m, List<FloatMenuOption> options,
            string mode, string interaction, string labelKey)
        {
            string label = labelKey.Translate().ToString();
            if (m.Mode == mode)
            {
                label = "RimWorldAccess.Compat.SimpleSidearms.CurrentSuffix".Translate(label).ToString();
            }
            options.Add(new FloatMenuOption(label,
                () => SidearmsReflection.FireInteraction(gizmo, interaction, LeftClick)));
        }

        private static void AddCarriedOptions(Gizmo gizmo, Model m, List<WeaponEntry> row, List<FloatMenuOption> options)
        {
            for (int i = 0; i < row.Count; i++)
            {
                WeaponEntry e = row[i];
                if (e.Blocked)
                {
                    continue;
                }
                string interaction = e.Memorised ? "Weapon" : "UnmemorisedWeapon";
                string name = e.Weapon.LabelCap;
                AddWeaponFire(options, LeftLabelFor(m, e, name), gizmo, interaction, LeftClick, e);
                string rightLabel = RightLabelFor(m, e, name);
                if (rightLabel != null)
                {
                    AddWeaponFire(options, rightLabel, gizmo, interaction, RightClick, e);
                }
                if (CanEquipAsOffhand(m, e))
                {
                    AddWeaponFire(options,
                        "RimWorldAccess.Compat.SimpleSidearms.OptEquipOffhand".Translate(name).ToString(),
                        gizmo, interaction, LeftClick, e, asOffhand: true);
                }
            }
        }

        private static void AddWeaponFire(List<FloatMenuOption> options, string label, Gizmo gizmo,
            string interaction, int button, WeaponEntry e, bool asOffhand = false)
        {
            options.Add(new FloatMenuOption(label,
                () => SidearmsReflection.FireInteraction(gizmo, interaction, button,
                    weapon: e.Weapon, asOffhand: asOffhand, isDuplicate: e.Duplicate)));
        }

        private static string LeftLabelFor(Model m, WeaponEntry e, string name)
        {
            if (m.Drafted)
            {
                return "RimWorldAccess.Compat.SimpleSidearms.OptForceDrafted".Translate(name).ToString();
            }
            if (!e.Memorised)
            {
                return "RimWorldAccess.Compat.SimpleSidearms.OptRemember".Translate(name).ToString();
            }
            if (SidearmsReflection.PairEquals(m.DefaultRanged, e.Pair)
                || SidearmsReflection.PairEquals(m.PreferredMelee, e.Pair)
                || SidearmsReflection.IsToolNotWeapon(e.Pair))
            {
                return "RimWorldAccess.Compat.SimpleSidearms.OptForce".Translate(name).ToString();
            }
            return e.Weapon.def.IsRangedWeapon
                ? "RimWorldAccess.Compat.SimpleSidearms.OptSetDefaultRanged".Translate(name).ToString()
                : "RimWorldAccess.Compat.SimpleSidearms.OptSetPreferredMelee".Translate(name).ToString();
        }

        private static string RightLabelFor(Model m, WeaponEntry e, string name)
        {
            if (e.Offhand)
            {
                return "RimWorldAccess.Compat.SimpleSidearms.OptUnequipOffhand".Translate(name).ToString();
            }
            bool primary = m.Pawn.equipment?.Primary == e.Weapon;
            if (m.Drafted)
            {
                if (SidearmsReflection.PairEquals(m.ForcedDrafted, e.Pair) && primary)
                {
                    return "RimWorldAccess.Compat.SimpleSidearms.OptStopForcing".Translate(name).ToString();
                }
                // A memorised weapon's drafted right-click is otherwise a no-op branch.
                return e.Memorised ? null
                    : "RimWorldAccess.Compat.SimpleSidearms.OptDrop".Translate(name).ToString();
            }
            if (!e.Memorised)
            {
                return "RimWorldAccess.Compat.SimpleSidearms.OptDrop".Translate(name).ToString();
            }
            if (SidearmsReflection.PairEquals(m.Forced, e.Pair) && primary)
            {
                return "RimWorldAccess.Compat.SimpleSidearms.OptStopForcing".Translate(name).ToString();
            }
            if (e.Weapon.def.IsRangedWeapon && SidearmsReflection.PairEquals(m.DefaultRanged, e.Pair))
            {
                return "RimWorldAccess.Compat.SimpleSidearms.OptUnsetDefaultRanged".Translate(name).ToString();
            }
            if (SidearmsReflection.PairEquals(m.PreferredMelee, e.Pair))
            {
                return "RimWorldAccess.Compat.SimpleSidearms.OptUnsetPreferredMelee".Translate(name).ToString();
            }
            return "RimWorldAccess.Compat.SimpleSidearms.OptDropForget".Translate(name).ToString();
        }

        /// <summary>Tacticowl's corner hotspot, mirrored from DrawIconForWeapon's gate chain.</summary>
        private static bool CanEquipAsOffhand(Model m, WeaponEntry e)
        {
            if (!SidearmsReflection.TacticowlIsActive || e.Offhand)
            {
                return false;
            }
            ThingWithComps primary = m.Pawn.equipment?.Primary;
            ThingDef weaponDef = SidearmsReflection.PairThingDef(e.Pair) ?? e.Weapon.def;
            return primary != null && primary != e.Weapon
                && SidearmsReflection.DualWieldActive()
                && SidearmsReflection.CanBeOffHand(weaponDef)
                && !SidearmsReflection.IsTwoHanded(primary.def)
                && (!SidearmsReflection.VfeIsActive || SidearmsReflection.OffHandShield(m.Pawn) == null);
        }

        private static void AddMemoryOptions(Gizmo gizmo, Model m, List<MemoryEntry> missing,
            bool ranged, List<FloatMenuOption> options)
        {
            for (int i = 0; i < missing.Count; i++)
            {
                MemoryEntry entry = missing[i];
                string name = SidearmsReflection.PairLabelCap(entry.Pair);
                if (!m.Drafted)
                {
                    string leftKey = ranged
                        ? "RimWorldAccess.Compat.SimpleSidearms.OptSetDefaultRanged"
                        : "RimWorldAccess.Compat.SimpleSidearms.OptSetPreferredMelee";
                    AddMemoryFire(options, leftKey.Translate(name).ToString(), gizmo, LeftClick, entry);
                }
                string rightLabel = MemoryRightLabelFor(m, entry, ranged, name);
                if (rightLabel != null)
                {
                    AddMemoryFire(options, rightLabel, gizmo, RightClick, entry);
                }
            }
        }

        private static void AddMemoryFire(List<FloatMenuOption> options, string label, Gizmo gizmo,
            int button, MemoryEntry entry)
        {
            options.Add(new FloatMenuOption(label,
                () => SidearmsReflection.FireInteraction(gizmo, "WeaponMemory", button, weaponType: entry.Pair)));
        }

        private static string MemoryRightLabelFor(Model m, MemoryEntry entry, bool ranged, string name)
        {
            if (m.Drafted)
            {
                return SidearmsReflection.PairEquals(m.ForcedDrafted, entry.Pair)
                    ? "RimWorldAccess.Compat.SimpleSidearms.OptStopForcing".Translate(name).ToString()
                    : null;
            }
            if (SidearmsReflection.PairEquals(m.Forced, entry.Pair))
            {
                return "RimWorldAccess.Compat.SimpleSidearms.OptStopForcing".Translate(name).ToString();
            }
            if (ranged && SidearmsReflection.PairEquals(m.DefaultRanged, entry.Pair))
            {
                return "RimWorldAccess.Compat.SimpleSidearms.OptUnsetDefaultRanged".Translate(name).ToString();
            }
            if (SidearmsReflection.PairEquals(m.PreferredMelee, entry.Pair))
            {
                return "RimWorldAccess.Compat.SimpleSidearms.OptUnsetPreferredMelee".Translate(name).ToString();
            }
            return "RimWorldAccess.Compat.SimpleSidearms.OptForget".Translate(name).ToString();
        }

        private static void AddUnarmedOptions(Gizmo gizmo, Model m, List<FloatMenuOption> options)
        {
            string leftKey;
            if (m.Drafted)
            {
                leftKey = "RimWorldAccess.Compat.SimpleSidearms.OptForceUnarmedDrafted";
            }
            else if (m.PreferredUnarmed)
            {
                leftKey = "RimWorldAccess.Compat.SimpleSidearms.OptForceUnarmed";
            }
            else
            {
                leftKey = "RimWorldAccess.Compat.SimpleSidearms.OptPreferUnarmed";
            }
            options.Add(new FloatMenuOption(leftKey.Translate().ToString(),
                () => SidearmsReflection.FireInteraction(gizmo, "Unarmed", LeftClick)));

            string rightKey = null;
            if (m.Drafted && m.ForcedUnarmedDrafted)
            {
                rightKey = "RimWorldAccess.Compat.SimpleSidearms.OptStopForcingUnarmed";
            }
            else if (m.ForcedUnarmed)
            {
                rightKey = "RimWorldAccess.Compat.SimpleSidearms.OptStopForcingUnarmed";
            }
            else if (m.PreferredUnarmed)
            {
                rightKey = "RimWorldAccess.Compat.SimpleSidearms.OptUnsetPreferUnarmed";
            }
            if (rightKey != null)
            {
                options.Add(new FloatMenuOption(rightKey.Translate().ToString(),
                    () => SidearmsReflection.FireInteraction(gizmo, "Unarmed", RightClick)));
            }
        }

        private static void AddSettingsOption(List<FloatMenuOption> options)
        {
            object settings = SidearmsReflection.Settings;
            if (settings == null || (bool)SidearmsReflection.SettingsEverOpened.GetValue(settings))
            {
                return;
            }
            options.Add(new FloatMenuOption(
                "RimWorldAccess.Compat.SimpleSidearms.OptOpenSettings".Translate().ToString(),
                () =>
                {
                    if (SidearmsReflection.ModSingleton.GetValue(null) is Mod mod)
                    {
                        Find.WindowStack.Add(new Dialog_ModSettings(mod));
                    }
                }));
        }
    }
}
