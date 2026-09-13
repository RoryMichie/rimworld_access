using HarmonyLib;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Progression: Education (Progression: Therapy plugs its subjects into the same class
    /// system and needs no module of its own). The mod's plain dialogs are modal and ride the
    /// generic reader; what needs bespoke work is the Education main tab
    /// (<see cref="PeEducationTabScope"/>), the class dialogs' drag-and-drop role grid
    /// (<see cref="PeClassDialogScope"/>), and the Knowledge panel the mod transpiles into the
    /// character card (<see cref="PeProficiencyCard"/>).
    /// </summary>
    internal sealed class PeModule : CompatModule
    {
        public override string TargetPackageId => "ferny.ProgressionEducation";

        public override void Activate(Harmony harmony)
        {
            if (!PeCompat.Ready)
            {
                return;
            }

            ScopeForWindow.Register(PeCompat.MainTabType, delegate (Window w)
            {
                return new PeEducationTabScope(w);
            });
            ScopeForWindow.Register(PeCompat.DialogCreateType, delegate (Window w)
            {
                return new PeClassDialogScope(w);
            });
            ScopeForWindow.Register(PeCompat.DialogEditType, delegate (Window w)
            {
                return new PeClassDialogScope(w);
            });
            PeClassDialogAcceptGuardPatch.Install(harmony, PeCompat.DialogCreateType);
            PeClassDialogAcceptGuardPatch.Install(harmony, PeCompat.DialogEditType);

            if (PeProficiencyCard.Ready)
            {
                PeProficiencyCard.Register();
            }

            Log.Message("[RimWorld Access] Progression Education compat: education tab, class dialogs, knowledge panel");
        }
    }
}
