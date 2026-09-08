using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Reflection-only compatibility surface for Alpha Memes' four <c>Dialog_ChangeStyles*</c>
    /// windows, the icon grids its style-change ability opens. All four are the same grid over a
    /// <c>List&lt;StyleCategoryDef&gt;</c>; they differ only in what they hold the styles FOR --
    /// one thing, a set of things, or a set of things plus the source style a swap is matching
    /// against. <see cref="Shell.AlphaMemesStylePickerScope"/> reads every one of them through
    /// here and never touches reflection itself.
    ///
    /// The swap pair is a two-step flow: the first window picks a source style and opens the
    /// second, so <see cref="CreateSwapSecond"/> lives here too -- it hands the successor the
    /// very same <c>HashSet&lt;Thing&gt;</c> instance the first window was constructed with,
    /// which a copy would break.
    /// </summary>
    internal static class AlphaMemesStyleCompat
    {
        private static readonly Type singleType;
        private static readonly Type areaType;
        private static readonly Type swapType;
        private static readonly Type swapSecondType;

        private static readonly FieldInfo singleThingField;
        private static readonly FieldInfo areaThingsField;
        private static readonly FieldInfo swapThingsField;
        private static readonly FieldInfo swapSecondThingsField;
        private static readonly FieldInfo swapSecondStyleField;

        private static readonly FieldInfo singleStylesField;
        private static readonly FieldInfo areaStylesField;
        private static readonly FieldInfo swapStylesField;
        private static readonly FieldInfo swapSecondStylesField;

        private static readonly ConstructorInfo swapSecondCtor;

        private static readonly bool ready;

        public static bool Ready => ready;
        public static Type SingleType => singleType;
        public static Type AreaType => areaType;
        public static Type SwapType => swapType;
        public static Type SwapSecondType => swapSecondType;

        static AlphaMemesStyleCompat()
        {
            var surface = new ReflectionSurface("AlphaMemesStyles");

            singleType = surface.Type("AlphaMemes.Dialog_ChangeStyles");
            areaType = surface.Type("AlphaMemes.Dialog_ChangeStyles_Area");
            swapType = surface.Type("AlphaMemes.Dialog_ChangeStyles_Swap");
            swapSecondType = surface.Type("AlphaMemes.Dialog_ChangeStyles_Swap_Second");

            singleThingField = surface.Field(singleType, "thingToChange");
            areaThingsField = surface.Field(areaType, "thingsToChange");
            swapThingsField = surface.Field(swapType, "thingsToChange");
            swapSecondThingsField = surface.Field(swapSecondType, "thingsToChange");
            swapSecondStyleField = surface.Field(swapSecondType, "styleToBeChanged");

            singleStylesField = surface.Field(singleType, "listStyles");
            areaStylesField = surface.Field(areaType, "listStyles");
            swapStylesField = surface.Field(swapType, "listStyles");
            swapSecondStylesField = surface.Field(swapSecondType, "listStyles");

            swapSecondCtor = surface.Constructor(swapSecondType,
                new[] { typeof(HashSet<Thing>), typeof(StyleCategoryDef) });

            ready = surface.Ready;
        }

        // ------------------------------------------------------------------
        // Reads.
        // ------------------------------------------------------------------

        /// <summary>
        /// The style categories the dialog was constructed with, in its own draw order. Already
        /// narrowed by the dialog itself: to the primary ideo's categories unless the mod's
        /// makeChangeStyleAbilityUseAllStyles setting is on, and additionally (single-thing
        /// dialog only) to categories that have a style for the target's def.
        /// </summary>
        public static List<StyleCategoryDef> Styles(Window dialog)
        {
            var result = new List<StyleCategoryDef>();
            FieldInfo field = FieldFor(dialog, singleStylesField, areaStylesField, swapStylesField, swapSecondStylesField);
            if (field == null)
                return result;
            try
            {
                if (field.GetValue(dialog) is IList styles)
                {
                    foreach (object style in styles)
                    {
                        if (style is StyleCategoryDef typed)
                            result.Add(typed);
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error($"AlphaMemesStyleCompat.Styles failed: {ex.Message}");
            }
            return result;
        }

        /// <summary>The single-thing dialog's target, or null on any other variant.</summary>
        public static Thing SingleThing(Window dialog)
        {
            FieldInfo field = FieldFor(dialog, singleThingField, null, null, null);
            if (field == null)
                return null;
            try
            {
                return field.GetValue(dialog) as Thing;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"AlphaMemesStyleCompat.SingleThing failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>The multi-thing variants' target set, snapshotted for iteration.</summary>
        public static List<Thing> Things(Window dialog)
        {
            var result = new List<Thing>();
            FieldInfo field = FieldFor(dialog, null, areaThingsField, swapThingsField, swapSecondThingsField);
            if (field == null)
                return result;
            try
            {
                if (field.GetValue(dialog) is IEnumerable things)
                {
                    foreach (object thing in things)
                    {
                        if (thing is Thing typed)
                            result.Add(typed);
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error($"AlphaMemesStyleCompat.Things failed: {ex.Message}");
            }
            return result;
        }

        /// <summary>
        /// The source style the swap's second window is matching things against, null when the
        /// player picked "no style" in the first window (a meaningful value there, not an
        /// absence) or on any other variant.
        /// </summary>
        public static StyleCategoryDef SourceStyle(Window dialog)
        {
            FieldInfo field = FieldFor(dialog, null, null, null, swapSecondStyleField);
            if (field == null)
                return null;
            try
            {
                return field.GetValue(dialog) as StyleCategoryDef;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"AlphaMemesStyleCompat.SourceStyle failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// The successor window the swap's first step opens, built with that step's own live
        /// thing set rather than a copy of it.
        /// </summary>
        public static Window CreateSwapSecond(Window swapDialog, StyleCategoryDef sourceStyle)
        {
            if (!ready || swapThingsField == null || swapSecondCtor == null)
                return null;
            try
            {
                object things = swapThingsField.GetValue(swapDialog);
                return swapSecondCtor.Invoke(new object[] { things, sourceStyle }) as Window;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"AlphaMemesStyleCompat.CreateSwapSecond failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>Picks the per-variant member for whichever of the four dialogs this is.</summary>
        private static FieldInfo FieldFor(Window dialog, FieldInfo single, FieldInfo area, FieldInfo swap, FieldInfo swapSecond)
        {
            if (!ready || dialog == null)
                return null;
            if (singleType.IsInstanceOfType(dialog))
                return single;
            if (areaType.IsInstanceOfType(dialog))
                return area;
            if (swapType.IsInstanceOfType(dialog))
                return swap;
            if (swapSecondType.IsInstanceOfType(dialog))
                return swapSecond;
            return null;
        }
    }
}
