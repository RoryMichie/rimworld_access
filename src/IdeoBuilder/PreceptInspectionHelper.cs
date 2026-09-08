using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// The shared Alt+I picker for a row backed by one or more Defs -- used by the ideoligion
    /// issue screen (<c>IdeoPreceptScreenScope</c>) and the typed-precept-list screen
    /// (<c>IdeoTypedPreceptScreenScope</c>). Vanilla never opens <c>Dialog_InfoCard</c> for a
    /// <c>PreceptDef</c> itself: it has no <c>SpecialDisplayStats</c> override (the card would
    /// show only a bare description), and no vanilla call site (<c>Widgets.InfoCardButton</c> /
    /// <c>Dialog_InfoCard.Hyperlink</c>) ever passes one as its subject -- the one PreceptDef-
    /// adjacent InfoCardButton call, <c>ITab_Bills.cs:127</c>, takes a RecipeDef as its subject
    /// and only a <c>Precept_ThingStyle</c> as context. So a live Precept itself is never a
    /// target here -- callers offer only the Defs a citable vanilla card site opens a real
    /// <c>Dialog_InfoCard</c> for (e.g. a precept's linked ThingDef or XenotypeDef via
    /// <see cref="IdeoTypedPreceptState.LinkedDefFor"/>); zero such Defs means no target at all.
    /// </summary>
    public static class PreceptInspectionHelper
    {
        public static void ShowPicker(List<Def> defs)
        {
            if (defs.Count == 0)
            {
                InfoCardState.SpeakNoInfoCardAvailable();
                return;
            }
            if (defs.Count == 1)
            {
                InfoCardState.OpenInfoCardForDef(defs[0]);
                return;
            }
            var options = new List<FloatMenuOption>();
            foreach (Def def in defs)
            {
                Def captured = def;
                string label = def.label != null ? def.label.CapitalizeFirst() : def.defName;
                options.Add(new FloatMenuOption(label, () => InfoCardState.OpenInfoCardForDef(captured)));
            }
            TolkHelper.Speak("RimWorldAccess.InfoCard.ChooseItemToInspect".Loc());
            WindowlessFloatMenuState.Open(options, false);
        }
    }
}
