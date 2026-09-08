using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    public class GravshipLaunchAdapter : IdeologyRitualAdapter
    {
        private readonly Dialog_BeginGravshipLaunch gravshipDialog;

        public GravshipLaunchAdapter(Dialog_BeginGravshipLaunch dialog) : base(dialog)
        {
            gravshipDialog = dialog;
        }

        public override string LocalizedDialogName => "RimWorldAccess.Rituals.Gravship.DialogName".Translate();

        public override string ClosingAnnouncement => "RimWorldAccess.Rituals.Gravship.DialogClosed".Translate();

        public override IReadOnlyList<LordJobExtraToggle> BuildExtraToggles()
        {
            var toggles = new List<LordJobExtraToggle>();
            AddToggle(toggles, "forceVisitorsToLeave", "GravshipForceVisitorsToLeaveLabel", "GravshipForceVisitorsToLeaveTooltip");
            AddToggle(toggles, "boardColonyAnimals", "GravshipBoardColonyAnimalsLabel", "GravshipBoardColonyAnimalsTooltip");
            if (ModsConfig.BiotechActive)
            {
                AddToggle(toggles, "boardColonyMechs", "GravshipBoardColonyMechsLabel", "GravshipBoardColonyMechsTooltip");
            }
            return toggles;
        }

        // MUTATION-C: mirrors Dialog_BeginGravshipLaunch.DoRightColumn's three
        // Widgets.CheckboxLabeled(rect, label, ref field) calls (decompiled
        // Dialog_BeginGravshipLaunch.cs:35-53 -- forceVisitorsToLeave, boardColonyAnimals,
        // boardColonyMechs, the third gated on the same ModsConfig.BiotechActive check
        // BuildExtraToggles above already applies); no vehicle A/B exists because vanilla writes
        // these private fields directly with no gated setter or extractable delegate.
        public override bool ApplyExtraToggle(LordJobExtraToggle toggle)
        {
            if (toggle == null || !(toggle.AdapterTag is FieldInfo field)) return false;
            try
            {
                bool newValue = !toggle.Checked;
                field.SetValue(gravshipDialog, newValue);
                toggle.Checked = newValue;
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning($"[GravshipLaunchAdapter] Toggle failed: {ex.Message}");
                return false;
            }
        }

        private void AddToggle(List<LordJobExtraToggle> toggles, string fieldName, string labelKey, string tooltipKey)
        {
            var field = AccessTools.Field(typeof(Dialog_BeginGravshipLaunch), fieldName);
            if (field == null) return;

            bool current;
            try { current = (bool)field.GetValue(gravshipDialog); }
            catch { return; }

            string tooltip = null;
            try { tooltip = tooltipKey.Translate().Resolve(); }
            catch { /* tolerate */ }

            toggles.Add(new LordJobExtraToggle
            {
                Label = labelKey.Translate().Resolve(),
                Checked = current,
                Tooltip = tooltip,
                AdapterTag = field,
            });
        }
    }
}
