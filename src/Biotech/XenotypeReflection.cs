using System.Reflection;
using HarmonyLib;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Cached reflection handles into Dialog_CreateXenotype / GeneCreationDialogBase touched by
    /// more than one Xenotype-editor class, so XenotypeEditorState, XenotypeTreeBuilder,
    /// GeneConflictReader and XenogermState share one resolution.
    ///
    /// Deliberately NOT exhaustive: members with a single caller stay declared in that class.
    /// Accept/CanAccept in particular stay local to each dialog's own state class, resolved
    /// against its concrete type, because scripts/check_mutation_doctrine.py verifies the
    /// CanAccept pairing per file.
    /// </summary>
    internal static class XenotypeReflection
    {
        public static readonly FieldInfo XenotypeNameField;
        public static readonly FieldInfo XenotypeNameLockedField;
        public static readonly FieldInfo InheritableField;
        public static readonly FieldInfo IgnoreRestrictionsField;
        public static readonly FieldInfo IgnoreRestrictionsConfirmationSentField;
        public static readonly MethodInfo OnGenesChangedMethod;
        /// <summary>GeneCreationDialogBase.WithinAcceptableBiostatLimits(bool showMessage) — the biostat gate vanilla's load callbacks fold into ignoreRestrictions.</summary>
        public static readonly MethodInfo WithinAcceptableBiostatLimitsMethod;

        /// <summary>GeneCreationDialogBase.iconDef — shared with XenogermState (Dialog_CreateXenogerm).</summary>
        public static readonly FieldInfo IconDefField;
        /// <summary>GeneCreationDialogBase.gcx (current genetic complexity) — shared with XenogermState.</summary>
        public static readonly FieldInfo GcxField;
        /// <summary>GeneCreationDialogBase.met (net metabolism) — shared with XenogermState.</summary>
        public static readonly FieldInfo MetField;
        /// <summary>GeneCreationDialogBase.arc (archite capsules required) — shared with XenogermState.</summary>
        public static readonly FieldInfo ArcField;
        /// <summary>GeneCreationDialogBase.maxGCX (complexity cap, -1 if none) — shared with XenogermState.</summary>
        public static readonly FieldInfo MaxGCXField;

        /// <summary>Dialog_CreateXenotype.collapsedCategories — declared on the concrete dialog, not the base (Dialog_CreateXenogerm has no such state).</summary>
        public static readonly FieldInfo CollapsedCategoriesField;

        /// <summary>GeneCreationDialogBase's conflict caches — shared with GeneConflictReader.</summary>
        public static readonly FieldInfo LeftChosenGroupsField;
        public static readonly FieldInfo RandomChosenGroupsField;

        static XenotypeReflection()
        {
            XenotypeNameField = VanillaAccess.GetField(typeof(GeneCreationDialogBase), "xenotypeName");
            XenotypeNameLockedField = VanillaAccess.GetField(typeof(GeneCreationDialogBase), "xenotypeNameLocked");
            InheritableField = AccessTools.Field(typeof(Dialog_CreateXenotype), "inheritable");
            IgnoreRestrictionsField = VanillaAccess.GetField(typeof(GeneCreationDialogBase), "ignoreRestrictions");
            IgnoreRestrictionsConfirmationSentField = AccessTools.Field(typeof(Dialog_CreateXenotype), "ignoreRestrictionsConfirmationSent");
            OnGenesChangedMethod = VanillaAccess.GetMethod(typeof(GeneCreationDialogBase), "OnGenesChanged");
            WithinAcceptableBiostatLimitsMethod = VanillaAccess.GetMethod(typeof(GeneCreationDialogBase), "WithinAcceptableBiostatLimits");
            IconDefField = VanillaAccess.GetField(typeof(GeneCreationDialogBase), "iconDef");
            GcxField = VanillaAccess.GetField(typeof(GeneCreationDialogBase), "gcx");
            MetField = VanillaAccess.GetField(typeof(GeneCreationDialogBase), "met");
            ArcField = VanillaAccess.GetField(typeof(GeneCreationDialogBase), "arc");
            MaxGCXField = VanillaAccess.GetField(typeof(GeneCreationDialogBase), "maxGCX");
            CollapsedCategoriesField = VanillaAccess.GetField(typeof(Dialog_CreateXenotype), "collapsedCategories");
            LeftChosenGroupsField = VanillaAccess.GetField(typeof(GeneCreationDialogBase), "leftChosenGroups");
            RandomChosenGroupsField = VanillaAccess.GetField(typeof(GeneCreationDialogBase), "randomChosenGroups");
        }
    }
}
