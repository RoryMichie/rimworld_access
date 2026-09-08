using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Editors for the ideoligion's description, culture, and styles — the surfaces vanilla
    /// itself serves with page-level float menus and buttons. The symbol fields (name,
    /// adjective, member name, worship room, icon, color) live on the real
    /// <see cref="Dialog_ChooseIdeoSymbols"/> via <c>ChooseIdeoSymbolsScope</c>, and the
    /// narrative editor on the real <see cref="Dialog_EditIdeoDescription"/>.
    ///
    /// Opened from the builder hub. Each edit mutates the live Ideo, regenerates any derived
    /// data, and asks the hub to refresh + re-announce.
    /// </summary>
    public static class IdeoSymbolEditState
    {
        /// <summary>
        /// The narrative's page surface, mirroring vanilla's own controls around the description
        /// box: the edit button opens the real <see cref="Dialog_EditIdeoDescription"/> (which
        /// carries Randomize itself), and the lock toggle mirrors the page's lock button.
        /// </summary>
        public static void OpenDescriptionMenu(Ideo ideo)
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("EditNarrative".Translate(), () =>
                    Find.WindowStack.Add(new Dialog_EditIdeoDescription(ideo))),
                // Lock toggle. MUTATION-C: mirrors IdeoUIUtility's page lock button body
                // (decompiled RimWorld/IdeoUIUtility.cs:1087-1101); the label states the CURRENT
                // lock state and selecting it flips it.
                new FloatMenuOption(LockStateText(ideo), () =>
                {
                    ideo.descriptionLocked = !ideo.descriptionLocked;
                    (ideo.descriptionLocked ? SoundDefOf.Checkbox_TurnedOn : SoundDefOf.Checkbox_TurnedOff)
                        .PlayOneShotOnCamera();
                    TolkHelper.SpeakData(LockStateText(ideo), SpeechPriority.High);
                }),
            };
            WindowlessFloatMenuState.Open(options, colonistOrders: false, titleText: "CoreNarrative".Loc().ToString());
        }

        private static string LockStateText(Ideo ideo)
        {
            return (ideo.descriptionLocked ? "LockInOn" : "LockInOff")
                .Translate("Narrative".Translate(), "NarrativeLower".Translate());
        }

        #region Culture / Styles pickers

        public static void OpenCulturePicker(Ideo ideo)
        {
            var options = new List<FloatMenuOption>();
            foreach (var culture in DefDatabase<CultureDef>.AllDefs.OrderBy(c => c.label))
            {
                var captured = culture;
                string label = culture.LabelCap.ToString();
                if (!string.IsNullOrEmpty(culture.description))
                    label += ". " + culture.description;
                if (culture == ideo.culture) label += ". " + "RimWorldAccess.Ideology.Builder.PreceptCurrent".Translate();
                options.Add(new FloatMenuOption(label, () =>
                {
                    if (ideo.culture != captured)
                    {
                        ideo.culture = captured;
                        ideo.foundation.RandomizeStyles();
                        ideo.style.RecalculateAvailableStyleItems();
                        if (ideo.foundation is IdeoFoundation_Deity deityFoundation)
                            deityFoundation.GenerateDeities();
                        ideo.RegenerateDescription(force: true);
                    }
                    AfterEdit();
                }));
            }
            if (options.Count == 0)
                options.Add(new FloatMenuOption("NoneLower".Translate(), null));
            WindowlessFloatMenuState.Open(options, colonistOrders: false, titleText: "ChooseCulture".Loc().ToString());
        }

        public static void OpenStylePicker(Ideo ideo)
        {
            OpenStylePicker(ideo, 0, "Styles".Loc().ToString());
        }

        /// <summary>
        /// The styles submenu: one row per style-category slot plus an Add row, mirroring vanilla's
        /// style-category slot model (see <see cref="IdeoStyleSlotLimit"/>, which mods raise).
        /// Re-opened — never left closed — whenever the per-slot category picker below
        /// closes, so choosing a category pops exactly ONE level (only Escape may
        /// leave a menu we constructed). <paramref name="startIndex"/> lands the cursor on the slot
        /// the picker's action affected, whose row label already states its new category;
        /// <paramref name="title"/> carries what no row can state on its own (the screen name on
        /// first open, a removal confirmation afterwards), folded into that row's announcement as a
        /// single utterance.
        /// </summary>
        private static void OpenStylePicker(Ideo ideo, int startIndex, string title)
        {
            var options = new List<FloatMenuOption>();
            var slots = ideo.thingStyleCategories;

            for (int i = 0; i < slots.Count; i++)
            {
                int slotIndex = i;
                string slotName = slots[i]?.category != null ? slots[i].category.LabelCap.ToString() : "Random".Translate().ToString();
                options.Add(new FloatMenuOption("Styles".Translate() + " " + (i + 1) + ": " + slotName,
                    () => OpenStyleSlotPicker(ideo, slotIndex)));
            }

            if (slots.Count < IdeoStyleSlotLimit.Current)
                options.Add(new FloatMenuOption("AddStyleCategory".Translate().ToString(), () => OpenStyleSlotPicker(ideo, -1)));

            WindowlessFloatMenuState.Open(options, colonistOrders: false, startIndex: startIndex,
                titleText: title);
        }

        private static void OpenStyleSlotPicker(Ideo ideo, int slotIndex)
        {
            var slots = ideo.thingStyleCategories;
            var options = new List<FloatMenuOption>();

            // Where the styles submenu lands once this picker closes, and the confirmation folded
            // into that landing. Rewritten by whichever option runs; left untouched by Escape, so
            // cancelling returns the cursor to the very slot the player drilled in from.
            int landOn = slotIndex >= 0 ? slotIndex : slots.Count;
            string confirmation = null;

            var available = DefDatabase<StyleCategoryDef>.AllDefs
                .Where(s => !s.fixedIdeoOnly && !slots.Any(p => p?.category == s))
                .ToList();

            foreach (var style in available)
            {
                var captured = style;
                string styleLabel = style.LabelCap.ToString();
                if (!string.IsNullOrEmpty(style.description))
                    styleLabel += ". " + style.description;
                options.Add(new FloatMenuOption(styleLabel, () =>
                {
                    // MUTATION-C: mirrors IdeoUIUtility.DoStyles's own add/replace float-menu
                    // bodies (decompiled RimWorld/IdeoUIUtility.cs:797-846) — vanilla assigns
                    // each new ThingStyleCategoryWithPriority priority "3 - index", where index
                    // is the slot's position among the ideo's style-category slots;
                    // Ideo.SortStyleCategories then uses that priority to arbitrate which
                    // category wins when several could style the same thing.
                    // The 3 in the priority is vanilla's own literal and is NOT the slot cap: the
                    // mods' transpiler rewrites only the loop-bound constant, so vanilla still
                    // assigns 3 - index at the raised slots.
                    if (slotIndex == -1)
                        slots.Add(new ThingStyleCategoryWithPriority(captured, 3 - slots.Count));
                    else
                    {
                        slots[slotIndex].category = captured;
                        slots[slotIndex].priority = 3 - slotIndex;
                    }
                    ideo.SortStyleCategories();
                    ideo.style.RecalculateAvailableStyleItems();
                    // SortStyleCategories reorders by priority, so the affected slot is found by
                    // its category rather than assumed to still sit at the edited index.
                    int moved = slots.FindIndex(p => p != null && p.category == captured);
                    landOn = moved >= 0 ? moved : landOn;
                    AfterEditSilent();
                }));
            }

            // Remove option for existing slots. Vanilla's Remove (IdeoUIUtility.DoStyles,
            // decompiled RimWorld/IdeoUIUtility.cs:797-802) is unconditional — no minimum-slot-
            // count guard — so this mirrors that rather than inventing a "keep at least one"
            // restriction.
            if (slotIndex >= 0)
            {
                options.Add(new FloatMenuOption("Remove".Translate().ToString(), () =>
                {
                    StyleCategoryDef removed = slots[slotIndex]?.category;
                    slots.RemoveAt(slotIndex);
                    ideo.SortStyleCategories();
                    ideo.style.RecalculateAvailableStyleItems();
                    // The removed row is gone: land on whichever row now occupies its position (the
                    // Add row once the last slot goes) and say what left, since no row can state it.
                    landOn = System.Math.Min(slotIndex, slots.Count);
                    if (removed != null)
                        confirmation = removed.LabelCap.ToString() + ", "
                            + (string)"RimWorldAccess.Ideology.Builder.Status.Removed".Translate();
                    AfterEditSilent();
                }));
            }

            if (options.Count == 0)
                options.Add(new FloatMenuOption("NoneLower".Translate(), null));
            // announceSelection: false — the styles submenu re-opening below is the confirmation;
            // the generic "<option> selected" echo on top of it would be a second utterance.
            WindowlessFloatMenuState.Open(options, colonistOrders: false, announceSelection: false,
                onClose: _ => OpenStylePicker(ideo, landOn, confirmation));
        }

        #endregion

        /// <summary>
        /// The silent twin of <see cref="AfterEdit"/>, for an edit that immediately re-opens its own
        /// menu (the styles slot picker): the host's rows are refreshed but NOT re-announced, because
        /// the re-opened menu's landing row is the single utterance the action gets. The host speaks
        /// for itself again when the player Escapes out and it regains focus.
        /// </summary>
        private static void AfterEditSilent()
        {
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            IdeoEditNotifyHub.Notify(announce: false);
        }

        private static void AfterEdit()
        {
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            // Refresh whichever ideo-editing host is currently live. See IdeoEditNotifyHub for
            // the registration contract that
            // replaced this method's former hardcoded IsActive-chain (IdeoSectionEditorState ->
            // IdeoReformState -> IdeoBuilderScreenScope's static 'active' field).
            IdeoEditNotifyHub.Notify();
        }
    }
}
