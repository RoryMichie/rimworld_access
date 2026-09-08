using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Compat for Allow Tool.
    ///
    /// The mod's Designator_UnlimitedDragger family does not designate cells: its
    /// CanDesignateCell returns false unconditionally, and the real work reads a rectangle
    /// off a private UnlimitedAreaDragger that only a physical mouse drag ever fills in.
    /// This surface exposes exactly the members a keyboard rectangle needs in order to
    /// arrive at the mod's own entry points looking like a mouse drag; see
    /// <see cref="AllowToolRectDesignationHandler"/> for the sequence.
    /// </summary>
    internal static class AllowToolCompat
    {
        internal static Type DraggerOwnerType;
        internal static Type SelectSimilarType;
        internal static PropertyInfo DraggerProperty;
        internal static FieldInfo SelectedAreaField;
        internal static FieldInfo SelectionInProgressField;
        internal static FieldInfo SelectionStartCellField;
        internal static FieldInfo SelectionStartField;
        internal static FieldInfo SelectionChangedField;
        internal static FieldInfo SelectionCompleteField;

        internal static readonly LazyReflectionGate RectDesignationGate =
            new LazyReflectionGate("AllowTool rect designation", surface =>
            {
                Type ownerType = surface.Type("AllowTool.Designator_UnlimitedDragger");
                Type selectSimilarType = surface.Type("AllowTool.Designator_SelectSimilar");
                Type draggerType = surface.Type("AllowTool.UnlimitedAreaDragger");

                PropertyInfo draggerProperty = surface.Property(ownerType, "Dragger");
                FieldInfo selectedArea = ResolveField(surface, draggerType, "SelectedArea");
                FieldInfo inProgress = ResolveField(surface, draggerType, "SelectionInProgress");
                FieldInfo startCell = ResolveField(surface, draggerType, "SelectionStartCell");
                FieldInfo selectionStart = ResolveField(surface, draggerType, "SelectionStart");
                FieldInfo selectionChanged = ResolveField(surface, draggerType, "SelectionChanged");
                FieldInfo selectionComplete = ResolveField(surface, draggerType, "SelectionComplete");

                if (!surface.Ready)
                    return false;

                DraggerOwnerType = ownerType;
                SelectSimilarType = selectSimilarType;
                DraggerProperty = draggerProperty;
                SelectedAreaField = selectedArea;
                SelectionInProgressField = inProgress;
                SelectionStartCellField = startCell;
                SelectionStartField = selectionStart;
                SelectionChangedField = selectionChanged;
                SelectionCompleteField = selectionComplete;
                return true;
            });

        internal static MethodInfo GetMenuProviderMethod;
        internal static PropertyInfo HasCustomEnabledEntriesProperty;
        internal static MethodInfo OpenContextMenuMethod;

        /// <summary>
        /// Kept separate from <see cref="RectDesignationGate"/> so a shape change in one
        /// half of the mod's surface does not disable the other.
        /// </summary>
        internal static readonly LazyReflectionGate ContextMenuGate =
            new LazyReflectionGate("AllowTool context menus", surface =>
            {
                Type controllerType = surface.Type("AllowTool.Context.DesignatorContextMenuController");
                Type providerType = surface.Type("AllowTool.Context.ContextMenuProvider");

                MethodInfo getProvider = surface.Method(controllerType, "GetMenuProviderForDesignator",
                    new[] { typeof(Designator) });
                PropertyInfo hasEntries = surface.Property(providerType, "HasCustomEnabledEntries");
                MethodInfo openMenu = surface.Method(providerType, "OpenContextMenu",
                    new[] { typeof(Designator) });

                if (!surface.Ready)
                    return false;

                GetMenuProviderMethod = getProvider;
                HasCustomEnabledEntriesProperty = hasEntries;
                OpenContextMenuMethod = openMenu;
                return true;
            });

        public static void RegisterRectDesignationHandler()
        {
            try
            {
                RectDesignationRouter.Register(new AllowToolRectDesignationHandler());
                ModLogger.Msg("Allow Tool compat: registered rect designation handler");
            }
            catch (Exception ex)
            {
                ModLogger.Error("Allow Tool compat registration failed: " + ex.Message);
            }
        }

        public static void RegisterContextMenuProvider()
        {
            try
            {
                DesignatorContextMenuRouter.Register(new AllowToolContextMenuProvider());
                ModLogger.Msg("Allow Tool compat: registered context menu provider");
            }
            catch (Exception ex)
            {
                ModLogger.Error("Allow Tool compat registration failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Auto-property backing fields and field-like events are compiler artifacts whose
        /// names differ by compiler, so try the mangled name first and the plain one second.
        /// A non-field-like event resolves neither, and declining is correct then.
        /// </summary>
        private static FieldInfo ResolveField(ReflectionSurface surface, Type type, string name)
        {
            FieldInfo field = type == null
                ? null
                : AccessTools.Field(type, "<" + name + ">k__BackingField") ?? AccessTools.Field(type, name);
            return surface.Required("UnlimitedAreaDragger." + name, field);
        }
    }
}
