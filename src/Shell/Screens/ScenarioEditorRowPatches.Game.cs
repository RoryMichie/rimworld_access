using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Steamworks;
using UnityEngine;
using Verse;
using Verse.Sound;
using Verse.Steam;


namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The tokens the scenario editor's keyed rows are identified by, shared
    /// between the manifests below (which harvest or emit them) and
    /// <see cref="ScenarioEditorScreenScope"/> (which maps a focused model row
    /// onto one).
    /// </summary>
    internal static class ScenarioEditorRowKeys
    {
        internal const string Load = "Load";

        internal const string Save = "Save";

        internal const string RandomizeSeed = "RandomizeSeed";

        internal const string Seed = "Seed";

        internal const string EditMode = "EditMode";

        internal const string AddPart = "AddPart";

        /// <summary>
        /// The upload button's label comes from
        /// <c>Workshop.UploadButtonLabel</c> (decompiled
        /// RimWorld/Page_ScenarioEditor.cs:133), so its call site carries no
        /// literal at all — the one synthetic token here, and therefore the one
        /// with a mandatory tripwire.
        /// </summary>
        internal const string UploadSite = "Page_ScenarioEditor.DoConfigControls#6";

        internal const string Title = "ScenarioTitle";

        internal const string Summary = "Summary";

        internal const string Description = "Description";
    }

    /// <summary>
    /// The row calls the editor's two listing methods make, resolved by exact
    /// signature: plain ButtonText (decompiled Verse/Listing_Standard.cs:248),
    /// the TaggedString Label overload every label here binds (:95) and the
    /// default CheckboxLabeled (:215).
    /// </summary>
    internal static class ScenarioEditorRowCalls
    {
        internal static readonly MethodInfo ButtonText =
            AccessTools.Method(typeof(Listing_Standard), "ButtonText",
                new[] { typeof(string), typeof(string), typeof(float) });

        internal static readonly MethodInfo Label =
            AccessTools.Method(typeof(Listing_Standard), "Label",
                new[] { typeof(TaggedString), typeof(float), typeof(string) });

        internal static readonly MethodInfo CheckboxLabeled =
            AccessTools.Method(typeof(Listing_Standard), "CheckboxLabeled",
                new[] { typeof(string), typeof(bool).MakeByRefType(), typeof(string), typeof(float), typeof(float) });
    }

    /// <summary>
    /// Each scenario part's own <c>ScenPart</c>, straight off vanilla's
    /// parameter: <c>GetScenPartRect</c> allocates the row with <c>GetRect</c>
    /// as its first act (decompiled Verse/Listing_ScenEdit.cs:19), so the ring
    /// gets the FULL row rect even though the method returns only
    /// <c>rect.RightPart(0.5f)</c> as the editing area (:43).
    /// </summary>
    [HarmonyPatch(typeof(Listing_ScenEdit), "GetScenPartRect")]
    internal static class ScenarioEditorPartRowPatch
    {
        [HarmonyPrefix]
        public static void Prefix(ScenPart part)
        {
            ListingRowCapture.SetPendingRowObject(part);
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            ListingRowCapture.SetPendingRowObject(null);
        }
    }

    /// <summary>
    /// The config column's seven rows, in IL order (decompiled
    /// RimWorld/Page_ScenarioEditor.cs:93, :101, :105, :113, :125, :129, :133).
    /// The seed gate and the upload site's short-circuited <c>&amp;&amp;</c>
    /// chain need no mirroring on our side: an unexecuted branch emits no
    /// marker.
    /// </summary>
    [HarmonyPatch(typeof(Page_ScenarioEditor), "DoConfigControls")]
    internal static class ScenarioEditorConfigRowMarkerPatch
    {
        private static readonly MarkerPlan Plan = new MarkerPlan
        {
            RowCallTargets = new[]
            {
                ScenarioEditorRowCalls.ButtonText, ScenarioEditorRowCalls.Label,
                ScenarioEditorRowCalls.CheckboxLabeled,
            },
            Sites = new[]
            {
                new MarkerSite
                {
                    RowCall = ScenarioEditorRowCalls.ButtonText,
                    Payload = MarkerPayload.HarvestedKey,
                    ExpectedKey = ScenarioEditorRowKeys.Load,
                },
                new MarkerSite
                {
                    RowCall = ScenarioEditorRowCalls.ButtonText,
                    Payload = MarkerPayload.HarvestedKey,
                    ExpectedKey = ScenarioEditorRowKeys.Save,
                },
                new MarkerSite
                {
                    RowCall = ScenarioEditorRowCalls.ButtonText,
                    Payload = MarkerPayload.HarvestedKey,
                    ExpectedKey = ScenarioEditorRowKeys.RandomizeSeed,
                },
                new MarkerSite
                {
                    RowCall = ScenarioEditorRowCalls.Label,
                    Payload = MarkerPayload.HarvestedKey,
                    ExpectedKey = ScenarioEditorRowKeys.Seed,
                },
                new MarkerSite
                {
                    RowCall = ScenarioEditorRowCalls.CheckboxLabeled,
                    Payload = MarkerPayload.HarvestedKey,
                    ExpectedKey = ScenarioEditorRowKeys.EditMode,
                },
                new MarkerSite
                {
                    RowCall = ScenarioEditorRowCalls.ButtonText,
                    Payload = MarkerPayload.HarvestedKey,
                    ExpectedKey = ScenarioEditorRowKeys.AddPart,
                },
                new MarkerSite
                {
                    RowCall = ScenarioEditorRowCalls.ButtonText,
                    Payload = MarkerPayload.SyntheticKey,
                    ExpectedKey = ScenarioEditorRowKeys.UploadSite,
                },
            },
        };

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            return ListingRowMarkerInjector.Inject(instructions, __originalMethod, Plan);
        }
    }

    /// <summary>
    /// The edit pane's three metadata labels (decompiled
    /// RimWorld/ScenarioUI.cs:43, :45, :47), each with its own translation key
    /// at the call site. The parts loop below them needs no marker — its rows
    /// come from <see cref="ScenarioEditorPartRowPatch"/>.
    /// </summary>
    [HarmonyPatch(typeof(ScenarioUI), "DrawScenarioEditInterface")]
    internal static class ScenarioEditorMetadataRowMarkerPatch
    {
        private static readonly MarkerPlan Plan = new MarkerPlan
        {
            RowCallTargets = new[] { ScenarioEditorRowCalls.Label },
            Sites = new[]
            {
                new MarkerSite
                {
                    RowCall = ScenarioEditorRowCalls.Label,
                    Payload = MarkerPayload.HarvestedKey,
                    ExpectedKey = ScenarioEditorRowKeys.Title,
                },
                new MarkerSite
                {
                    RowCall = ScenarioEditorRowCalls.Label,
                    Payload = MarkerPayload.HarvestedKey,
                    ExpectedKey = ScenarioEditorRowKeys.Summary,
                },
                new MarkerSite
                {
                    RowCall = ScenarioEditorRowCalls.Label,
                    Payload = MarkerPayload.HarvestedKey,
                    ExpectedKey = ScenarioEditorRowKeys.Description,
                },
            },
        };

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            return ListingRowMarkerInjector.Inject(instructions, __originalMethod, Plan);
        }
    }

    /// <summary>
    /// Shared "does the editor family currently own the whole keyboard surface" test for
    /// the two guard twins below: this scope is the live top, or one of the three
    /// remaining windowless overlays (add-part/save/load) is active, or a Dropdown
    /// field's WindowlessFloatMenuState picker is open, or a real window (the delete
    /// confirmation's Dialog_MessageBox included) sits above the
    /// editor page (<see cref="ScenarioScopeGuards.WindowAboveScenarioEditor"/> — this
    /// generalizes the old narrow WindowlessScenarioDeleteConfirmState check to ANY real
    /// window stacked above, exactly like the overlay mirror's own stand-down).
    /// </summary>
    internal static class ScenarioEditorScreenScopeGuard
    {
        internal static bool OverlayOwnsInput()
        {
            return FocusStack.Top is ScenarioEditorScreenScope
                || ScenarioBuilderAddPartState.IsActive
                || WindowlessFloatMenuState.IsActive
                || ScenarioScopeGuards.WindowAboveScenarioEditor();
        }
    }

    /// <summary>
    /// R6 CanDoNext guard: blocks the wizard-advance Accept poll (Page.cs:69) while this
    /// scope (or a windowless overlay it hosts) owns the editor's keyboard surface and the
    /// Next declared action isn't itself the one driving CanDoNext. Page_ScenarioEditor DOES
    /// override CanDoNext (decompiled-verified), so this patches that declaring type directly.
    /// </summary>
    [HarmonyPatch(typeof(Page_ScenarioEditor), "CanDoNext")]
    public class ScenarioEditorScreenScopePatch_CanDoNext
    {
        [HarmonyPrefix]
        static bool Prefix(ref bool __result)
        {
            if (ScenarioEditorScreenScope.AdvanceRequested)
            {
                return true;
            }
            if (KeyBindingDefOf.Accept.KeyDownEvent && ScenarioEditorScreenScopeGuard.OverlayOwnsInput())
            {
                __result = false;
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// R6 CanDoBack guard: blocks the Back-navigation Escape (Page.cs:58) under the same
    /// condition as the CanDoNext twin above. Page_ScenarioEditor does not override
    /// CanDoBack, so this patches the DECLARING type (Page) with an instance guard —
    /// composes safely with <see cref="ScenarioBuilderDoBackPatch"/>'s independent
    /// dirty-check prefix on Page.DoBack (unchanged this slice): when this guard blocks
    /// CanDoBack, DoBack never runs at all; when it allows CanDoBack through (this scope's
    /// own BackRequested-flagged Back action), DoBack runs and the dirty-check fires as before.
    /// </summary>
    [HarmonyPatch(typeof(Page), "CanDoBack")]
    public class ScenarioEditorScreenScopePatch_CanDoBack
    {
        [HarmonyPrefix]
        static bool Prefix(Page __instance, ref bool __result)
        {
            if (!(__instance is Page_ScenarioEditor)) return true;
            if (ScenarioEditorScreenScope.BackRequested) return true;
            if (KeyBindingDefOf.Cancel.KeyDownEvent && ScenarioEditorScreenScopeGuard.OverlayOwnsInput())
            {
                __result = false;
                return false;
            }
            return true;
        }
    }
}
