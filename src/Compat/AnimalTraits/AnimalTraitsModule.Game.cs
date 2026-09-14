using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    internal sealed class AnimalTraitsModule : CompatModule
    {
        public override string TargetPackageId => "luved.animaltraits";

        public override void Activate(Harmony harmony)
        {
            AnimalTraitsSpareColumnHandler.Register();
            AnimalTraitsTraitColumnHandler.Register();
        }
    }

    /// <summary>
    /// Exact-type override for the mod's Traits text column: cell text stays the worker's own
    /// GetTextFor, but the tip is recomposed from the trait hediff defs because the mod's GetTip
    /// truncates descriptions to 97 chars and carries visual formatting. Unregistered, the type
    /// chain falls back to <see cref="TextColumnHandler"/>.
    /// </summary>
    internal sealed class AnimalTraitsTraitColumnHandler : PawnColumnHandler
    {
        private static readonly MethodInfo getTextFor = AccessTools.Method(typeof(PawnColumnWorker_Text), "GetTextFor");

        private readonly MethodInfo getTraitDefNames;

        private AnimalTraitsTraitColumnHandler(MethodInfo getTraitDefNames)
        {
            this.getTraitDefNames = getTraitDefNames;
        }

        public static void Register()
        {
            try
            {
                Type workerType = AccessTools.TypeByName("AnimalTraitExtension.PawnColumnWorker_AnimalTraits");
                MethodInfo getTraitDefNames = workerType != null
                    ? AccessTools.Method(workerType, "GetAnimalTraitDefNames", new[] { typeof(Pawn) })
                    : null;
                if (workerType == null || getTraitDefNames == null)
                {
                    return;
                }
                PawnColumnHandlerRegistry.Register(workerType, new AnimalTraitsTraitColumnHandler(getTraitDefNames));
            }
            catch (Exception ex)
            {
                Log.Error("[RimWorld Access] Animal Traits System traits column handler registration failed: " + ex.Message);
            }
        }

        public override string CellText(PawnColumnDef def, Pawn pawn)
        {
            string text = (string)getTextFor.Invoke(def.Worker, new object[] { pawn });
            return text != null ? text.StripTags() : "";
        }

        public override string CellTip(PawnColumnDef def, Pawn pawn)
        {
            var traitNames = getTraitDefNames.Invoke(def.Worker, new object[] { pawn }) as List<string>;
            if (traitNames == null || traitNames.Count == 0)
            {
                return null;
            }
            var entries = new List<string>();
            foreach (string defName in traitNames)
            {
                HediffDef trait = DefDatabase<HediffDef>.GetNamedSilentFail(defName);
                if (trait == null)
                {
                    continue;
                }
                string label = !trait.label.NullOrEmpty() ? trait.LabelCap.ToString() : defName;
                string polarity = (trait.isBad
                    ? "RimWorldAccess.Compat.AnimalTraits.Negative"
                    : "RimWorldAccess.Compat.AnimalTraits.Positive").Loc().ToString();
                string description = trait.description.NullOrEmpty() ? "" : trait.description.StripTags().Trim();
                entries.Add("RimWorldAccess.Compat.AnimalTraits.TraitTipEntry".Loc(label, polarity, description).ToString().Trim());
            }
            return entries.Count > 0 ? string.Join(" ", entries) : null;
        }

        /// <summary>Text-column behavior: a widget can hide behind the text, so extras stay visible.</summary>
        public override bool SuppressCapturedExtras(PawnColumnDef def)
        {
            return false;
        }
    }

    /// <summary>
    /// Animal Traits System's Spare Traits column (a direct <see cref="PawnColumnWorker"/>
    /// subclass). Cell state reads through the worker's own ShouldSpare/ShouldCullFromSlaughter
    /// gates; the tip lists the matching trait labels. The column's only interaction is its
    /// header's configure float menu, so Enter serves that same menu on the cell — table-free,
    /// hence <see cref="ActivatesCellWithoutTable"/>.
    /// </summary>
    internal sealed class AnimalTraitsSpareColumnHandler : PawnColumnHandler
    {
        private readonly MethodInfo shouldSpare;
        private readonly MethodInfo shouldCull;
        private readonly MethodInfo getTraitDefNames;
        private readonly MethodInfo getCurrentComponent;
        private readonly MethodInfo getSpareTraits;
        private readonly MethodInfo getCullTraits;
        private readonly Type dialogType;

        private AnimalTraitsSpareColumnHandler(MethodInfo shouldSpare, MethodInfo shouldCull,
            MethodInfo getTraitDefNames, MethodInfo getCurrentComponent, MethodInfo getSpareTraits,
            MethodInfo getCullTraits, Type dialogType)
        {
            this.shouldSpare = shouldSpare;
            this.shouldCull = shouldCull;
            this.getTraitDefNames = getTraitDefNames;
            this.getCurrentComponent = getCurrentComponent;
            this.getSpareTraits = getSpareTraits;
            this.getCullTraits = getCullTraits;
            this.dialogType = dialogType;
        }

        /// <summary>No-ops (column stays on the honest fallback) when the mod's shape has drifted.</summary>
        public static void Register()
        {
            try
            {
                Type workerType = AccessTools.TypeByName("AnimalTraitExtension.PawnColumnWorker_SpareTraits");
                Type componentType = AccessTools.TypeByName("AnimalTraitExtension.AnimalTraitsGameComponent");
                Type dialogType = AccessTools.TypeByName("AnimalTraitExtension.Dialog_SelectSpareTraits");
                if (workerType == null || componentType == null || dialogType == null)
                {
                    return;
                }

                MethodInfo shouldSpare = AccessTools.Method(workerType, "ShouldSpareFromSlaughter", new[] { typeof(Pawn) });
                MethodInfo shouldCull = AccessTools.Method(workerType, "ShouldCullFromSlaughter", new[] { typeof(Pawn) });
                MethodInfo getTraitDefNames = AccessTools.Method(workerType, "GetAnimalTraitDefNames", new[] { typeof(Pawn) });
                MethodInfo getCurrentComponent = AccessTools.PropertyGetter(componentType, "Current");
                MethodInfo getSpareTraits = AccessTools.Method(componentType, "GetSpareTraits");
                MethodInfo getCullTraits = AccessTools.Method(componentType, "GetCullTraits");
                if (shouldSpare == null || shouldCull == null || getTraitDefNames == null
                    || getCurrentComponent == null || getSpareTraits == null || getCullTraits == null)
                {
                    Log.Warning("[RimWorld Access] Animal Traits System spare column handler: expected members not found; the mod build may have drifted. The column stays on the fallback.");
                    return;
                }

                PawnColumnHandlerRegistry.Register(workerType, new AnimalTraitsSpareColumnHandler(
                    shouldSpare, shouldCull, getTraitDefNames, getCurrentComponent, getSpareTraits, getCullTraits, dialogType));
            }
            catch (Exception ex)
            {
                Log.Error("[RimWorld Access] Animal Traits System spare column handler registration failed: " + ex.Message);
            }
        }

        /// <summary>The column def carries no label or headerTip (icon-only header).</summary>
        public override string HeaderLabel(PawnColumnDef def)
        {
            return "RimWorldAccess.Compat.AnimalTraits.SpareColumnName".Loc().ToString();
        }

        public override string CellText(PawnColumnDef def, Pawn pawn)
        {
            // The mod's own DoCell early-out: blank for anything but a player-faction animal.
            if (pawn == null || pawn.RaceProps == null || !pawn.RaceProps.Animal || pawn.Faction != Faction.OfPlayer)
            {
                return "";
            }
            if ((bool)shouldSpare.Invoke(null, new object[] { pawn }))
            {
                return "RimWorldAccess.Compat.AnimalTraits.Protected".Loc().ToString();
            }
            if ((bool)shouldCull.Invoke(null, new object[] { pawn }))
            {
                return "RimWorldAccess.Compat.AnimalTraits.MarkedForCull".Loc().ToString();
            }
            return "";
        }

        /// <summary>The trait labels behind the cell's state, mirroring the mod's own icon tooltip.</summary>
        public override string CellTip(PawnColumnDef def, Pawn pawn)
        {
            if (pawn == null || pawn.RaceProps == null || !pawn.RaceProps.Animal || pawn.Faction != Faction.OfPlayer)
            {
                return null;
            }
            bool spared = (bool)shouldSpare.Invoke(null, new object[] { pawn });
            if (!spared && !(bool)shouldCull.Invoke(null, new object[] { pawn }))
            {
                return null;
            }
            object component = getCurrentComponent.Invoke(null, null);
            if (component == null)
            {
                return null;
            }
            var relevant = (spared ? getSpareTraits : getCullTraits).Invoke(component, null) as List<string>;
            var traitNames = getTraitDefNames.Invoke(def.Worker, new object[] { pawn }) as List<string>;
            if (relevant == null || traitNames == null)
            {
                return null;
            }
            var relevantSet = new HashSet<string>(relevant);
            var labels = new List<string>();
            foreach (string defName in traitNames)
            {
                if (!relevantSet.Contains(defName))
                {
                    continue;
                }
                HediffDef trait = DefDatabase<HediffDef>.GetNamedSilentFail(defName);
                labels.Add(trait != null && !trait.label.NullOrEmpty() ? trait.LabelCap.ToString() : defName);
            }
            return labels.Count > 0 ? string.Join(", ", labels) : null;
        }

        public override PawnColumnActivation ActivateCell(PawnColumnDef def, Pawn pawn, PawnTable table)
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("RimWorldAccess.Compat.AnimalTraits.ConfigureSpare".Loc().ToString(), delegate
                {
                    Find.WindowStack.Add((Window)Activator.CreateInstance(dialogType, new object[] { false }));
                }),
                new FloatMenuOption("RimWorldAccess.Compat.AnimalTraits.ConfigureCull".Loc().ToString(), delegate
                {
                    Find.WindowStack.Add((Window)Activator.CreateInstance(dialogType, new object[] { true }));
                }),
            };
            WindowlessFloatMenuState.Open(options, colonistOrders: false);
            return PawnColumnActivation.OpenedUI;
        }

        public override bool ActivatesCellWithoutTable(PawnColumnDef def)
        {
            return true;
        }
    }
}
