using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Reflection-only compatibility surface for Vanilla Expanded Framework's
    /// <c>VEF.Memes.Dialog_FloatMenuOptions</c> -- the searchable window its IdeoFloatMenuPlus
    /// feature substitutes for a plain <c>FloatMenu</c> once a precept's option list passes
    /// thirty entries (venerated animals, styles). The window holds the very same
    /// <see cref="FloatMenuOption"/> objects vanilla would have put in the menu, plus its own
    /// search box, so <see cref="Shell.VefPreceptOptionsScope"/> reads both through here and
    /// never touches reflection itself.
    /// </summary>
    internal static class VefPreceptOptionsCompat
    {
        private static readonly Type dialogType;
        private static readonly FieldInfo optionsField;
        private static readonly FieldInfo searchTextField;
        private static readonly bool ready;

        public static bool Ready => ready;
        public static Type DialogType => dialogType;

        static VefPreceptOptionsCompat()
        {
            var surface = new ReflectionSurface("VefPreceptOptions");

            dialogType = surface.Type("VEF.Memes.Dialog_FloatMenuOptions");

            optionsField = surface.Field(dialogType, "options");
            searchTextField = surface.Field(dialogType, "searchText");

            ready = surface.Ready;
        }

        // ------------------------------------------------------------------
        // Reads.
        // ------------------------------------------------------------------

        /// <summary>Every option the dialog was constructed with, unfiltered.</summary>
        public static List<FloatMenuOption> Options(Window dialog)
        {
            var result = new List<FloatMenuOption>();
            if (!ready)
                return result;
            try
            {
                if (optionsField.GetValue(dialog) is IList options)
                {
                    foreach (object option in options)
                    {
                        if (option is FloatMenuOption typed)
                            result.Add(typed);
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VefPreceptOptionsCompat.Options failed: {ex.Message}");
            }
            return result;
        }

        /// <summary>The live contents of the dialog's search box, which is what it filters its drawn list by.</summary>
        public static string SearchText(Window dialog)
        {
            if (!ready)
                return "";
            try
            {
                return (string)searchTextField.GetValue(dialog) ?? "";
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VefPreceptOptionsCompat.SearchText failed: {ex.Message}");
                return "";
            }
        }

        // ------------------------------------------------------------------
        // Mutators.
        // ------------------------------------------------------------------

        public static void SetSearchText(Window dialog, string text)
        {
            if (!ready)
                return;
            try
            {
                // MUTATION-C: mirrors the dialog's own Widgets.TextField write-back to searchText
                // (Dialog_FloatMenuOptions.DoWindowContents). Display state only, no setter.
                searchTextField.SetValue(dialog, text ?? "");
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VefPreceptOptionsCompat.SetSearchText failed: {ex.Message}");
            }
        }
    }
}
