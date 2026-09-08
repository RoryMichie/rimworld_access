using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using RimWorldAccess.Shell;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// The three bare-<see cref="PawnColumnWorker"/> slider columns
    /// (<c>TM_GolemThreatRange</c>, <c>TM_GolemRestPercent</c>,
    /// <c>TM_GolemAwakenPercent</c>). Their class names are CROSSED against
    /// the <c>CompGolem</c> field each one's own <c>DoCell</c> writes:
    /// <c>PawnColumnWorker_GolemRestPercent</c> writes
    /// <c>energyPctShouldAwaken</c>
    /// (PawnColumnWorker_GolemRestPercent.cs:43-55), and
    /// <c>PawnColumnWorker_GolemAwakenPercent</c> writes
    /// <c>energyPctShouldRest</c> (PawnColumnWorker_GolemAwakenPercent.cs:
    /// 43-55). This handler names and presents each column for the field it
    /// actually controls rather than for the worker's misleading class name;
    /// <c>TM_GolemThreatRange</c> writes <c>threatRange</c>
    /// (PawnColumnWorker_GolemThreatRange.cs:43-56). Each instance serves one
    /// column; the picker's step is our own keyboard-sized granularity, not
    /// the visual slider's continuous drag step.
    /// </summary>
    internal sealed class RwomGolemSliderColumnHandler : PawnColumnHandler
    {
        private readonly Type compGolemType;
        private readonly FieldInfo field;
        private readonly int startPercent;   // display units: 0-100 for both raw (threatRange) and percent fields
        private readonly int stepPercent;
        private readonly bool isPercent;      // true: raw field value = display / 100. false: raw field value = display.
        private readonly string headerKey;
        private readonly string cellTextKey;  // "{0}" (threatRange) or "{0} percent" (the two percent fields)

        private RwomGolemSliderColumnHandler(Type compGolemType, FieldInfo field, int startPercent, int stepPercent,
            bool isPercent, string headerKey, string cellTextKey)
        {
            this.compGolemType = compGolemType;
            this.field = field;
            this.startPercent = startPercent;
            this.stepPercent = stepPercent;
            this.isPercent = isPercent;
            this.headerKey = headerKey;
            this.cellTextKey = cellTextKey;
        }

        public static void TryRegisterAll()
        {
            try
            {
                var surface = new ReflectionSurface("RwomGolemSliderColumnHandler");
                Type compGolemType = surface.Type("TorannMagic.Golems.CompGolem");
                FieldInfo threatRangeField = surface.Field(compGolemType, "threatRange");
                FieldInfo awakenField = surface.Field(compGolemType, "energyPctShouldAwaken");
                FieldInfo restField = surface.Field(compGolemType, "energyPctShouldRest");

                Type threatRangeWorker = surface.Type("TorannMagic.Golems.PawnColumnWorker_GolemThreatRange");
                Type restPercentWorker = surface.Type("TorannMagic.Golems.PawnColumnWorker_GolemRestPercent");
                Type awakenPercentWorker = surface.Type("TorannMagic.Golems.PawnColumnWorker_GolemAwakenPercent");

                if (!surface.Ready)
                {
                    return;
                }

                PawnColumnHandlerRegistry.Register(threatRangeWorker,
                    new RwomGolemSliderColumnHandler(compGolemType, threatRangeField, 0, 5, isPercent: false,
                        "RimWorldAccess.Compat.Rwom.GolemThreatRange", "RimWorldAccess.Compat.Rwom.GolemThreatRangeCell"));
                // energyPctShouldAwaken's own floor is .1 (PawnColumnWorker_GolemRestPercent.cs:52) -- the picker starts at 10 percent to match.
                PawnColumnHandlerRegistry.Register(restPercentWorker,
                    new RwomGolemSliderColumnHandler(compGolemType, awakenField, 10, 10, isPercent: true,
                        "RimWorldAccess.Compat.Rwom.GolemWakeAt", "RimWorldAccess.Compat.Rwom.GolemPercentCell"));
                PawnColumnHandlerRegistry.Register(awakenPercentWorker,
                    new RwomGolemSliderColumnHandler(compGolemType, restField, 0, 10, isPercent: true,
                        "RimWorldAccess.Compat.Rwom.GolemRestAt", "RimWorldAccess.Compat.Rwom.GolemPercentCell"));
            }
            catch (Exception ex)
            {
                ModLogger.Error($"RwomGolemCompat golem slider column registration failed: {ex.Message}");
            }
        }

        /// <summary>These three columns ship with an empty <c>headerTip</c> and no <c>label</c> (icon-headed) -- the header falls back to this handler.</summary>
        public override string HeaderLabel(PawnColumnDef def)
        {
            return headerKey.Translate().ToString();
        }

        public override string CellText(PawnColumnDef def, Pawn pawn)
        {
            try
            {
                object cg = RwomGolemCompat.FindComp(pawn, compGolemType);
                if (cg == null)
                {
                    return "";
                }
                float raw = (float)field.GetValue(cg);
                int display = ToDisplay(raw);
                return cellTextKey.Translate(display).ToString();
            }
            catch (Exception ex)
            {
                ModLogger.Error($"RwomGolemSliderColumnHandler.CellText failed: {ex.Message}");
                return "RimWorldAccess.Shell.Generic.CellNotReadable".Loc().ToString();
            }
        }

        public override PawnColumnActivation ActivateCell(PawnColumnDef def, Pawn pawn, PawnTable table)
        {
            object cg = RwomGolemCompat.FindComp(pawn, compGolemType);
            if (cg == null)
            {
                return PawnColumnActivation.NotHandled;
            }

            var options = new List<FloatMenuOption>();
            for (int display = startPercent; display <= 100; display += stepPercent)
            {
                int capturedDisplay = display;
                string label = cellTextKey.Translate(capturedDisplay).ToString();
                options.Add(new FloatMenuOption(label, delegate
                {
                    // MUTATION-C: mirrors TorannMagic.Golems.PawnColumnWorker_GolemThreatRange/
                    // GolemRestPercent/GolemAwakenPercent.DoCell (cited at each registration
                    // above) -- each slider writes its bare CompGolem field inline in the draw
                    // pass via Widgets.HorizontalSlider's return value, with no Try*/Can* twin.
                    field.SetValue(cg, ToRaw(capturedDisplay));
                    SoundDefOf.DragSlider.PlayOneShotOnCamera(null);
                }));
            }
            WindowlessFloatMenuState.Open(options, colonistOrders: false);
            return PawnColumnActivation.OpenedUI;
        }

        private float ToRaw(int display)
        {
            return isPercent ? display / 100f : display;
        }

        private int ToDisplay(float raw)
        {
            return isPercent ? Mathf.RoundToInt(raw * 100f) : Mathf.RoundToInt(raw);
        }
    }
}
