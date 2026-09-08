using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RimWorldAccess
{
    /// <summary>Stateless helpers shared by the caravan formation and splitting dialogs.</summary>
    public static class CaravanUIHelper
    {
        /// <summary>Category types for filtering transferables.</summary>
        public enum TransferableCategory
        {
            Pawns,
            FoodAndMedicine,  // Also known as TravelSupplies
            Items
        }

        /// <summary>
        /// One row of the Pawns section list: a real pawn <see cref="TransferableOneWay"/> or a
        /// read-only header row carrying vanilla's own section title.
        /// </summary>
        public readonly struct PawnSectionRow
        {
            /// <summary>Non-null only for a header row.</summary>
            public readonly string Header;

            /// <summary>Non-null only for a data row.</summary>
            public readonly TransferableOneWay Transferable;

            public bool IsHeader => Header != null;

            internal PawnSectionRow(string header)
            {
                Header = header;
                Transferable = null;
            }

            internal PawnSectionRow(TransferableOneWay transferable)
            {
                Header = null;
                Transferable = transferable;
            }
        }

        /// <summary>
        /// Filters transferables by category. For <see cref="TransferableCategory.Pawns"/>, pass the
        /// dialog's own live pawns <see cref="TransferableOneWayWidget"/> when available so
        /// membership and order come from the game's own section list rather than a hand-copied
        /// predicate set.
        /// </summary>
        public static List<TransferableOneWay> FilterByCategory(
            List<TransferableOneWay> allTransferables,
            TransferableCategory category,
            TransferableOneWayWidget liveWidget = null)
        {
            if (allTransferables == null)
                return new List<TransferableOneWay>();

            switch (category)
            {
                case TransferableCategory.Pawns:
                    // Section membership/order come from GetPawnSectionRows: the live widget's own
                    // sections, or the fallback predicate set below when no widget is available.
                    return GetPawnSectionRows(allTransferables, liveWidget)
                        .Where(r => !r.IsHeader)
                        .Select(r => r.Transferable)
                        .ToList();

                case TransferableCategory.FoodAndMedicine:
                    // Food and medicine, matching CaravanUIUtility.cs exactly (no extra null check
                    // on building).
                    return allTransferables
                        .Where(t => t.ThingDef.category != ThingCategory.Pawn &&
                                   ((!t.ThingDef.thingCategories.NullOrEmpty() && t.ThingDef.thingCategories.Contains(ThingCategoryDefOf.Medicine)) ||
                                    (t.ThingDef.IsIngestible && !t.ThingDef.IsDrug && !t.ThingDef.IsCorpse && (t.ThingDef.plant == null || !t.ThingDef.plant.IsTree)) ||
                                    (t.AnyThing.GetInnerIfMinified().def.IsBed && t.AnyThing.GetInnerIfMinified().def.building.bed_caravansCanUse)))
                        .ToList();

                case TransferableCategory.Items:
                    // Everything that is not a pawn and not food/medicine, matching
                    // CaravanUIUtility.cs exactly.
                    return allTransferables
                        .Where(t => t.ThingDef.category != ThingCategory.Pawn &&
                                   !((!t.ThingDef.thingCategories.NullOrEmpty() && t.ThingDef.thingCategories.Contains(ThingCategoryDefOf.Medicine)) ||
                                    (t.ThingDef.IsIngestible && !t.ThingDef.IsDrug && !t.ThingDef.IsCorpse && (t.ThingDef.plant == null || !t.ThingDef.plant.IsTree)) ||
                                    (t.AnyThing.GetInnerIfMinified().def.IsBed && t.AnyThing.GetInnerIfMinified().def.building.bed_caravansCanUse)))
                        .ToList();

                default:
                    return allTransferables;
            }
        }

        /// <summary>
        /// Pawns-region rows in vanilla's own section order, with a read-only header row per
        /// non-empty section. Reads the dialog's LIVE <paramref name="liveWidget"/> when available,
        /// falling back to <see cref="FallbackPawnSectionRows"/> only when there is no widget or a
        /// game update renamed the reflected fields.
        /// </summary>
        public static List<PawnSectionRow> GetPawnSectionRows(List<TransferableOneWay> allTransferables, TransferableOneWayWidget liveWidget)
        {
            return ReadLivePawnSections(liveWidget) ?? FallbackPawnSectionRows(allTransferables ?? new List<TransferableOneWay>());
        }

        // Live widget reading. CaravanFormationScope/TransportPodLoadingScope/SplitCaravanScope all
        // build their pawnsTransfer widget through CaravanUIUtility.CreateCaravanTransferableWidgets
        // -> AddPawnsSections (decompiled RimWorld.Planet/CaravanUIUtility.cs:103-142).

        private static class LiveSectionFields
        {
            private const BindingFlags InstanceNonPublic = BindingFlags.NonPublic | BindingFlags.Instance;
            private const BindingFlags InstancePublic = BindingFlags.Public | BindingFlags.Instance;

            // TransferableOneWayWidget.cs:23 -- "private List<Section> sections".
            internal static readonly FieldInfo Sections = typeof(TransferableOneWayWidget).GetField("sections", InstanceNonPublic);

            // The private nested Section struct is inaccessible by name, but its type is reachable
            // via the sections field's generic argument and its title/transferables fields are
            // public, so reflection sees both.
            internal static readonly Type SectionType =
                Sections != null && Sections.FieldType.IsGenericType
                    ? Sections.FieldType.GetGenericArguments()[0]
                    : null;

            internal static readonly FieldInfo Title = SectionType?.GetField("title", InstancePublic);
            internal static readonly FieldInfo Transferables = SectionType?.GetField("transferables", InstancePublic);

            internal static readonly bool AllPresent = Sections != null && SectionType != null && Title != null && Transferables != null;
        }

        private static bool warnedLiveSectionsFallback;

        /// <summary>
        /// Reads a dialog's live <see cref="TransferableOneWayWidget"/> Pawns section list by
        /// reflection: its private <c>sections</c> field, each entry a private <c>Section</c> struct
        /// with public <c>title</c>/<c>transferables</c>, populated by
        /// <see cref="CaravanUIUtility.AddPawnsSections"/>. That is the exact membership and title
        /// vanilla would draw for this instance, so the DLC gates, the section order and the
        /// exclusion of unmatched pawns all come along. Uses the raw <c>transferables</c> field
        /// (vanilla's pre-search membership), not <c>cachedTransferables</c>, which also reflects
        /// vanilla's own quicksearch and sort dropdowns. Null when the widget is null or a game
        /// update renamed a field; callers fall back to <see cref="FallbackPawnSectionRows"/>.
        /// </summary>
        private static List<PawnSectionRow> ReadLivePawnSections(TransferableOneWayWidget widget)
        {
            if (widget == null)
                return null;

            if (!LiveSectionFields.AllPresent)
            {
                if (!warnedLiveSectionsFallback)
                {
                    warnedLiveSectionsFallback = true;
                    Log.Warning("[RimWorld Access] TransferableOneWayWidget.sections not readable; falling back to transcribed pawn-section predicates.");
                }
                return null;
            }

            if (!(LiveSectionFields.Sections.GetValue(widget) is IEnumerable sectionsEnum))
                return null;

            var rows = new List<PawnSectionRow>();
            foreach (object sectionBoxed in sectionsEnum)
            {
                string title = LiveSectionFields.Title.GetValue(sectionBoxed) as string;
                if (!(LiveSectionFields.Transferables.GetValue(sectionBoxed) is IEnumerable membersEnum))
                    continue;

                List<TransferableOneWay> members = new List<TransferableOneWay>();
                foreach (object item in membersEnum)
                {
                    if (item is TransferableOneWay t)
                        members.Add(t);
                }

                // Vanilla's own FillMainRect skips a section (title included) once its cached list
                // is empty, so an empty section never adds a header nobody can select into.
                if (members.Count == 0)
                    continue;

                if (title != null)
                    rows.Add(new PawnSectionRow(title));
                foreach (TransferableOneWay t in members)
                    rows.Add(new PawnSectionRow(t));
            }
            return rows;
        }

        /// <summary>
        /// Fallback pawn-section builder for when no live widget is available. Mirrors
        /// <see cref="CaravanUIUtility.AddPawnsSections"/> bucket-for-bucket, DLC gates and vanilla
        /// translation keys included: a pawn matching none of the seven predicates is EXCLUDED, as
        /// vanilla does, never swept into a catch-all bucket.
        /// </summary>
        private static List<PawnSectionRow> FallbackPawnSectionRows(List<TransferableOneWay> allTransferables)
        {
            var rows = new List<PawnSectionRow>();
            IEnumerable<TransferableOneWay> source = allTransferables.Where(t => t.ThingDef.category == ThingCategory.Pawn);

            AddPawnBucket(rows, "ColonistsSection".Translate().ToString(),
                source.Where(t => ((Pawn)t.AnyThing).IsFreeNonSlaveColonist));

            if (ModsConfig.IdeologyActive)
            {
                AddPawnBucket(rows, "SlavesSection".Translate().ToString(),
                    source.Where(t => ((Pawn)t.AnyThing).IsSlave));
            }

            AddPawnBucket(rows, "PrisonersSection".Translate().ToString(),
                source.Where(t => ((Pawn)t.AnyThing).IsPrisoner));

            AddPawnBucket(rows, "CaptureSection".Translate().ToString(),
                source.Where(t => ((Pawn)t.AnyThing).Downed && CaravanUtility.ShouldAutoCapture((Pawn)t.AnyThing, Faction.OfPlayer)));

            AddPawnBucket(rows, "AnimalsSection".Translate().ToString(),
                source.Where(t => ((Pawn)t.AnyThing).IsAnimal));

            if (ModsConfig.BiotechActive)
            {
                AddPawnBucket(rows, "MechsSection".Translate().ToString(),
                    source.Where(t => ((Pawn)t.AnyThing).IsColonyMech
                        && ((Pawn)t.AnyThing).OverseerSubject != null
                        && ((Pawn)t.AnyThing).OverseerSubject.State == OverseerSubjectState.Overseen));
            }

            if (ModsConfig.AnomalyActive)
            {
                AddPawnBucket(rows, "EntitiesSection".Translate().ToString(),
                    source.Where(t => ((Pawn)t.AnyThing).IsColonySubhuman
                        && ((Pawn)t.AnyThing).mutant.Def.canTravelInCaravan));
            }

            return rows;
        }

        private static void AddPawnBucket(List<PawnSectionRow> rows, string title, IEnumerable<TransferableOneWay> members)
        {
            List<TransferableOneWay> list = members.ToList();
            if (list.Count == 0)
                return;
            rows.Add(new PawnSectionRow(title));
            foreach (TransferableOneWay t in list)
                rows.Add(new PawnSectionRow(t));
        }

        /// <summary>The pawn at <paramref name="selectedIndex"/>, or null when that row is not a pawn.</summary>
        public static Pawn GetSelectedPawn(List<TransferableOneWay> transferables, int selectedIndex)
        {
            if (transferables == null || transferables.Count == 0 ||
                selectedIndex < 0 || selectedIndex >= transferables.Count)
                return null;

            return transferables[selectedIndex]?.AnyThing as Pawn;
        }


        /// <summary>
        /// Toggles pawn selection for caravan membership. True when toggled on, false when off,
        /// null when the transferable is not a pawn.
        /// </summary>
        public static bool? TogglePawnSelection(TransferableOneWay transferable, Action notifyChanged)
        {
            if (transferable == null || !(transferable.AnyThing is Pawn))
                return null;

            bool nowChecked;
            if (transferable.CountToTransfer > 0)
            {
                transferable.AdjustTo(0);
                TolkHelper.Speak("RimWorldAccess.Caravan.UI.TransferableUnchecked".Loc(transferable.LabelCap.StripTags()));
                nowChecked = false;
            }
            else
            {
                int max = transferable.MaxCount;
                transferable.AdjustTo(max);
                TolkHelper.Speak("RimWorldAccess.Caravan.UI.TransferableChecked".Loc(transferable.LabelCap.StripTags()));
                nowChecked = true;
            }

            notifyChanged?.Invoke();
            return nowChecked;
        }
    }
}
