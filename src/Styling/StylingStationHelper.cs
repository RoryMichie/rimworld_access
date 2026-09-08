using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimWorldAccess
{
    /// <summary>
    /// The five player-facing styling tabs plus the two devEditMode-gated ones. Ordinals must match
    /// vanilla's nested <c>StylingTab</c> enum, since <see cref="StylingStationHelper.SetVisibleTab"/>
    /// casts this enum's int value straight onto it.
    /// </summary>
    public enum StylingTabKind
    {
        Hair,
        Beard,
        FaceTattoo,
        BodyTattoo,
        ApparelColor,
        BodyType,
        HeadType
    }

    /// <summary>
    /// Reflection bridge and data helpers for <c>Dialog_StylingStation</c>. The dialog keeps its
    /// working state and apply logic private, so Accept/Reset drive those members directly to
    /// behave exactly like vanilla's buttons.
    /// </summary>
    public static class StylingStationHelper
    {
        private static readonly Type DialogType = AccessTools.TypeByName("RimWorld.Dialog_StylingStation");
        private static readonly Type TabEnumType = DialogType?.GetNestedType("StylingTab", BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly FieldInfo PawnField = AccessTools.Field(DialogType, "pawn");
        private static readonly FieldInfo StationField = AccessTools.Field(DialogType, "stylingStation");
        private static readonly FieldInfo DesiredHairColorField = AccessTools.Field(DialogType, "desiredHairColor");
        private static readonly FieldInfo DesiredSkinColorField = AccessTools.Field(DialogType, "desiredSkinColor");
        private static readonly FieldInfo ApparelColorsField = AccessTools.Field(DialogType, "apparelColors");
        private static readonly FieldInfo CurTabField = AccessTools.Field(DialogType, "curTab");
        private static readonly FieldInfo DevEditModeField = AccessTools.Field(DialogType, "devEditMode");

        private static readonly FieldInfo InitialHairField = AccessTools.Field(DialogType, "initialHairDef");
        private static readonly FieldInfo InitialBeardField = AccessTools.Field(DialogType, "initialBeardDef");
        private static readonly FieldInfo InitialFaceTattooField = AccessTools.Field(DialogType, "initialFaceTattoo");
        private static readonly FieldInfo InitialBodyTattooField = AccessTools.Field(DialogType, "initialBodyTattoo");
        private static readonly FieldInfo InitialBodyTypeField = AccessTools.Field(DialogType, "initialBodyType");
        private static readonly FieldInfo InitialHeadTypeField = AccessTools.Field(DialogType, "initialHeadType");
        private static readonly FieldInfo InitialSkinColorField = AccessTools.Field(DialogType, "initialSkinColor");

        private static readonly PropertyInfo AllHairColorsProp = AccessTools.Property(DialogType, "AllHairColors");
        private static readonly PropertyInfo AllColorsProp = AccessTools.Property(DialogType, "AllColors");
        private static readonly PropertyInfo AllSkinColorsProp = AccessTools.Property(DialogType, "AllSkinColors");

        private static readonly MethodInfo ResetMethod = AccessTools.Method(DialogType, "Reset");
        private static readonly MethodInfo ApplyApparelColorsMethod = AccessTools.Method(DialogType, "ApplyApparelColors");

        public static Pawn GetPawn(Window dialog) => PawnField?.GetValue(dialog) as Pawn;
        public static Thing GetStation(Window dialog) => StationField?.GetValue(dialog) as Thing;

        public static Color GetDesiredHairColor(Window dialog) =>
            DesiredHairColorField != null ? (Color)DesiredHairColorField.GetValue(dialog) : Color.white;

        public static void SetDesiredHairColor(Window dialog, Color color) =>
            DesiredHairColorField?.SetValue(dialog, color);

        public static Dictionary<Apparel, Color> GetApparelColors(Window dialog) =>
            ApparelColorsField?.GetValue(dialog) as Dictionary<Apparel, Color>;

        public static Color GetDesiredSkinColor(Window dialog) =>
            DesiredSkinColorField != null ? (Color)DesiredSkinColorField.GetValue(dialog) : Color.white;

        /// <summary>Sets the desired skin color and applies it to the pawn at once, so the change is live before the announcement that follows.</summary>
        public static void SetDesiredSkinColor(Window dialog, Pawn pawn, Color color)
        {
            // MUTATION-C: mirrors Widgets.ColorSelector's ref-write to
            // desiredSkinColor AND its immediately-following unconditional
            // skinColorOverride apply (decompiled :655-656, :710-711).
            DesiredSkinColorField?.SetValue(dialog, color);
            // MUTATION-C: see above — the same vanilla lines, applied here too.
            if (pawn?.story != null)
                pawn.story.skinColorOverride = color;
        }

        /// <summary>The dialog's own "DEV: Show all" edit-mode flag (Dialog_StylingStation.cs:73).</summary>
        public static bool GetDevEditMode(Window dialog) =>
            DevEditModeField != null && (bool)DevEditModeField.GetValue(dialog);

        /// <summary>Flips the dialog's "DEV: Show all" flag so its draw path reveals every style item and the extra dev tabs.</summary>
        public static void SetDevEditMode(Window dialog, bool value)
        {
            // MUTATION-C: mirrors the vanilla "DEV: Show all" CheckboxLabeled, which
            // writes devEditMode by ref directly (Dialog_StylingStation.cs:233); the
            // field is private with no gated setter.
            DevEditModeField?.SetValue(dialog, value);
        }

        /// <summary>Syncs the dialog's visible tab to ours so a sighted onlooker sees the same panel.</summary>
        public static void SetVisibleTab(Window dialog, StylingTabKind kind)
        {
            if (CurTabField == null || TabEnumType == null) return;
            try { CurTabField.SetValue(dialog, Enum.ToObject(TabEnumType, (int)kind)); }
            catch { /* visual-only; ignore if the enum layout ever diverges */ }
        }

        /// <summary>The rows for a style-item tab, mirroring DrawStylingItemType's filter and sort.</summary>
        public static List<StyleItemDef> BuildItems(Pawn pawn, StylingTabKind kind, bool devEditMode = false)
        {
            IEnumerable<StyleItemDef> source;
            StyleItemDef initial;

            switch (kind)
            {
                case StylingTabKind.Hair:
                    source = DefDatabase<HairDef>.AllDefs;
                    initial = pawn.story.hairDef;
                    break;
                case StylingTabKind.Beard:
                    source = DefDatabase<BeardDef>.AllDefs;
                    initial = pawn.style.beardDef;
                    break;
                case StylingTabKind.FaceTattoo:
                    source = DefDatabase<TattooDef>.AllDefs.Where(t => t.tattooType == TattooType.Face);
                    initial = pawn.style.FaceTattoo;
                    break;
                case StylingTabKind.BodyTattoo:
                    source = DefDatabase<TattooDef>.AllDefs.Where(t => t.tattooType == TattooType.Body);
                    initial = pawn.style.BodyTattoo;
                    break;
                default:
                    return new List<StyleItemDef>();
            }

            return source
                .Where(x => devEditMode || PawnStyleItemChooser.WantsToUseStyle(pawn, x) || x == initial)
                .OrderByDescending(x => PawnStyleItemChooser.FrequencyFromGender(x, pawn))
                .ToList();
        }

        /// <summary>The style item currently applied to the pawn for the given tab.</summary>
        public static StyleItemDef GetCurrentItem(Pawn pawn, StylingTabKind kind)
        {
            switch (kind)
            {
                case StylingTabKind.Hair: return pawn.story.hairDef;
                case StylingTabKind.Beard: return pawn.style.beardDef;
                case StylingTabKind.FaceTattoo: return pawn.style.FaceTattoo;
                case StylingTabKind.BodyTattoo: return pawn.style.BodyTattoo;
                default: return null;
            }
        }

        /// <summary>Applies a style item live (mirrors the dialog's per-item selectAction).</summary>
        public static void SelectItem(Pawn pawn, StylingTabKind kind, StyleItemDef item)
        {
            switch (kind)
            {
                case StylingTabKind.Hair: pawn.story.hairDef = (HairDef)item; break;
                case StylingTabKind.Beard: pawn.style.beardDef = (BeardDef)item; break;
                case StylingTabKind.FaceTattoo: pawn.style.FaceTattoo = (TattooDef)item; break;
                case StylingTabKind.BodyTattoo: pawn.style.BodyTattoo = (TattooDef)item; break;
            }
            pawn.Drawer.renderer.SetAllGraphicsDirty();
        }

        /// <summary>
        /// The dev-only Body type tab's rows: every BodyTypeDef except Child/Baby, in DefDatabase's
        /// declared order (vanilla applies no sort here, unlike the style-item tabs).
        /// </summary>
        public static List<Def> BuildBodyTypeItems()
        {
            var list = new List<Def>();
            foreach (BodyTypeDef def in DefDatabase<BodyTypeDef>.AllDefs)
            {
                if (def != BodyTypeDefOf.Child && def != BodyTypeDefOf.Baby)
                    list.Add(def);
            }
            return list;
        }

        /// <summary>The dev-only Head type tab's rows: every HeadTypeDef except Skull/Stump, unsorted.</summary>
        public static List<Def> BuildHeadTypeItems()
        {
            var list = new List<Def>();
            foreach (HeadTypeDef def in DefDatabase<HeadTypeDef>.AllDefs)
            {
                if (def != HeadTypeDefOf.Skull && def != HeadTypeDefOf.Stump)
                    list.Add(def);
            }
            return list;
        }

        public static BodyTypeDef GetCurrentBodyType(Pawn pawn) => pawn.story.bodyType;
        public static HeadTypeDef GetCurrentHeadType(Pawn pawn) => pawn.story.headType;

        /// <summary>
        /// Applies a body type live. MUTATION-C: mirrors
        /// Dialog_StylingStation.DevDrawBodyType's inline selectAction delegate
        /// (decompiled :368-371, <c>pawn.story.bodyType = t;</c>) plus its
        /// ButtonInvisible branch's SetAllGraphicsDirty call (:641-643); no vehicle
        /// A/B exists because that delegate is private and inline to the tab's own
        /// IMGUI loop.
        /// </summary>
        public static void SelectBodyType(Pawn pawn, BodyTypeDef def)
        {
            pawn.story.bodyType = def;
            pawn.Drawer.renderer.SetAllGraphicsDirty();
        }

        /// <summary>
        /// Applies a head type live. MUTATION-C: mirrors
        /// Dialog_StylingStation.DevDrawHeadType's inline selectAction delegate
        /// (decompiled :380-383, <c>pawn.story.headType = t;</c>) plus its
        /// ButtonInvisible branch's SetAllGraphicsDirty call (:696-699); same
        /// no-vehicle-A/B rationale as <see cref="SelectBodyType"/>.
        /// </summary>
        public static void SelectHeadType(Pawn pawn, HeadTypeDef def)
        {
            pawn.story.headType = def;
            pawn.Drawer.renderer.SetAllGraphicsDirty();
        }

        /// <summary>The apparel-color tab's rows: worn apparel with a colorable comp, in worn order.</summary>
        public static List<Apparel> BuildApparelList(Pawn pawn, Window dialog)
        {
            var colors = GetApparelColors(dialog);
            if (colors == null || pawn?.apparel == null) return new List<Apparel>();
            return pawn.apparel.WornApparel.Where(colors.ContainsKey).ToList();
        }

        // The dialog's own lists, so the swatch set matches what a sighted player sees: it dedups
        // near-identical colors (hair within 0.15, apparel by IndistinguishableFrom).

        /// <summary>The deduped hair color swatches (raw Colors), as the dialog builds them.</summary>
        public static List<Color> GetAllHairColors(Window dialog) =>
            AllHairColorsProp?.GetValue(dialog) as List<Color> ?? new List<Color>();

        /// <summary>The deduped apparel color swatches (raw Colors), as the dialog builds them.</summary>
        public static List<Color> GetAllColors(Window dialog) =>
            AllColorsProp?.GetValue(dialog) as List<Color> ?? new List<Color>();

        /// <summary>The skin color swatches (raw Colors) — one per skin-color gene.</summary>
        public static List<Color> GetAllSkinColors(Window dialog) =>
            AllSkinColorsProp?.GetValue(dialog) as List<Color> ?? new List<Color>();

        /// <summary>The hint vanilla marks a favorite-color or ideoligion-color apparel swatch with; null when unmarked.</summary>
        public static string ApparelColorSwatchTip(Pawn pawn, Color color)
        {
            if (pawn?.story?.favoriteColor != null && color.IndistinguishableFrom(pawn.story.favoriteColor.color))
                return "FavoriteColorPickerTip".Translate(pawn.Named("PAWN"));
            if (pawn?.Ideo != null && !Find.IdeoManager.classicMode && color.IndistinguishableFrom(pawn.Ideo.ApparelColor))
                return "IdeoColorPickerTip".Translate(pawn.Named("PAWN"));
            return null;
        }

        /// <summary>Applies all pending changes like the dialog's Accept button: schedules the styling
        /// job when anything changed, applies pending apparel colors, then closes.</summary>
        public static void Accept(Window dialog)
        {
            Pawn pawn = GetPawn(dialog);
            Thing station = GetStation(dialog);
            if (pawn == null) { dialog.Close(); return; }

            // MUTATION-C: mirrors Dialog_StylingStation.DoBottomButtons' Accept
            // branch's full "anything changed" condition (decompiled :731-739), now
            // that the dev-only Body type/Head type tabs and the skin-color picker
            // make bodyType/headType/skinColorOverride reachable too — vanilla's own
            // condition already compares all three alongside hair/beard/tattoo/
            // hairColor; this hand-copy previously omitted them, harmless only while
            // those fields were unreachable, which the two dev tabs changed. No vehicle
            // A/B exists because vanilla's condition is private and inline to
            // DoBottomButtons' own button click handler.
            Color? initialSkinColor = InitialSkinColorField != null ? (Color?)InitialSkinColorField.GetValue(dialog) : null;
            Color? currentSkinColor = pawn.story.skinColorOverride;
            bool styleChanged =
                pawn.story.hairDef != (HairDef)InitialHairField?.GetValue(dialog)
                || pawn.style.beardDef != (BeardDef)InitialBeardField?.GetValue(dialog)
                || pawn.style.FaceTattoo != (TattooDef)InitialFaceTattooField?.GetValue(dialog)
                || pawn.style.BodyTattoo != (TattooDef)InitialBodyTattooField?.GetValue(dialog)
                || pawn.story.HairColor != GetDesiredHairColor(dialog)
                || currentSkinColor != initialSkinColor
                || pawn.story.bodyType != (BodyTypeDef)InitialBodyTypeField?.GetValue(dialog)
                || pawn.story.headType != (HeadTypeDef)InitialHeadTypeField?.GetValue(dialog);

            if (styleChanged && station != null)
            {
                pawn.style.SetupNextLookChangeData(pawn.story.hairDef, pawn.style.beardDef,
                    pawn.style.FaceTattoo, pawn.style.BodyTattoo, GetDesiredHairColor(dialog));
                // Revert the live preview defs (the scheduled job applies the desired look);
                // resetColors:false keeps the pending apparel and hair colors.
                ResetMethod?.Invoke(dialog, new object[] { false });
                pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.UseStylingStation, station), JobTag.Misc);
            }

            ApplyApparelColorsMethod?.Invoke(dialog, null);
            dialog.Close();
        }

        /// <summary>Reverts every pending change (mirrors the dialog's Reset button).</summary>
        public static void ResetAll(Window dialog)
        {
            ResetMethod?.Invoke(dialog, new object[] { true });
        }

        /// <summary>Vanilla's red unwanted-style banner: the pawn's ideoligion rejects one of its
        /// hair/beard/face tattoo/body tattoo. Null when nothing is unwanted.</summary>
        public static string GetUnwantedStyleWarning(Pawn pawn)
        {
            if (pawn?.style == null || !pawn.style.HasAnyUnwantedStyleItem) return null;

            var unwantedNames = new List<string>();
            if (pawn.style.HasUnwantedHairStyle) unwantedNames.Add((string)"Hair".Translate());
            if (pawn.style.HasUnwantedBeard) unwantedNames.Add((string)"Beard".Translate());
            if (pawn.style.HasUnwantedFaceTattoo) unwantedNames.Add((string)"TattooFace".Translate());
            if (pawn.style.HasUnwantedBodyTattoo) unwantedNames.Add((string)"TattooBody".Translate());

            return (string)"Warning".Translate() + ": "
                + (string)"PawnUnhappyWithStyleItems".Translate(pawn.Named("PAWN")) + ": "
                + unwantedNames.ToCommaList().CapitalizeFirst();
        }

        /// <summary>
        /// The hair-color dye cost/shortage warning, exactly matching vanilla's
        /// DrawHairColors gate (Dialog_StylingStation.cs:411-428): only relevant once
        /// the pawn is spawned, the pending hair color differs from its current one,
        /// and that pending color isn't already queued as nextHairColor (a change
        /// already scheduled, so no fresh dye is needed for it). Returns null when the
        /// requirement doesn't apply.
        /// </summary>
        public static string GetHairDyeWarning(Window dialog, Pawn pawn)
        {
            if (pawn == null || !pawn.Spawned) return null;

            Color desired = GetDesiredHairColor(dialog);
            if (desired == pawn.story.HairColor) return null;

            Color? next = pawn.style?.nextHairColor;
            if (next.HasValue && desired == next.Value) return null;

            string required = (string)"Required".Translate() + ": 1 " + ThingDefOf.Dye.label;
            if (pawn.Map.resourceCounter.GetCount(ThingDefOf.Dye) < 1)
            {
                required += ". " + (string)"NotEnoughDye".Translate() + " " + (string)"NotEnoughDyeWillRecolorHair".Translate();
            }
            return required;
        }

        /// <summary>
        /// The apparel-color dye cost/shortage warning, exactly matching vanilla's
        /// DrawApparelColor gate (Dialog_StylingStation.cs:485-499): the count of
        /// unlocked worn apparel whose pending color differs from its current one
        /// (num), shown only once the pawn is spawned.
        /// </summary>
        public static string GetApparelDyeWarning(Window dialog, Pawn pawn)
        {
            if (pawn == null || !pawn.Spawned || pawn.apparel == null) return null;

            var colors = GetApparelColors(dialog);
            if (colors == null) return null;

            // Matches vanilla's own !IsLocked(item) || DevMode gate (Dialog_StylingStation.cs:445).
            // DevMode there means "stylingStation == null" (Dialog_StylingStation.cs:109) -- true
            // only for the debug action that opens this dialog without a real station -- so in
            // that case vanilla counts locked apparel toward the dye requirement too.
            bool devMode = GetStation(dialog) == null;

            int num = 0;
            foreach (Apparel item in pawn.apparel.WornApparel)
            {
                if (!colors.TryGetValue(item, out Color value)) continue;
                if (pawn.apparel.IsLocked(item) && !devMode) continue;
                if (!value.IndistinguishableFrom(item.GetColorIgnoringTainted())) num++;
            }

            if (num == 0) return null;

            string required = (string)"Required".Translate() + ": " + num + " " + ThingDefOf.Dye.label;
            if (pawn.Map.resourceCounter.GetCount(ThingDefOf.Dye) < num)
            {
                required += ". " + (string)"NotEnoughDye".Translate() + " " + (string)"NotEnoughDyeWillRecolorApparel".Translate();
            }
            return required;
        }
    }
}
