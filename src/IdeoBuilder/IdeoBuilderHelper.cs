using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Section model and builders for the IdeoBuilder hub, a flat menu with one row per editable
    /// facet of the ideoligion. Row labels come from the game's own translation keys and the live
    /// Ideo's current values, so the hub stays in sync as edits happen and stays localized.
    /// </summary>
    public static class IdeoBuilderHelper
    {
        public enum SectionKind
        {
            Name,
            Adjective,
            MemberName,
            WorshipRoom,
            Description,
            Culture,
            Styles,
            Icon,
            Color,
            StructureMeme,
            NormalMemes,
            Deities,
            Precepts,
            Roles,
            Rituals,
            Buildings,
            Relics,
            Weapons,
            VeneratedAnimals,
            PreferredXenotypes,
            Apparel,
            Appearance,
            Continue,
        }

        public class HubSection
        {
            public SectionKind Kind;
            public string Label;
            public string ValueSummary;
            public bool Disabled;
            public string DisabledReason;
            public List<Def> InspectableDefs;

            /// <summary>
            /// Set only on a section discovered from a mod's precept class
            /// (<see cref="DiscoverExtraPreceptClasses"/>). <see cref="Kind"/> carries no meaning
            /// while this is non-null, so every consumer must branch on this field first.
            /// </summary>
            public System.Type PreceptClass;
        }

        /// <summary>
        /// The precept classes the typed sections claim, matched by <c>IsInstanceOfType</c> as those
        /// sections match their own rows, so a subclass belongs to its base's section.
        /// <c>typeof(Precept)</c> is deliberately absent — every other class derives from it, so
        /// assignability here would claim the whole hierarchy; it is claimed by exact match in
        /// <see cref="IsClaimedByTypedSection"/>.
        /// </summary>
        private static readonly System.Type[] TypedSectionPreceptClasses =
        {
            typeof(Precept_Role),
            typeof(Precept_Ritual),
            typeof(Precept_Building),
            typeof(Precept_RitualSeat),
            typeof(Precept_Relic),
            typeof(Precept_Weapon),
            typeof(Precept_Animal),
            typeof(Precept_Xenotype),
            typeof(Precept_Apparel),
        };

        public static List<HubSection> BuildSections(Ideo ideo)
        {
            var sections = new List<HubSection>();
            if (ideo == null) return sections;

            sections.Add(BuildName(ideo));
            sections.Add(BuildAdjective(ideo));
            sections.Add(BuildMemberName(ideo));
            sections.Add(BuildWorshipRoom(ideo));
            sections.Add(BuildDescription(ideo));
            sections.Add(BuildCulture(ideo));
            sections.Add(BuildStyles(ideo));
            sections.Add(BuildIcon(ideo));
            sections.Add(BuildColor(ideo));
            sections.Add(BuildStructureMeme(ideo));
            sections.Add(BuildNormalMemes(ideo));

            if (ideo.foundation is IdeoFoundation_Deity)
                sections.Add(BuildDeities(ideo));

            List<DiscoveredPreceptClass> extraPreceptClasses = DiscoverExtraPreceptClasses(ideo);

            sections.Add(BuildPrecepts(ideo, extraPreceptClasses));
            sections.Add(BuildPreceptType(ideo, SectionKind.Roles, "IdeoRoles", typeof(Precept_Role)));
            sections.Add(BuildPreceptType(ideo, SectionKind.Rituals, "Rituals", typeof(Precept_Ritual)));
            sections.Add(BuildBuildingsSection(ideo));
            sections.Add(BuildPreceptType(ideo, SectionKind.Relics, "IdeoRelics", typeof(Precept_Relic)));
            sections.Add(BuildPreceptType(ideo, SectionKind.Weapons, "IdeoWeapons", typeof(Precept_Weapon)));
            sections.Add(BuildPreceptType(ideo, SectionKind.VeneratedAnimals, "VeneratedAnimals", typeof(Precept_Animal)));

            if (ModsConfig.BiotechActive)
                sections.Add(BuildPreceptType(ideo, SectionKind.PreferredXenotypes, "PreferredXenotypes", typeof(Precept_Xenotype)));

            sections.Add(BuildPreceptType(ideo, SectionKind.Apparel, "IdeoApparel", typeof(Precept_Apparel)));

            // Ahead of Appearance, matching where a mod's section lands on screen: vanilla draws
            // every precept category before the appearance boxes, and a mod's DoPrecepts postfix
            // paints its own category at the end of that block.
            foreach (DiscoveredPreceptClass discovered in extraPreceptClasses)
                sections.Add(BuildDiscoveredPreceptType(ideo, discovered));

            sections.Add(BuildAppearance(ideo));

            return sections;
        }

        #region Mod-added precept classes

        /// <summary>A mod-added precept class, paired with the section title derived for it.</summary>
        public class DiscoveredPreceptClass
        {
            public System.Type PreceptClass;
            public string Label;
        }

        /// <summary>The (issue label, defName) a discovered class orders by, compared in two stages so no separator character is needed.</summary>
        private struct PreceptClassOrder : System.IComparable<PreceptClassOrder>
        {
            private readonly string issueLabel;
            private readonly string defName;

            public PreceptClassOrder(string issueLabel, string defName)
            {
                this.issueLabel = issueLabel ?? "";
                this.defName = defName ?? "";
            }

            public int CompareTo(PreceptClassOrder other)
            {
                int byIssue = string.CompareOrdinal(issueLabel, other.issueLabel);
                return byIssue != 0 ? byIssue : string.CompareOrdinal(defName, other.defName);
            }
        }

        /// <summary>
        /// What one discovered class accumulates over the single pass across the precept defs: the
        /// labels its title derives from, its sort position, and whether this ideoligion can list any
        /// of its defs at all.
        /// </summary>
        private class DiscoveredClassBuilder
        {
            private readonly List<IssueDef> issues = new List<IssueDef>();
            private readonly List<string> labels = new List<string>();
            private PreceptClassOrder order;
            private bool ordered;

            public bool Listable;

            public PreceptClassOrder Order
            {
                get { return order; }
            }

            public void Note(PreceptDef def)
            {
                if (def.issue != null && !issues.Contains(def.issue)) issues.Add(def.issue);
                string label = def.LabelCap.NullOrEmpty() ? def.defName : def.LabelCap.ToString();
                if (!labels.Contains(label)) labels.Add(label);

                var candidate = new PreceptClassOrder(def.issue != null ? def.issue.label : "", def.defName);
                if (!ordered || candidate.CompareTo(order) < 0)
                {
                    order = candidate;
                    ordered = true;
                }
            }

            /// <summary>
            /// The section title from the game's own resolved labels: the issue every def of the class
            /// shares, else those defs' own labels. A mod passes its title straight into vanilla's
            /// private drawing routine, so it is never readable here.
            /// </summary>
            public string BuildLabel(System.Type preceptClass)
            {
                if (issues.Count == 1) return issues[0].LabelCap.ToString();
                if (labels.Count > 0) return string.Join(", ", labels);
                return preceptClass.Name;
            }
        }

        /// <summary>
        /// The precept classes in play that no typed section claims, one section each, so a mod's
        /// category is named and countable instead of folding into the general Precepts count.
        /// Grouped by <c>PreceptDef.preceptClass</c> and ordered by the class's lowest
        /// (issue label, defName) def, which keeps row order stable across rebuilds — an unstable
        /// order would relocate the cursor on every refresh. Titles are derived in the same pass, so
        /// naming a row costs no further def-database sweep. Empty with no ideology mods loaded.
        /// </summary>
        public static List<DiscoveredPreceptClass> DiscoverExtraPreceptClasses(Ideo ideo)
        {
            var result = new List<DiscoveredPreceptClass>();
            if (ideo == null) return result;

            var builders = new Dictionary<System.Type, DiscoveredClassBuilder>();
            foreach (PreceptDef def in DefDatabase<PreceptDef>.AllDefs)
            {
                System.Type preceptClass = def.preceptClass;
                // An abstract base or non-Precept type can only be named by a def in error:
                // PreceptMaker could never instantiate one, so it can never become a row.
                if (preceptClass == null || preceptClass.IsAbstract) continue;
                if (!typeof(Precept).IsAssignableFrom(preceptClass)) continue;
                if (IsClaimedByTypedSection(preceptClass)) continue;

                if (!builders.TryGetValue(preceptClass, out DiscoveredClassBuilder builder))
                {
                    builder = new DiscoveredClassBuilder();
                    builders[preceptClass] = builder;
                }
                // Title and order come from every def of the class, so neither shifts as this
                // ideoligion's eligibility changes; only Listable is gated on the ideo.
                builder.Note(def);
                if (!builder.Listable && CanIdeoListPreceptDef(ideo, def)) builder.Listable = true;
            }

            foreach (var pair in builders)
            {
                if (pair.Value.Listable)
                    result.Add(new DiscoveredPreceptClass { PreceptClass = pair.Key, Label = pair.Value.BuildLabel(pair.Key) });
            }
            result.Sort((a, b) => builders[a.PreceptClass].Order.CompareTo(builders[b.PreceptClass].Order));
            return result;
        }

        private static bool IsClaimedByTypedSection(System.Type preceptClass)
        {
            if (preceptClass == typeof(Precept)) return true;
            foreach (System.Type claimed in TypedSectionPreceptClasses)
                if (claimed.IsAssignableFrom(preceptClass)) return true;
            return false;
        }

        /// <summary>
        /// Whether this ideoligion has reason to show a section for the class this def names: it
        /// already carries such a precept, or vanilla's listing gate accepts the def — that gate
        /// tracks the mod's own draw, but NREs on foundation-less ideos (classic-save carryovers).
        /// </summary>
        private static bool CanIdeoListPreceptDef(Ideo ideo, PreceptDef def)
        {
            if (ideo.foundation != null
                && IdeoUIUtility.CanListPrecept(ideo, def, IdeoEditMode.GameStart).Accepted) return true;
            return ideo.PreceptsListForReading.Any(p => p.def == def && p.def.visible);
        }

        private static HubSection BuildDiscoveredPreceptType(Ideo ideo, DiscoveredPreceptClass discovered)
        {
            return new HubSection
            {
                Label = discovered.Label,
                ValueSummary = PreceptListSummary(
                    ideo.PreceptsListForReading.Where(discovered.PreceptClass.IsInstanceOfType)),
                PreceptClass = discovered.PreceptClass,
            };
        }

        #endregion

        /// <summary>Allowed hair/beard and tattoo styles (vanilla's DoAppearanceItems).</summary>
        private static HubSection BuildAppearance(Ideo ideo)
        {
            return new HubSection
            {
                Kind = SectionKind.Appearance,
                Label = GetLocalizedSectionLabel(SectionKind.Appearance),
                ValueSummary = AppearanceSummary(ideo),
            };
        }

        /// <summary>"{n} hair and beards, {m} tattoos" — the count of available appearance styles.</summary>
        public static string AppearanceSummary(Ideo ideo)
        {
            if (ideo?.style == null) return "";
            // The counts stay -1 until the game caches them, which it skips while the ideo has no
            // culture, so an uninitialized ideo would otherwise read "-1 hair and beards" aloud.
            int hair = System.Math.Max(0, ideo.style.NumHairAndBeardStylesAvailable);
            int tattoo = System.Math.Max(0, ideo.style.NumTattooStylesAvailable);
            return hair + " " + "HairAndBeards".Translate().ToString().ToLower()
                 + ", " + tattoo + " " + "Tattoos".Translate().ToString().ToLower();
        }

        /// <summary>
        /// A vanilla section's title. A mod-added section has no <see cref="SectionKind"/> and is
        /// titled from its class's label discovery instead; <see cref="IdeoTypedPreceptState.SectionLabel"/>
        /// picks between the two.
        /// </summary>
        public static string GetLocalizedSectionLabel(SectionKind kind)
        {
            switch (kind)
            {
                case SectionKind.Name: return "Name".Translate().CapitalizeFirst();
                case SectionKind.Adjective: return "Adjective".Translate().CapitalizeFirst();
                case SectionKind.MemberName: return "IdeoMembers".Translate().CapitalizeFirst();
                case SectionKind.WorshipRoom: return "WorshipRoom".Translate().CapitalizeFirst();
                case SectionKind.Description: return "Description".Translate().CapitalizeFirst();
                case SectionKind.Culture: return "Culture".Translate().CapitalizeFirst();
                case SectionKind.Styles: return "Styles".Translate().CapitalizeFirst();
                case SectionKind.Icon: return "Icon".Translate().CapitalizeFirst();
                case SectionKind.Color: return "Color".Translate().CapitalizeFirst();
                case SectionKind.StructureMeme: return "StructureMeme".Translate().CapitalizeFirst();
                case SectionKind.NormalMemes: return "Memes".Translate().CapitalizeFirst();
                case SectionKind.Deities: return "Deities".Translate().CapitalizeFirst();
                case SectionKind.Precepts: return "Precepts".Translate().CapitalizeFirst();
                case SectionKind.Roles: return "IdeoRoles".Translate().CapitalizeFirst();
                case SectionKind.Rituals: return "Rituals".Translate().CapitalizeFirst();
                case SectionKind.Buildings: return "IdeoBuildings".Translate().CapitalizeFirst();
                case SectionKind.Relics: return "IdeoRelics".Translate().CapitalizeFirst();
                case SectionKind.Weapons: return "IdeoWeapons".Translate().CapitalizeFirst();
                case SectionKind.VeneratedAnimals: return "VeneratedAnimals".Translate().CapitalizeFirst();
                case SectionKind.PreferredXenotypes: return "PreferredXenotypes".Translate().CapitalizeFirst();
                case SectionKind.Apparel: return "IdeoApparel".Translate().CapitalizeFirst();
                case SectionKind.Appearance: return "Appearance".Translate().CapitalizeFirst();
                default: return kind.ToString();
            }
        }

        #region Section builders

        private static HubSection BuildName(Ideo ideo)
        {
            string value = ideo.name.NullOrEmpty()
                ? ("None".Translate().ToString())
                : ideo.name;
            return new HubSection
            {
                Kind = SectionKind.Name,
                Label = GetLocalizedSectionLabel(SectionKind.Name),
                ValueSummary = value,
            };
        }

        private static HubSection BuildAdjective(Ideo ideo)
        {
            string value = ideo.adjective.NullOrEmpty()
                ? ("None".Translate().ToString())
                : ideo.adjective;
            return new HubSection
            {
                Kind = SectionKind.Adjective,
                Label = GetLocalizedSectionLabel(SectionKind.Adjective),
                ValueSummary = value,
            };
        }

        private static HubSection BuildMemberName(Ideo ideo)
        {
            string value = ideo.memberName.NullOrEmpty()
                ? ("None".Translate().ToString())
                : ideo.memberName;
            return new HubSection
            {
                Kind = SectionKind.MemberName,
                Label = GetLocalizedSectionLabel(SectionKind.MemberName),
                ValueSummary = value,
            };
        }

        private static HubSection BuildWorshipRoom(Ideo ideo)
        {
            string value = ideo.WorshipRoomLabel.NullOrEmpty()
                ? ("None".Translate().ToString())
                : ideo.WorshipRoomLabel;
            return new HubSection
            {
                Kind = SectionKind.WorshipRoom,
                Label = GetLocalizedSectionLabel(SectionKind.WorshipRoom),
                ValueSummary = value,
            };
        }

        private static HubSection BuildDescription(Ideo ideo)
        {
            string value;
            if (ideo.description.NullOrEmpty())
                value = "None".Translate().ToString();
            else
            {
                // Flatten whitespace only. Never truncate — the full description is presented.
                value = ideo.description.Replace("\r", " ").Replace("\n", " ").Trim();
            }
            return new HubSection
            {
                Kind = SectionKind.Description,
                Label = GetLocalizedSectionLabel(SectionKind.Description),
                ValueSummary = value,
            };
        }

        private static HubSection BuildCulture(Ideo ideo)
        {
            string value;
            if (ideo.culture == null)
            {
                value = "None".Translate().ToString();
            }
            else
            {
                value = ideo.culture.LabelCap.ToString();
                if (!string.IsNullOrEmpty(ideo.culture.description))
                    value += ". " + ideo.culture.description;
            }
            return new HubSection
            {
                Kind = SectionKind.Culture,
                Label = GetLocalizedSectionLabel(SectionKind.Culture),
                ValueSummary = value,
            };
        }

        private static HubSection BuildStyles(Ideo ideo)
        {
            string value;
            if (ideo.thingStyleCategories == null || ideo.thingStyleCategories.Count == 0)
            {
                value = "None".Translate().ToString();
            }
            else
            {
                value = string.Join(", ", ideo.thingStyleCategories
                    .Where(s => s?.category != null)
                    .Select(s => s.category.LabelCap.ToString()));
                if (string.IsNullOrEmpty(value))
                    value = "None".Translate().ToString();
            }
            return new HubSection
            {
                Kind = SectionKind.Styles,
                Label = GetLocalizedSectionLabel(SectionKind.Styles),
                ValueSummary = value,
            };
        }

        private static HubSection BuildIcon(Ideo ideo)
        {
            string value = ideo.iconDef != null
                ? (ideo.iconDef.label.NullOrEmpty() ? ideo.iconDef.defName : ideo.iconDef.LabelCap.ToString())
                : "None".Translate().ToString();
            return new HubSection
            {
                Kind = SectionKind.Icon,
                Label = GetLocalizedSectionLabel(SectionKind.Icon),
                ValueSummary = value,
            };
        }

        private static HubSection BuildColor(Ideo ideo)
        {
            string value = ideo.colorDef != null
                ? (ideo.colorDef.label.NullOrEmpty() ? ideo.colorDef.defName : ideo.colorDef.LabelCap.ToString())
                : "None".Translate().ToString();
            return new HubSection
            {
                Kind = SectionKind.Color,
                Label = GetLocalizedSectionLabel(SectionKind.Color),
                ValueSummary = value,
            };
        }

        private static HubSection BuildStructureMeme(Ideo ideo)
        {
            var structure = ideo.memes?.FirstOrDefault(m => m.category == MemeCategory.Structure);
            string value;
            if (structure == null)
            {
                value = "None".Translate().ToString();
            }
            else
            {
                value = structure.LabelCap.ToString();
                if (!string.IsNullOrEmpty(structure.description))
                    value += ". " + structure.description;
            }
            // No InspectableDefs: vanilla never opens an info card for a MemeDef, so offering one
            // here would fabricate it. The full content is already in ValueSummary above.
            return new HubSection
            {
                Kind = SectionKind.StructureMeme,
                Label = GetLocalizedSectionLabel(SectionKind.StructureMeme),
                ValueSummary = value,
            };
        }

        private static HubSection BuildNormalMemes(Ideo ideo)
        {
            var normals = ideo.memes?.Where(m => m.category == MemeCategory.Normal).ToList() ?? new List<MemeDef>();
            string value;
            if (normals.Count == 0)
            {
                value = "None".Translate().ToString();
            }
            else
            {
                var names = string.Join(", ", normals.Select(m => m.LabelCap.ToString()));
                int impact = ImpactOf(normals);
                string impactLabel = IdeoImpactUtility.OverallImpactLabel(impact);
                value = $"{normals.Count}. {names}. {"IdeoImpact".Translate()}: {impactLabel}";
            }
            // No InspectableDefs: vanilla gives a MemeDef no real info card. See BuildStructureMeme.
            return new HubSection
            {
                Kind = SectionKind.NormalMemes,
                Label = GetLocalizedSectionLabel(SectionKind.NormalMemes),
                ValueSummary = value,
            };
        }

        private static HubSection BuildDeities(Ideo ideo)
        {
            var foundation = ideo.foundation as IdeoFoundation_Deity;
            string value;
            if (foundation == null || foundation.DeitiesListForReading == null || foundation.DeitiesListForReading.Count == 0)
            {
                value = "None".Translate().ToString();
            }
            else
            {
                var names = foundation.DeitiesListForReading
                    .Select(d => string.IsNullOrEmpty(d.name) ? d.type ?? "" : d.name)
                    .Where(n => !string.IsNullOrEmpty(n));
                value = string.Join(", ", names);
                if (string.IsNullOrEmpty(value))
                    value = foundation.DeitiesListForReading.Count.ToString();
            }
            return new HubSection
            {
                Kind = SectionKind.Deities,
                Label = GetLocalizedSectionLabel(SectionKind.Deities),
                ValueSummary = value,
            };
        }

        private static HubSection BuildPrecepts(Ideo ideo, List<DiscoveredPreceptClass> extraPreceptClasses)
        {
            // Issue-based precepts, excluding every list that has a section of its own — typed and
            // mod-added alike — or those would be counted twice.
            var basePrecepts = ideo.PreceptsListForReading
                .Where(p => !TypedSectionPreceptClasses.Any(t => t.IsInstanceOfType(p))
                         && !extraPreceptClasses.Any(d => d.PreceptClass.IsInstanceOfType(p)))
                .ToList();

            string value = basePrecepts.Count == 0
                ? "None".Translate().ToString()
                : basePrecepts.Count.ToString();
            return new HubSection
            {
                Kind = SectionKind.Precepts,
                Label = GetLocalizedSectionLabel(SectionKind.Precepts),
                ValueSummary = value,
            };
        }

        private static HubSection BuildPreceptType(Ideo ideo, SectionKind kind, string labelKey, System.Type preceptType)
        {
            return new HubSection
            {
                Kind = kind,
                Label = GetLocalizedSectionLabel(kind),
                ValueSummary = PreceptListSummary(ideo.PreceptsListForReading.Where(preceptType.IsInstanceOfType)),
            };
        }

        private static HubSection BuildBuildingsSection(Ideo ideo)
        {
            // Buildings section covers both Precept_Building and Precept_RitualSeat (matches viewer).
            return new HubSection
            {
                Kind = SectionKind.Buildings,
                Label = GetLocalizedSectionLabel(SectionKind.Buildings),
                ValueSummary = PreceptListSummary(
                    ideo.PreceptsListForReading.Where(p => p is Precept_Building || p is Precept_RitualSeat)),
            };
        }

        /// <summary>"{count}. {labels}" for a precept section's row value, or "None" when empty.</summary>
        private static string PreceptListSummary(IEnumerable<Precept> matching)
        {
            var list = matching.ToList();
            return list.Count == 0
                ? "None".Translate().ToString()
                : $"{list.Count}. {string.Join(", ", list.Select(PreceptLabel))}";
        }

        /// <summary>
        /// A precept's display label, matching what vanilla draws in the precept box
        /// (UIInfoFirstLine, plus UIInfoSecondLine when it adds information) rather than the generic
        /// generated name — this is what surfaces the venerated animal, the role's title, and so on.
        /// </summary>
        public static string PreceptLabel(Precept precept)
        {
            if (precept == null) return "";
            string first = precept.UIInfoFirstLine?.Trim();
            string second = precept.UIInfoSecondLine?.Trim();
            if (string.IsNullOrEmpty(first))
                first = (string)precept.LabelCap;
            if (!string.IsNullOrEmpty(second) && second != first)
                return first + ". " + second;
            return first;
        }

        /// <summary>
        /// Strips rich-text tags and unresolved grammar tokens from game text and collapses
        /// whitespace runs, so markup and template placeholders are never read aloud.
        /// </summary>
        public static string CleanGameText(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s = s.StripTags();
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\{[^{}]*\}", "");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"[ \t]+", " ");
            return s.Trim();
        }

        #endregion

        /// <summary>
        /// Sum of all meme impact values, clamped to the game's combined cap. Mirrors the private
        /// IdeoUIUtility.ImpactOf behind vanilla's own continue-button label, kept local rather than
        /// depending on that private API.
        /// </summary>
        public static int ImpactOf(IEnumerable<MemeDef> memes)
        {
            if (memes == null) return 0;
            int total = 0;
            foreach (var m in memes)
                total += m.impact;
            if (total < 0) total = 0;
            if (total > IdeoImpactUtility.MaxCombinedImpact) total = IdeoImpactUtility.MaxCombinedImpact;
            return total;
        }

        #region Validation summary

        /// <summary>
        /// A player-readable validation message for the current ideoligion, or empty when valid.
        /// Mirrors Page_ConfigureIdeo.CanDoNext's checks so the block is known in advance.
        /// </summary>
        public static string BuildValidationSummary(Ideo ideo)
        {
            if (ideo == null)
                return "MessageMustChooseIdeo".Translate();

            if (ideo.name.NullOrEmpty())
                return "MessageIdeoNameCantBeEmpty".Translate();

            var pair = ideo.FirstIncompatiblePreceptPair();
            if (pair != default(Pair<Precept, Precept>))
            {
                return "MessageIdeoIncompatiblePrecepts".Translate(
                    pair.First.Label.Named("PRECEPT1"),
                    pair.Second.Label.Named("PRECEPT2")
                ).CapitalizeFirst();
            }

            var missingRitualTarget = ideo.FirstRitualMissingTarget();
            if (missingRitualTarget != null)
            {
                return "MessageRitualMissingTarget".Translate(missingRitualTarget.Item1.LabelCap.Named("PRECEPT"))
                    + ": " + missingRitualTarget.Item2.ToCommaList().CapitalizeFirst() + ".";
            }

            var missingBuildingRitual = ideo.FirstConsumableBuildingMissingRitual();
            if (missingBuildingRitual != null)
                return "MessageBuildingMissingRitual".Translate(missingBuildingRitual.LabelCap.Named("PRECEPT"));

            return "";
        }

        /// <summary>
        /// The non-blocking warning vanilla shows near the continue button
        /// (Ideo.FirstPreceptWithWarning / Precept.GetPlayerWarning), or empty. Surfaced alongside
        /// the impact readout; it never blocks continuing.
        /// </summary>
        public static string BuildPlayerWarning(Ideo ideo)
        {
            var precept = ideo?.FirstPreceptWithWarning();
            if (precept == null) return "";
            if (!precept.GetPlayerWarning(out var shortText, out var description))
                return "";
            string text = "Warning".Translate() + ": " + (shortText ?? "").CapitalizeFirst();
            if (!string.IsNullOrEmpty(description))
                text += ". " + description;
            return text;
        }

        #endregion

        #region Opening announcement

        /// <summary>The first-time announcement when the builder hub opens.</summary>
        public static string BuildOpeningAnnouncement(Ideo ideo)
        {
            var sb = new StringBuilder();
            sb.Append("CustomizeIdeoligion".Translate().ToString());
            if (ideo != null && !ideo.name.NullOrEmpty())
            {
                sb.Append(". ");
                sb.Append(ideo.name);
            }

            // The impact line, which sighted players see near the continue button.
            if (ideo?.memes != null)
            {
                var normals = ideo.memes.Where(m => m.category == MemeCategory.Normal).ToList();
                if (normals.Count > 0)
                {
                    int impact = ImpactOf(normals);
                    string impactLabel = IdeoImpactUtility.OverallImpactLabel(impact);
                    sb.Append(". ").Append("IdeoImpact".Translate()).Append(": ").Append(impactLabel);
                }
            }

            return sb.ToString();
        }

        #endregion
    }
}
