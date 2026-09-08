using System;
using System.Reflection;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Gizmo handler for VanillaPsycastsExpanded.UI.PsychicStatusGizmo, which
    /// replaces the vanilla psychic entropy gizmo wholesale via a Harmony
    /// prefix on Pawn_PsychicEntropyTracker.GetGizmo. It wraps the same public
    /// Pawn_PsychicEntropyTracker (in a private field named `tracker`, same as
    /// vanilla's own PsychicEntropyGizmo), so the label/status/description
    /// facets delegate to PsychicEntropyGizmoHandler's shared builders — the
    /// pawn hears the identical readout for either gizmo.
    /// </summary>
    internal sealed class VpePsychicStatusHandler : GizmoHandlerBase
    {
        private readonly Type gizmoType;
        private readonly FieldInfo trackerField;
        private readonly bool ready;

        public VpePsychicStatusHandler(Type gizmoType)
        {
            this.gizmoType = gizmoType;

            var surface = new ReflectionSurface("VpePsychicStatusHandler");
            surface.Supplied("VanillaPsycastsExpanded.UI.PsychicStatusGizmo", gizmoType);
            trackerField = surface.Field(gizmoType, "tracker");

            // The shared builders read a vanilla tracker; a field of some other
            // type means VPE rebuilt the gizmo around something else.
            bool trackerIsVanilla = typeof(Pawn_PsychicEntropyTracker).IsAssignableFrom(trackerField?.FieldType);
            if (surface.Ready && !trackerIsVanilla)
                ModLogger.Error("VpePsychicStatusHandler: PsychicStatusGizmo.tracker is no longer a Pawn_PsychicEntropyTracker; declining all facets.");

            ready = surface.Ready && trackerIsVanilla;
        }

        public override bool TryGetLabel(Gizmo gizmo, out string label)
        {
            label = null;
            if (!ready || !gizmoType.IsInstanceOfType(gizmo))
                return false;

            // Same spoken identity as the vanilla gizmo ("Neural Heat and Psyfocus").
            label = "RimWorldAccess.Inspection.Gizmo.Type.PsychicEntropy".Translate();
            return true;
        }

        public override bool TryGetStatus(Gizmo gizmo, out string status)
        {
            status = null;

            Pawn_PsychicEntropyTracker tracker = GetTracker(gizmo);
            if (tracker == null)
                return false;

            try
            {
                status = PsychicEntropyGizmoHandler.BuildStatus(tracker);
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VpePsychicStatusHandler.TryGetStatus failed: {ex.Message}");
                return false;
            }
        }

        public override bool TryGetDescription(Gizmo gizmo, out string description)
        {
            description = null;

            Pawn_PsychicEntropyTracker tracker = GetTracker(gizmo);
            if (tracker == null)
                return false;

            try
            {
                description = PsychicEntropyGizmoHandler.BuildDescription(tracker);
                return !string.IsNullOrEmpty(description);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VpePsychicStatusHandler.TryGetDescription failed: {ex.Message}");
                return false;
            }
        }

        public override bool TryExecute(Gizmo gizmo, GizmoHandlerContext ctx, out bool runEpilogue)
        {
            runEpilogue = false;

            Pawn_PsychicEntropyTracker tracker = GetTracker(gizmo);
            // PsychicStatusGizmo.GizmoOnGUI (~line 231) draws the limit button
            // only for colonist-player-controlled pawns; a sighted player has no
            // limiter toggle on other pawns, so decline and let the ladder fall
            // through rather than mutate a pawn the player doesn't control.
            if (tracker?.Pawn == null || !tracker.Pawn.IsColonistPlayerControlled)
                return false;

            try
            {
                bool newState = PsychicEntropyGizmoHandler.ToggleNeuralHeatLimiter(tracker);
                string stateStr = (newState
                    ? "RimWorldAccess.Inspection.Gizmo.LimiterStateOn"
                    : "RimWorldAccess.Inspection.Gizmo.LimiterStateOff").Translate();
                TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.NeuralHeatLimiter".Loc(stateStr));
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VpePsychicStatusHandler.TryExecute failed: {ex.Message}");
                return false;
            }
        }

        public override bool TryGetSliderAdapter(Gizmo gizmo, out GizmoSliderAdapter adapter)
        {
            adapter = null;

            Pawn_PsychicEntropyTracker tracker = GetTracker(gizmo);
            // NOTE: VPE's psyfocus-bar drag is NOT gated on player control (unlike
            // vanilla's, which sits inside an IsColonistPlayerControlled block in
            // PsychicEntropyGizmo.GizmoOnGUI) — that reads as an oversight, and we
            // mirror vanilla's intent (and our own vanilla handler) here rather
            // than let the keyboard set meditation targets on pawns the player
            // doesn't control.
            if (tracker?.Pawn == null || !tracker.Pawn.IsColonistPlayerControlled)
                return false;

            try
            {
                adapter = new GizmoSliderAdapter
                {
                    Title = "PsyfocusLabelGizmo".Translate(),
                    // PsychicStatusGizmo has no persistent drag-store field like
                    // vanilla's targetValue; its selectedPsyfocusTarget is
                    // transient drag state initialized to -1 — leave it untouched
                    // and read the committed target instead.
                    Value = tracker.TargetPsyfocus,
                    Min = 0f,
                    Max = 1f,
                    // VPE snaps psyfocus-bar drags to 1/16ths (GizmoOnGUI num6).
                    Step = 1f / 16f,
                    // The exact public call VPE's own MouseUp commit makes (vehicle A).
                    Write = value => tracker.SetPsyfocusTarget(value),
                    // Enter toggles the neural heat limiter (TryExecute above);
                    // arrows adjust the target directly.
                    InteractionHint = "RimWorldAccess.Inspection.Gizmo.HintPsychicEntropyArrows".Translate(),
                };
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VpePsychicStatusHandler.TryGetSliderAdapter failed: {ex.Message}");
                adapter = null;
                return false;
            }
        }

        /// <summary>
        /// Resolves the gizmo's private tracker field and casts it to the public
        /// Pawn_PsychicEntropyTracker. Null when not ready, the gizmo isn't this
        /// type, or the reflective read fails.
        /// </summary>
        private Pawn_PsychicEntropyTracker GetTracker(Gizmo gizmo)
        {
            if (!ready || !gizmoType.IsInstanceOfType(gizmo))
                return null;

            try
            {
                return trackerField.GetValue(gizmo) as Pawn_PsychicEntropyTracker;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VpePsychicStatusHandler.GetTracker failed: {ex.Message}");
                return null;
            }
        }
    }
}
