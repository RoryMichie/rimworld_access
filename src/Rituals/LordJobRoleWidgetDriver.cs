using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>One group of the dialog's pawn list, exactly as vanilla draws it: a headline box of portraits.</summary>
    public sealed class LordJobRoleGroup
    {
        public enum Kind { Role, Spectators, NotParticipating }

        public Kind Type;
        /// <summary>Vanilla's headline over the box (pluralized category label, spectators label, not-participating label).</summary>
        public string Headline;
        /// <summary>Vanilla's maxPawns for the box; 0 means unlimited.</summary>
        public int MaxCount;
        /// <summary>Vanilla's lock icon: every assigned pawn is required.</summary>
        public bool Locked;
        /// <summary>Vanilla's warning-icon tooltip (extra role info, minimum-count notice).</summary>
        public string ExtraInfo;
        public readonly List<Pawn> Pawns = new List<Pawn>();
        /// <summary>The <c>IGrouping&lt;string, RoleType&gt;</c> behind a Role group; null otherwise.</summary>
        public object Group;
    }

    /// <summary>
    /// Keyboard access to the dialog's own <c>PawnRoleSelectionWidgetBase&lt;RoleType&gt;</c>:
    /// the groups it draws and the assignment moves its mouse handlers perform.
    /// </summary>
    public interface ILordJobRoleWidgetDriver
    {
        List<LordJobRoleGroup> BuildGroups();
        bool Required(Pawn pawn);
        /// <summary>The widget's grey-out reason for a portrait, or null when drawn normally.</summary>
        string GrayOutReason(Pawn pawn);
        /// <summary>The widget's extra tooltip lines for a portrait (stats, ideo, ritual extras).</summary>
        string ExtraTipContents(Pawn pawn);
        /// <summary>The move menu vanilla opens on a portrait, each option running the widget's own assignment path then <paramref name="afterChange"/>.</summary>
        List<FloatMenuOption> BuildMoveOptions(Pawn pawn, Action afterChange);
        /// <summary>Vanilla's portrait click for the pawn's current group. True when the assignment changed.</summary>
        bool QuickMove(Pawn pawn, LordJobRoleGroup from);
        void Notify_AssignmentsChanged();
    }

    public static class LordJobRoleWidgetDriver
    {
        private static readonly FieldInfo ParticipantsDrawerField =
            AccessTools.Field(typeof(Dialog_BeginLordJob), "participantsDrawer");

        /// <summary>Closes the generic driver on the widget's own RoleType; null when the dialog has no vanilla widget.</summary>
        public static ILordJobRoleWidgetDriver TryCreate(Dialog_BeginLordJob dialog)
        {
            object widget = dialog != null ? ParticipantsDrawerField?.GetValue(dialog) : null;
            if (widget == null) return null;
            for (Type t = widget.GetType(); t != null; t = t.BaseType)
            {
                if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(PawnRoleSelectionWidgetBase<>))
                {
                    Type closed = typeof(LordJobRoleWidgetDriver<>).MakeGenericType(t.GetGenericArguments()[0]);
                    return Activator.CreateInstance(closed, widget) as ILordJobRoleWidgetDriver;
                }
            }
            return null;
        }
    }

    /// <summary>
    /// The generic half: <c>assignments</c> and <c>candidatePool</c> are the widget's own, so the
    /// public interface calls are direct; the private mouse-path methods (CannotAssignReason,
    /// TryAssign, TryAssignReplace, TryAssignAnyRole, ExtraPawnAssignmentInfo,
    /// PostProcessFloatLabel) are invoked on the closed type so every move runs vanilla's gates.
    /// </summary>
    public sealed class LordJobRoleWidgetDriver<RoleType> : ILordJobRoleWidgetDriver
        where RoleType : class, ILordJobRole
    {
        private static readonly Type WidgetType = typeof(PawnRoleSelectionWidgetBase<RoleType>);
        private static readonly FieldInfo AssignmentsField = AccessTools.Field(WidgetType, "assignments");
        private static readonly FieldInfo CandidatePoolField = AccessTools.Field(WidgetType, "candidatePool");
        private static readonly FieldInfo RolesGroupedTmpField = AccessTools.Field(WidgetType, "rolesGroupedTmp");
        private static readonly MethodInfo CannotAssignReasonMethod = AccessTools.Method(WidgetType, "CannotAssignReason");
        private static readonly MethodInfo TryAssignMethod = AccessTools.Method(WidgetType, "TryAssign");
        private static readonly MethodInfo TryAssignReplaceMethod = AccessTools.Method(WidgetType, "TryAssignReplace");
        private static readonly MethodInfo TryAssignAnyRoleMethod = AccessTools.Method(WidgetType, "TryAssignAnyRole");
        private static readonly MethodInfo ExtraPawnAssignmentInfoMethod = AccessTools.Method(WidgetType, "ExtraPawnAssignmentInfo");
        private static readonly MethodInfo PostProcessFloatLabelMethod = AccessTools.Method(WidgetType, "PostProcessFloatLabel");
        private static readonly MethodInfo ExtraTipContentsMethod = AccessTools.Method(WidgetType, "ExtraTipContents");

        private readonly PawnRoleSelectionWidgetBase<RoleType> widget;
        private readonly ILordJobAssignmentsManager<RoleType> assignments;
        private readonly ILordJobCandidatePool candidatePool;

        public LordJobRoleWidgetDriver(object widget)
        {
            this.widget = (PawnRoleSelectionWidgetBase<RoleType>)widget;
            assignments = AssignmentsField?.GetValue(widget) as ILordJobAssignmentsManager<RoleType>;
            candidatePool = CandidatePoolField?.GetValue(widget) as ILordJobCandidatePool;
        }

        public List<LordJobRoleGroup> BuildGroups()
        {
            var groups = new List<LordJobRoleGroup>();
            if (assignments == null || candidatePool == null) return groups;

            foreach (IGrouping<string, RoleType> group in assignments.RoleGroups())
            {
                RoleType first = group.First();
                int max = group.Sum(r => r.MaxCount);
                var view = new LordJobRoleGroup
                {
                    Type = LordJobRoleGroup.Kind.Role,
                    Headline = Find.ActiveLanguageWorker.Pluralize(first.CategoryLabelCap, max),
                    MaxCount = max,
                    ExtraInfo = ExtraPawnAssignmentInfoMethod?.Invoke(widget, new object[] { group, null }) as string,
                    Group = group,
                };
                view.Pawns.AddRange(group.SelectMany(r => assignments.AssignedPawns(r)));
                view.Locked = view.Pawns.Count > 0 && view.Pawns.All(assignments.Required);
                groups.Add(view);
            }

            List<Pawn> candidates = candidatePool.AllCandidatePawns;
            if (assignments.SpectatorsAllowed)
            {
                var spectators = new LordJobRoleGroup
                {
                    Type = LordJobRoleGroup.Kind.Spectators,
                    Headline = widget.SpectatorsLabel(),
                    MaxCount = candidates.Count,
                };
                spectators.Pawns.AddRange(assignments.SpectatorsForReading);
                groups.Add(spectators);
            }

            var pool = new List<Pawn>(candidates);
            pool.AddRange(candidatePool.NonAssignablePawns);
            pool.RemoveDuplicates();
            var notParticipating = new LordJobRoleGroup
            {
                Type = LordJobRoleGroup.Kind.NotParticipating,
                Headline = widget.NotParticipatingLabel(),
                MaxCount = pool.Count,
            };
            notParticipating.Pawns.AddRange(pool.Where(p => !assignments.PawnParticipating(p)));
            groups.Add(notParticipating);
            return groups;
        }

        public bool Required(Pawn pawn)
        {
            return assignments != null && assignments.Required(pawn);
        }

        public string GrayOutReason(Pawn pawn)
        {
            TaggedString reason;
            if (!widget.ShouldGrayOut(pawn, out reason)) return null;
            return reason.NullOrEmpty() ? null : reason.Resolve();
        }

        public string ExtraTipContents(Pawn pawn)
        {
            return ExtraTipContentsMethod?.Invoke(widget, new object[] { pawn }) as string;
        }

        public List<FloatMenuOption> BuildMoveOptions(Pawn pawn, Action afterChange)
        {
            var options = new List<FloatMenuOption>();
            if (assignments == null) return options;

            string reason = CannotAssignReason(pawn, null, out _, out _, false);
            if (assignments.SpectatorsAllowed)
            {
                Action assign = reason != null ? null : (Action)(() => assignments.TryAssignSpectate(pawn));
                options.Add(new FloatMenuOption(FloatLabel(widget.SpectatorsLabel(), reason, null), Wrap(assign, afterChange)));
            }
            foreach (IGrouping<string, RoleType> group in assignments.RoleGroups())
            {
                IGrouping<string, RoleType> localGroup = group;
                bool mustReplace;
                reason = CannotAssignReason(pawn, localGroup, out _, out mustReplace, true);
                Pawn replacing = mustReplace ? localGroup.SelectMany(r => assignments.AssignedPawns(r)).Last() : null;
                Action assign = reason != null ? null : (Action)(() =>
                {
                    if (mustReplace) TryAssignReplaceMethod.Invoke(widget, new object[] { pawn, localGroup, replacing });
                    else TryAssign(pawn, localGroup);
                });
                options.Add(new FloatMenuOption(FloatLabel(group.First().LabelCap, reason, replacing), Wrap(assign, afterChange)));
            }
            // The keyboard form of dragging a portrait onto the Not participating box.
            if (assignments.PawnParticipating(pawn))
            {
                Action remove = assignments.Required(pawn) ? null : (Action)(() =>
                {
                    assignments.RemoveParticipant(pawn);
                    SoundDefOf.DropElement.PlayOneShotOnCamera();
                });
                options.Add(new FloatMenuOption(widget.NotParticipatingLabel(), Wrap(remove, afterChange)));
            }
            return options;
        }

        public bool QuickMove(Pawn pawn, LordJobRoleGroup from)
        {
            if (assignments == null || from == null) return false;
            RoleType roleBefore = assignments.RoleForPawn(pawn);
            bool spectatingBefore = assignments.PawnSpectating(pawn);
            bool participatingBefore = assignments.PawnParticipating(pawn);

            switch (from.Type)
            {
                case LordJobRoleGroup.Kind.Role:
                    if (!assignments.Required(pawn))
                    {
                        if (!assignments.TryAssignSpectate(pawn)) assignments.RemoveParticipant(pawn);
                        SoundDefOf.Tick_High.PlayOneShotOnCamera();
                    }
                    break;
                case LordJobRoleGroup.Kind.Spectators:
                    if (!TryAssignAnyRole(pawn)) MessageAnyRoleReasons(pawn);
                    break;
                default:
                    string reason = CannotAssignReason(pawn, null, out _, out _, false);
                    if (reason != null)
                    {
                        if (!TryAssignAnyRole(pawn))
                            Messages.Message(reason, LookTargets.Invalid, MessageTypeDefOf.RejectInput, historical: false);
                    }
                    else
                    {
                        assignments.TryAssignSpectate(pawn);
                        SoundDefOf.Tick_High.PlayOneShotOnCamera();
                    }
                    break;
            }

            return !ReferenceEquals(roleBefore, assignments.RoleForPawn(pawn))
                || spectatingBefore != assignments.PawnSpectating(pawn)
                || participatingBefore != assignments.PawnParticipating(pawn);
        }

        public void Notify_AssignmentsChanged()
        {
            widget.Notify_AssignmentsChanged();
        }

        private string CannotAssignReason(Pawn pawn, IEnumerable<RoleType> roles, out RoleType firstRole, out bool mustReplace, bool isReplacing)
        {
            var args = new object[] { pawn, roles, null, false, isReplacing };
            string reason = CannotAssignReasonMethod?.Invoke(widget, args) as string;
            firstRole = args[2] as RoleType;
            mustReplace = args[3] is bool b && b;
            return reason;
        }

        private bool TryAssign(Pawn pawn, IEnumerable<RoleType> roles)
        {
            object result = TryAssignMethod?.Invoke(widget, new object[] { pawn, roles, true, null, true, false });
            return result is bool b && b;
        }

        /// <summary>Vanilla's any-role click reads the static group cache its draw pass fills; refill it so the call never sees a stale list.</summary>
        private bool TryAssignAnyRole(Pawn pawn)
        {
            if (RolesGroupedTmpField?.GetValue(null) is List<IGrouping<string, RoleType>> cache)
            {
                cache.Clear();
                cache.AddRange(assignments.RoleGroups());
            }
            object result = TryAssignAnyRoleMethod?.Invoke(widget, new object[] { pawn });
            return result is bool b && b;
        }

        /// <summary>Vanilla's any-role click messages its reason only for a single role group; with several it leaves the reasons to the tooltips.</summary>
        private void MessageAnyRoleReasons(Pawn pawn)
        {
            List<IGrouping<string, RoleType>> groups = assignments.RoleGroups().ToList();
            if (groups.Count == 1) return;
            List<string> reasons = groups
                .Select(g => CannotAssignReason(pawn, g, out _, out _, true))
                .Where(r => r != null)
                .Distinct()
                .ToList();
            if (reasons.Count > 0)
            {
                Messages.Message(string.Join(" ", reasons), LookTargets.Invalid, MessageTypeDefOf.RejectInput, historical: false);
            }
        }

        private static string FloatLabel(string label, string unavailableReason, Pawn replacing)
        {
            return PostProcessFloatLabelMethod?.Invoke(null, new object[] { label, unavailableReason, replacing }) as string ?? label;
        }

        private static Action Wrap(Action vanillaAction, Action afterChange)
        {
            if (vanillaAction == null) return null;
            return () =>
            {
                vanillaAction();
                afterChange?.Invoke();
            };
        }
    }
}
