using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Reflection facade for the ExpandMemory extension (cj.rimtalk.expandmemory):
    /// <c>MainTabWindow_Memory</c> ("Mind Stream"), <c>Dialog_CommonKnowledge</c>,
    /// <c>FourLayerMemoryComp</c>, <c>MemoryEntry</c>, <c>CommonKnowledgeEntry</c>/<c>Library</c>
    /// and <c>MemoryManager</c>. The RimTalkMemoryPatch assembly is never linked, so every member
    /// is resolved by name.
    ///
    /// <see cref="RimTalkMemoryMainTabScope"/> and <see cref="RimTalkCommonKnowledgeScope"/> are
    /// the two bespoke scopes this backs; both mod surfaces hand-roll row hit-testing as raw
    /// <c>Event.current.mousePosition</c> arithmetic with no clickable widget, so neither is
    /// reachable by the generic reader. Everything else in the mod needs no code here: the six
    /// editor dialogs draw plain captured Widgets in reading order and
    /// <see cref="ScopeForWindow.GenericReaderEligible"/> already attaches
    /// <see cref="GenericWindowScope"/> to them; the settings surfaces live inside the shared
    /// <c>Dialog_ModSettings</c> (the API key is masked in speech only, by GenericWindowScope's
    /// mod-agnostic <c>IsSensitiveFieldLabel</c>); <c>ITab_Memory</c> is dead code.
    ///
    /// <see cref="RegisterMemoriesInspectionRow"/> adds a read-only "Memories" branch to the
    /// "Character" inspection category for any pawn carrying the comp, found by <c>AllComps</c>
    /// type-name match (never <c>ThingCompUtility.TryGetComp&lt;T&gt;</c>, which needs a
    /// compile-time T). Read-only by design: a second editor would duplicate Mind Stream's own.
    /// </summary>
    internal static class RimTalkMemoryCompat
    {
        private const string PackageId = "cj.rimtalk.expandmemory";

        // MainTabWindow_Memory
        private static readonly Type mainTabType;
        private static readonly FieldInfo selectedPawnField;
        private static readonly FieldInfo currentMemoryCompField;
        private static readonly FieldInfo filterTypeField;
        private static readonly FieldInfo selectedMemoriesField;
        private static readonly FieldInfo lastSelectedMemoryField;
        private static readonly FieldInfo filtersDirtyField;
        private static readonly MethodInfo getFilteredMemoriesMethod;
        private static readonly MethodInfo showCreateMemoryMenuMethod;
        private static readonly FieldInfo cachedMemoriesField;
        private static readonly FieldInfo cachedCardYPositionsField;
        private static readonly FieldInfo cachedCardHeightsField;

        // FourLayerMemoryComp
        private static readonly Type memoryCompType;
        private static readonly PropertyInfo activeMemoriesProp;
        private static readonly PropertyInfo situationalMemoriesProp;
        private static readonly PropertyInfo eventLogMemoriesProp;
        private static readonly PropertyInfo archiveMemoriesProp;
        private static readonly MethodInfo pinMemoryMethod;

        // MemoryEntry
        private static readonly Type memoryEntryType;
        private static readonly FieldInfo memIdField;
        private static readonly FieldInfo memContentField;
        private static readonly FieldInfo memTypeField;
        private static readonly FieldInfo memLayerField;
        private static readonly PropertyInfo memImportanceProp;
        private static readonly FieldInfo memActivityField;
        private static readonly FieldInfo memRelatedPawnNameField;
        private static readonly FieldInfo memIsPinnedField;
        private static readonly PropertyInfo memAgeStringProp;

        // MemoryLayer / MemoryType enums
        private static readonly Type memoryLayerType;
        private static readonly Type memoryTypeType;

        // Dialog_EditMemory
        private static readonly ConstructorInfo editMemoryCtor;

        // MemoryManager / CommonKnowledgeLibrary
        private static readonly Type dialogCommonKnowledgeType;

        // CommonKnowledgeLibrary
        private static readonly Type libraryType;
        private static readonly PropertyInfo entriesProp; // List<CommonKnowledgeEntry>
        private static readonly MethodInfo removeEntryMethod;
        private static readonly MethodInfo addEntryStringsMethod; // AddEntry(string tag, string content)

        // Dialog_CommonKnowledge instance state
        private static readonly FieldInfo dckSearchFilterField;
        private static readonly FieldInfo dckSelectedEntriesField;
        private static readonly FieldInfo dckLastSelectedEntryField;
        private static readonly FieldInfo dckCurrentCategoryField;
        private static readonly FieldInfo dckEditModeField;
        private static readonly FieldInfo dckLibraryField;
        private static readonly MethodInfo dckGetCategoryCountMethod;
        private static readonly Type knowledgeCategoryType;

        /// <summary>The mod's own enum names (RimTalk.Memory.UI.KnowledgeCategory), read once at static init so a mod update can never leave this list stale against the Enum.Parse calls below. The literal fallback only serves the type-missing case, where the compat never attaches anyway.</summary>
        private static readonly string[] KnowledgeCategoryNames;

        // CommonKnowledgeEntry
        private static readonly Type entryType;
        private static readonly FieldInfo entryTagField;
        private static readonly FieldInfo entryContentField;
        private static readonly FieldInfo entryImportanceField;
        private static readonly FieldInfo entryIsEnabledField;
        private static readonly FieldInfo entryTargetPawnIdField;

        // ExtendedKnowledgeEntry / CommonKnowledgeUIHelpers
        private static readonly MethodInfo canBeExtractedMethod;
        private static readonly MethodInfo setCanBeExtractedMethod;
        private static readonly MethodInfo canBeMatchedMethod;
        private static readonly MethodInfo setCanBeMatchedMethod;
        private static readonly MethodInfo getEntryCategoryMethod;
        private static readonly MethodInfo getCategoryLabelMethod;
        private static readonly MethodInfo getVisibilityTextMethod;

        // FourLayerMemoryComp lookup via Pawn.AllComps (never ThingCompUtility.TryGetComp<T>,
        // which needs a compile-time T we don't have).
        private static readonly bool ready;
        private static readonly bool commonKnowledgeReady;

        public static bool Ready { get { return ready; } }
        public static bool CommonKnowledgeReady { get { return commonKnowledgeReady; } }

        static RimTalkMemoryCompat()
        {
            var main = new ReflectionSurface("RimTalk memory compat (Mind Stream)");

            mainTabType = main.Type("RimTalk.Memory.UI.MainTabWindow_Memory");
            memoryCompType = main.Type("RimTalk.Memory.FourLayerMemoryComp");
            memoryEntryType = main.Type("RimTalk.Memory.MemoryEntry");
            memoryLayerType = main.Type("RimTalk.Memory.MemoryLayer");
            memoryTypeType = main.Type("RimTalk.Memory.MemoryType");
            Type editMemoryType = main.Type("RimTalk.Memory.UI.Dialog_EditMemory");
            dialogCommonKnowledgeType = main.Type("RimTalk.Memory.UI.Dialog_CommonKnowledge");
            libraryType = main.Type("RimTalk.Memory.CommonKnowledgeLibrary");

            selectedPawnField = main.Field(mainTabType, "selectedPawn");
            currentMemoryCompField = main.Field(mainTabType, "currentMemoryComp");
            filterTypeField = main.Field(mainTabType, "filterType");
            selectedMemoriesField = main.Field(mainTabType, "selectedMemories");
            lastSelectedMemoryField = main.Field(mainTabType, "lastSelectedMemory");
            filtersDirtyField = main.Field(mainTabType, "filtersDirty");
            getFilteredMemoriesMethod = main.Method(mainTabType, "GetFilteredMemories");
            cachedMemoriesField = main.Field(mainTabType, "cachedMemories");
            cachedCardYPositionsField = main.Field(mainTabType, "cachedCardYPositions");
            cachedCardHeightsField = main.Field(mainTabType, "cachedCardHeights");
            showCreateMemoryMenuMethod = memoryLayerType != null
                ? main.Method(mainTabType, "ShowCreateMemoryMenu", new[] { memoryLayerType })
                : null;

            activeMemoriesProp = main.Property(memoryCompType, "ActiveMemories");
            situationalMemoriesProp = main.Property(memoryCompType, "SituationalMemories");
            eventLogMemoriesProp = main.Property(memoryCompType, "EventLogMemories");
            archiveMemoriesProp = main.Property(memoryCompType, "ArchiveMemories");
            pinMemoryMethod = main.Method(memoryCompType, "PinMemory", new[] { typeof(string), typeof(bool) });

            memIdField = main.Field(memoryEntryType, "Id");
            memContentField = main.Field(memoryEntryType, "Content");
            memTypeField = main.Field(memoryEntryType, "Type");
            memLayerField = main.Field(memoryEntryType, "Layer");
            memImportanceProp = main.Property(memoryEntryType, "Importance");
            memActivityField = main.Field(memoryEntryType, "Activity");
            memRelatedPawnNameField = main.Field(memoryEntryType, "relatedPawnName");
            memIsPinnedField = main.Field(memoryEntryType, "IsPinned");
            memAgeStringProp = main.Property(memoryEntryType, "AgeString");

            editMemoryCtor = editMemoryType != null && memoryEntryType != null && memoryCompType != null
                ? AccessTools.Constructor(editMemoryType, new[] { memoryEntryType, memoryCompType })
                : null;

            ready = main.Ready && editMemoryCtor != null;

            var ck = new ReflectionSurface("RimTalk memory compat (Common Knowledge)");

            entryType = ck.Type("RimTalk.Memory.CommonKnowledgeEntry");
            knowledgeCategoryType = ck.Type("RimTalk.Memory.UI.KnowledgeCategory");
            Type extendedType = ck.Type("RimTalk.Memory.ExtendedKnowledgeEntry");
            Type uiHelpersType = ck.Type("RimTalk.Memory.UI.CommonKnowledgeUIHelpers");

            entriesProp = ck.Property(libraryType, "Entries");
            removeEntryMethod = entryType != null ? ck.Method(libraryType, "RemoveEntry", new[] { entryType }) : null;
            addEntryStringsMethod = ck.Method(libraryType, "AddEntry", new[] { typeof(string), typeof(string) });

            dckSearchFilterField = ck.Field(dialogCommonKnowledgeType, "searchFilter");
            dckSelectedEntriesField = ck.Field(dialogCommonKnowledgeType, "selectedEntries");
            dckLastSelectedEntryField = ck.Field(dialogCommonKnowledgeType, "lastSelectedEntry");
            dckCurrentCategoryField = ck.Field(dialogCommonKnowledgeType, "currentCategory");
            dckEditModeField = ck.Field(dialogCommonKnowledgeType, "editMode");
            dckLibraryField = ck.Field(dialogCommonKnowledgeType, "library");
            dckGetCategoryCountMethod = knowledgeCategoryType != null
                ? ck.Method(dialogCommonKnowledgeType, "GetCategoryCount", new[] { knowledgeCategoryType })
                : null;

            entryTagField = ck.Field(entryType, "tag");
            entryContentField = ck.Field(entryType, "content");
            entryImportanceField = ck.Field(entryType, "importance");
            entryIsEnabledField = ck.Field(entryType, "isEnabled");
            entryTargetPawnIdField = ck.Field(entryType, "targetPawnId");

            if (entryType != null)
            {
                canBeExtractedMethod = ck.Method(extendedType, "CanBeExtracted", new[] { entryType });
                setCanBeExtractedMethod = ck.Method(extendedType, "SetCanBeExtracted", new[] { entryType, typeof(bool) });
                canBeMatchedMethod = ck.Method(extendedType, "CanBeMatched", new[] { entryType });
                setCanBeMatchedMethod = ck.Method(extendedType, "SetCanBeMatched", new[] { entryType, typeof(bool) });
                getEntryCategoryMethod = ck.Method(uiHelpersType, "GetEntryCategory", new[] { entryType });
                getVisibilityTextMethod = ck.Method(uiHelpersType, "GetVisibilityText", new[] { entryType });
            }
            getCategoryLabelMethod = knowledgeCategoryType != null
                ? ck.Method(uiHelpersType, "GetCategoryLabel", new[] { knowledgeCategoryType })
                : null;

            KnowledgeCategoryNames = knowledgeCategoryType != null
                ? Enum.GetNames(knowledgeCategoryType)
                : new[] { "All", "Instructions", "Lore", "PawnStatus", "History", "Other" };

            commonKnowledgeReady = ready && ck.Ready;
        }

        public static void Register()
        {
            if (!ModsConfig.IsActive(PackageId))
            {
                return;
            }

            if (ready)
            {
                ScopeForWindow.Register(mainTabType, delegate (Window w)
                {
                    return new RimTalkMemoryMainTabScope(w);
                });
                if (commonKnowledgeReady)
                {
                    ScopeForWindow.Register(dialogCommonKnowledgeType, delegate (Window w)
                    {
                        return new RimTalkCommonKnowledgeScope(w);
                    });
                }
                InspectNodeRegistry.RegisterCategoryExtender("Character", AddMemoriesNode);
            }
        }

        // MainTabWindow_Memory

        internal static Pawn GetSelectedPawn(Window w) { return selectedPawnField.GetValue(w) as Pawn; }
        internal static object GetCurrentMemoryComp(Window w) { return currentMemoryCompField.GetValue(w); }

        /// <summary>Null = "All". Boxed MemoryType otherwise.</summary>
        internal static object GetFilterType(Window w) { return filterTypeField.GetValue(w); }

        /// <summary>MUTATION-C: mirrors the Type-filter buttons' own body (bare Nullable&lt;MemoryType&gt; field, no gated setter) -- clears the SAME live HashSet instance rather than reassigning the field, matching `selectedMemories.Clear()`.</summary>
        internal static void SetFilterType(Window w, object boxedMemoryTypeOrNull)
        {
            filterTypeField.SetValue(w, boxedMemoryTypeOrNull);
            InvokeSetMethod(selectedMemoriesField.GetValue(w), "Clear", null);
            // MUTATION-C: filtersDirty is the same bare-field mirror as filterType above.
            filtersDirtyField.SetValue(w, true);
        }

        internal static object GetMemoryTypeConversation()
        {
            return memoryTypeType != null ? Enum.Parse(memoryTypeType, "Conversation") : null;
        }

        internal static object GetMemoryTypeAction()
        {
            return memoryTypeType != null ? Enum.Parse(memoryTypeType, "Action") : null;
        }

        internal static IList GetFilteredMemories(Window w)
        {
            return getFilteredMemoriesMethod.Invoke(w, null) as IList;
        }

        /// <summary>DrawTimeline's own three parallel caches: the memory entries it draws, and each card's y-position and height inside the timeline scroll view. Null when the field is gone, which reads as "no card geometry this pass".</summary>
        internal static IList GetCachedMemories(Window w)
        {
            return cachedMemoriesField?.GetValue(w) as IList;
        }

        internal static IList<float> GetCachedCardYPositions(Window w)
        {
            return cachedCardYPositionsField?.GetValue(w) as IList<float>;
        }

        internal static IList<float> GetCachedCardHeights(Window w)
        {
            return cachedCardHeightsField?.GetValue(w) as IList<float>;
        }

        /// <summary>The exact text DrawControlPanel's own type-filter buttons show, in the same order the Filters region lists them.</summary>
        internal static string FilterButtonRawLabel(int index)
        {
            switch (index)
            {
                case 0: return Translator.Translate("RimTalk_MindStream_All").Resolve();
                case 1: return Translator.Translate("RimTalk_MindStream_Conversation").Resolve();
                default: return Translator.Translate("RimTalk_MindStream_Action").Resolve();
            }
        }

        /// <summary>Vehicle A: the mod's own private ShowCreateMemoryMenu(MemoryLayer) verbatim -- the exact 2-option FloatMenu (Conversation/Action) a mouse right-click opens, reached here as the keyboard's declared-action equivalent of that gesture. Wrapped in ScopeDelegateGuard.Run so the real FloatMenu it opens (Find.WindowStack.Add(new FloatMenu(list))) rides the windowless path instead of self-dismissing far from the mouse.</summary>
        internal static void ShowCreateMemoryMenu(Window w, string layerName)
        {
            object layer = Enum.Parse(memoryLayerType, layerName);
            ScopeDelegateGuard.Run(() => showCreateMemoryMenuMethod.Invoke(w, new object[] { layer }));
        }

        /// <summary>MUTATION-C: mirrors HandleMemoryClick's plain-click branch (Clear then Add then set lastSelectedMemory -- all bare fields with no gated setter).</summary>
        internal static void ReplaceSelectionWith(Window w, object memoryEntry)
        {
            object set = selectedMemoriesField.GetValue(w);
            InvokeSetMethod(set, "Clear", null);
            InvokeSetMethod(set, "Add", memoryEntry);
            // MUTATION-C: same bare-field mirror as the HashSet ops just above.
            lastSelectedMemoryField.SetValue(w, memoryEntry);
        }

        /// <summary>MUTATION-C: mirrors HandleMemoryClick's Ctrl-click branch (Contains/Remove/Add toggle on the bare HashSet field, plus lastSelectedMemory).</summary>
        internal static void ToggleSelection(Window w, object memoryEntry)
        {
            object set = selectedMemoriesField.GetValue(w);
            bool contains = (bool)InvokeSetMethod(set, "Contains", memoryEntry);
            InvokeSetMethod(set, contains ? "Remove" : "Add", memoryEntry);
            // MUTATION-C: same bare-field mirror as the HashSet toggle just above.
            lastSelectedMemoryField.SetValue(w, memoryEntry);
        }

        internal static bool IsSelected(Window w, object memoryEntry)
        {
            object set = selectedMemoriesField.GetValue(w);
            return (bool)InvokeSetMethod(set, "Contains", memoryEntry);
        }

        private static object InvokeSetMethod(object set, string name, object arg)
        {
            if (set == null)
            {
                return name == "Contains" ? (object)false : null;
            }
            MethodInfo m = arg == null
                ? set.GetType().GetMethod(name, Type.EmptyTypes)
                : set.GetType().GetMethod(name, new[] { memoryEntryType });
            return m?.Invoke(set, arg == null ? null : new[] { arg });
        }

        // FourLayerMemoryComp / MemoryEntry

        internal static IList GetActiveMemories(object comp) { return activeMemoriesProp.GetValue(comp) as IList; }
        internal static IList GetSituationalMemories(object comp) { return situationalMemoriesProp.GetValue(comp) as IList; }
        internal static IList GetEventLogMemories(object comp) { return eventLogMemoriesProp.GetValue(comp) as IList; }
        internal static IList GetArchiveMemories(object comp) { return archiveMemoriesProp.GetValue(comp) as IList; }

        /// <summary>Vehicle B: FourLayerMemoryComp's own gated PinMemory(id, pinned) -- promotes ABM to SCM on pin exactly as the mod's own Pin button does.</summary>
        internal static void PinMemory(object comp, string memoryId, bool pinned)
        {
            pinMemoryMethod.Invoke(comp, new object[] { memoryId, pinned });
        }

        internal static string MemId(object entry) { return memIdField.GetValue(entry) as string; }
        internal static string MemContent(object entry) { return memContentField.GetValue(entry) as string; }
        internal static string MemTypeName(object entry) { return memTypeField.GetValue(entry).ToString(); }
        internal static string MemLayerName(object entry) { return memLayerField.GetValue(entry).ToString(); }
        internal static float MemImportance(object entry) { return (float)memImportanceProp.GetValue(entry); }
        internal static float MemActivity(object entry) { return (float)memActivityField.GetValue(entry); }
        internal static string MemRelatedPawnName(object entry) { return memRelatedPawnNameField.GetValue(entry) as string; }
        internal static bool MemIsPinned(object entry) { return (bool)memIsPinnedField.GetValue(entry); }
        internal static string MemAgeString(object entry) { return memAgeStringProp.GetValue(entry) as string; }

        /// <summary>Vehicle A: `new Dialog_EditMemory(memory, comp)` verbatim -- the mod's own Rename/Edit button click.</summary>
        internal static void OpenEditMemory(object memoryEntry, object comp)
        {
            object instance = editMemoryCtor.Invoke(new[] { memoryEntry, comp });
            Find.WindowStack.Add((Window)instance);
        }

        // CommonKnowledgeLibrary / CommonKnowledgeEntry

        internal static IList GetEntries(object library) { return entriesProp.GetValue(library) as IList; }

        /// <summary>Vehicle A: CommonKnowledgeLibrary's own gated RemoveEntry (also cleans up ExtendedKnowledgeEntry's side table and any vector-DB sync the mod's settings enabled).</summary>
        internal static void RemoveEntry(object library, object entry)
        {
            removeEntryMethod.Invoke(library, new[] { entry });
        }

        internal static string EntryTag(object entry) { return entryTagField.GetValue(entry) as string; }
        internal static string EntryContent(object entry) { return entryContentField.GetValue(entry) as string; }
        internal static float EntryImportance(object entry) { return (float)entryImportanceField.GetValue(entry); }
        internal static bool EntryIsEnabled(object entry) { return (bool)entryIsEnabledField.GetValue(entry); }

        /// <summary>MUTATION-C: mirrors DrawEntryRow's own return-assign (`Widgets.Checkbox(..., ref entry.isEnabled, ...)`) -- a bare public field with no gated setter.</summary>
        internal static void SetEntryIsEnabled(object entry, bool value) { entryIsEnabledField.SetValue(entry, value); }

        internal static bool EntryCanBeExtracted(object entry) { return (bool)canBeExtractedMethod.Invoke(null, new[] { entry }); }
        internal static void SetEntryCanBeExtracted(object entry, bool value) { setCanBeExtractedMethod.Invoke(null, new object[] { entry, value }); }
        internal static bool EntryCanBeMatched(object entry) { return (bool)canBeMatchedMethod.Invoke(null, new[] { entry }); }
        internal static void SetEntryCanBeMatched(object entry, bool value) { setCanBeMatchedMethod.Invoke(null, new object[] { entry, value }); }

        internal static string EntryCategoryLabel(object entry)
        {
            object category = getEntryCategoryMethod.Invoke(null, new[] { entry });
            return getCategoryLabelMethod.Invoke(null, new[] { category }) as string;
        }

        internal static string EntryVisibilityText(object entry)
        {
            return getVisibilityTextMethod.Invoke(null, new[] { entry }) as string;
        }

        // Dialog_CommonKnowledge instance state

        internal static string GetSearchFilter(Window w) { return dckSearchFilterField.GetValue(w) as string ?? ""; }

        /// <summary>MUTATION-C: mirrors DrawToolbar's own return-assign (`searchFilter = Widgets.TextField(...)`) -- a bare private field with no gated setter.</summary>
        internal static void SetSearchFilter(Window w, string value) { dckSearchFilterField.SetValue(w, value ?? ""); }

        internal static object GetCurrentCategory(Window w) { return dckCurrentCategoryField.GetValue(w); }

        /// <summary>MUTATION-C: mirrors DrawSidebar's own category-button click body (currentCategory assign + selectedEntries.Clear()).</summary>
        internal static void SetCurrentCategory(Window w, string categoryName)
        {
            dckCurrentCategoryField.SetValue(w, Enum.Parse(knowledgeCategoryType, categoryName));
            InvokeSetMethod(dckSelectedEntriesField.GetValue(w), "Clear", null, entryType);
        }

        internal static IEnumerable<string> KnowledgeCategories()
        {
            return KnowledgeCategoryNames;
        }

        internal static string CategoryLabel(string categoryName)
        {
            object category = Enum.Parse(knowledgeCategoryType, categoryName);
            return getCategoryLabelMethod.Invoke(null, new[] { category }) as string;
        }

        /// <summary>Vehicle A: the dialog's own private GetCategoryCount(KnowledgeCategory) verbatim.</summary>
        internal static int GetCategoryCount(Window w, string categoryName)
        {
            object category = Enum.Parse(knowledgeCategoryType, categoryName);
            object result = dckGetCategoryCountMethod.Invoke(w, new[] { category });
            return result is int i ? i : 0;
        }

        internal static object GetLibrary(Window w) { return dckLibraryField.GetValue(w); }

        internal static bool IsEditMode(Window w) { return (bool)dckEditModeField.GetValue(w); }

        internal static bool IsEntrySelected(Window w, object entry)
        {
            return (bool)InvokeSetMethod(dckSelectedEntriesField.GetValue(w), "Contains", entry, entryType);
        }

        internal static int SelectedEntryCount(Window w)
        {
            object set = dckSelectedEntriesField.GetValue(w);
            PropertyInfo countProp = set?.GetType().GetProperty("Count");
            return countProp != null ? (int)countProp.GetValue(set) : 0;
        }

        internal static IEnumerable<object> SelectedEntries(Window w)
        {
            object set = dckSelectedEntriesField.GetValue(w);
            if (set is IEnumerable enumerable)
            {
                foreach (object item in enumerable)
                {
                    yield return item;
                }
            }
        }

        /// <summary>MUTATION-C: mirrors HandleEntryClick's plain-click branch (Clear, Add, set lastSelectedEntry, clear editMode -- all bare fields with no gated setter).</summary>
        internal static void ReplaceEntrySelectionWith(Window w, object entry)
        {
            // MUTATION-C: mirrors HandleEntryClick's plain-click branch verbatim (Clear, Add, set lastSelectedEntry, clear editMode -- all bare fields with no gated setter).
            object set = dckSelectedEntriesField.GetValue(w);
            InvokeSetMethod(set, "Clear", null, entryType);
            InvokeSetMethod(set, "Add", entry, entryType);
            dckLastSelectedEntryField.SetValue(w, entry);
            // MUTATION-C: same plain-click branch as above -- clears edit mode too.
            dckEditModeField.SetValue(w, false);
        }

        /// <summary>MUTATION-C: mirrors HandleEntryClick's Ctrl-click branch (Contains/Remove/Add toggle, plus lastSelectedEntry -- editMode is untouched on this branch, decompiled-verified).</summary>
        internal static void ToggleEntrySelection(Window w, object entry)
        {
            // MUTATION-C: see summary above -- Ctrl-click toggle branch.
            object set = dckSelectedEntriesField.GetValue(w);
            bool contains = (bool)InvokeSetMethod(set, "Contains", entry, entryType);
            InvokeSetMethod(set, contains ? "Remove" : "Add", entry, entryType);
            dckLastSelectedEntryField.SetValue(w, entry);
        }

        private static object InvokeSetMethod(object set, string name, object arg, Type elementType)
        {
            if (set == null)
            {
                return name == "Contains" ? (object)false : null;
            }
            MethodInfo m = arg == null
                ? set.GetType().GetMethod(name, Type.EmptyTypes)
                : set.GetType().GetMethod(name, new[] { elementType });
            return m?.Invoke(set, arg == null ? null : new[] { arg });
        }

        /// <summary>MUTATION-C: reorders the SAME live backing list (`List&lt;CommonKnowledgeEntry&gt; Entries`) a single position -- the keyboard equivalent of the mouse's drag-to-reorder, one row at a time rather than reproducing its multi-select block-move math. Returns false when already at that end.</summary>
        internal static bool ReorderEntry(object library, object entry, int direction)
        {
            IList entries = GetEntries(library);
            if (entries == null)
            {
                return false;
            }
            int index = entries.IndexOf(entry);
            int target = index + direction;
            if (index < 0 || target < 0 || target >= entries.Count)
            {
                return false;
            }
            object swap = entries[target];
            entries[target] = entry;
            entries[index] = swap;
            return true;
        }

        // Read-only Memories reviewer (pawn inspection tree, "Character" category)

        private static void AddMemoriesNode(InspectionTreeItem categoryItem, object obj)
        {
            if (!ready || !(obj is Pawn pawn))
            {
                return;
            }
            object comp = FindMemoryComp(pawn);
            if (comp == null)
            {
                return;
            }
            IList active = GetActiveMemories(comp);
            IList situational = GetSituationalMemories(comp);
            IList eventLog = GetEventLogMemories(comp);
            IList archive = GetArchiveMemories(comp);
            int total = (active?.Count ?? 0) + (situational?.Count ?? 0) + (eventLog?.Count ?? 0) + (archive?.Count ?? 0);
            if (total == 0)
            {
                return;
            }

            string label = "RimWorldAccess.Compat.RimTalk.Memory.MemoriesNode".Translate(total);
            InspectNodeFactory.Section(categoryItem, label, pawn, delegate (InspectionTreeItem section)
            {
                AddLayerSubsection(section, "RimWorldAccess.Compat.RimTalk.Memory.LayerActive".Translate(), active);
                AddLayerSubsection(section, "RimWorldAccess.Compat.RimTalk.Memory.LayerSituational".Translate(), situational);
                AddLayerSubsection(section, "RimWorldAccess.Compat.RimTalk.Memory.LayerEventLog".Translate(), eventLog);
                AddLayerSubsection(section, "RimWorldAccess.Compat.RimTalk.Memory.LayerArchive".Translate(), archive);
            });
        }

        private static void AddLayerSubsection(InspectionTreeItem parent, string layerLabel, IList memories)
        {
            if (memories == null || memories.Count == 0)
            {
                return;
            }
            string label = "RimWorldAccess.Compat.RimTalk.Memory.LayerNode".Translate(layerLabel, memories.Count);
            InspectNodeFactory.Section(parent, label, memories, delegate (InspectionTreeItem layerSection)
            {
                foreach (object entry in memories)
                {
                    if (entry == null)
                    {
                        continue;
                    }
                    string relatedPawn = MemRelatedPawnName(entry);
                    string line = string.IsNullOrEmpty(relatedPawn)
                        ? "RimWorldAccess.Compat.RimTalk.Memory.MemoryLine".Translate(
                            MemTypeName(entry), MemContent(entry), MemAgeString(entry),
                            MemImportance(entry).ToString("F2"), MemActivity(entry).ToString("F2"))
                        : "RimWorldAccess.Compat.RimTalk.Memory.MemoryLineWithPawn".Translate(
                            MemTypeName(entry), MemContent(entry), MemAgeString(entry),
                            MemImportance(entry).ToString("F2"), MemActivity(entry).ToString("F2"), relatedPawn);
                    if (MemIsPinned(entry))
                    {
                        line = "RimWorldAccess.Compat.RimTalk.Memory.MemoryLinePinned".Translate(line);
                    }
                    InspectNodeFactory.DetailLine(layerSection, line);
                }
            });
        }

        /// <summary>Reflective AllComps type-name match -- never <c>ThingCompUtility.TryGetComp&lt;T&gt;</c>, which needs a compile-time T we don't have for a type in an unreferenced assembly.</summary>
        private static object FindMemoryComp(Pawn pawn)
        {
            if (pawn?.AllComps == null)
            {
                return null;
            }
            foreach (ThingComp comp in pawn.AllComps)
            {
                if (comp != null && comp.GetType().Name == "FourLayerMemoryComp")
                {
                    return comp;
                }
            }
            return null;
        }

    }
}
