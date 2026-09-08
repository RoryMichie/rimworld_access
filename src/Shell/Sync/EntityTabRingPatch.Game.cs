using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The tokens the entity tab's rows are identified by, shared between the
    /// manifests below (which emit them) and <see cref="EntityTabScope"/> (which
    /// maps a focused row onto one).
    ///
    /// Only the study-period row carries its translation key as the literal
    /// nearest its call. Every other caption concatenates a live value onto the
    /// translated text, leaving a formatting fragment (": ", ")") nearest the
    /// call — display text, never an admissible token — so those sites take a "#"
    /// site token, and the "#" shape makes a tripwire mandatory at the ring point.
    /// </summary>
    internal static class EntityTabRowKeys
    {
        private const string Site = "ITab_Entity.FillTab#";

        /// <summary>
        /// The tripwire the three raw <c>GetRect</c> bands carry. Those bands run
        /// under no outer Listing_Standard tap, so their DrawnLabel is null and
        /// any non-null tripwire passes; this value is never a label vanilla
        /// draws, so a stale label from a preceding row cannot match it either.
        /// </summary>
        internal const string RawBand = "<raw band>";

        /// <summary>decompiled RimWorld/ITab_Entity.cs:76.</summary>
        internal const string ContainmentStrength = Site + "containmentStrength";

        /// <summary>:92.</summary>
        internal const string EscapeMtb = Site + "escapeMtb";

        /// <summary>:106, the medicine dropdown band.</summary>
        internal const string Medicine = Site + "medicine";

        /// <summary>:123, the one band all four containment radios split between them.</summary>
        internal const string Containment = Site + "containment";

        /// <summary>:166, the extract-bioferrite band.</summary>
        internal const string Extract = Site + "extract";

        /// <summary>:198, whose tooltip key is the literal nearest the call.</summary>
        internal const string StudyInterval = "StudyIntervalDesc";

        /// <summary>:233. The tooltip is built at the top of the method, so nothing stable sits near the call.</summary>
        internal const string KnowledgeGain = "ITab_Entity.DoKnowledgeGainListing#0";
    }

    /// <summary>
    /// The row calls the entity tab makes, resolved by exact signature: the two
    /// stat rows in FillTab pass a TipSignal tooltip and so bind the string Label
    /// overload (decompiled Verse/Listing_Standard.cs:100), the two stat helpers
    /// pass a translated string and bind the TaggedString one (:95), and the
    /// three interactive bands are raw <c>Listing.GetRect</c> allocations
    /// (Verse/Listing.cs:62) that vanilla fills with Widgets calls of its own.
    /// </summary>
    internal static class EntityTabRowCalls
    {
        internal static readonly MethodInfo Label =
            AccessTools.Method(typeof(Listing_Standard), "Label",
                new[] { typeof(string), typeof(float), typeof(TipSignal?) });

        internal static readonly MethodInfo TaggedLabel =
            AccessTools.Method(typeof(Listing_Standard), "Label",
                new[] { typeof(TaggedString), typeof(float), typeof(string) });

        internal static readonly MethodInfo GetRect =
            AccessTools.Method(typeof(Listing), "GetRect", new[] { typeof(float), typeof(float) });
    }

    /// <summary>
    /// Arms the listing ring for the entity tab. The tab draws inside the inspect
    /// pane's ImmediateWindow, which vanilla creates internally, so this scope
    /// owns no window and the generic InnerWindowOnGUI arm can never match it —
    /// hence a bespoke bracket on the tab's own body (decompiled
    /// RimWorld/ITab_Entity.cs:65, the declaring type's override), the shape the
    /// fishing tab established.
    /// </summary>
    [HarmonyPatch(typeof(ITab_Entity), "FillTab")]
    internal static class EntityTabRingPatch
    {
        private static bool openedPass;

        [HarmonyPrefix]
        public static void Prefix()
        {
            if (IsLayoutPass())
            {
                return;
            }
            EntityTabScope scope = EntityTabScope.Current;
            if (scope == null)
            {
                return;
            }
            // A leaked flag means our own postfix never ran; BeginPass clears
            // whatever that pass left behind.
            if (ListingRowCapture.IsPassOpen && !openedPass)
            {
                return;
            }
            openedPass = true;
            ListingRowCapture.BeginPass(scope.CurrentListingFocus());
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            if (!openedPass)
            {
                return;
            }
            openedPass = false;
            ListingRowCapture.EndPass();
        }

        private static bool IsLayoutPass()
        {
            return Event.current != null && Event.current.type == EventType.Layout;
        }
    }

    /// <summary>
    /// The five row calls of <c>FillTab</c>, in IL order (decompiled
    /// RimWorld/ITab_Entity.cs:76, :92, :106, :123, :166). The containment-strength
    /// row draws only for a pawn held by a real platform (:70), so its branch may
    /// emit no marker at all, which simply leaves that row unringed.
    /// </summary>
    [HarmonyPatch(typeof(ITab_Entity), "FillTab")]
    internal static class EntityTabRowMarkerPatch
    {
        private static readonly MarkerPlan Plan = new MarkerPlan
        {
            RowCallTargets = new[] { EntityTabRowCalls.Label, EntityTabRowCalls.GetRect },
            Sites = new[]
            {
                Synthetic(EntityTabRowCalls.Label, EntityTabRowKeys.ContainmentStrength),
                Synthetic(EntityTabRowCalls.Label, EntityTabRowKeys.EscapeMtb),
                Synthetic(EntityTabRowCalls.GetRect, EntityTabRowKeys.Medicine),
                Synthetic(EntityTabRowCalls.GetRect, EntityTabRowKeys.Containment),
                Synthetic(EntityTabRowCalls.GetRect, EntityTabRowKeys.Extract),
            },
        };

        private static MarkerSite Synthetic(MethodInfo rowCall, string key)
        {
            return new MarkerSite { RowCall = rowCall, Payload = MarkerPayload.SyntheticKey, ExpectedKey = key };
        }

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            return ListingRowMarkerInjector.Inject(instructions, __originalMethod, Plan);
        }
    }

    /// <summary>
    /// The study-period row (decompiled RimWorld/ITab_Entity.cs:198). Both stat
    /// helpers are public statics other tabs also call, but a marker set while no
    /// pass is open is discarded, so patching them costs those callers nothing.
    /// </summary>
    [HarmonyPatch(typeof(ITab_Entity), "DoStudyPeriodListing")]
    internal static class EntityTabStudyPeriodMarkerPatch
    {
        private static readonly MarkerPlan Plan = new MarkerPlan
        {
            RowCallTargets = new[] { EntityTabRowCalls.TaggedLabel },
            Sites = new[]
            {
                new MarkerSite
                {
                    RowCall = EntityTabRowCalls.TaggedLabel,
                    Payload = MarkerPayload.HarvestedKey,
                    ExpectedKey = EntityTabRowKeys.StudyInterval,
                },
            },
        };

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            return ListingRowMarkerInjector.Inject(instructions, __originalMethod, Plan);
        }
    }

    /// <summary>The knowledge-gain row (decompiled RimWorld/ITab_Entity.cs:233).</summary>
    [HarmonyPatch(typeof(ITab_Entity), "DoKnowledgeGainListing")]
    internal static class EntityTabKnowledgeGainMarkerPatch
    {
        private static readonly MarkerPlan Plan = new MarkerPlan
        {
            RowCallTargets = new[] { EntityTabRowCalls.TaggedLabel },
            Sites = new[]
            {
                new MarkerSite
                {
                    RowCall = EntityTabRowCalls.TaggedLabel,
                    Payload = MarkerPayload.SyntheticKey,
                    ExpectedKey = EntityTabRowKeys.KnowledgeGain,
                },
            },
        };

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            return ListingRowMarkerInjector.Inject(instructions, __originalMethod, Plan);
        }
    }
}
