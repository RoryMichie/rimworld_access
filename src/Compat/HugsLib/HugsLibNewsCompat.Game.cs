using System;
using System.Reflection;
using HarmonyLib;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Reflection glue for HugsLib's update-news dialog (<c>HugsLib.News.Dialog_UpdateFeatures</c>
    /// and its internal subclass <c>Dialog_UpdateFeaturesFiltered</c>, the one HugsLib actually
    /// opens). Shipped-DLL drift is real here, so the probed member list is authoritative over the
    /// on-disk mod source.
    /// </summary>
    internal static class HugsLibNewsCompat
    {
        internal static bool Ready { get; private set; }

        internal static Type DialogType;
        internal static Type FilteredType;
        internal static Type FeatureEntryType;
        internal static Type DescriptionSegmentType;
        internal static Type UpdateFeatureDefType;
        internal static Type IgnoredNewsIdsType;
        internal static Type DialogConfirmType;

        internal static FieldInfo EntriesField;                 // Dialog_UpdateFeatures.entries : List<FeatureEntry>
        internal static FieldInfo IgnoredNewsProvidersField;     // Dialog_UpdateFeatures.ignoredNewsProviders
        internal static FieldInfo FeDefField;                    // FeatureEntry.def : UpdateFeatureDef
        internal static FieldInfo FeSegmentsField;                // FeatureEntry.segments : List<DescriptionSegment>
        internal static FieldInfo SegTypeField;                  // DescriptionSegment.type (SegmentType enum)
        internal static FieldInfo SegTextField;                  // DescriptionSegment.text : string

        internal static PropertyInfo DefOwningModIdProperty;      // UpdateFeatureDef.OwningModId (modIdentifier, else modContentPack.PackageId)
        internal static FieldInfo DefModIdentifierField;
        internal static FieldInfo DefModNameReadableField;
        internal static FieldInfo DefTitleOverrideField;
        internal static FieldInfo DefAssemblyVersionField;
        internal static FieldInfo DefLinkUrlField;

        internal static MethodInfo ContainsMethod;               // IgnoredNewsIds.Contains(string) : bool
        internal static MethodInfo SetIgnoredMethod;              // IgnoredNewsIds.SetIgnored(string, bool)

        internal static FieldInfo DefFilterField;                 // Dialog_UpdateFeaturesFiltered.defFilter
        internal static PropertyInfo CurrentFilterModNameReadableProperty; // UpdateFeatureDefFilteringProvider.CurrentFilterModNameReadable
        internal static FieldInfo AllModsFilterLabelField;         // Dialog_UpdateFeaturesFiltered.allModsFilterLabel
        internal static MethodInfo ShowFilterOptionsMenuMethod;    // Dialog_UpdateFeaturesFiltered.ShowFilterOptionsMenu()

        internal static ConstructorInfo DialogConfirmCtor;         // Dialog_Confirm(string, Action, bool, string)

        public static void TryRegister()
        {
            try
            {
                var surface = new ReflectionSurface("HugsLibNewsCompat");
                DialogType = surface.Type("HugsLib.News.Dialog_UpdateFeatures");
                UpdateFeatureDefType = surface.Type("HugsLib.UpdateFeatureDef");
                DialogConfirmType = surface.Type("HugsLib.Utils.Dialog_Confirm");

                FeatureEntryType = surface.Supplied("Dialog_UpdateFeatures+FeatureEntry",
                    DialogType != null ? AccessTools.Inner(DialogType, "FeatureEntry") : null);
                DescriptionSegmentType = surface.Supplied("Dialog_UpdateFeatures+DescriptionSegment",
                    DialogType != null ? AccessTools.Inner(DialogType, "DescriptionSegment") : null);

                EntriesField = surface.Field(DialogType, "entries");
                IgnoredNewsProvidersField = surface.Field(DialogType, "ignoredNewsProviders");
                IgnoredNewsIdsType = IgnoredNewsProvidersField?.FieldType;

                FeDefField = surface.Field(FeatureEntryType, "def");
                FeSegmentsField = surface.Field(FeatureEntryType, "segments");
                SegTypeField = surface.Field(DescriptionSegmentType, "type");
                SegTextField = surface.Field(DescriptionSegmentType, "text");

                // Version-optional: the scope falls back to the modIdentifier field when
                // the convenience property is absent.
                DefOwningModIdProperty = UpdateFeatureDefType != null
                    ? AccessTools.Property(UpdateFeatureDefType, "OwningModId")
                    : null;

                DefModIdentifierField = surface.Field(UpdateFeatureDefType, "modIdentifier");
                DefModNameReadableField = surface.Field(UpdateFeatureDefType, "modNameReadable");
                DefTitleOverrideField = surface.Field(UpdateFeatureDefType, "titleOverride");
                DefAssemblyVersionField = surface.Field(UpdateFeatureDefType, "assemblyVersion");
                DefLinkUrlField = surface.Field(UpdateFeatureDefType, "linkUrl");

                ContainsMethod = surface.Method(IgnoredNewsIdsType, "Contains", new[] { typeof(string) });
                SetIgnoredMethod = surface.Method(IgnoredNewsIdsType, "SetIgnored", new[] { typeof(string), typeof(bool) });

                // Version-optional subclass; where it exists, its members are required.
                FilteredType = AccessTools.TypeByName("HugsLib.News.Dialog_UpdateFeaturesFiltered");
                if (FilteredType != null)
                {
                    DefFilterField = surface.Field(FilteredType, "defFilter");
                    CurrentFilterModNameReadableProperty = surface.Property(DefFilterField?.FieldType, "CurrentFilterModNameReadable");
                    AllModsFilterLabelField = surface.Field(FilteredType, "allModsFilterLabel");
                    ShowFilterOptionsMenuMethod = surface.Method(FilteredType, "ShowFilterOptionsMenu");
                }

                DialogConfirmCtor = DialogConfirmType != null
                    ? AccessTools.Constructor(DialogConfirmType, new[] { typeof(string), typeof(Action), typeof(bool), typeof(string) })
                    : null;

                Ready = surface.Ready && DialogConfirmCtor != null;
                if (!Ready)
                {
                    if (surface.Ready)
                    {
                        ModLogger.Error("HugsLibNewsCompat: Dialog_Confirm's (string, Action, bool, string) constructor " +
                            "did not resolve; declining the update-news scope.");
                    }
                    return;
                }

                // RegisterHierarchy's IsAssignableFrom test matches the base type itself as well
                // as every subclass, so one factory serves Dialog_UpdateFeaturesFiltered too;
                // HugsLibNewsScope tells them apart at construction via FilteredType.
                ScopeForWindow.RegisterHierarchy(DialogType, delegate (Window w) { return new HugsLibNewsScope(w); });

                ModLogger.Msg("HugsLib compat: registered update-news scope");
            }
            catch (Exception ex)
            {
                ModLogger.Error("HugsLib update-news scope registration failed: " + ex.Message);
            }
        }
    }
}
