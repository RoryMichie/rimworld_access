using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace RimWorldAccess
{
    internal static partial class CmrCompat
    {
        /// <summary>
        /// Colony Manager Redux's Import/Export tab and its <c>Dialog_ImportJobs</c> mismatch window.
        /// <c>SaveFileInfo</c> and <c>MultiCheckboxState</c> are vanilla <c>Verse</c> types the tab and
        /// dialog hold in plain public-element lists, so those two lists are cast directly with no
        /// reflection on their element type.
        /// Mutation vehicles: the mod's own gated members are invoked directly — <see cref="SetMode"/>
        /// (self-gating on no-change), <see cref="TryImport"/>/<see cref="TryExport"/> (each running
        /// the mod's own confirmation and messages), and the settings singleton's
        /// <c>DefaultTemplateName</c> property. <see cref="SetSaveName"/>,
        /// <see cref="SetExportJobState"/>, <see cref="SetDialogJobState"/> and
        /// <see cref="DeleteFile"/> are MUTATION-C, each documented at its own member.
        /// </summary>
        internal static class ImportExport
        {
            private static readonly Type tabType;
            private static readonly Type dialogType;
            private static readonly Type settingsHostType;
            private static readonly Type settingsType;
            private static readonly Type managerJobType;

            private static readonly FieldInfo templateModeField;
            private static readonly MethodInfo setModeMethod;
            private static readonly FieldInfo saveFilesField;
            private static readonly FieldInfo saveNameField;
            private static readonly FieldInfo jobsField;
            private static readonly FieldInfo selectedJobsField;
            private static readonly MethodInfo tryImportMethod;
            private static readonly MethodInfo tryExportMethod;
            private static readonly MethodInfo refreshMethod;

            private static readonly MethodInfo settingsGetter;
            private static readonly MethodInfo defaultTemplateNameGetter;
            private static readonly MethodInfo defaultTemplateNameSetter;

            private static readonly FieldInfo dialogJobsField;
            private static readonly FieldInfo dialogSelectedJobsField;

            private static readonly MethodInfo jobIsValidGetter;

            private static readonly bool ready;

            static ImportExport()
            {
                var surface = new ReflectionSurface("CmrCompat.ImportExport");

                tabType = surface.Type("ColonyManagerRedux.Managers.ManagerTab_ImportExport");
                dialogType = surface.Type("ColonyManagerRedux.Managers.Dialog_ImportJobs");
                settingsHostType = surface.Type("ColonyManagerRedux.ColonyManagerReduxMod");
                settingsType = surface.Type("ColonyManagerRedux.Settings");
                managerJobType = surface.Type("ColonyManagerRedux.ManagerJob");

                templateModeField = surface.Field(tabType, "_templateMode");
                setModeMethod = surface.Method(tabType, "SetMode", new[] { typeof(bool) });
                saveFilesField = surface.Field(tabType, "_saveFiles");
                saveNameField = surface.Field(tabType, "_saveName");
                jobsField = surface.Field(tabType, "_jobs");
                selectedJobsField = surface.Field(tabType, "_selectedJobs");
                tryImportMethod = surface.Method(tabType, "TryImport", new[] { typeof(SaveFileInfo) });
                tryExportMethod = surface.Method(tabType, "TryExport", new[] { typeof(string) });
                refreshMethod = surface.Method(tabType, "Refresh", new Type[0]);

                settingsGetter = Getter(surface.Property(settingsHostType, "Settings"));
                PropertyInfo defaultTemplateNameProperty = surface.Property(settingsType, "DefaultTemplateName");
                defaultTemplateNameGetter = Getter(defaultTemplateNameProperty);
                defaultTemplateNameSetter = surface.Required("Settings.DefaultTemplateName setter",
                    Setter(defaultTemplateNameProperty));

                dialogJobsField = surface.Field(dialogType, "_jobs");
                dialogSelectedJobsField = surface.Field(dialogType, "_selectedJobs");

                jobIsValidGetter = Getter(surface.Property(managerJobType, "IsValid"));

                ready = surface.Ready;
            }

            /// <summary>True when every member the Import/Export detail rows read or write resolved.</summary>
            public static bool Ready
            {
                get { return ready; }
            }

            /// <summary>Whether this tab is the mod's Import/Export tab, by the type resolved above.</summary>
            public static bool HandlesTab(object tab)
            {
                return ready && tab != null && tabType != null && tabType.IsInstanceOfType(tab);
            }

            // Mode.

            public static bool TemplateMode(object tab)
            {
                return FieldFlag(templateModeField, tab, "TemplateMode");
            }

            /// <summary>The mode buttons' own click body (ManagerTab_ImportExport.cs:85-102): self-gates on no-change, refreshes the folder and file list itself.</summary>
            public static void SetMode(object tab, bool templateMode)
            {
                if (!ready || tab == null || setModeMethod == null)
                {
                    return;
                }
                try
                {
                    setModeMethod.Invoke(tab, new object[] { templateMode });
                }
                catch (Exception ex)
                {
                    Fail("SetMode", ex);
                }
            }

            // Load section.

            /// <summary>The tab's own saved-file list, newest write first (ManagerTab_ImportExport.cs:619-623).</summary>
            public static List<SaveFileInfo> SaveFiles(object tab)
            {
                var files = new List<SaveFileInfo>();
                var list = FieldValue(saveFilesField, tab, "SaveFiles") as IEnumerable<SaveFileInfo>;
                if (list == null)
                {
                    return files;
                }
                try
                {
                    files.AddRange(list);
                }
                catch (Exception ex)
                {
                    Fail("SaveFiles", ex);
                }
                return files;
            }

            /// <summary>The load button's own body (ManagerTab_ImportExport.cs:362-365): runs the mod's own mismatch confirmation and may open its Dialog_ImportJobs.</summary>
            public static void TryImport(object tab, SaveFileInfo file)
            {
                if (!ready || tab == null || file == null || tryImportMethod == null)
                {
                    return;
                }
                try
                {
                    tryImportMethod.Invoke(tab, new object[] { file });
                }
                catch (Exception ex)
                {
                    Fail("TryImport", ex);
                }
            }

            /// <summary>
            /// MUTATION-C: reproduces the delete button's own inline click body statement for
            /// statement (ManagerTab_ImportExport.cs:387-411): a vanilla confirm dialog whose accept
            /// action clears the default-template name when this file was the default, deletes the
            /// file, and refreshes the tab's own list. No single vanilla method wraps this sequence.
            /// </summary>
            public static void DeleteFile(object tab, SaveFileInfo file)
            {
                if (!ready || tab == null || file == null)
                {
                    return;
                }
                string name = System.IO.Path.GetFileNameWithoutExtension(file.FileInfo.Name);
                bool isDefaultTemplate = TemplateMode(tab) && DefaultTemplateName() == name;
                Find.WindowStack.Add(new Dialog_Confirm(
                    "ConfirmDelete".Translate(file.FileInfo.Name),
                    delegate
                    {
                        if (isDefaultTemplate)
                        {
                            SetDefaultTemplateName(null);
                        }
                        try
                        {
                            file.FileInfo.Delete();
                        }
                        catch (Exception ex)
                        {
                            Fail("DeleteFile", ex);
                        }
                        if (refreshMethod != null)
                        {
                            try
                            {
                                refreshMethod.Invoke(tab, null);
                            }
                            catch (Exception ex)
                            {
                                Fail("DeleteFile", ex);
                            }
                        }
                    }));
            }

            // Save section.

            public static string SaveName(object tab)
            {
                return FieldValue(saveNameField, tab, "SaveName") as string ?? "";
            }

            /// <summary>
            /// MUTATION-C: mirrors the name field's own write-back (ManagerTab_ImportExport.cs:483-487)
            /// -- a private field with no setter. Callers pass a name that already passed
            /// <c>GenText.IsValidFilename</c>, the same gate the field's own draw call applies.
            /// </summary>
            public static void SetSaveName(object tab, string name)
            {
                SetField(saveNameField, tab, name, "SetSaveName");
            }

            /// <summary>The tab's own export list, in the mod's own order (ManagerTab_ImportExport.cs:41).</summary>
            public static List<object> ExportJobs(object tab)
            {
                var jobs = new List<object>();
                var list = FieldValue(jobsField, tab, "ExportJobs") as IEnumerable;
                if (list == null)
                {
                    return jobs;
                }
                try
                {
                    foreach (object item in list)
                    {
                        if (item != null)
                        {
                            jobs.Add(item);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Fail("ExportJobs", ex);
                }
                return jobs;
            }

            public static MultiCheckboxState ExportJobState(object tab, int index)
            {
                var states = FieldValue(selectedJobsField, tab, "ExportJobState") as List<MultiCheckboxState>;
                return states != null && index >= 0 && index < states.Count
                    ? states[index]
                    : MultiCheckboxState.Off;
            }

            /// <summary>
            /// MUTATION-C: mirrors the export list's own <c>CheckboxMulti</c> write-back into
            /// <c>_selectedJobs</c> (ManagerTab_ImportExport.cs:531-535); IMGUI list state with no
            /// setter of its own.
            /// </summary>
            public static void SetExportJobState(object tab, int index, MultiCheckboxState state)
            {
                var states = FieldValue(selectedJobsField, tab, "SetExportJobState") as List<MultiCheckboxState>;
                if (states == null || index < 0 || index >= states.Count)
                {
                    return;
                }
                try
                {
                    states[index] = state;
                }
                catch (Exception ex)
                {
                    Fail("SetExportJobState", ex);
                }
            }

            /// <summary>The export button's own body (ManagerTab_ImportExport.cs:489-503): runs the mod's own overwrite confirmation and completion message.</summary>
            public static void TryExport(object tab)
            {
                if (!ready || tab == null || tryExportMethod == null)
                {
                    return;
                }
                try
                {
                    tryExportMethod.Invoke(tab, new object[] { SaveName(tab) });
                }
                catch (Exception ex)
                {
                    Fail("TryExport", ex);
                }
            }

            // Default template name — the mod's settings singleton.

            public static string DefaultTemplateName()
            {
                object settings = Get(settingsGetter, null, "DefaultTemplateName");
                return settings == null ? null : Get(defaultTemplateNameGetter, settings, "DefaultTemplateName") as string;
            }

            /// <summary>The set-default button's own body (ManagerTab_ImportExport.cs:378) -- the property's own setter.</summary>
            public static void SetDefaultTemplateName(string name)
            {
                object settings = Get(settingsGetter, null, "SetDefaultTemplateName");
                if (settings == null || defaultTemplateNameSetter == null)
                {
                    return;
                }
                try
                {
                    defaultTemplateNameSetter.Invoke(settings, new object[] { name });
                }
                catch (Exception ex)
                {
                    Fail("SetDefaultTemplateName", ex);
                }
            }

            // Dialog_ImportJobs, for CmrImportJobsScope.

            /// <summary>The resolved Dialog_ImportJobs type, for ScopeForWindow registration. Null on decline.</summary>
            public static Type ImportDialogType
            {
                get { return dialogType; }
            }

            /// <summary>The dialog's own job list (Dialog_ImportJobs.cs:13).</summary>
            public static List<object> DialogJobs(object window)
            {
                var jobs = new List<object>();
                var list = FieldValue(dialogJobsField, window, "DialogJobs") as IEnumerable;
                if (list == null)
                {
                    return jobs;
                }
                try
                {
                    foreach (object item in list)
                    {
                        if (item != null)
                        {
                            jobs.Add(item);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Fail("DialogJobs", ex);
                }
                return jobs;
            }

            public static MultiCheckboxState DialogJobState(object window, int index)
            {
                var states = FieldValue(dialogSelectedJobsField, window, "DialogJobState") as List<MultiCheckboxState>;
                return states != null && index >= 0 && index < states.Count
                    ? states[index]
                    : MultiCheckboxState.Off;
            }

            /// <summary>
            /// MUTATION-C: mirrors the dialog's own <c>CheckboxMulti</c> write-back into
            /// <c>_selectedJobs</c> (Dialog_ImportJobs.cs:79-84); IMGUI list state with no setter of
            /// its own.
            /// </summary>
            public static void SetDialogJobState(object window, int index, MultiCheckboxState state)
            {
                var states = FieldValue(dialogSelectedJobsField, window, "SetDialogJobState") as List<MultiCheckboxState>;
                if (states == null || index < 0 || index >= states.Count)
                {
                    return;
                }
                try
                {
                    states[index] = state;
                }
                catch (Exception ex)
                {
                    Fail("SetDialogJobState", ex);
                }
            }

            /// <summary>The public <c>ManagerJob.IsValid</c> the dialog's own rows gate a raw draw attempt on (Dialog_ImportJobs.cs:49).</summary>
            public static bool JobIsValid(object job)
            {
                return GetBool(jobIsValidGetter, job, "JobIsValid");
            }

            // Plumbing. Gated on this block's own Ready.

            private static MethodInfo Getter(PropertyInfo property)
            {
                return property != null ? property.GetGetMethod(true) : null;
            }

            private static MethodInfo Setter(PropertyInfo property)
            {
                return property != null ? property.GetSetMethod(true) : null;
            }

            private static object Get(MethodInfo getter, object instance, string member)
            {
                if (!ready || getter == null || (instance == null && !getter.IsStatic))
                {
                    return null;
                }
                try
                {
                    return getter.Invoke(instance, null);
                }
                catch (Exception ex)
                {
                    Fail(member, ex);
                    return null;
                }
            }

            private static bool GetBool(MethodInfo getter, object instance, string member)
            {
                return Get(getter, instance, member) is bool value && value;
            }

            private static object FieldValue(FieldInfo field, object instance, string member)
            {
                if (!ready || field == null || instance == null)
                {
                    return null;
                }
                try
                {
                    return field.GetValue(instance);
                }
                catch (Exception ex)
                {
                    Fail(member, ex);
                    return null;
                }
            }

            private static bool FieldFlag(FieldInfo field, object instance, string member)
            {
                return FieldValue(field, instance, member) is bool value && value;
            }

            private static void SetField(FieldInfo field, object instance, object value, string member)
            {
                if (!ready || field == null || instance == null)
                {
                    return;
                }
                try
                {
                    // MUTATION-C: the write the mod's own IMGUI field control performs through its
                    // own write-back -- see this class's remarks and SetSaveName's citation.
                    field.SetValue(instance, value);
                }
                catch (Exception ex)
                {
                    Fail(member, ex);
                }
            }

            private static readonly HashSet<string> loggedFailures = new HashSet<string>();

            /// <summary>Reports a reflection call that threw, once per member for the session.</summary>
            private static void Fail(string member, Exception ex)
            {
                if (loggedFailures.Add(member))
                {
                    ModLogger.Error("CmrCompat.ImportExport." + member + " failed: " + ex.Message);
                }
            }
        }
    }
}
