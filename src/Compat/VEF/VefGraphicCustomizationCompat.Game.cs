using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimWorldAccess
{
    /// <summary>
    /// Reflection-only compatibility surface for Vanilla Expanded Framework's appearance
    /// customization dialog -- <c>VEF.Graphics.Dialog_GraphicCustomization</c> (opened from a
    /// float menu on an item with <c>CompGraphicCustomization</c>). Mirrors the
    /// <see cref="VefHireCompat"/>/<see cref="VefContractsCompat"/> idiom: every VEF type and
    /// member is resolved once behind <see cref="Ready"/>, a missing TYPE is a silent decline
    /// (VEF not loaded), a resolved type missing a MEMBER is a logged error and a graceful
    /// decline. <see cref="Shell.VefGraphicCustomizationScope"/> is the sole consumer and never
    /// touches reflection directly -- every VEF-typed value (the dialog, a comp, a graphic part,
    /// a texture variant) stays boxed as <c>object</c>/<see cref="Window"/> here and is read back
    /// only through this facade's own methods.
    ///
    /// The dialog's whole point is a live preview image a screen-reader user cannot see; the
    /// accessible substitute this facade supports is naming the CURRENT VARIANT of each
    /// appearance part instead ("variant previewer" pattern).
    /// </summary>
    internal static class VefGraphicCustomizationCompat
    {
        private static readonly Type dialogType;
        private static readonly Type compType;
        private static readonly Type compPropsType;
        private static readonly Type graphicPartType;
        private static readonly Type textureVariantType;
        private static readonly Type defOfType;

        private static readonly FieldInfo compField;
        private static readonly FieldInfo currentVariantsField;
        private static readonly FieldInfo currentNameField;
        private static readonly FieldInfo compGeneratedNameField;
        private static readonly FieldInfo pawnField;

        private static readonly MethodInfo updateTextureMethod;
        private static readonly MethodInfo randomizeMethod;

        private static readonly FieldInfo texVariantsToCustomizeField;

        private static readonly FieldInfo graphicsField;

        private static readonly FieldInfo partNameField;
        private static readonly FieldInfo partTexVariantsField;

        private static readonly FieldInfo texNameField;

        private static readonly FieldInfo customizeJobField;

        private static readonly FieldInfo generatedNameField;

        private static readonly bool ready;

        public static bool Ready => ready;
        public static Type DialogType => dialogType;

        static VefGraphicCustomizationCompat()
        {
            var surface = new ReflectionSurface("VefGraphicCustomizationCompat");

            dialogType = surface.Type("VEF.Graphics.Dialog_GraphicCustomization");
            compType = surface.Type("VEF.Graphics.CompGraphicCustomization");
            compPropsType = surface.Type("VEF.Graphics.CompProperties_GraphicCustomization");
            graphicPartType = surface.Type("VEF.Graphics.GraphicPart");
            textureVariantType = surface.Type("VEF.Graphics.TextureVariant");
            defOfType = surface.Type("VEF.Graphics.GraphicCustomization_DefOf");

            compField = surface.Field(dialogType, "comp");
            currentVariantsField = surface.Field(dialogType, "currentVariants");
            currentNameField = surface.Field(dialogType, "currentName");
            compGeneratedNameField = surface.Field(dialogType, "compGeneratedName");
            pawnField = surface.Field(dialogType, "pawn");

            updateTextureMethod = surface.Method(dialogType, "UpdateTexture");
            randomizeMethod = surface.Method(dialogType, "Randomize");

            texVariantsToCustomizeField = surface.Field(compType, "texVariantsToCustomize");

            graphicsField = surface.Field(compPropsType, "graphics");

            partNameField = surface.Field(graphicPartType, "name");
            partTexVariantsField = surface.Field(graphicPartType, "texVariants");

            texNameField = surface.Field(textureVariantType, "texName");

            customizeJobField = surface.Field(defOfType, "VEF_CustomizeItem");

            generatedNameField = surface.Field(typeof(CompGeneratedNames), "name");

            ready = surface.Ready;
        }

        // ------------------------------------------------------------------
        // Helpers.
        // ------------------------------------------------------------------

        /// <summary>
        /// The part's currently selected variant -- the entry in <c>graphicPart.texVariants</c>
        /// that also appears in the dialog's <c>currentVariants</c> (matched by value-equality,
        /// mirroring <c>DrawCustomizationArea</c>'s own lookup).
        /// </summary>
        private static object CurrentVariantForPart(Window dialog, object part)
        {
            var partVariants = (IList)partTexVariantsField.GetValue(part);
            var currentVariants = (IList)currentVariantsField.GetValue(dialog);
            if (partVariants == null || currentVariants == null)
                return null;
            foreach (var v in partVariants)
            {
                if (currentVariants.Contains(v))
                    return v;
            }
            return null;
        }

        // ------------------------------------------------------------------
        // Reads.
        // ------------------------------------------------------------------

        public static string ItemLabel(Window dialog)
        {
            if (!ready)
                return "";
            try
            {
                object c = compField.GetValue(dialog);
                return c == null ? "" : ((ThingComp)c).parent.LabelCap;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VefGraphicCustomizationCompat.ItemLabel failed: {ex.Message}");
                return "";
            }
        }

        public static List<object> Parts(Window dialog)
        {
            var result = new List<object>();
            if (!ready)
                return result;
            try
            {
                object c = compField.GetValue(dialog);
                CompProperties props = c == null ? null : ((ThingComp)c).props;
                if (props == null)
                    return result;
                if (graphicsField.GetValue(props) is IList graphics)
                {
                    foreach (object part in graphics)
                    {
                        result.Add(part);
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VefGraphicCustomizationCompat.Parts failed: {ex.Message}");
            }
            return result;
        }

        public static string PartName(object part)
        {
            if (!ready || part == null)
                return "";
            try
            {
                return (string)partNameField.GetValue(part);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VefGraphicCustomizationCompat.PartName failed: {ex.Message}");
                return "";
            }
        }

        public static List<object> PartVariants(object part)
        {
            var result = new List<object>();
            if (!ready || part == null)
                return result;
            try
            {
                if (partTexVariantsField.GetValue(part) is IList variants)
                {
                    foreach (object v in variants)
                    {
                        result.Add(v);
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VefGraphicCustomizationCompat.PartVariants failed: {ex.Message}");
            }
            return result;
        }

        public static string VariantName(object variant)
        {
            if (!ready || variant == null)
                return "";
            try
            {
                return (string)texNameField.GetValue(variant);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VefGraphicCustomizationCompat.VariantName failed: {ex.Message}");
                return "";
            }
        }

        public static string CurrentVariantName(Window dialog, object part)
        {
            if (!ready)
                return "";
            try
            {
                return VariantName(CurrentVariantForPart(dialog, part));
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VefGraphicCustomizationCompat.CurrentVariantName failed: {ex.Message}");
                return "";
            }
        }

        public static bool HasNameField(Window dialog)
        {
            if (!ready)
                return false;
            try
            {
                return compGeneratedNameField.GetValue(dialog) != null;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VefGraphicCustomizationCompat.HasNameField failed: {ex.Message}");
                return false;
            }
        }

        public static string CurrentName(Window dialog)
        {
            if (!ready)
                return "";
            try
            {
                return (string)currentNameField.GetValue(dialog) ?? "";
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VefGraphicCustomizationCompat.CurrentName failed: {ex.Message}");
                return "";
            }
        }

        // ------------------------------------------------------------------
        // Mutators.
        // ------------------------------------------------------------------

        /// <summary>
        /// Sets a part's variant directly to the chosen entry, mirroring the center dropdown
        /// action's <c>FloatMenu</c> selection.
        /// </summary>
        public static void SetVariant(Window dialog, object part, object variant)
        {
            if (!ready)
                return;
            try
            {
                var currentVariants = (IList)currentVariantsField.GetValue(dialog);
                if (currentVariants == null)
                    return;
                object current = CurrentVariantForPart(dialog, part);
                if (current == null)
                    return;
                int slot = currentVariants.IndexOf(current);
                if (slot < 0)
                    return;
                // MUTATION-C: mirrors Dialog_GraphicCustomization's center dropdown FloatMenu action
                // (GenCollection.Replace(currentVariants, current, chosen); UpdateTexture()) --
                // inline delegate, no callable vehicle.
                currentVariants[slot] = variant;
                updateTextureMethod.Invoke(dialog, null);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VefGraphicCustomizationCompat.SetVariant failed: {ex.Message}");
            }
        }

        public static void SetName(Window dialog, string name)
        {
            if (!ready)
                return;
            try
            {
                // MUTATION-C: mirrors the dialog's own Widgets.TextField write-back to currentName
                // (staging only; applied to the item on Confirm). No callable setter.
                currentNameField.SetValue(dialog, name ?? "");
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VefGraphicCustomizationCompat.SetName failed: {ex.Message}");
            }
        }

        /// <summary>Vehicle A: the dialog's own <c>Randomize</c> method.</summary>
        public static void Randomize(Window dialog)
        {
            if (!ready)
                return;
            try
            {
                randomizeMethod.Invoke(dialog, null);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VefGraphicCustomizationCompat.Randomize failed: {ex.Message}");
            }
        }

        /// <summary>Mirrors the dialog's own inline Confirm button delegate.</summary>
        public static void Confirm(Window dialog)
        {
            if (!ready)
                return;
            try
            {
                object comp = compField.GetValue(dialog);
                object currentVariants = currentVariantsField.GetValue(dialog);
                // MUTATION-C: mirrors the Confirm delegate staging the working selection onto the
                // comp (comp.texVariantsToCustomize = currentVariants; VEF
                // Dialog_GraphicCustomization confirm ~L32038).
                texVariantsToCustomizeField.SetValue(comp, currentVariants);

                object nameComp = compGeneratedNameField.GetValue(dialog);
                if (nameComp != null)
                {
                    // MUTATION-C: mirrors the confirm delegate writing the item's generated name
                    // (private field, ~L32041).
                    generatedNameField.SetValue(nameComp, currentNameField.GetValue(dialog));
                }

                var parent = ((ThingComp)comp).parent;
                var pawn = (Pawn)pawnField.GetValue(dialog);
                var jobDef = (JobDef)customizeJobField.GetValue(null);
                // Vehicle A: enqueue the game's own customize job; JobDriver_CustomizeItem runs
                // comp.Customize().
                pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(jobDef, parent), JobTag.Misc, false);
                dialog.Close(true);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VefGraphicCustomizationCompat.Confirm failed: {ex.Message}");
            }
        }
    }
}
