using System.Collections.Generic;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// rjw.SexGizmo is a bare Gizmo (no Command chrome) drawing two meters and up
    /// to two hover-only image buttons while the pawn's current job runs. Status
    /// speaks the meters; the buttons become menu options gated exactly as the mod
    /// gates the drawn widgets, and Enter opens that menu.
    /// </summary>
    internal sealed class RjwStatusGizmoHandler : GizmoHandlerBase
    {
        public override bool TryGetStatus(Gizmo gizmo, out string status)
        {
            status = null;
            Pawn pawn = OwnerOf(gizmo);
            object driver = ActiveDriver(pawn);
            if (driver == null)
            {
                return false;
            }
            float progress = Guarded.Get(RjwReflection.JobDriverProgress.GetGetMethod(), driver,
                "Rjw status gizmo progress", 0f);
            List<string> parts = new List<string>
            {
                CompatText.ModText("RJW_SexGizmo_Orgasm") + ": " + (progress * 100f).ToString("F0") + " / 100",
            };
            Need_Rest rest = pawn.needs?.TryGetNeed<Need_Rest>();
            if (rest != null)
            {
                parts.Add(CompatText.ModText("RJW_SexGizmo_RestNeed") + ": " + rest.CurLevel.ToStringPercent());
            }
            status = string.Join(". ", parts);
            return true;
        }

        public override bool TryExecute(Gizmo gizmo, GizmoHandlerContext ctx, out bool runEpilogue)
        {
            runEpilogue = false;
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            if (!TryGetExtraOptions(gizmo, options) || options.Count == 0)
            {
                return false;
            }
            WindowlessFloatMenuState.OpenTitled(null, options);
            return true;
        }

        public override bool TryGetExtraOptions(Gizmo gizmo, List<FloatMenuOption> options)
        {
            Pawn pawn = OwnerOf(gizmo);
            object driver = ActiveDriver(pawn);
            if (driver == null
                || RjwReflection.IsInMultiplayer
                || !pawn.IsColonistPlayerControlled
                || !CanChangeDesignation(pawn)
                || !RjwReflection.JobDriverInitiatorType.IsInstanceOfType(driver))
            {
                return false;
            }

            options.Add(new CheckboxFloatMenuOption(
                SpeechFlatten.ToSentences(CompatText.ModText("RJW_SexGizmo_SexOverdriveTooltip")),
                delegate
                {
                    // MUTATION-C: mirrors rjw.SexGizmo.GizmoOnGUI's overdrive toggle branch; the
                    // widget body is inline IMGUI with no callable method on the driver.
                    bool now = !Guarded.FieldOf(RjwReflection.JobDriverOverdrive, driver,
                        "Rjw overdrive read", false);
                    RjwReflection.JobDriverOverdrive.SetValue(driver, now);
                    (now ? SoundDefOf.Tick_Low : SoundDefOf.Tick_High).PlayOneShotOnCamera(null);
                },
                () => Guarded.FieldOf(RjwReflection.JobDriverOverdrive, driver, "Rjw overdrive read", false)));

            if (RjwReflection.JobDriverForcedType.IsInstanceOfType(driver))
            {
                options.Add(new FloatMenuOption(
                    SpeechFlatten.ToSentences(CompatText.ModText("RJW_SexGizmo_HitPawn")),
                    delegate
                    {
                        // MUTATION-C: mirrors rjw.SexGizmo.GizmoOnGUI's strike button branch; the
                        // widget body is inline IMGUI with no callable method on the driver.
                        RjwReflection.JobDriverStrikeOnce.SetValue(driver, true);
                        SoundDefOf.Tick_High.PlayOneShotOnCamera(null);
                    }));
            }
            return true;
        }

        private static Pawn OwnerOf(Gizmo gizmo)
        {
            return Guarded.FieldOf<Pawn>(RjwReflection.StatusGizmoPawn, gizmo, "Rjw status gizmo owner", null);
        }

        /// <summary>The mod draws nothing (and offers nothing) unless the current driver is its own job type.</summary>
        private static object ActiveDriver(Pawn pawn)
        {
            object driver = pawn?.jobs?.curDriver;
            return driver != null && RjwReflection.JobDriverType.IsInstanceOfType(driver) ? driver : null;
        }

        private static bool CanChangeDesignation(Pawn pawn)
        {
            object result = Guarded.Call(RjwReflection.CanChangeDesignationColonist, null,
                "Rjw designation gate", pawn);
            return result is bool allowed && allowed;
        }
    }
}
