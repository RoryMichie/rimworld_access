using HarmonyLib;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    internal sealed class CmrModule : CompatModule
    {
        public override string TargetPackageId => "ilyvion.colonymanagerredux";

        public override bool ShouldActivate =>
            ModsConfig.IsActive(TargetPackageId)
            || AccessTools.TypeByName("ColonyManagerRedux.MainTabWindow_Manager") != null;

        public override void Activate(Harmony harmony)
        {
            if (CmrCompat.Ready)
            {
                ScopeForWindow.Register(CmrCompat.ManagerWindowType, w => new CmrManagerScope(w));
                ModLogger.Msg("Colony Manager Redux compat: registered the manager window");
            }

            // The threshold details window rides its own surface: a rename there must not cost the
            // manager window its screen, and the window is reachable from every threshold job rather
            // than from any one manager.
            if (CmrCompat.Threshold.Ready)
            {
                ScopeForWindow.Register(CmrCompat.Threshold.WindowType, CmrThresholdWindowScope.TryCreate);
                CmrThresholdWindowAcceptGuardPatch.Install(harmony, CmrCompat.Threshold.WindowType);
                ModLogger.Msg("Colony Manager Redux compat: registered the threshold details window");
            }

            if (CmrCompat.Livestock.Ready)
            {
                Shell.CmrLivestockColumnHandlers.InstallAll();
                ModLogger.Msg("Colony Manager Redux compat: registered the livestock animal-table columns");
            }

            if (CmrCompat.Overview.Ready)
            {
                Shell.CmrOverviewColumnHandlers.InstallAll();
                ModLogger.Msg("Colony Manager Redux compat: registered the overview worker-table columns");
            }

            // The import window rides the Import/Export surface: it only ever opens from that tab,
            // and its rows read the same job fields.
            if (CmrCompat.ImportExport.Ready && CmrCompat.ImportExport.ImportDialogType != null)
            {
                ScopeForWindow.Register(CmrCompat.ImportExport.ImportDialogType,
                    w => new CmrImportJobsScope(w));
                ModLogger.Msg("Colony Manager Redux compat: registered the import-jobs window");
            }
        }
    }
}
