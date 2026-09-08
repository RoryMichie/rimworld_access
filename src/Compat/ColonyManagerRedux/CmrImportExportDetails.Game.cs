using System;
using System.Collections.Generic;
using System.IO;
using Verse;
using static RimWorldAccess.Shell.CompatText;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The Import/Export tab's detail rows. Three regions -- Mode, Saved files, Export -- ARE the
    /// whole tab: the lead's scope suppresses the generic Jobs region and captured buttons here,
    /// since every control the mod draws on this tab lives inside one of these three sections
    /// (<c>ManagerTab_ImportExport.DoTabContents</c>).
    /// </summary>
    internal sealed class CmrImportExportDetails : ICmrJobDetailsProvider
    {
        public bool Handles(object tab)
        {
            return CmrCompat.ImportExport.HandlesTab(tab);
        }

        public List<CmrDetailRegion> Build(object tab, object job)
        {
            var regions = new List<CmrDetailRegion>();
            if (tab == null || !CmrCompat.ImportExport.Ready)
            {
                return regions;
            }
            regions.Add(BuildMode(tab));
            regions.Add(BuildFiles(tab));
            regions.Add(BuildExport(tab));
            return regions;
        }

        // ------------------------------------------------------------------
        // Mode: job saves vs. templates.
        // ------------------------------------------------------------------

        private static CmrDetailRegion BuildMode(object tab)
        {
            var region = new CmrDetailRegion("RimWorldAccess.Cmr.ImportExport.ModeRegion".Translate());
            region.Rows.Add(new CmrDetailRow
            {
                Label = ModText("ColonyManagerRedux.ManagerImportExport.JobSaves"),
                Role = ElementRole.RadioButton,
                Selected = () => !CmrCompat.ImportExport.TemplateMode(tab),
                Activate = delegate
                {
                    if (CmrCompat.ImportExport.TemplateMode(tab))
                    {
                        CmrCompat.ImportExport.SetMode(tab, false);
                    }
                },
            });
            region.Rows.Add(new CmrDetailRow
            {
                Label = ModText("ColonyManagerRedux.ManagerImportExport.Templates"),
                Role = ElementRole.RadioButton,
                Selected = () => CmrCompat.ImportExport.TemplateMode(tab),
                Activate = delegate
                {
                    if (!CmrCompat.ImportExport.TemplateMode(tab))
                    {
                        CmrCompat.ImportExport.SetMode(tab, true);
                    }
                },
            });
            return region;
        }

        // ------------------------------------------------------------------
        // Saved files: the load section.
        // ------------------------------------------------------------------

        private static CmrDetailRegion BuildFiles(object tab)
        {
            var region = new CmrDetailRegion("RimWorldAccess.Cmr.ImportExport.FilesRegion".Translate());
            List<SaveFileInfo> files = CmrCompat.ImportExport.SaveFiles(tab);
            if (files.Count == 0)
            {
                region.Rows.Add(new CmrDetailRow
                {
                    Label = ModText("ColonyManagerRedux.ManagerNoSaves"),
                    Role = ElementRole.None,
                });
                return region;
            }

            bool templateMode = CmrCompat.ImportExport.TemplateMode(tab);
            for (int i = 0; i < files.Count; i++)
            {
                SaveFileInfo file = files[i];
                string name = Path.GetFileNameWithoutExtension(file.FileInfo.Name);
                AddFileRow(region, tab, file, name, templateMode);
                if (templateMode)
                {
                    AddSetDefaultRow(region, name);
                }
                AddDeleteRow(region, tab, file, name);
            }
            return region;
        }

        /// <summary>The load button's own label as this row's tooltip -- what Enter here does -- plus the vanilla date-and-version zone the row draws (Dialog_FileList.DrawDateAndVersion, ManagerTab_ImportExport.cs:355-359) as the row's value.</summary>
        private static void AddFileRow(CmrDetailRegion region, object tab, SaveFileInfo file, string name,
            bool templateMode)
        {
            region.Rows.Add(new CmrDetailRow
            {
                Label = name,
                Role = ElementRole.MenuItem,
                Value = () => FileDateAndVersion(file),
                Tooltip = ModTip(templateMode
                    ? "ColonyManagerRedux.ManagerApplyTemplate"
                    : "ColonyManagerRedux.ManagerImport"),
                OpensWindow = true,
                Activate = () => CmrCompat.ImportExport.TryImport(tab, file),
            });
        }

        private static string FileDateAndVersion(SaveFileInfo file)
        {
            string version = Flatten(file.GameVersion);
            string date = file.LastWriteTime.ToString("yyyy-MM-dd HH:mm");
            return string.IsNullOrEmpty(version) ? date : date + ", " + version;
        }

        /// <summary>Template mode only: sets this file as the default template, greyed (Activate null) when it already is (ManagerTab_ImportExport.cs:367-384).</summary>
        private static void AddSetDefaultRow(CmrDetailRegion region, string name)
        {
            var row = new CmrDetailRow
            {
                Label = "RimWorldAccess.Cmr.ImportExport.SetDefaultRow".Translate(name),
                Role = ElementRole.RadioButton,
                Selected = () => CmrCompat.ImportExport.DefaultTemplateName() == name,
                Tooltip = () => TooltipForDefaultRow(CmrCompat.ImportExport.DefaultTemplateName() == name),
            };
            if (CmrCompat.ImportExport.DefaultTemplateName() != name)
            {
                row.Activate = () => CmrCompat.ImportExport.SetDefaultTemplateName(name);
            }
            region.Rows.Add(row);
        }

        private static string TooltipForDefaultRow(bool isDefault)
        {
            var parts = new List<string> { ModText("ColonyManagerRedux.ManagerSetTemplateAsDefault.Tip") };
            if (isDefault)
            {
                parts.Add(ModText("ColonyManagerRedux.ManagerTemplateIsDefault"));
            }
            return CompatText.JoinSentences(parts);
        }

        private static void AddDeleteRow(CmrDetailRegion region, object tab, SaveFileInfo file, string name)
        {
            region.Rows.Add(new CmrDetailRow
            {
                Label = "RimWorldAccess.Cmr.ImportExport.DeleteRow".Translate(name),
                Role = ElementRole.Button,
                Tooltip = ModTip("ColonyManagerRedux.DeleteThisManagerFile"),
                OpensWindow = true,
                Activate = () => CmrCompat.ImportExport.DeleteFile(tab, file),
            });
        }

        // ------------------------------------------------------------------
        // Export: the save section.
        // ------------------------------------------------------------------

        private static CmrDetailRegion BuildExport(object tab)
        {
            var region = new CmrDetailRegion("RimWorldAccess.Cmr.ImportExport.ExportRegion".Translate());
            bool templateMode = CmrCompat.ImportExport.TemplateMode(tab);

            region.Rows.Add(new CmrDetailRow
            {
                Label = Flatten(ModText(templateMode
                    ? "ColonyManagerRedux.SelectTemplateJobs"
                    : "ColonyManagerRedux.SelectExportJobs")),
                Role = ElementRole.None,
            });

            List<object> jobs = CmrCompat.ImportExport.ExportJobs(tab);
            for (int i = 0; i < jobs.Count; i++)
            {
                region.Rows.Add(ExportJobRow(tab, jobs[i], i));
            }

            AddNameRow(region, tab);
            AddExportRow(region, tab, jobs.Count);
            return region;
        }

        private static CmrDetailRow ExportJobRow(object tab, object job, int index)
        {
            return new CmrDetailRow
            {
                Label = Flatten(CmrCompat.JobLabel(job)),
                Value = () => Flatten(CmrCompat.TabGetSubLabel(CmrCompat.JobTab(job), job)),
                Tooltip = () => "RimWorldAccess.Cmr.ChecksInterval"
                    .Translate(CmrCompat.JobUpdateIntervalLabel(job)).ToString(),
                Role = ElementRole.Checkbox,
                Check = () => CheckStateFor(CmrCompat.ImportExport.ExportJobState(tab, index)),
                // The vanilla CheckboxMulti click cycle (Widgets.cs CheckboxMulti): partial or off
                // becomes on, on becomes off.
                Activate = () => CmrCompat.ImportExport.SetExportJobState(tab, index,
                    CmrCompat.ImportExport.ExportJobState(tab, index) != MultiCheckboxState.Off
                        ? MultiCheckboxState.Off
                        : MultiCheckboxState.On),
                SetChecked = value => CmrCompat.ImportExport.SetExportJobState(tab, index,
                    value ? MultiCheckboxState.On : MultiCheckboxState.Off),
            };
        }

        private static CheckState CheckStateFor(MultiCheckboxState state)
        {
            if (state == MultiCheckboxState.On)
            {
                return CheckState.Checked;
            }
            return state == MultiCheckboxState.Off ? CheckState.Unchecked : CheckState.PartiallyChecked;
        }

        private static void AddNameRow(CmrDetailRegion region, object tab)
        {
            region.Rows.Add(new CmrDetailRow
            {
                Label = "RimWorldAccess.Cmr.ImportExport.FileNameRow".Translate(),
                Role = ElementRole.TextField,
                Value = () => CmrCompat.ImportExport.SaveName(tab),
                Text = new CmrTextSpec
                {
                    Current = () => CmrCompat.ImportExport.SaveName(tab),
                    Apply = value => CmrCompat.ImportExport.SetSaveName(tab, value),
                },
            });
        }

        private static void AddExportRow(CmrDetailRegion region, object tab, int jobCount)
        {
            bool templateMode = CmrCompat.ImportExport.TemplateMode(tab);
            bool anySelected = AnySelected(tab, jobCount);
            var row = new CmrDetailRow
            {
                Label = ModText(templateMode ? "ColonyManagerRedux.SaveAsTemplate" : "ColonyManagerRedux.ManagerExport"),
                Role = ElementRole.Button,
            };
            if (anySelected)
            {
                row.OpensWindow = true;
                row.Activate = () => CmrCompat.ImportExport.TryExport(tab);
            }
            else
            {
                row.Tooltip = () => "RimWorldAccess.Cmr.ImportExport.ExportNeedsSelection".Translate().ToString();
            }
            region.Rows.Add(row);
        }

        private static bool AnySelected(object tab, int jobCount)
        {
            for (int i = 0; i < jobCount; i++)
            {
                if (CmrCompat.ImportExport.ExportJobState(tab, i) != MultiCheckboxState.Off)
                {
                    return true;
                }
            }
            return false;
        }

    }
}
