using HarmonyLib;
using RimWorld;
using Verse;
using RimWorldAccess.Shell;

namespace RimWorldAccess
{
    /// <summary>
    /// Support for the scenario selection page (<see cref="Page_SelectScenario"/>). All
    /// keyboard navigation and announcement now live in
    /// <see cref="ScenarioSelectScreenScope"/> (src/Shell/Screens/ScenarioSelectScreenScope.Game.cs);
    /// this class keeps only the two wizard-advance/back poll guards.
    ///
    /// The following no longer exist: the
    /// DoWindowContents Prefix (flat-list rebuild, one-shot title announcement, HostFocusReturn
    /// — the page uses no vanilla text field, so nothing needs IMGUI focus reclaim once the
    /// shell drives it) and Postfix (keyboard-cursor highlight box — no longer needed: the
    /// scope's own MUTATION-C write to the page's private <c>curScen</c> field on row
    /// activation means vanilla's own <c>Widgets.DrawOptionBackground(rect, curScen == scen)</c>
    /// highlight already follows the chosen row, the same way it does for
    /// StorytellerScreenScope's storyteller/difficulty radios); the PreOpen reset (vanilla's
    /// own <c>Page_SelectScenario.PreOpen</c> already calls <c>ScenarioLister.MarkDirty()</c> +
    /// <c>EnsureValidSelection()</c>, and the retired ScenarioNavigationState held no other
    /// static state left to reset).
    /// </summary>
    public static class ScenarioSelectionPatch
    {
    }

    /// <summary>
    /// Wizard-advance guard: blocks the raw keyboard Accept poll (Page.DoBottomButtons,
    /// Page.cs:69) so an Enter this scope consumed for row activation cannot ALSO advance
    /// the wizard. Page_SelectScenario overrides CanDoNext (decompiled-verified), so this
    /// patches the declaring type directly. State-based: fires only while the live top scope
    /// is <see cref="ScenarioSelectScreenScope"/> and the Next declared action has not set
    /// <see cref="ScenarioSelectScreenScope.AdvanceRequested"/>. Gated on the keyboard Accept
    /// event so a mouse click on the Next button (KeyDownEvent false) still advances.
    /// </summary>
    [HarmonyPatch(typeof(Page_SelectScenario), "CanDoNext")]
    public class ScenarioSelectionPatch_CanDoNext
    {
        [HarmonyPrefix]
        static bool Prefix(ref bool __result)
        {
            if (KeyBindingDefOf.Accept.KeyDownEvent
                && FocusStack.Top is ScenarioSelectScreenScope
                && !ScenarioSelectScreenScope.AdvanceRequested)
            {
                __result = false;
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Back-navigation guard twin. Page_SelectScenario does not override CanDoBack
    /// (decompiled-verified), so this patches the declaring type (RimWorld.Page) with an
    /// instance guard — see WorldParamsPatch_CanDoBack for why several instance-gated
    /// Page.CanDoBack patches coexist safely. Escape rides the scope's own Cancel claim
    /// (ScenarioSelectScreenScope.EscapeBack -> BackRequested -> gate+DoBack), so this blocks
    /// the raw Cancel poll outright while the scope is live.
    /// </summary>
    [HarmonyPatch(typeof(Page), "CanDoBack")]
    public class ScenarioSelectionPatch_CanDoBack
    {
        [HarmonyPrefix]
        static bool Prefix(Page __instance, ref bool __result)
        {
            if (!(__instance is Page_SelectScenario))
            {
                return true;
            }
            if (KeyBindingDefOf.Cancel.KeyDownEvent
                && !ScenarioSelectScreenScope.BackRequested
                && FocusStack.Top is ScenarioSelectScreenScope)
            {
                __result = false;
                return false;
            }
            return true;
        }
    }
}
