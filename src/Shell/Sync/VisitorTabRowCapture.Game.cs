using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Which widget kind of the prisoner/slave tab a focused row rings. Each kind is counted and
    /// ringed independently, so a shape change in one can never shift another kind's ordinals.
    /// </summary>
    internal enum VisitorTabRingKind
    {
        None,

        /// <summary>The exclusive interaction modes (prisoner) or the slave interaction modes.</summary>
        InteractionRadio,

        /// <summary>The non-exclusive prisoner interaction modes.</summary>
        NonExclusiveCheckbox,

        /// <summary>The allow-medicine dropdown button.</summary>
        MedicalCare,

        /// <summary>The ideoligion conversion target's icon.</summary>
        IdeoConversionIcon,
    }

    /// <summary>
    /// Which row of vanilla's tab the keyboard cursor sits on: a widget kind, its ordinal within
    /// that kind, and how many of that kind the scope expects the pass to draw. The count travels
    /// with the focus because the ring is painted inline, inside the widget call that owns the rect,
    /// long before the pass could total anything up.
    /// </summary>
    internal struct VisitorTabRingFocus
    {
        public VisitorTabRingKind Kind;
        public int Index;
        public int ExpectedCount;

        public static VisitorTabRingFocus None
        {
            get { return default(VisitorTabRingFocus); }
        }
    }

    /// <summary>
    /// The focus ring for the prisoner/slave tab. The tab draws inside the inspect pane's
    /// ImmediateWindow, which vanilla creates internally, so <see cref="PrisonerTabScope"/> owns no
    /// window and the generic InnerWindowOnGUI arm can never match it — hence a bespoke bracket on
    /// the tab's own body, on the declaring type.
    ///
    /// PER-KIND ORDINALS, not listing rows: vanilla's mode rows are raw RadioButtonLabeled and
    /// CheckboxLabeled calls inside a BeginGroup whose rects never reach a Listing, so the
    /// marker/GetRect machinery has nothing to see. Each kind is counted in draw order instead and
    /// the ring is painted in the widget's own postfix, where the rect is live and in the
    /// coordinate space vanilla just drew in.
    ///
    /// COUNT VALIDATION IS ONE PASS BEHIND: ringing inline means the ordinal is judged before the
    /// pass finishes counting, so the arming prefix validates the expected count against what the
    /// PREVIOUS pass drew. The tab redraws every frame from the same live defs, so only the first
    /// pass after a shape change can disagree, and it draws no ring.
    ///
    /// Vanilla's tab has no scroll view — FillTab sizes the ITab window to the listing's own height
    /// each pass — so every row is on screen and there is nothing to follow.
    /// </summary>
    internal static class VisitorTabRowCapture
    {
        private static bool armed;
        private static VisitorTabRingFocus focus;

        private static int radioOrdinal;
        private static int checkboxOrdinal;
        private static int medicalCareOrdinal;
        private static int ideoIconOrdinal;

        private static int lastRadioCount;
        private static int lastCheckboxCount;
        private static int lastMedicalCareCount;
        private static int lastIdeoIconCount;

        internal static bool Armed
        {
            get { return armed; }
        }

        internal static void BeginPass(VisitorTabRingFocus passFocus)
        {
            focus = passFocus;
            radioOrdinal = 0;
            checkboxOrdinal = 0;
            medicalCareOrdinal = 0;
            ideoIconOrdinal = 0;
            armed = true;
        }

        internal static void EndPass()
        {
            armed = false;
            lastRadioCount = radioOrdinal;
            lastCheckboxCount = checkboxOrdinal;
            lastMedicalCareCount = medicalCareOrdinal;
            lastIdeoIconCount = ideoIconOrdinal;
            focus = VisitorTabRingFocus.None;
        }

        internal static void RecordRadio(Rect rect)
        {
            int ordinal = radioOrdinal++;
            RingIfFocused(VisitorTabRingKind.InteractionRadio, ordinal, lastRadioCount, rect);
        }

        internal static void RecordCheckbox(Rect rect)
        {
            int ordinal = checkboxOrdinal++;
            RingIfFocused(VisitorTabRingKind.NonExclusiveCheckbox, ordinal, lastCheckboxCount, rect);
        }

        internal static void RecordMedicalCareButton(Rect rect)
        {
            int ordinal = medicalCareOrdinal++;
            RingIfFocused(VisitorTabRingKind.MedicalCare, ordinal, lastMedicalCareCount, rect);
        }

        internal static void RecordIdeoIcon(Rect rect)
        {
            int ordinal = ideoIconOrdinal++;
            // Vanilla draws the icon into rect7.ContractedBy(2f); the ring belongs on rect7 itself.
            RingIfFocused(VisitorTabRingKind.IdeoConversionIcon, ordinal, lastIdeoIconCount, rect.ExpandedBy(2f));
        }

        private static void RingIfFocused(VisitorTabRingKind kind, int ordinal, int lastCount, Rect rect)
        {
            if (focus.Kind != kind || ordinal != focus.Index || lastCount != focus.ExpectedCount)
            {
                return;
            }
            FocusRing.Draw(rect);
        }
    }

    /// <summary>
    /// Arms the ring for the duration of the tab's body. <c>FillTab</c> is declared on
    /// <see cref="ITab_Pawn_Visitor"/> and no subclass overrides it, so a type test on __instance
    /// would be vacuous; the actual gate is <see cref="PrisonerTabScope.Current"/>, which exists
    /// only while the keyboard is on a prisoner's or slave's tab, never the guest tab that shares
    /// this same body.
    /// </summary>
    [HarmonyPatch(typeof(ITab_Pawn_Visitor), "FillTab")]
    internal static class VisitorTabRingPatch
    {
        private static bool openedPass;

        [HarmonyPrefix]
        public static void Prefix()
        {
            if (Event.current != null && Event.current.type == EventType.Layout)
            {
                return;
            }
            PrisonerTabScope scope = PrisonerTabScope.Current;
            if (scope == null)
            {
                return;
            }
            // A leaked flag means our own finalizer never ran; falling through lets BeginPass clear
            // whatever that pass left behind rather than leaving stale focus armed.
            openedPass = true;
            VisitorTabRowCapture.BeginPass(scope.CurrentRingFocus());
        }

        [HarmonyFinalizer]
        public static void Finalizer()
        {
            if (!openedPass)
            {
                return;
            }
            openedPass = false;
            VisitorTabRowCapture.EndPass();
        }
    }

    /// <summary>
    /// The interaction-mode rows: the prisoner set through <c>DrawExclusiveInteractionRow</c> and
    /// the slave set inline in <c>DoSlaveTab</c>, both in vanilla's own listOrder enumeration order.
    /// </summary>
    [HarmonyPatch(typeof(Widgets), "RadioButtonLabeled",
        new Type[] { typeof(Rect), typeof(string), typeof(bool), typeof(bool) })]
    internal static class VisitorTabRadioTapPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Rect rect)
        {
            if (!VisitorTabRowCapture.Armed)
            {
                return;
            }
            VisitorTabRowCapture.RecordRadio(rect);
        }
    }

    /// <summary>
    /// The non-exclusive mode rows. Targeted via TargetMethod because a by-ref parameter type
    /// cannot be expressed in an attribute argument array (CS0182).
    /// </summary>
    [HarmonyPatch]
    internal static class VisitorTabCheckboxTapPatch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Widgets), "CheckboxLabeled",
                new Type[] { typeof(Rect), typeof(string), typeof(bool).MakeByRefType(), typeof(bool), typeof(Texture2D), typeof(Texture2D), typeof(bool), typeof(bool) });
        }

        [HarmonyPostfix]
        public static void Postfix(Rect rect)
        {
            if (!VisitorTabRowCapture.Armed)
            {
                return;
            }
            VisitorTabRowCapture.RecordCheckbox(rect);
        }
    }

    /// <summary>
    /// The allow-medicine dropdown. Vanilla offers the five care levels behind this ONE button, so
    /// every row of the scope's medical-care region rings it.
    /// </summary>
    [HarmonyPatch(typeof(MedicalCareUtility), "MedicalCareSelectButton")]
    internal static class VisitorTabMedicalCareTapPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Rect rect)
        {
            if (!VisitorTabRowCapture.Armed)
            {
                return;
            }
            VisitorTabRowCapture.RecordMedicalCareButton(rect);
        }
    }

    /// <summary>
    /// The ideoligion conversion target's icon: vanilla puts every player ideoligion in a FloatMenu
    /// behind it, so every row of the scope's armed picker rings this one icon.
    /// </summary>
    [HarmonyPatch(typeof(Ideo), "DrawIcon")]
    internal static class VisitorTabIdeoIconTapPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Rect rect)
        {
            if (!VisitorTabRowCapture.Armed)
            {
                return;
            }
            VisitorTabRowCapture.RecordIdeoIcon(rect);
        }
    }
}
