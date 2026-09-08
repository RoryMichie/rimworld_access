using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using RimWorldAccess.Shell;

namespace RimWorldAccess
{
    /// <summary>
    /// Support for the ideoligion preset page (<see cref="Page_ChooseIdeoPreset"/>).
    /// All keyboard navigation and announcement now lives in
    /// <see cref="IdeoPresetScreenScope"/> (src/Shell/Screens/IdeoPresetScreenScope.Game.cs);
    /// this class keeps only the reflected accessors for the page's
    /// private selection fields, the MUTATION-C write helpers the scope's rows call as
    /// their mutation vehicles, and the flag-shape wizard-advance poll guards.
    ///
    /// No longer present:
    /// the DoWindowContents Prefix (per-frame state Initialize, HostFocusReturn, the
    /// windowless-picker draw-skip, the one-shot announcement — the scope announces on
    /// focus, the shell owns focus, and the new CanDoNext/CanDoBack guards below key on
    /// WindowlessFloatMenuState.IsActive directly instead of skipping the page's draw),
    /// the PostOpen/PreClose reset patches (IdeologyNavigationState carried the only
    /// session state; the scope rebuilds every row from the live page fields each
    /// RefreshContent, so nothing needs a deterministic reset hook any more), and the
    /// old tab/tree-based navigation surface (IdeologyNavigationState.cs, deleted).
    /// </summary>
    public static class IdeologySelectionPatch
    {
        // presetSelection is a PRIVATE NESTED enum (Page_ChooseIdeoPreset.PresetSelection)
        // and so cannot be named from here — plain FieldInfo + Enum.ToObject is the only
        // way to read or write it, exactly as the retired handler did.
        private static readonly Type presetSelectionEnumType = AccessTools.Inner(typeof(Page_ChooseIdeoPreset), "PresetSelection");
        private static readonly FieldInfo presetSelectionField = AccessTools.Field(typeof(Page_ChooseIdeoPreset), "presetSelection");
        private static readonly MethodInfo recacheStylesMethod = AccessTools.Method(typeof(Page_ChooseIdeoPreset), "RecacheStyleCategoriesWithPriority");

        // Named mirrors of Page_ChooseIdeoPreset's private PresetSelection enum values
        // (Classic, CustomFluid, CustomFixed, Load, Preset in declaration order) so
        // callers never spell out the magic numbers.
        internal const int PresetSelectionClassic = 0;
        internal const int PresetSelectionCustomFluid = 1;
        internal const int PresetSelectionCustomFixed = 2;
        internal const int PresetSelectionLoad = 3;
        internal const int PresetSelectionPreset = 4;

        internal static int GetPresetSelectionValue(Page_ChooseIdeoPreset page)
        {
            return (int)presetSelectionField.GetValue(page);
        }

        private static void SetPresetSelectionValue(Page_ChooseIdeoPreset page, int value)
        {
            presetSelectionField.SetValue(page, Enum.ToObject(presetSelectionEnumType, value));
        }

        /// <summary>
        /// One of the four Options-tab cards (Classic/CustomFluid/CustomFixed/Load),
        /// selected by its PresetSelection enum value (0-3).
        /// </summary>
        internal static void SelectOption(Page_ChooseIdeoPreset page, int presetSelectionValue)
        {
            // MUTATION-C: mirrors DrawSelectable's four onSelect delegates (decompiled
            // Page_ChooseIdeoPreset.cs DoWindowContents :135-168) — each clears
            // selectedIdeo and writes presetSelection to its own value; neither private
            // field has a gated setter.
            IdeoPresetScreenScope.SelectedIdeoField(page) = null;
            SetPresetSelectionValue(page, presetSelectionValue);
        }

        internal static void SelectPreset(Page_ChooseIdeoPreset page, IdeoPresetDef preset)
        {
            // MUTATION-C: mirrors DrawIdeo's onSelect delegate (decompiled :291-295) —
            // sets presetSelection to Preset (4) and writes selectedIdeo; no gated setter
            // exists for either private field.
            SetPresetSelectionValue(page, PresetSelectionPreset);
            IdeoPresetScreenScope.SelectedIdeoField(page) = preset;
        }

        internal static void SetStructure(Page_ChooseIdeoPreset page, MemeDef meme)
        {
            // MUTATION-C: mirrors the structure FloatMenu's Random/meme actions
            // (decompiled DrawStructureAndStyleSelection :382-394); selectedStructure is
            // a bare private field with no gated setter.
            IdeoPresetScreenScope.SelectedStructureField(page) = meme;
        }

        internal static void SetStyleSlot(Page_ChooseIdeoPreset page, int slotIndex, StyleCategoryDef style)
        {
            // MUTATION-C: mirrors FillAllAvailableStyles' Random/each-style actions for an
            // EXISTING slot (decompiled :412-442); selectedStyles is a bare private field.
            var styles = IdeoPresetScreenScope.SelectedStylesField(page);
            if (slotIndex < 0 || slotIndex >= styles.Count)
            {
                return;
            }
            styles[slotIndex] = style;
            RecacheStyles(page);
        }

        internal static void AddStyleSlot(Page_ChooseIdeoPreset page, StyleCategoryDef style)
        {
            // MUTATION-C: mirrors FillAllAvailableStyles' forIndex==-1 add branch
            // (decompiled :412-442).
            IdeoPresetScreenScope.SelectedStylesField(page).Add(style);
            RecacheStyles(page);
        }

        internal static void RemoveStyleSlot(Page_ChooseIdeoPreset page, int slotIndex)
        {
            // MUTATION-C: mirrors FillAllAvailableStyles' Remove option (decompiled
            // :447-451).
            var styles = IdeoPresetScreenScope.SelectedStylesField(page);
            if (slotIndex < 0 || slotIndex >= styles.Count)
            {
                return;
            }
            styles.RemoveAt(slotIndex);
            RecacheStyles(page);
        }

        private static void RecacheStyles(Page_ChooseIdeoPreset page)
        {
            // Vanilla's own private method (decompiled RecacheStyleCategoriesWithPriority,
            // :619-630), invoked reflectively only because it is private — no logic is
            // duplicated here, so this is not a MUTATION-C mirror.
            recacheStylesMethod.Invoke(page, null);
        }
    }

    /// <summary>
    /// Wizard-advance guard: blocks the raw keyboard Accept poll (Page.DoBottomButtons,
    /// Page.cs:69) so an Enter this scope consumed for row activation or a windowless
    /// combo-picker selection cannot ALSO fire the page's own overridden DoNext.
    /// Page_ChooseIdeoPreset does NOT override CanDoNext (decompiled-verified — only
    /// DoNext is overridden), so this patches the DECLARING type (RimWorld.Page) with an
    /// instance guard, the same posture as the other CanDoNext/CanDoBack twins in this
    /// wave (each independently instance-gated so they coexist safely).
    /// </summary>
    [HarmonyPatch(typeof(Page), "CanDoNext")]
    public class IdeologySelectionPatch_CanDoNext
    {
        [HarmonyPrefix]
        static bool Prefix(Page __instance, ref bool __result)
        {
            if (!(__instance is Page_ChooseIdeoPreset))
            {
                return true;
            }
            if (KeyBindingDefOf.Accept.KeyDownEvent
                && !IdeoPresetScreenScope.AdvanceRequested
                && (FocusStack.Top is IdeoPresetScreenScope || WindowlessFloatMenuState.IsActive))
            {
                __result = false;
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Back-navigation guard twin. Page_ChooseIdeoPreset does not override CanDoBack
    /// either (decompiled-verified), so this patches the same declaring type with its own
    /// instance guard — see WorldParamsPatch_CanDoBack's remarks for why the resulting
    /// several independent instance-gated patches on Page.CanDoBack coexist safely.
    /// </summary>
    [HarmonyPatch(typeof(Page), "CanDoBack")]
    public class IdeologySelectionPatch_CanDoBack
    {
        [HarmonyPrefix]
        static bool Prefix(Page __instance, ref bool __result)
        {
            if (!(__instance is Page_ChooseIdeoPreset))
            {
                return true;
            }
            if (KeyBindingDefOf.Cancel.KeyDownEvent
                && !IdeoPresetScreenScope.BackRequested
                && (FocusStack.Top is IdeoPresetScreenScope || WindowlessFloatMenuState.IsActive))
            {
                __result = false;
                return false;
            }
            return true;
        }
    }
}
