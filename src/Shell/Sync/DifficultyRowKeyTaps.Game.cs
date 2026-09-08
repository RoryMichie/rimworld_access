using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The non-display tokens the storyteller/difficulty pane's rows are
    /// identified by, shared between the manifests below (which harvest or emit
    /// them) and <see cref="StorytellerScopeBase"/> (which maps a focused model
    /// row onto one).
    /// </summary>
    internal static class DifficultyRowKeys
    {
        /// <summary>
        /// Vanilla's "AnomalySettings..." button builds its label by
        /// concatenation (decompiled RimWorld/StorytellerUI.cs:136), so the
        /// literal sitting nearest the call is the "..." fragment rather than a
        /// translation key — the one site here that needs a synthetic token,
        /// and therefore a mandatory tripwire.
        /// </summary>
        internal const string AnomalyButtonSite = "StorytellerUI.DrawStorytellerSelectionInterface#3";

        /// <summary>
        /// Both save-mode radios evaluate their mode key before their info key,
        /// so the literal nearest each call is the INFO key (StorytellerUI.cs:122, :128).
        /// </summary>
        internal const string ReloadAnytimeMode = "ReloadAnytimeModeInfo";

        internal const string CommitmentMode = "PermadeathModeInfo";

        /// <summary>
        /// The four Anomaly rows come from the label overload, which receives no
        /// optionName; their call sites carry the info key instead
        /// (StorytellerUI.cs:240, :245, :247, :249). Maps the
        /// <c>Difficulty</c> field our row covers onto that key, null for every
        /// other row.
        /// </summary>
        internal static string AnomalySliderKey(string coveredField)
        {
            switch (coveredField)
            {
                case "overrideAnomalyThreatsFraction": return "Difficulty_AnomalyThreats_Info";
                case "anomalyThreatsInactiveFraction": return "Difficulty_AnomalyThreatsInactive_Info";
                case "anomalyThreatsActiveFraction": return "Difficulty_AnomalyThreatsActive_Info";
                case "studyEfficiencyFactor": return "Difficulty_StudyEfficiency_Info";
                default: return null;
            }
        }
    }

    /// <summary>
    /// The row calls the storyteller pane makes, resolved by exact signature:
    /// the 5-parameter RadioButton convenience overload the call sites bind to
    /// (decompiled Verse/Listing_Standard.cs:169) and the plain ButtonText (:248).
    /// </summary>
    internal static class DifficultyRowCalls
    {
        internal static readonly MethodInfo RadioButton =
            AccessTools.Method(typeof(Listing_Standard), "RadioButton",
                new[] { typeof(string), typeof(bool), typeof(float), typeof(string), typeof(float?) });

        internal static readonly MethodInfo ButtonText =
            AccessTools.Method(typeof(Listing_Standard), "ButtonText",
                new[] { typeof(string), typeof(string), typeof(float) });
    }

    /// <summary>
    /// The difficulty radios, the two save-mode radios and the Anomaly button
    /// (decompiled RimWorld/StorytellerUI.cs:100, :122, :128, :136). The
    /// per-preset Reset button (:176) is a row call in the same method, so the
    /// manifest names it to keep the site count honest; no model row ever maps
    /// to its key, which is how it stays silent.
    /// </summary>
    [HarmonyPatch(typeof(StorytellerUI), "DrawStorytellerSelectionInterface")]
    internal static class StorytellerSelectionRowMarkerPatch
    {
        private static readonly MarkerPlan Plan = new MarkerPlan
        {
            RowCallTargets = new[] { DifficultyRowCalls.RadioButton, DifficultyRowCalls.ButtonText },
            Sites = new[]
            {
                new MarkerSite
                {
                    RowCall = DifficultyRowCalls.RadioButton,
                    Payload = MarkerPayload.LoopLocal,
                    LocalSource = AccessTools.Method(typeof(IEnumerator<DifficultyDef>), "get_Current"),
                },
                new MarkerSite
                {
                    RowCall = DifficultyRowCalls.RadioButton,
                    Payload = MarkerPayload.HarvestedKey,
                    ExpectedKey = DifficultyRowKeys.ReloadAnytimeMode,
                },
                new MarkerSite
                {
                    RowCall = DifficultyRowCalls.RadioButton,
                    Payload = MarkerPayload.HarvestedKey,
                    ExpectedKey = DifficultyRowKeys.CommitmentMode,
                },
                new MarkerSite
                {
                    RowCall = DifficultyRowCalls.ButtonText,
                    Payload = MarkerPayload.SyntheticKey,
                    ExpectedKey = DifficultyRowKeys.AnomalyButtonSite,
                },
                new MarkerSite
                {
                    RowCall = DifficultyRowCalls.ButtonText,
                    Payload = MarkerPayload.HarvestedKey,
                    ExpectedKey = "DifficultyReset",
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
    /// The four Anomaly rows of the custom grid (decompiled
    /// RimWorld/StorytellerUI.cs:240, :245, :247, :249), which take the label
    /// overload and so carry no optionName the bracket below could read. Each
    /// marker rings the label row the helper draws first; its slider stays
    /// unringed, the same posture every untapped widget in this pane takes.
    /// </summary>
    [HarmonyPatch(typeof(StorytellerUI), "DrawCustomLeft")]
    internal static class CustomDifficultyAnomalyRowMarkerPatch
    {
        private static readonly MethodInfo LabelSuffixOverload =
            AccessTools.Method(typeof(StorytellerUI), "DrawCustomDifficultySlider",
                new[]
                {
                    typeof(Listing_Standard), typeof(string), typeof(string), typeof(string),
                    typeof(float).MakeByRefType(), typeof(ToStringStyle), typeof(ToStringNumberSense),
                    typeof(float), typeof(float), typeof(float), typeof(bool), typeof(float),
                });

        private static readonly MethodInfo LabelOverload =
            AccessTools.Method(typeof(StorytellerUI), "DrawCustomDifficultySlider",
                new[]
                {
                    typeof(Listing_Standard), typeof(string), typeof(string),
                    typeof(float).MakeByRefType(), typeof(ToStringStyle), typeof(ToStringNumberSense),
                    typeof(float), typeof(float), typeof(float), typeof(bool), typeof(float),
                });

        private static readonly MarkerPlan Plan = new MarkerPlan
        {
            RowCallTargets = new[] { LabelSuffixOverload, LabelOverload },
            Sites = new[]
            {
                new MarkerSite
                {
                    RowCall = LabelSuffixOverload,
                    Payload = MarkerPayload.HarvestedKey,
                    ExpectedKey = "Difficulty_AnomalyThreats_Info",
                },
                new MarkerSite
                {
                    RowCall = LabelSuffixOverload,
                    Payload = MarkerPayload.HarvestedKey,
                    ExpectedKey = "Difficulty_AnomalyThreatsInactive_Info",
                },
                new MarkerSite
                {
                    RowCall = LabelSuffixOverload,
                    Payload = MarkerPayload.HarvestedKey,
                    ExpectedKey = "Difficulty_AnomalyThreatsActive_Info",
                },
                new MarkerSite
                {
                    RowCall = LabelOverload,
                    Payload = MarkerPayload.HarvestedKey,
                    ExpectedKey = "Difficulty_StudyEfficiency_Info",
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
    /// The standard custom-difficulty slider helper (decompiled
    /// RimWorld/StorytellerUI.cs:345), which builds its label from the
    /// <c>optionName</c> it receives. Sticky rather than one-shot: one call
    /// draws a label row and a slider row that are one visual unit.
    /// </summary>
    [HarmonyPatch]
    internal static class CustomDifficultySliderKeyPatch
    {
        // TargetMethod, not a signature attribute: MakeByRefType is not a
        // constant expression (CS0182).
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(StorytellerUI), "DrawCustomDifficultySlider",
                new[]
                {
                    typeof(Listing_Standard), typeof(string), typeof(float).MakeByRefType(),
                    typeof(ToStringStyle), typeof(ToStringNumberSense),
                    typeof(float), typeof(float), typeof(float), typeof(bool), typeof(float),
                });
        }

        [HarmonyPrefix]
        public static void Prefix(string optionName)
        {
            ListingRowCapture.EnterKeyedRow(optionName);
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            ListingRowCapture.ExitKeyedRow();
        }
    }

    /// <summary>The checkbox twin of <see cref="CustomDifficultySliderKeyPatch"/> (decompiled RimWorld/StorytellerUI.cs:401).</summary>
    [HarmonyPatch]
    internal static class CustomDifficultyCheckboxKeyPatch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(StorytellerUI), "DrawCustomDifficultyCheckbox",
                new[]
                {
                    typeof(Listing_Standard), typeof(string), typeof(bool).MakeByRefType(),
                    typeof(bool), typeof(bool),
                });
        }

        [HarmonyPrefix]
        public static void Prefix(string optionName)
        {
            ListingRowCapture.EnterKeyedRow(optionName);
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            ListingRowCapture.ExitKeyedRow();
        }
    }
}
