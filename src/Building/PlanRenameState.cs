using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Modal text-edit session for renaming a <see cref="Plan"/>. Thin facade over
    /// <see cref="SimpleRenameSession{T}"/>, mirroring <see cref="ZoneRenameState"/>.
    /// </summary>
    public static class PlanRenameState
    {
        private static readonly SimpleRenameSession<Plan> Session = new SimpleRenameSession<Plan>(
            targetNoun: "plan",
            labelKey: "RimWorldAccess.TextInput.LabelPlan",
            errorKey: "RimWorldAccess.Building.Rename.PlanError",
            getLabel: plan => plan.RenamableLabel,
            setLabel: (plan, newName) => plan.RenamableLabel = newName);

        public static bool IsActive => Session.IsActive;

        public static void Open(Plan plan) => Session.Open(plan);
    }
}
