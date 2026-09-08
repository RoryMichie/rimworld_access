using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The shared data-column set for the transfer-screen family (transport pod loading, map
    /// portals, caravan formation, caravan split).
    ///
    /// The column set is READ OFF THE GAME'S OWN LIVE WIDGET, never transcribed: each dialog
    /// constructs a <c>TransferableOneWayWidget</c> whose private draw* fields are the exact
    /// switches its renderer consults per cell, and <see cref="ViewFor"/> reflects them into a
    /// <see cref="WidgetView"/>. A column therefore cannot appear where a sighted player sees
    /// none, and a mod changing the widget's flags changes this table the same frame. The
    /// <see cref="ColumnProfile"/> presets survive only as the FALLBACK for a null or
    /// unreflectable widget.
    ///
    /// Cell values come from the same game calculators the widget itself calls; never hardcoded
    /// English, never render-pass state (only vanilla's tooltip VISIBILITY is hover-gated, never
    /// the values). Identity/name-column handling stays with each scope's own row-label builder;
    /// this module supplies the data columns plus a Name sorter for the identity column.
    /// </summary>
    public static class TransferableTableColumns
    {
        public enum ColumnKind
        {
            Mass,
            MarketValue,
            DaysUntilRot,
            ItemNutrition,
            NutritionEatenPerDay,
            ForagedFoodPerDay,
            MechEnergy,
        }

        /// <summary>Which fixed columns are active, mirroring one dialog/tab's own draw* constructor flags.</summary>
        public readonly struct ColumnProfile
        {
            public readonly bool Mass;
            public readonly bool MarketValue;
            public readonly bool DaysUntilRot;
            public readonly bool ItemNutrition;
            public readonly bool NutritionEatenPerDay;
            public readonly bool ForagedFoodPerDay;
            public readonly bool MechEnergy;

            public ColumnProfile(bool mass, bool marketValue, bool daysUntilRot, bool itemNutrition,
                bool nutritionEatenPerDay, bool foragedFoodPerDay, bool mechEnergy)
            {
                Mass = mass;
                MarketValue = marketValue;
                DaysUntilRot = daysUntilRot;
                ItemNutrition = itemNutrition;
                NutritionEatenPerDay = nutritionEatenPerDay;
                ForagedFoodPerDay = foragedFoodPerDay;
                MechEnergy = mechEnergy;
            }

            /// <summary>Dialog_LoadTransporters, Pawns tab: mass, market value, nutrition/day, foraged food/day (no mech energy, no days-until-rot).</summary>
            public static readonly ColumnProfile PodPawns = new ColumnProfile(true, true, false, false, true, true, false);
            /// <summary>Dialog_EnterPortal, Pawns tab: mass only (no mass cap, no other columns).</summary>
            public static readonly ColumnProfile PortalPawns = new ColumnProfile(true, false, false, false, false, false, false);
            /// <summary>Dialog_FormCaravan / Dialog_SplitCaravan, Pawns tab: mass, market value, nutrition/day, foraged food/day, mech energy.
            /// Vanilla passes drawMechEnergy unconditionally but gates every draw on
            /// ModsConfig.BiotechActive, so without Biotech the column does not exist on screen and
            /// the "MechEnergy" Keyed string would not resolve. A property, not a static readonly
            /// field, to keep DLC checks out of type-initialization order.</summary>
            public static ColumnProfile CaravanPawns => new ColumnProfile(true, true, false, false, true, true, ModsConfig.BiotechActive);

            // Items / travel-supplies tabs.
            /// <summary>Dialog_LoadTransporters, Items tab: mass, market value, item nutrition, days-until-rot.</summary>
            public static readonly ColumnProfile PodItems = new ColumnProfile(true, true, true, true, false, false, false);
            /// <summary>Dialog_EnterPortal, Items tab: mass only.</summary>
            public static readonly ColumnProfile PortalItems = new ColumnProfile(true, false, false, false, false, false, false);
            /// <summary>Dialog_FormCaravan / Dialog_SplitCaravan, Items AND Travel supplies tabs (identical flag sets): mass, market value, item nutrition, days-until-rot.</summary>
            public static readonly ColumnProfile CaravanItems = new ColumnProfile(true, true, true, true, false, false, false);
        }

        /// <summary>
        /// A read-only view over one live <c>TransferableOneWayWidget</c>: the game's own column
        /// switches, rot tile and mass-cell mode flags, reflected off the instance the dialog
        /// renders with. Obtain via <see cref="ViewFor"/>. Do NOT cache across dialog recaches — the
        /// dialogs REPLACE their widgets in CalculateAndRecacheTransferables.
        /// </summary>
        public sealed class WidgetView
        {
            public readonly ColumnProfile Profile;
            public readonly PlanetTile? Tile;
            internal readonly IgnorePawnsInventoryMode IgnoreInventoryMode;
            internal readonly bool IncludePawnsMassInMassUsage;
            internal readonly bool IgnoreSpawnedCorpseGearAndInventoryMass;

            internal WidgetView(ColumnProfile profile, PlanetTile? tile,
                IgnorePawnsInventoryMode ignoreInventoryMode,
                bool includePawnsMassInMassUsage,
                bool ignoreSpawnedCorpseGearAndInventoryMass)
            {
                Profile = profile;
                Tile = tile;
                IgnoreInventoryMode = ignoreInventoryMode;
                IncludePawnsMassInMassUsage = includePawnsMassInMassUsage;
                IgnoreSpawnedCorpseGearAndInventoryMass = ignoreSpawnedCorpseGearAndInventoryMass;
            }
        }

        private static class WidgetFields
        {
            private const System.Reflection.BindingFlags Flags =
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

            internal static readonly System.Reflection.FieldInfo DrawMass = Get("drawMass");
            internal static readonly System.Reflection.FieldInfo DrawMarketValue = Get("drawMarketValue");
            internal static readonly System.Reflection.FieldInfo DrawDaysUntilRot = Get("drawDaysUntilRot");
            internal static readonly System.Reflection.FieldInfo DrawItemNutrition = Get("drawItemNutrition");
            internal static readonly System.Reflection.FieldInfo DrawNutritionEatenPerDay = Get("drawNutritionEatenPerDay");
            internal static readonly System.Reflection.FieldInfo DrawForagedFoodPerDay = Get("drawForagedFoodPerDay");
            internal static readonly System.Reflection.FieldInfo DrawMechEnergy = Get("drawMechEnergy");
            internal static readonly System.Reflection.FieldInfo IncludePawnsMassInMassUsage = Get("includePawnsMassInMassUsage");
            internal static readonly System.Reflection.FieldInfo IgnorePawnInventoryMass = Get("ignorePawnInventoryMass");
            internal static readonly System.Reflection.FieldInfo IgnoreSpawnedCorpseGearAndInventoryMass = Get("ignoreSpawnedCorpseGearAndInventoryMass");
            internal static readonly System.Reflection.FieldInfo Tile = Get("tile");

            internal static readonly bool AllPresent =
                DrawMass != null && DrawMarketValue != null && DrawDaysUntilRot != null
                && DrawItemNutrition != null && DrawNutritionEatenPerDay != null
                && DrawForagedFoodPerDay != null && DrawMechEnergy != null
                && IncludePawnsMassInMassUsage != null && IgnorePawnInventoryMass != null
                && IgnoreSpawnedCorpseGearAndInventoryMass != null && Tile != null;

            private static System.Reflection.FieldInfo Get(string name)
            {
                return typeof(TransferableOneWayWidget).GetField(name, Flags);
            }
        }

        private static bool warnedFallback;

        /// <summary>
        /// The view for a dialog's live widget, falling back to the transcribed preset with a
        /// one-time warning only when the widget is null or a game update renamed its fields.
        /// </summary>
        public static WidgetView ViewFor(TransferableOneWayWidget widget, ColumnProfile fallbackProfile, PlanetTile? fallbackTile)
        {
            if (widget == null || !WidgetFields.AllPresent)
            {
                if (!warnedFallback)
                {
                    warnedFallback = true;
                    Log.Warning("[RimWorld Access] Transfer widget not readable (null="
                        + (widget == null) + ", fieldsPresent=" + WidgetFields.AllPresent
                        + "); falling back to transcribed column presets.");
                }
                return new WidgetView(fallbackProfile, fallbackTile,
                    IgnorePawnsInventoryMode.DontIgnore,
                    includePawnsMassInMassUsage: false,
                    ignoreSpawnedCorpseGearAndInventoryMass: false);
            }
            // Vanilla row-gates every mech-energy draw on ModsConfig.BiotechActive, so without
            // Biotech the column does not exist on screen even when the flag is set, and the
            // "MechEnergy" Keyed string is Biotech content.
            var profile = new ColumnProfile(
                (bool)WidgetFields.DrawMass.GetValue(widget),
                (bool)WidgetFields.DrawMarketValue.GetValue(widget),
                (bool)WidgetFields.DrawDaysUntilRot.GetValue(widget),
                (bool)WidgetFields.DrawItemNutrition.GetValue(widget),
                (bool)WidgetFields.DrawNutritionEatenPerDay.GetValue(widget),
                (bool)WidgetFields.DrawForagedFoodPerDay.GetValue(widget),
                (bool)WidgetFields.DrawMechEnergy.GetValue(widget) && ModsConfig.BiotechActive);
            PlanetTile tile = (PlanetTile)WidgetFields.Tile.GetValue(widget);
            return new WidgetView(profile,
                tile.Valid ? (PlanetTile?)tile : null,
                (IgnorePawnsInventoryMode)WidgetFields.IgnorePawnInventoryMass.GetValue(widget),
                (bool)WidgetFields.IncludePawnsMassInMassUsage.GetValue(widget),
                (bool)WidgetFields.IgnoreSpawnedCorpseGearAndInventoryMass.GetValue(widget));
        }

        /// <summary>
        /// The trade dialog's data-column view: Mass and Market value only. Vanilla trade draws
        /// neither as a cell, but both are trade sorters offered by its own sort dropdowns, and the
        /// header row is where sorting lives in the table model. Pawn mass reads as plain mass, not
        /// the caravan screens' remaining-carry-capacity offset — a trade has no mass budget.
        /// </summary>
        public static WidgetView TradeView()
        {
            return new WidgetView(
                new ColumnProfile(true, true, false, false, false, false, false),
                null,
                IgnorePawnsInventoryMode.DontIgnore,
                includePawnsMassInMassUsage: true,
                ignoreSpawnedCorpseGearAndInventoryMass: false);
        }

        public static int ColumnCount(ColumnProfile p)
        {
            int n = 0;
            if (p.Mass) n++;
            if (p.MarketValue) n++;
            if (p.DaysUntilRot) n++;
            if (p.ItemNutrition) n++;
            if (p.NutritionEatenPerDay) n++;
            if (p.ForagedFoodPerDay) n++;
            if (p.MechEnergy) n++;
            return n;
        }

        /// <summary>Data-column kind at a 0-based data-column index (vanilla's own left-to-right order among active columns).</summary>
        public static ColumnKind KindAt(ColumnProfile p, int index)
        {
            int i = 0;
            if (p.Mass) { if (i == index) return ColumnKind.Mass; i++; }
            if (p.MarketValue) { if (i == index) return ColumnKind.MarketValue; i++; }
            if (p.DaysUntilRot) { if (i == index) return ColumnKind.DaysUntilRot; i++; }
            if (p.ItemNutrition) { if (i == index) return ColumnKind.ItemNutrition; i++; }
            if (p.NutritionEatenPerDay) { if (i == index) return ColumnKind.NutritionEatenPerDay; i++; }
            if (p.ForagedFoodPerDay) { if (i == index) return ColumnKind.ForagedFoodPerDay; i++; }
            if (p.MechEnergy) { if (i == index) return ColumnKind.MechEnergy; i++; }
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        /// <summary>
        /// Header label, tooltip and sortability for a data column; all numeric columns are
        /// sortable. <paramref name="isPawnColumn"/> distinguishes Mass's two vanilla header tips:
        /// item rows carry the static ItemWeightTip, pawn rows the per-pawn breakdown from
        /// <see cref="CellTip"/>.
        /// </summary>
        public static TableColumnInfo ColumnInfo(ColumnKind kind, bool isPawnColumn)
        {
            switch (kind)
            {
                case ColumnKind.Mass:
                    return new TableColumnInfo(
                        StatDefOf.Mass.LabelCap.Resolve(),
                        isPawnColumn ? null : "ItemWeightTip".Translate().ToString(),
                        true);
                case ColumnKind.MarketValue:
                    // Vanilla's MarketValueTip resolves to the literal "Market value", identical to
                    // the column name already spoken on column context.
                    return new TableColumnInfo(StatDefOf.MarketValue.LabelCap.Resolve(), null, true);
                case ColumnKind.DaysUntilRot:
                    return new TableColumnInfo(
                        "RimWorldAccess.Shell.Table.Transferable.DaysUntilRot".Translate().ToString(),
                        "DaysUntilRotTip".Translate().ToString(),
                        true);
                case ColumnKind.ItemNutrition:
                    return new TableColumnInfo(
                        StatDefOf.Nutrition.LabelCap.Resolve(),
                        "ItemNutritionTip".Translate(AdultDailyNutritionNeed()).ToString(),
                        true);
                case ColumnKind.NutritionEatenPerDay:
                    return new TableColumnInfo("FoodConsumption".Translate().ToString(), null, true);
                case ColumnKind.ForagedFoodPerDay:
                    return new TableColumnInfo("ForagedFoodPerDay".Translate().ToString(), null, true);
                case ColumnKind.MechEnergy:
                    return new TableColumnInfo("MechEnergy".Translate().ToString(), null, true);
                default:
                    return new TableColumnInfo();
            }
        }

        private static string AdultDailyNutritionNeed()
        {
            return (1.6f * ThingDefOf.Human.race.baseHungerRate).ToString("0.##");
        }

        /// <summary>
        /// Cell text for a data column, or "" where vanilla would leave the cell blank. Takes the
        /// <see cref="Transferable"/> base so the one-way transfer family and trade's
        /// <see cref="Tradeable"/> rows share one implementation; every value reads base members only.
        /// </summary>
        public static string CellText(ColumnKind kind, Transferable t, WidgetView view)
        {
            if (t == null || !t.HasAnyThing)
                return "";
            switch (kind)
            {
                case ColumnKind.Mass:
                    return MassText(t, view);
                case ColumnKind.MarketValue:
                    return t.ThingDef.tradeability != Tradeability.None
                        ? t.AnyThing.MarketValue.ToStringMoney()
                        : "";
                case ColumnKind.DaysUntilRot:
                    return DaysUntilRotText(t, view.Tile);
                case ColumnKind.ItemNutrition:
                    return t.ThingDef.IsNutritionGivingIngestible
                        ? t.AnyThing.GetStatValue(StatDefOf.Nutrition).ToString("0.##")
                        : "";
                case ColumnKind.NutritionEatenPerDay:
                    return CanEatFood(t.AnyThing as Pawn)
                        ? RaceProperties.NutritionEatenPerDay((Pawn)t.AnyThing)
                        : "";
                case ColumnKind.ForagedFoodPerDay:
                    return ForagedFoodText(t.AnyThing as Pawn);
                case ColumnKind.MechEnergy:
                    return HasMechEnergy(t.AnyThing as Pawn)
                        ? ((Pawn)t.AnyThing).needs.energy.CurLevelPercentage.ToStringPercent()
                        : "";
                default:
                    return "";
            }
        }

        /// <summary>
        /// Per-cell dynamic tooltip, always spoken, unlike the static column HeaderTip which speaks
        /// only on column-context change. Only the two columns whose vanilla tooltip genuinely
        /// varies per pawn carry one.
        /// </summary>
        public static string CellTip(ColumnKind kind, Transferable t, WidgetView view)
        {
            if (t == null || !t.HasAnyThing)
                return null;
            switch (kind)
            {
                case ColumnKind.Mass:
                    return MassTip(t, view);
                case ColumnKind.NutritionEatenPerDay:
                    return CanEatFood(t.AnyThing as Pawn)
                        ? RaceProperties.NutritionEatenPerDayExplanation((Pawn)t.AnyThing, showDiet: true, showLegend: true, showCalculations: false)
                        : null;
                case ColumnKind.ForagedFoodPerDay:
                    return ForagedFoodTip(t.AnyThing as Pawn);
                default:
                    return null;
            }
        }

        /// <summary>
        /// The mass cell, replicating vanilla's TransferableOneWayWidget.DrawMass branch for branch:
        /// pawns that can never carry are blank; item rows, and pawn rows where the dialog counts
        /// pawn mass in usage, show the thing's mass through the widget's own GetMass adjustments;
        /// caravan pawn rows show REMAINING CARRY CAPACITY as a signed offset.
        /// </summary>
        private static string MassText(Transferable t, WidgetView view)
        {
            Thing anyThing = t.AnyThing;
            Pawn pawn = anyThing as Pawn;
            if (pawn != null && !view.IncludePawnsMassInMassUsage && !MassUtility.CanEverCarryAnything(pawn))
                return "";
            if (pawn == null || view.IncludePawnsMassInMassUsage)
                return WidgetMass(anyThing, view).ToStringMass();
            float capacity = MassUtility.Capacity(pawn);
            float gearMass = MassUtility.GearMass(pawn);
            float invMass = InventoryCalculatorsUtility.ShouldIgnoreInventoryOf(pawn, view.IgnoreInventoryMode)
                ? 0f
                : MassUtility.InventoryMass(pawn);
            return (capacity - gearMass - invMass).ToStringMassOffset();
        }

        /// <summary>
        /// The pawn mass cell's dynamic tooltip, replicating vanilla's GetPawnMassTip line for line
        /// with periods instead of newlines. Item rows keep the static ItemWeightTip on the header.
        /// </summary>
        private static string MassTip(Transferable t, WidgetView view)
        {
            if (!(t.AnyThing is Pawn pawn))
                return null;
            if (!view.IncludePawnsMassInMassUsage && !MassUtility.CanEverCarryAnything(pawn))
                return null;
            float gearMass = MassUtility.GearMass(pawn);
            float invMass = InventoryCalculatorsUtility.ShouldIgnoreInventoryOf(pawn, view.IgnoreInventoryMode)
                ? 0f
                : MassUtility.InventoryMass(pawn);
            var parts = new List<string>();
            if (pawn != null && !view.IncludePawnsMassInMassUsage)
                parts.Add("MassCapacity".Translate() + ": " + MassUtility.Capacity(pawn).ToStringMass());
            else
                parts.Add("Mass".Translate() + ": " + (WidgetMass(pawn, view) - gearMass - invMass).ToStringMass());
            if (gearMass != 0f)
                parts.Add("EquipmentAndApparelMass".Translate() + ": " + gearMass.ToStringMass());
            if (invMass != 0f)
                parts.Add("InventoryMass".Translate() + ": " + invMass.ToStringMass());
            return string.Join(". ", parts);
        }

        /// <summary>Vanilla's private TransferableOneWayWidget.GetMass: stat mass minus the widget-configured inventory and corpse adjustments.</summary>
        private static float WidgetMass(Thing thing, WidgetView view)
        {
            if (thing == null)
                return 0f;
            float mass = thing.GetStatValue(StatDefOf.Mass);
            if (thing is Pawn pawn)
            {
                if (InventoryCalculatorsUtility.ShouldIgnoreInventoryOf(pawn, view.IgnoreInventoryMode))
                    mass -= MassUtility.InventoryMass(pawn);
            }
            else if (view.IgnoreSpawnedCorpseGearAndInventoryMass && thing is Corpse corpse && corpse.Spawned)
            {
                mass -= MassUtility.GearAndInventoryMass(corpse.InnerPawn);
            }
            return mass;
        }

        private static bool CanEatFood(Pawn pawn)
        {
            return pawn != null && pawn.RaceProps != null && pawn.RaceProps.EatsFood && !pawn.Dead && pawn.needs?.food != null;
        }

        private static bool HasMechEnergy(Pawn pawn)
        {
            return pawn != null && ModsConfig.BiotechActive && !pawn.Dead && pawn.needs?.energy != null;
        }

        /// <summary>36,000,000 ticks = 600 days, vanilla's "never rots" sentinel.</summary>
        private const int NeverRotsTicks = 36000000;

        private static string DaysUntilRotText(Transferable t, PlanetTile? tile)
        {
            if (!t.ThingDef.IsNutritionGivingIngestible || !tile.HasValue)
                return "";
            CompRottable comp = t.AnyThing.TryGetComp<CompRottable>();
            if (comp == null || !comp.Active)
                return "Never".Translate().ToString();
            int ticks = DaysUntilRotCalculator.ApproxTicksUntilRot_AssumeTimePassesBy(comp, tile.Value);
            if (ticks >= NeverRotsTicks)
                return "Never".Translate().ToString();
            string days = (ticks / 60000f).ToString("0.#");
            return "RimWorldAccess.Shell.Table.Transferable.DaysValue".Translate(days).ToString();
        }

        private static string ForagedFoodText(Pawn pawn)
        {
            if (pawn == null)
                return "";
            if (VirtualPlantsUtility.CanEverEatVirtualPlants(pawn))
                return "RimWorldAccess.Shell.Table.Transferable.CanGraze".Translate().ToString();
            float perDay = ForagedFoodPerDayCalculator.GetBaseForagedNutritionPerDay(pawn, out bool skip);
            return skip ? "" : "+" + perDay.ToString("0.##");
        }

        private static string ForagedFoodTip(Pawn pawn)
        {
            if (pawn == null)
                return null;
            if (VirtualPlantsUtility.CanEverEatVirtualPlants(pawn))
                return "AnimalCanGrazeTip".Translate().ToString();
            ForagedFoodPerDayCalculator.GetBaseForagedNutritionPerDay(pawn, out bool skip);
            if (skip)
                return null;
            StatRequest req = StatRequest.For(pawn);
            float value = StatDefOf.ForagedNutritionPerDay.Worker.GetValue(req);
            string explanation = StatDefOf.ForagedNutritionPerDay.Worker.GetExplanationFull(
                req, StatDefOf.ForagedNutritionPerDay.toStringNumberSense, value);
            return "NutritionForagedPerDayTip".Translate(explanation).ToString();
        }

        /// <summary>
        /// The numeric sort key for columns with no vanilla TransferableSorterDef. NaN means
        /// inapplicable, and SortByColumn keeps those rows at the bottom in both directions. "Never
        /// rots" is float.MaxValue rather than NaN: it is a genuine longest-lasting value, so a
        /// descending sort puts non-perishables first.
        /// </summary>
        private static float NumericValue(ColumnKind kind, Transferable t, PlanetTile? tile)
        {
            if (t == null || !t.HasAnyThing)
                return float.NaN;
            switch (kind)
            {
                case ColumnKind.DaysUntilRot:
                    if (!t.ThingDef.IsNutritionGivingIngestible || !tile.HasValue)
                        return float.NaN;
                    CompRottable comp = t.AnyThing.TryGetComp<CompRottable>();
                    if (comp == null || !comp.Active)
                        return float.MaxValue;
                    int ticks = DaysUntilRotCalculator.ApproxTicksUntilRot_AssumeTimePassesBy(comp, tile.Value);
                    return ticks >= NeverRotsTicks ? float.MaxValue : ticks / 60000f;
                case ColumnKind.ItemNutrition:
                    return t.ThingDef.IsNutritionGivingIngestible ? t.AnyThing.GetStatValue(StatDefOf.Nutrition) : float.NaN;
                case ColumnKind.NutritionEatenPerDay:
                    if (CanEatFood(t.AnyThing as Pawn)
                        && float.TryParse(RaceProperties.NutritionEatenPerDay((Pawn)t.AnyThing), out float eaten))
                        return eaten;
                    return float.NaN;
                case ColumnKind.ForagedFoodPerDay:
                    if (t.AnyThing is Pawn foragePawn && !VirtualPlantsUtility.CanEverEatVirtualPlants(foragePawn))
                    {
                        float perDay = ForagedFoodPerDayCalculator.GetBaseForagedNutritionPerDay(foragePawn, out bool skip);
                        return skip ? float.NaN : perDay;
                    }
                    return float.NaN;
                case ColumnKind.MechEnergy:
                    return HasMechEnergy(t.AnyThing as Pawn) ? ((Pawn)t.AnyThing).needs.energy.CurLevelPercentage : float.NaN;
                default:
                    return float.NaN;
            }
        }

        /// <summary>The vanilla TransferableSorterDef for a column, or null when the column sorts by its own NumericValue instead.</summary>
        private static TransferableSorterDef SorterDefFor(ColumnKind kind)
        {
            switch (kind)
            {
                case ColumnKind.Mass: return DefDatabase<TransferableSorterDef>.GetNamedSilentFail("Mass");
                case ColumnKind.MarketValue: return DefDatabase<TransferableSorterDef>.GetNamedSilentFail("MarketValue");
                default: return null;
            }
        }

        /// <summary>
        /// Re-orders a data column, matching vanilla's own comparer for Mass and MarketValue and
        /// this module's numeric value for the rest, tiebroken by
        /// TransferableUIUtility.DefaultListOrderPriority as vanilla's own sort chain does.
        /// </summary>
        public static List<T> SortByColumn<T>(List<T> items, ColumnKind kind, bool descending, WidgetView view)
            where T : Transferable
        {
            PlanetTile? tile = view.Tile;
            var sorted = new List<T>(items);
            TransferableSorterDef sorterDef = SorterDefFor(kind);
            if (sorterDef != null)
            {
                TransferableComparer comparer = sorterDef.Comparer;
                sorted.SortStable((a, b) =>
                {
                    int primary = descending ? comparer.Compare(b, a) : comparer.Compare(a, b);
                    return primary != 0 ? primary : TiebreakDescending(a, b);
                });
            }
            else
            {
                sorted.SortStable((a, b) =>
                {
                    float va = NumericValue(kind, a, tile);
                    float vb = NumericValue(kind, b, tile);
                    // Inapplicable rows stay at the bottom in BOTH directions: a player sorting by
                    // mech energy wants the mechs first, not every non-mech row.
                    bool blankA = float.IsNaN(va);
                    bool blankB = float.IsNaN(vb);
                    int primary;
                    if (blankA || blankB)
                        primary = blankA == blankB ? 0 : (blankA ? 1 : -1);
                    else
                        primary = descending ? vb.CompareTo(va) : va.CompareTo(vb);
                    return primary != 0 ? primary : TiebreakDescending(a, b);
                });
            }
            return sorted;
        }

        /// <summary>The identity column's sort, via vanilla's own TransferableComparer_Name; language-dependent by design.</summary>
        public static List<T> SortByIdentity<T>(List<T> items, bool descending)
            where T : Transferable
        {
            var sorted = new List<T>(items);
            TransferableSorterDef nameSorter = DefDatabase<TransferableSorterDef>.GetNamedSilentFail("Name");
            if (nameSorter == null)
                return sorted;
            TransferableComparer comparer = nameSorter.Comparer;
            sorted.SortStable((a, b) => descending ? comparer.Compare(b, a) : comparer.Compare(a, b));
            return sorted;
        }

        private static int TiebreakDescending(Transferable a, Transferable b)
        {
            return TransferableUIUtility.DefaultListOrderPriority(b).CompareTo(TransferableUIUtility.DefaultListOrderPriority(a));
        }

        /// <summary>The shared identity/name column header: "Name" (mod-wide reused key) plus the dialog's own count tooltip.</summary>
        public static TableColumnInfo IdentityColumnInfo(string countTooltip)
        {
            return new TableColumnInfo("RimWorldAccess.Common.NameColumn".Translate().ToString(), countTooltip, true);
        }
    }
}
