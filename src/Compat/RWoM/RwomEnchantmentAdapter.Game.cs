using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Inspection-tree adapter for RimWorld of Magic's per-item enchantment
    /// tab (TorannMagic.Enchantment.ITab_Enchantment). The mod injects this
    /// tab plus a CompEnchantedItem onto every non-TorannMagic weapon and
    /// apparel def at startup; the tab (and this category) is only visible
    /// when the item's comp has an enchantment.
    ///
    /// Read-only detail lines mirroring ITab_Enchantment.FillTab (Enchantment/
    /// ITab_Enchantment.cs:41-188) exactly: one line per non-zero/non-null
    /// stat, in the source's own draw order, each carrying its own
    /// EnchantmentTier (or the shared skillTier for the ability-style stats)
    /// as a spoken suffix in place of the source's colour coding. The eleven
    /// pre-rendered Label properties (MaxMPLabel, MPCostLabel,
    /// MPRegenRateLabel, CoolDownLabel, XPGainLabel, ArcaneResLabel,
    /// ArcaneDmgLabel, ArcaneSpectreLabel, PhantomShiftLabel, HediffLabel,
    /// EnchantmentActionLabel) already call the mod's own Translate()
    /// internally, so their resolved strings are the sighted text presented
    /// verbatim — this adapter never calls Translate() on a mod key itself.
    ///
    /// <see cref="ComposeStatLines"/> is the single composer shared with the
    /// Gear tab's per-item enchantment rows (HarmonyPatches.cs:7554's
    /// DrawThingRow postfix, mirrored in PawnGearAdapter via
    /// <see cref="TryGetGearEnchantmentLines"/>), so the two surfaces can
    /// never drift apart.
    /// </summary>
    internal sealed class RwomEnchantmentAdapter : InspectNodeAdapter
    {
        private static Type compType; // TorannMagic.Enchantment.CompEnchantedItem

        private static PropertyInfo hasEnchantmentProperty;

        private static FieldInfo maxMPField, mpCostField, mpRegenRateField, coolDownField, xpGainField, arcaneResField, arcaneDmgField;
        private static FieldInfo maxMPTierField, mpCostTierField, mpRegenRateTierField, coolDownTierField, xpGainTierField, arcaneResTierField, arcaneDmgTierField;
        private static FieldInfo skillTierField;
        private static FieldInfo arcaneSpectreField, phantomShiftField, hediffField, enchantmentActionField;
        private static FieldInfo magicAbilitiesField, soulOrbTraitsField;
        private static FieldInfo enchantmentActionTypeField; // TorannMagic.Enchantment.EnchantmentAction.type

        private static PropertyInfo maxMPLabelProperty, mpCostLabelProperty, mpRegenRateLabelProperty, coolDownLabelProperty,
            xpGainLabelProperty, arcaneResLabelProperty, arcaneDmgLabelProperty, arcaneSpectreLabelProperty,
            phantomShiftLabelProperty, hediffLabelProperty, enchantmentActionLabelProperty;

        private static bool ready;

        public override bool Ready { get { return ready; } }

        private RwomEnchantmentAdapter()
        {
        }

        public static void TryRegister()
        {
            CompatRegistration.TabAdapter("TorannMagic.Enchantment.ITab_Enchantment",
                t => { ResolveMembers(); return new RwomEnchantmentAdapter(); },
                "RimWorld of Magic enchantment tab compat");
        }

        private static void ResolveMembers()
        {
            var surface = new ReflectionSurface("RwomEnchantmentAdapter");
            compType = surface.Type("TorannMagic.Enchantment.CompEnchantedItem");
            Type enchantmentActionType = surface.Type("TorannMagic.Enchantment.EnchantmentAction");

            hasEnchantmentProperty = surface.Property(compType, "HasEnchantment");

            maxMPField = surface.Field(compType, "maxMP");
            mpCostField = surface.Field(compType, "mpCost");
            mpRegenRateField = surface.Field(compType, "mpRegenRate");
            coolDownField = surface.Field(compType, "coolDown");
            xpGainField = surface.Field(compType, "xpGain");
            arcaneResField = surface.Field(compType, "arcaneRes");
            arcaneDmgField = surface.Field(compType, "arcaneDmg");

            maxMPTierField = surface.Field(compType, "maxMPTier");
            mpCostTierField = surface.Field(compType, "mpCostTier");
            mpRegenRateTierField = surface.Field(compType, "mpRegenRateTier");
            coolDownTierField = surface.Field(compType, "coolDownTier");
            xpGainTierField = surface.Field(compType, "xpGainTier");
            arcaneResTierField = surface.Field(compType, "arcaneResTier");
            arcaneDmgTierField = surface.Field(compType, "arcaneDmgTier");
            skillTierField = surface.Field(compType, "skillTier");

            arcaneSpectreField = surface.Field(compType, "arcaneSpectre");
            phantomShiftField = surface.Field(compType, "phantomShift");
            hediffField = surface.Field(compType, "hediff");
            enchantmentActionField = surface.Field(compType, "enchantmentAction");
            magicAbilitiesField = surface.Field(compType, "MagicAbilities");
            soulOrbTraitsField = surface.Field(compType, "SoulOrbTraits");

            maxMPLabelProperty = surface.Property(compType, "MaxMPLabel");
            mpCostLabelProperty = surface.Property(compType, "MPCostLabel");
            mpRegenRateLabelProperty = surface.Property(compType, "MPRegenRateLabel");
            coolDownLabelProperty = surface.Property(compType, "CoolDownLabel");
            xpGainLabelProperty = surface.Property(compType, "XPGainLabel");
            arcaneResLabelProperty = surface.Property(compType, "ArcaneResLabel");
            arcaneDmgLabelProperty = surface.Property(compType, "ArcaneDmgLabel");
            arcaneSpectreLabelProperty = surface.Property(compType, "ArcaneSpectreLabel");
            phantomShiftLabelProperty = surface.Property(compType, "PhantomShiftLabel");
            hediffLabelProperty = surface.Property(compType, "HediffLabel");
            enchantmentActionLabelProperty = surface.Property(compType, "EnchantmentActionLabel");

            enchantmentActionTypeField = surface.Field(enchantmentActionType, "type");

            ready = surface.Ready;
        }

        // Stable English dispatch token (l10n-exempt: never displayed raw —
        // DisplayName/CategoryDisplayName render our own key below).
        public override string CategoryKey => "RwomEnchantment";

        public override TabHandlerType Handler => TabHandlerType.RichNavigation;

        // The mod's own labelKey ("TabEnchantment") resolves to "Enchanted",
        // which reads awkwardly as a category name, so this uses our own key
        // instead of TabRegistry's live-label pattern.
        public override string DisplayName(InspectTabBase tab) => "RimWorldAccess.Compat.Rwom.TabEnchantment".Translate();

        public override string CategoryDisplayName(object obj) => "RimWorldAccess.Compat.Rwom.TabEnchantment".Translate();

        // The tab's IsVisible (SelectedCompEnchantment != null && HasEnchantment)
        // already gated whether this category exists before BuildChildren runs
        // (the RwomClassCardAdapter precedent) — CanExpand keeps the base
        // "always true" and BuildChildren's own defensive checks are the only
        // gate left.

        public override void BuildChildren(InspectionTreeItem categoryItem, object obj, InspectionMode mode)
        {
            if (categoryItem.Children.Count > 0)
                return;

            Thing thing = obj as Thing;
            if (thing == null)
                return;

            object comp = FindComp(thing);
            if (comp == null)
                return;

            try
            {
                bool hasEnchantment = (bool)hasEnchantmentProperty.GetValue(comp);
                if (!hasEnchantment)
                    return;

                foreach (string line in ComposeStatLines(comp))
                    InspectNodeFactory.DetailLine(categoryItem, line);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"RwomEnchantmentAdapter.BuildChildren failed: {ex.Message}");
            }
        }

        private static object FindComp(Thing thing)
        {
            if (thing is ThingWithComps twc && twc.AllComps != null)
            {
                foreach (ThingComp c in twc.AllComps)
                {
                    if (compType.IsInstanceOfType(c))
                        return c;
                }
            }
            return null;
        }

        // ---- Part 2 hook: gear-tab enchantment rows (PawnGearAdapter) ----

        /// <summary>
        /// Cheap probe used by PawnGearAdapter to decide whether a gear row
        /// should be expandable even in ReadOnly mode. Returns false whenever
        /// the mod isn't loaded, the item lacks the comp, or the comp reports
        /// no enchantment — PawnGearAdapter never needs to know RWoM exists.
        /// </summary>
        internal static bool HasGearEnchantment(Thing thing)
        {
            if (!ready || thing == null)
                return false;
            object comp = FindComp(thing);
            if (comp == null)
                return false;
            try
            {
                return (bool)hasEnchantmentProperty.GetValue(comp);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"RwomEnchantmentAdapter.HasGearEnchantment failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Mirrors HarmonyPatches.cs:7554's ITab_Pawn_Gear.DrawThingRow
        /// postfix: a leading "Enchanted." row followed by the same stat
        /// lines <see cref="ComposeStatLines"/> composes for the enchantment
        /// tab itself (the postfix's own tooltip lists exactly the same
        /// stats, minus the two-key confirmation the source omits: the
        /// Abilities/Absorbed-traits/enchantment-action lines the tab draws
        /// but the gear-row tooltip does not — included here anyway since
        /// the doctrine presents everything a sighted player can reach, and
        /// the tab itself is one drill-in away for a spawned/equipped item
        /// only, not for a dropped one sitting in an inventory list).
        /// Returns null when there is nothing to add.
        /// </summary>
        internal static List<string> TryGetGearEnchantmentLines(Thing thing)
        {
            if (!HasGearEnchantment(thing))
                return null;

            try
            {
                object comp = FindComp(thing);
                var lines = new List<string> { "RimWorldAccess.Compat.Rwom.GearEnchanted".Translate() };
                lines.AddRange(ComposeStatLines(comp));
                return lines;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"RwomEnchantmentAdapter.TryGetGearEnchantmentLines failed: {ex.Message}");
                return null;
            }
        }

        // ---- Shared composer (Parts 1 & 2) ----

        /// <summary>
        /// Mirrors ITab_Enchantment.FillTab's row order and inclusion
        /// conditions exactly (Enchantment/ITab_Enchantment.cs):
        /// maxMP != 0 (:57), mpCost != 0 (:65), mpRegenRate != 0 (:73),
        /// coolDown != 0 (:81), xpGain != 0 (:89), arcaneRes != 0 (:97),
        /// arcaneDmg != 0 (:105), arcaneSpectre (:113), phantomShift (:121),
        /// hediff != null (:129), MagicAbilities non-empty (:137),
        /// SoulOrbTraits non-empty (:159), enchantmentAction present and its
        /// type != EnchantmentActionType.Null (:182). The first seven carry
        /// their own named tier field; the remaining single-stat lines share
        /// skillTier (source passes skillTier as the colour argument for all
        /// of arcaneSpectre/phantomShift/hediff/enchantmentAction alike).
        /// </summary>
        internal static List<string> ComposeStatLines(object comp)
        {
            var lines = new List<string>();
            if (!ready || comp == null)
                return lines;

            AppendFloatStatLine(lines, comp, maxMPField, maxMPTierField, maxMPLabelProperty);
            AppendFloatStatLine(lines, comp, mpCostField, mpCostTierField, mpCostLabelProperty);
            AppendFloatStatLine(lines, comp, mpRegenRateField, mpRegenRateTierField, mpRegenRateLabelProperty);
            AppendFloatStatLine(lines, comp, coolDownField, coolDownTierField, coolDownLabelProperty);
            AppendFloatStatLine(lines, comp, xpGainField, xpGainTierField, xpGainLabelProperty);
            AppendFloatStatLine(lines, comp, arcaneResField, arcaneResTierField, arcaneResLabelProperty);
            AppendFloatStatLine(lines, comp, arcaneDmgField, arcaneDmgTierField, arcaneDmgLabelProperty);

            AppendBoolStatLine(lines, comp, arcaneSpectreField, arcaneSpectreLabelProperty);
            AppendBoolStatLine(lines, comp, phantomShiftField, phantomShiftLabelProperty);

            if (hediffField.GetValue(comp) != null)
                lines.Add(WithTierSuffix((string)hediffLabelProperty.GetValue(comp), skillTierField.GetValue(comp)));

            string abilitiesLine = ComposeAbilitiesLine(comp);
            if (abilitiesLine != null)
                lines.Add(abilitiesLine);

            string traitsLine = ComposeTraitsLine(comp);
            if (traitsLine != null)
                lines.Add(traitsLine);

            object action = enchantmentActionField.GetValue(comp);
            if (action != null)
            {
                object actionTypeValue = enchantmentActionTypeField.GetValue(action);
                if (actionTypeValue == null || actionTypeValue.ToString() != "Null")
                    lines.Add(WithTierSuffix((string)enchantmentActionLabelProperty.GetValue(comp), skillTierField.GetValue(comp)));
            }

            return lines;
        }

        private static void AppendFloatStatLine(List<string> lines, object comp, FieldInfo statField, FieldInfo tierField, PropertyInfo labelProperty)
        {
            float value = (float)statField.GetValue(comp);
            if (value == 0f)
                return;
            lines.Add(WithTierSuffix((string)labelProperty.GetValue(comp), tierField.GetValue(comp)));
        }

        private static void AppendBoolStatLine(List<string> lines, object comp, FieldInfo boolField, PropertyInfo labelProperty)
        {
            bool value = (bool)boolField.GetValue(comp);
            if (!value)
                return;
            lines.Add(WithTierSuffix((string)labelProperty.GetValue(comp), skillTierField.GetValue(comp)));
        }

        private static string WithTierSuffix(string label, object tierValue)
        {
            string tierName = tierValue != null ? tierValue.ToString().ToLowerInvariant() : "";
            return label + "RimWorldAccess.Compat.Rwom.EnchantTierSuffix".Translate(tierName);
        }

        private static string ComposeAbilitiesLine(object comp)
        {
            IList abilities = magicAbilitiesField.GetValue(comp) as IList;
            if (abilities == null || abilities.Count == 0)
                return null;

            var labels = new List<string>();
            foreach (object a in abilities)
            {
                if (a is Def def)
                    labels.Add(def.LabelCap);
            }
            if (labels.Count == 0)
                return null;
            return "RimWorldAccess.Compat.Rwom.EnchantAbilities".Translate(string.Join(", ", labels));
        }

        private static string ComposeTraitsLine(object comp)
        {
            IList traits = soulOrbTraitsField.GetValue(comp) as IList;
            if (traits == null || traits.Count == 0)
                return null;

            var labels = new List<string>();
            foreach (object t in traits)
            {
                if (t is Trait trait)
                    labels.Add(trait.LabelCap);
            }
            if (labels.Count == 0)
                return null;
            return "RimWorldAccess.Compat.Rwom.EnchantTraits".Translate(string.Join(", ", labels));
        }
    }
}
