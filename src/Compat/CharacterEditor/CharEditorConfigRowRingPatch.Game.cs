using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Arms the listing focus ring for <see cref="CharEditorConfigScope"/>. The mod options dialog
    /// draws its Options rows through <c>Listing_X.CheckboxLabeledWithDefault</c> and its Custom
    /// Data / Pawn Slots rows through <c>Listing_X.TextEntryLabeledWithDefaultAndCopy</c>; both open
    /// with a <c>Listing.GetRect</c> call (ilspy CharacterEditor.dll v1.6: BaseCheckboxLabeled's
    /// first statement, TextEntryLabeledWithDefaultAndCopy's first statement), so the shell's
    /// universal ring point already sees every row. What it lacks is identity and a caption, which
    /// the four patches here supply:
    /// <list type="bullet">
    /// <item>Section brackets on <c>DrawBoolean</c>/<c>DrawStrings</c> open a sticky keyed row, so
    /// every row those methods draw carries the section's key.</item>
    /// <item>Label taps on the two row methods publish the caption vanilla is about to draw, giving
    /// the ring the tripwire that picks one row out of the section.</item>
    /// </list>
    /// The Hotkeys and Numerics regions draw at fixed SZWidgets rects with no Listing at all
    /// (DrawHotkey, DrawNumeric), so no row ever reaches the listing ring point there. Those two
    /// regions ring through <see cref="CharEditorConfigScope.FocusedContentRect"/> instead, fed by
    /// the second mechanism below:
    /// <list type="bullet">
    /// <item>Section brackets on <c>DrawHotkey</c>/<c>DrawNumeric</c> name the region being drawn
    /// and hold the dialog instance.</item>
    /// <item>A tap on <c>SZWidgets.Label</c> -- the caption every one of those rows opens with --
    /// reads the dialog's OWN rect properties (RectHotkeyLabel/RectHotkey/RectRemoveHotkey for a
    /// hotkey row, RectHalfWidth for a numeric one) off that instance at draw time. They are
    /// computed from a running <c>y</c> field the dialog advances only AFTER the row finishes, so
    /// reading them at the caption is reading the row's own geometry, with no layout math of
    /// ours.</item>
    /// </list>
    ///
    /// Every ListingRowCapture entry point used here is pass-gated, so all four patches are inert
    /// unless the scope has a ring pass open. Reflection-only throughout: a hard reference to
    /// CharacterEditor.dll would poison the type for the whole session on a startup type sweep.
    /// </summary>
    internal static class CharEditorConfigRowRingPatch
    {
        /// <summary>
        /// The section keys are ours rather than harvested from the mod, so they use a dotted shape
        /// instead of the '#'-suffixed synthetic form the matcher distrusts -- and the focus side
        /// must therefore ALWAYS pair them with a tripwire, since one key stands for a whole
        /// section's worth of rows.
        /// </summary>
        internal const string BoolSectionKey = "CharEd.Config.Bools";

        /// <summary>Covers both Custom Data and Pawn Slots: DrawStrings draws them in one listing.</summary>
        internal const string StringSectionKey = "CharEd.Config.Strings";

        /// <summary>Set while a <see cref="CharEditorConfigScope"/> drives; the taps are one static read otherwise.</summary>
        internal static bool Recording;

        private static MethodInfo rectHotkeyLabelGetter;
        private static MethodInfo rectHotkeyGetter;
        private static MethodInfo rectRemoveHotkeyGetter;
        private static MethodInfo rectHalfWidthGetter;

        // Rects recorded by the current/most recent draw, in absolute UI points. The hotkey pair
        // is written in place every frame (two rows per DrawHotkey call, at the base its kbdName
        // picks); the numeric list is cleared and refilled by DrawNumeric's own bracket.
        private static readonly Rect[] hotkeyRects = new Rect[4];
        private static readonly List<Rect> numericRects = new List<Rect>();

        // The dialog instance whose DrawHotkey/DrawNumeric body is on the call stack, or null.
        // Never both: DoWindowContents calls the two in sequence and neither nests the other.
        private static object hotkeyDialog;
        private static int hotkeyBase;
        private static object numericDialog;

        /// <summary>Called from CharEditorDialogCompat after the options dialog's scope registers. No-op when anything fails to resolve.</summary>
        public static void Register()
        {
            try
            {
                Type dialogType = CharEditorConfigCompat.DialogType;
                Type listingType = AccessTools.TypeByName("CharacterEditor.Listing_X");
                if (dialogType == null || listingType == null)
                {
                    ModLogger.Error("CharEditorConfigRowRingPatch: could not resolve DialogConfigurate/Listing_X; declining the ring patches.");
                    return;
                }

                MethodInfo drawBoolean = AccessTools.Method(dialogType, "DrawBoolean");
                MethodInfo drawStrings = AccessTools.Method(dialogType, "DrawStrings");
                MethodInfo checkboxRow = AccessTools.Method(listingType, "CheckboxLabeledWithDefault");
                MethodInfo textRow = AccessTools.Method(listingType, "TextEntryLabeledWithDefaultAndCopy");
                if (drawBoolean == null || drawStrings == null || checkboxRow == null || textRow == null)
                {
                    ModLogger.Error("CharEditorConfigRowRingPatch: could not resolve the options dialog's row methods; declining the ring patches.");
                    return;
                }

                Harmony harmony = RimWorldAccessMod.HarmonyInstance;
                harmony.Patch(drawBoolean,
                    prefix: new HarmonyMethod(typeof(CharEditorConfigRowRingPatch), nameof(EnterBoolSection)),
                    postfix: new HarmonyMethod(typeof(CharEditorConfigRowRingPatch), nameof(ExitSection)));
                harmony.Patch(drawStrings,
                    prefix: new HarmonyMethod(typeof(CharEditorConfigRowRingPatch), nameof(EnterStringSection)),
                    postfix: new HarmonyMethod(typeof(CharEditorConfigRowRingPatch), nameof(ExitSection)));
                harmony.Patch(checkboxRow,
                    prefix: new HarmonyMethod(typeof(CharEditorConfigRowRingPatch), nameof(EnterRowLabel)),
                    postfix: new HarmonyMethod(typeof(CharEditorConfigRowRingPatch), nameof(ExitRowLabel)));
                harmony.Patch(textRow,
                    prefix: new HarmonyMethod(typeof(CharEditorConfigRowRingPatch), nameof(EnterRowLabel)),
                    postfix: new HarmonyMethod(typeof(CharEditorConfigRowRingPatch), nameof(ExitRowLabel)));

                RegisterFixedRectRing(harmony, dialogType);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"CharEditorConfigRowRingPatch.Register failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Arms the Hotkeys/Numerics ring. Declines quietly and leaves the two regions unringed if
        /// any member is missing -- a mod update that renames one must never break the screen.
        /// </summary>
        private static void RegisterFixedRectRing(Harmony harmony, Type dialogType)
        {
            Type szWidgetsType = AccessTools.TypeByName("CharacterEditor.SZWidgets");
            Type optionSType = AccessTools.TypeByName("CharacterEditor.OptionS");
            if (szWidgetsType == null || optionSType == null)
            {
                ModLogger.Error("CharEditorConfigRowRingPatch: could not resolve SZWidgets/OptionS; declining the fixed-rect ring.");
                return;
            }

            MethodInfo drawHotkey = AccessTools.Method(dialogType, "DrawHotkey",
                new[] { typeof(string), optionSType, typeof(string), typeof(string) });
            MethodInfo drawNumeric = AccessTools.Method(dialogType, "DrawNumeric", Type.EmptyTypes);
            MethodInfo label = AccessTools.Method(szWidgetsType, "Label",
                new[] { typeof(Rect), typeof(string), typeof(Action), typeof(string) });
            rectHotkeyLabelGetter = AccessTools.PropertyGetter(dialogType, "RectHotkeyLabel");
            rectHotkeyGetter = AccessTools.PropertyGetter(dialogType, "RectHotkey");
            rectRemoveHotkeyGetter = AccessTools.PropertyGetter(dialogType, "RectRemoveHotkey");
            rectHalfWidthGetter = AccessTools.PropertyGetter(dialogType, "RectHalfWidth");
            if (drawHotkey == null || drawNumeric == null || label == null || rectHotkeyLabelGetter == null
                || rectHotkeyGetter == null || rectRemoveHotkeyGetter == null || rectHalfWidthGetter == null)
            {
                ModLogger.Error("CharEditorConfigRowRingPatch: could not resolve the options dialog's fixed-rect draw members; declining the fixed-rect ring.");
                return;
            }

            harmony.Patch(drawHotkey,
                prefix: new HarmonyMethod(typeof(CharEditorConfigRowRingPatch), nameof(EnterHotkeyRow)),
                finalizer: new HarmonyMethod(typeof(CharEditorConfigRowRingPatch), nameof(ExitHotkeyRow)));
            harmony.Patch(drawNumeric,
                prefix: new HarmonyMethod(typeof(CharEditorConfigRowRingPatch), nameof(EnterNumericSection)),
                finalizer: new HarmonyMethod(typeof(CharEditorConfigRowRingPatch), nameof(ExitNumericSection)));
            harmony.Patch(label,
                postfix: new HarmonyMethod(typeof(CharEditorConfigRowRingPatch), nameof(RecordRowFromCaption)));
        }

        /// <summary>The Hotkeys row rect recorded by the most recent draw, or empty when it drew none.</summary>
        internal static Rect HotkeyRowRect(int index)
        {
            return index >= 0 && index < hotkeyRects.Length ? hotkeyRects[index] : default(Rect);
        }

        /// <summary>
        /// The Numerics row rect recorded by the most recent draw. Draw order is the identity:
        /// DrawNumeric walks the same dicInt keys, with the same STACKLIMIT and dev-mode-VERSION
        /// exclusions, that <see cref="CharEditorConfigCompat.IntKeys"/> and the scope's own
        /// RebuildKeys apply -- so the n-th caption drawn is the n-th row of the region.
        /// </summary>
        internal static Rect NumericRowRect(int index)
        {
            return index >= 0 && index < numericRects.Count ? numericRects[index] : default(Rect);
        }

        public static void EnterHotkeyRow(object __instance, string kbdName)
        {
            if (!Recording)
            {
                return;
            }
            hotkeyDialog = __instance;
            hotkeyBase = kbdName == CharEditorConfigScope.EditorHotkeyDefName ? 0 : 2;
        }

        public static void ExitHotkeyRow()
        {
            hotkeyDialog = null;
        }

        public static void EnterNumericSection(object __instance)
        {
            if (!Recording)
            {
                return;
            }
            numericDialog = __instance;
            numericRects.Clear();
        }

        public static void ExitNumericSection()
        {
            numericDialog = null;
        }

        /// <summary>
        /// Every Hotkeys and Numerics row opens with exactly one <c>SZWidgets.Label</c> caption
        /// (DrawHotkey's title, DrawNumeric's per-key title -- the dev-only VERSION row included,
        /// and the never-drawn STACKLIMIT key excluded, matching the scope's own row list), so the
        /// caption is both the row's timing signal and its ordinal.
        /// </summary>
        public static void RecordRowFromCaption()
        {
            // Static gate first: the mod draws every one of its own screens through this method,
            // so the tap must cost one bool read whenever the options dialog is not driving.
            if (!Recording)
            {
                return;
            }
            try
            {
                if (hotkeyDialog != null)
                {
                    // The delete icon is its own row in the scope; the caption and the binding
                    // button are the one combo row a sighted player reads.
                    Rect caption = RectOf(rectHotkeyLabelGetter, hotkeyDialog);
                    Rect binding = RectOf(rectHotkeyGetter, hotkeyDialog);
                    hotkeyRects[hotkeyBase] = GuiSpace.VisibleScreenRect(Rect.MinMaxRect(
                        Mathf.Min(caption.xMin, binding.xMin), Mathf.Min(caption.yMin, binding.yMin),
                        Mathf.Max(caption.xMax, binding.xMax), Mathf.Max(caption.yMax, binding.yMax)));
                    hotkeyRects[hotkeyBase + 1] = GuiSpace.VisibleScreenRect(RectOf(rectRemoveHotkeyGetter, hotkeyDialog));
                }
                else if (numericDialog != null)
                {
                    // RectHalfWidth is the dialog's own full-width band for this column, which is
                    // what the caption, the two steppers and the entry field are laid out inside.
                    numericRects.Add(GuiSpace.VisibleScreenRect(RectOf(rectHalfWidthGetter, numericDialog)));
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("CharEditorConfigRowRingPatch row rect capture error", ex);
            }
        }

        private static Rect RectOf(MethodInfo getter, object dialog)
        {
            return (Rect)getter.Invoke(dialog, null);
        }

        public static void EnterBoolSection()
        {
            ListingRowCapture.EnterKeyedRow(BoolSectionKey);
        }

        public static void EnterStringSection()
        {
            ListingRowCapture.EnterKeyedRow(StringSectionKey);
        }

        public static void ExitSection()
        {
            ListingRowCapture.ExitKeyedRow();
        }

        public static void EnterRowLabel(string label)
        {
            ListingRowCapture.EnterDrawnLabel(label);
        }

        public static void ExitRowLabel()
        {
            ListingRowCapture.ExitDrawnLabel();
        }
    }
}
