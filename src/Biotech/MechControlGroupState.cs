using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;
using RimWorld;
using UnityEngine;
using HarmonyLib;

namespace RimWorldAccess
{
    /// <summary>
    /// Data and lifecycle facade for the windowless mechanitor control-group detail view, over two
    /// areas: Settings (work mode, recharge thresholds, select all) and Members. The cursor and row
    /// presentation belong to <see cref="RimWorldAccess.Shell.MechControlGroupScope"/>; what stays
    /// here is the lifecycle, the group and member data, the range editor's working value, the
    /// mutation vehicles (vanilla's work-mode options, control-group assign, selector calls) and the
    /// action-outcome announcements that are whole sentences rather than row grammar.
    /// </summary>
    public static class MechControlGroupState
    {
        private static bool isActive = false;
        private static MechanitorControlGroup controlGroup;

        // The gizmo the drill-in came from, kept for the focus ring only (never invoked).
        private static Gizmo sourceGizmo;

        // Rebuilt from the group on every model refresh.
        private static readonly List<Pawn> memberMechs = new List<Pawn>();

        private static bool isEditingRange = false;
        private static bool editingMinimum = true;
        private static FloatRange editingRange;

        private static FieldInfo controlGroupField;
        private static FieldInfo mergedGroupsField;

        public static bool IsActive => isActive;

        /// <summary>Whether the inline recharge-range editor owns the keyboard.</summary>
        internal static bool IsEditingRange => isEditingRange;

        /// <summary>Which bound the range editor is on: true = minimum, false = maximum.</summary>
        internal static bool EditingMinimum => editingMinimum;

        /// <summary>The range editor's working value; nothing reaches the group until Enter confirms.</summary>
        internal static FloatRange EditingRange => editingRange;

        /// <summary>The open control group, or null while inactive.</summary>
        internal static MechanitorControlGroup Group => controlGroup;

        /// <summary>
        /// The gizmo this view was opened from, for the command-bar focus ring. A stale snapshot from
        /// an older GetGizmos enumeration is enough, since GizmoRectRegistry falls back to vanilla's
        /// GroupsWith, which matches on the control group.
        /// </summary>
        internal static Gizmo SourceGizmo => sourceGizmo;

        public static MechanitorControlGroup GetControlGroupFromGizmo(Gizmo gizmo)
        {
            if (controlGroupField == null)
            {
                controlGroupField = AccessTools.Field(
                    typeof(MechanitorControlGroupGizmo), "controlGroup");
            }
            return controlGroupField?.GetValue(gizmo) as MechanitorControlGroup;
        }

        private static List<MechanitorControlGroup> GetMergedGroupsFromGizmo(Gizmo gizmo)
        {
            if (mergedGroupsField == null)
            {
                mergedGroupsField = AccessTools.Field(
                    typeof(MechanitorControlGroupGizmo), "mergedControlGroups");
            }
            return mergedGroupsField?.GetValue(gizmo) as List<MechanitorControlGroup>;
        }

        /// <summary>
        /// Opens the detail view on the Settings area. The scope's first-focus hook speaks the header
        /// and landing row once the windowless mirror has pushed it.
        /// </summary>
        public static void Open(MechanitorControlGroup group, Gizmo gizmo = null)
        {
            if (group == null)
            {
                Log.Error("[RimWorld Access] Cannot open mech control group state: group is null");
                return;
            }

            controlGroup = group;
            sourceGizmo = gizmo;
            isActive = true;
            isEditingRange = false;
            RefreshMembers();
        }

        /// <summary>Closes the state and returns the cursor to the gizmo bar.</summary>
        public static void Close()
        {
            ClearWithoutGizmoReturn();
            GizmoNavigationState.Open();
        }

        /// <summary>Escape from browse mode: close and say so.</summary>
        internal static void CloseAndAnnounce()
        {
            Close();
            TolkHelper.Speak("RimWorldAccess.Biotech.Mech.GroupClosed".Loc());
        }

        /// <summary>
        /// Silent hard reset for a session boundary. Unlike <see cref="Close"/> it does NOT reopen
        /// gizmo navigation, which would be wrong during a game-start or main-menu reset where there
        /// is no gizmo bar to return to.
        /// </summary>
        internal static void ResetHard()
        {
            ClearWithoutGizmoReturn();
        }

        /// <summary>
        /// Drops every field without returning to the gizmo bar, which the jump-to-mech and select-all
        /// paths need: both hand the selection to the map, so returning to the OLD selection's gizmo
        /// bar would be wrong.
        /// </summary>
        private static void ClearWithoutGizmoReturn()
        {
            isActive = false;
            isEditingRange = false;
            controlGroup = null;
            sourceGizmo = null;
            memberMechs.Clear();
        }

        /// <summary>The header fragment, matching the group's own vanilla label.</summary>
        internal static string GroupLabel()
        {
            if (controlGroup == null)
                return "";
            return "ControlGroup".Translate() + " " + controlGroup.Index;
        }

        /// <summary>Vanilla's own "Select all mechs" command label, with the group's mech count when it has any.</summary>
        internal static string SelectAllLabel()
        {
            string label = "CommandSelectAllMechs".Translate();
            int mechCount = controlGroup != null ? controlGroup.MechsForReading.Count : 0;
            if (mechCount > 0)
                label += $" ({mechCount})";
            return label;
        }

        internal static int MemberCount => memberMechs.Count;

        internal static Pawn MemberAt(int index)
        {
            return (index >= 0 && index < memberMechs.Count) ? memberMechs[index] : null;
        }

        /// <summary>Re-reads the group's mech list (called from the scope's own content refresh).</summary>
        internal static void RefreshMembers()
        {
            memberMechs.Clear();
            if (controlGroup != null)
            {
                memberMechs.AddRange(controlGroup.MechsForReading);
            }
        }

        /// <summary>
        /// One mech row's supplementary status: its energy readout plus an uncontrolled marker when
        /// the mechanitor cannot currently control it. The row's label is the mech's name.
        /// </summary>
        internal static string MemberStatus(Pawn mech)
        {
            if (mech == null)
                return "";
            var parts = new List<string>();
            if (mech.needs?.energy != null)
            {
                parts.Add("RimWorldAccess.Biotech.Mech.MechEnergy".Translate(
                    FormatPercent(mech.needs.energy.CurLevelPercentage),
                    "EnergyLower".Translate()).ToString());
            }
            if (controlGroup != null && !controlGroup.Tracker.ControlledPawns.Contains(mech))
            {
                parts.Add("RimWorldAccess.Biotech.Mech.Uncontrolled".Translate().ToString());
            }
            return string.Join(". ", parts.ToArray());
        }

        /// <summary>
        /// Leaves the detail view and hands the mech to the map. No gizmo-bar return: the selection
        /// is now the mech itself.
        /// </summary>
        internal static void JumpToMember(int index)
        {
            Pawn mech = MemberAt(index);
            if (mech == null)
                return;

            ClearWithoutGizmoReturn();

            CameraJumper.TryJumpAndSelect(mech);
            MapNavigationState.SpeakJumpedTo(mech.LabelCap);
        }

        /// <summary>
        /// Vanilla's own work-mode options, the same ones its right-click menu shows, each wrapped
        /// only to re-announce afterwards: the option's own action does the work.
        /// </summary>
        internal static void OpenWorkModeMenu()
        {
            if (controlGroup == null)
                return;

            var options = MechanitorControlGroupGizmo.GetWorkModeOptions(controlGroup).ToList();
            if (options.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Biotech.Mech.NoWorkModes".Loc());
                return;
            }

            var wrappedOptions = new List<FloatMenuOption>();
            foreach (var opt in options)
            {
                var originalAction = opt.action;
                string label = opt.Label;
                wrappedOptions.Add(new FloatMenuOption(label, delegate
                {
                    originalAction?.Invoke();
                    if (isActive)
                    {
                        TolkHelper.Speak("RimWorldAccess.Biotech.Mech.WorkModeSet".Loc(controlGroup.WorkMode.LabelCap));
                    }
                }, opt.iconThing, opt.iconColor)
                {
                    tooltip = opt.tooltip
                });
            }

            WindowlessFloatMenuState.Open(wrappedOptions, false);
        }

        /// <summary>
        /// Opens the inline range editor on the minimum bound. The scope announces the focused bound;
        /// nothing reaches the group until <see cref="RangeConfirm"/>.
        /// </summary>
        internal static void OpenRangeEditor()
        {
            if (controlGroup == null)
                return;
            isEditingRange = true;
            editingMinimum = true;
            editingRange = controlGroup.mechRechargeThresholds;
        }

        /// <summary>
        /// Vanilla's own select-all behaviour, one Selector.Select per mech. Leaves the detail view
        /// first, since the selection it builds replaces the one the view was opened from.
        /// </summary>
        internal static void SelectAllMechs()
        {
            if (controlGroup == null)
                return;

            var mechs = controlGroup.MechsForReading;
            if (mechs.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Biotech.Mech.NoMechsInGroup".Loc());
                return;
            }

            // Snapshot before the state is cleared: MechsForReading belongs to the group.
            var selection = new List<Pawn>(mechs);
            ClearWithoutGizmoReturn();

            Find.Selector.ClearSelection();
            foreach (var mech in selection)
            {
                Find.Selector.Select(mech, forceDesignatorDeselect: false);
            }

            TolkHelper.Speak(selection.Count == 1
                ? "RimWorldAccess.Biotech.Mech.SelectedMechOne".Loc(selection.Count)
                : "RimWorldAccess.Biotech.Mech.SelectedMechMany".Loc(selection.Count));
        }

        /// <summary>
        /// The reassignment picker for one member row: one option per other control group, each
        /// riding that group's own <c>Assign</c>.
        /// </summary>
        internal static void OpenReassignMenu(int index)
        {
            Pawn selectedMech = MemberAt(index);
            if (selectedMech == null || controlGroup == null)
                return;

            var tracker = controlGroup.Tracker;
            var allGroups = tracker.controlGroups;

            if (allGroups.Count <= 1)
            {
                TolkHelper.Speak("RimWorldAccess.Biotech.Mech.NoOtherGroups".Loc());
                return;
            }

            var options = new List<FloatMenuOption>();
            foreach (var group in allGroups)
            {
                if (group == controlGroup)
                    continue;

                int groupIndex = group.Index;
                string label = "AssignMechToControlGroup".Translate(groupIndex)
                    + " (" + group.WorkMode.LabelCap + ")";

                var targetGroup = group;
                options.Add(new FloatMenuOption(label, delegate
                {
                    targetGroup.Assign(selectedMech);

                    if (isActive)
                    {
                        RefreshMembers();

                        string announcement = "RimWorldAccess.Biotech.Mech.AssignedToGroup".Translate(
                            selectedMech.LabelCap, "ControlGroup".Translate(), groupIndex);
                        if (memberMechs.Count == 0)
                        {
                            announcement += ". " + "NoMechs".Translate();
                        }
                        TolkHelper.SpeakData(announcement);

                        if (memberMechs.Count > 0)
                            Shell.MechControlGroupScope.Live?.AnnounceCurrentRow();
                    }
                }));
            }

            WindowlessFloatMenuState.Open(options, false);
        }

        /// <summary>Toggles between the min and max bound. Silent — the scope announces the new bound.</summary>
        internal static void RangeToggleBound()
        {
            editingMinimum = !editingMinimum;
        }

        /// <summary>
        /// Steps the focused bound by 0.01 with Shift and 0.05 otherwise, clamped against the other
        /// bound. Silent — the scope announces the new value.
        /// </summary>
        internal static void RangeAdjust(int direction, bool shiftHeld)
        {
            float adjustment = (shiftHeld ? 0.01f : 0.05f) * direction;

            if (editingMinimum)
            {
                float newMin = Mathf.Clamp(editingRange.min + adjustment, 0f, editingRange.max);
                editingRange.min = Mathf.Round(newMin * 100f) / 100f;
            }
            else
            {
                float newMax = Mathf.Clamp(editingRange.max + adjustment, editingRange.min, 1f);
                editingRange.max = Mathf.Round(newMax * 100f) / 100f;
            }
        }

        /// <summary>
        /// Resets both bounds to vanilla's default, mirroring Dialog_RechargeSettings' Reset button:
        /// it rewrites only the working value, so Enter or Escape still confirm or discard it.
        /// </summary>
        internal static void RangeReset()
        {
            editingRange = MechanitorControlGroup.DefaultMechRechargeThresholds;
            TolkHelper.Speak("RimWorldAccess.Biotech.Mech.RangeReset".Loc(
                FormatPercent(editingRange.min), FormatPercent(editingRange.max)));
        }

        internal static void RangeConfirm() => CloseRangeEditor(save: true);

        internal static void RangeCancel() => CloseRangeEditor(save: false);

        private static void CloseRangeEditor(bool save)
        {
            if (save && controlGroup != null)
            {
                // MUTATION-C: mirrors Dialog_RechargeSettings' own OK button, which assigns
                // the working range straight onto controlGroup.mechRechargeThresholds
                // (decompiled Verse/Dialog_RechargeSettings.cs, the OK ButtonText branch);
                // no gated setter exists for that field.
                controlGroup.mechRechargeThresholds = editingRange;
                TolkHelper.Speak("RimWorldAccess.Biotech.Mech.RangeSaved".Loc(
                    FormatPercent(editingRange.min), FormatPercent(editingRange.max)));
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Biotech.Mech.RangeCancelled".Loc());
            }

            isEditingRange = false;
        }

        /// <summary>The accessible label for a MechanitorControlGroupGizmo, merged groups included.</summary>
        public static string GetGizmoLabel(Gizmo gizmo)
        {
            var group = GetControlGroupFromGizmo(gizmo);
            if (group == null)
                return "RimWorldAccess.Biotech.Mech.GizmoLabelFallback".Translate();

            var mergedGroups = GetMergedGroupsFromGizmo(gizmo);
            int mechCount = group.MechsForReading.Count;

            if (mergedGroups != null && mergedGroups.Count > 0)
            {
                string indices = group.Index.ToString();
                var sorted = mergedGroups.OrderBy(g => g.Index).ToList();
                foreach (var mg in sorted)
                {
                    indices += ", " + mg.Index;
                    mechCount += mg.MechsForReading.Count;
                }

                string label = "Groups".Translate() + " " + indices;
                if (mechCount == 0)
                    label += ", " + "NoMechs".Translate();
                else
                    label += ", " + (mechCount == 1
                        ? "RimWorldAccess.Biotech.Mech.MechCountOne".Translate(mechCount)
                        : "RimWorldAccess.Biotech.Mech.MechCountMany".Translate(mechCount));
                return label;
            }
            else
            {
                string label = "ControlGroup".Translate() + " " + group.Index;
                label += ", " + group.WorkMode.LabelCap;

                if (mechCount == 0)
                    label += ", " + "NoMechs".Translate();
                else
                    label += ", " + (mechCount == 1
                        ? "RimWorldAccess.Biotech.Mech.MechCountOne".Translate(mechCount)
                        : "RimWorldAccess.Biotech.Mech.MechCountMany".Translate(mechCount));

                return label;
            }
        }

        /// <summary>The gizmo's status value: mechs grouped by type, with count and average energy.</summary>
        public static string GetGizmoStatus(Gizmo gizmo)
        {
            var group = GetControlGroupFromGizmo(gizmo);
            if (group == null)
                return "";

            var mechs = group.MechsForReading;
            if (mechs.Count == 0)
                return "";

            var grouped = new Dictionary<PawnKindDef, List<Pawn>>();
            foreach (var mech in mechs)
            {
                if (!grouped.ContainsKey(mech.kindDef))
                    grouped[mech.kindDef] = new List<Pawn>();
                grouped[mech.kindDef].Add(mech);
            }

            var entries = new List<string>();
            foreach (var kvp in grouped)
            {
                string typeName = kvp.Key.LabelCap;
                int count = kvp.Value.Count;

                float totalEnergy = 0f;
                int energyCount = 0;
                foreach (var mech in kvp.Value)
                {
                    if (mech.needs?.energy != null)
                    {
                        totalEnergy += mech.needs.energy.CurLevelPercentage;
                        energyCount++;
                    }
                }

                string entry = typeName + ": " + count;
                if (energyCount > 0)
                {
                    float avgEnergy = totalEnergy / energyCount;
                    entry += ". " + "RimWorldAccess.Biotech.Mech.AverageEnergy".Translate(
                        "EnergyLower".Translate(), FormatPercent(avgEnergy));
                }
                entries.Add(entry);
            }

            return string.Join(". ", entries);
        }

        /// <summary>The recharge row's spoken value, also used for the range editor's working range.</summary>
        internal static string FormatRange(FloatRange range)
        {
            return "RimWorldAccess.Mechs.PercentRange".Translate(
                FormatPercent(range.min), FormatPercent(range.max)).ToString();
        }

        internal static string FormatPercent(float value)
        {
            return Mathf.RoundToInt(value * 100f) + "%";
        }
    }
}
