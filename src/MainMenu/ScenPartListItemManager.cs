using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Unifies Scenario Builder's list-based ScenPart handling: every operation consults the one
    /// descriptor table below, and the backing field resolves through
    /// <see cref="ResolveBackingField"/> rather than a literal string re-typed at each call site.
    ///
    /// Also provides a generic tier for MODDED list-based ScenParts with a qualifying public
    /// List&lt;T&gt; field, mirroring ScenarioBuilderState.AddGenericModdedFields' single-field
    /// fallback. A shape gets that treatment only when Add and Delete can be built soundly for it
    /// (see <see cref="GetGenericListField"/>); anything else stays non-list, never an action that
    /// no-ops.
    /// </summary>
    internal static class ScenPartListItemManager
    {
        private sealed class ListPartDescriptor
        {
            public Func<ScenPart, bool> Matches;
            public string FieldName;
            public string ItemTypeLabelKey;
            public Func<ScenPart, FieldInfo, List<ScenarioBuilderState.ListItemData>> ExtractItems;
            public Action<ScenPart, FieldInfo> AddNewItem;
        }

        private static readonly List<ListPartDescriptor> VanillaDescriptors = new List<ListPartDescriptor>
        {
            new ListPartDescriptor
            {
                Matches = part => part is ScenPart_ConfigPage_ConfigureStartingPawns_KindDefs,
                FieldName = "kindCounts",
                ItemTypeLabelKey = "RimWorldAccess.ScenarioBuilder.ItemType.PawnKindEntry",
                ExtractItems = ExtractKindDefsListItems,
                AddNewItem = AddKindDefsItem
            },
            new ListPartDescriptor
            {
                Matches = part => ModsConfig.BiotechActive && part is ScenPart_ConfigPage_ConfigureStartingPawns_Xenotypes,
                FieldName = "xenotypeCounts",
                ItemTypeLabelKey = "RimWorldAccess.ScenarioBuilder.ItemType.XenotypeEntry",
                ExtractItems = ExtractXenotypeListItems,
                AddNewItem = AddXenotypeItem
            },
            new ListPartDescriptor
            {
                Matches = part => ModsConfig.AnomalyActive && part is ScenPart_ConfigPage_ConfigureStartingPawns_Mutants,
                FieldName = "mutantCounts",
                ItemTypeLabelKey = "RimWorldAccess.ScenarioBuilder.ItemType.MutantEntry",
                ExtractItems = ExtractMutantListItems,
                AddNewItem = AddMutantItem
            },
            new ListPartDescriptor
            {
                // A hidden layer draws no connections list either, matching the `hide`
                // short-circuit in ScenarioBuilderState.ExtractPartFields.
                Matches = part => part is ScenPart_PlanetLayer layer && !layer.hide,
                FieldName = "connections",
                ItemTypeLabelKey = "RimWorldAccess.ScenarioBuilder.ItemType.Connection",
                ExtractItems = ExtractLayerConnectionItems,
                AddNewItem = AddLayerConnectionItem
            }
        };

        private static ListPartDescriptor FindVanillaDescriptor(ScenPart part) =>
            VanillaDescriptors.FirstOrDefault(d => d.Matches(part));

        private static FieldInfo ResolveBackingField(ScenPart part, ListPartDescriptor d) =>
            VanillaAccess.GetField(part.GetType(), d.FieldName);

        public static bool IsListBasedPart(ScenPart part)
        {
            return FindVanillaDescriptor(part) != null || GetGenericListField(part.GetType()) != null;
        }

        public static List<ScenarioBuilderState.ListItemData> ExtractListItems(ScenPart part)
        {
            var descriptor = FindVanillaDescriptor(part);
            if (descriptor != null)
            {
                var field = ResolveBackingField(part, descriptor);
                if (field == null) return new List<ScenarioBuilderState.ListItemData>();
                return descriptor.ExtractItems(part, field) ?? new List<ScenarioBuilderState.ListItemData>();
            }

            var genericField = GetGenericListField(part.GetType());
            if (genericField != null)
                return ExtractGenericListItems(part, genericField);

            return new List<ScenarioBuilderState.ListItemData>();
        }

        /// <summary>Adds a new item to a list-based part.</summary>
        public static void AddListItem(ScenarioBuilderState.PartTreeItem partItem)
        {
            var part = partItem.Part;

            var descriptor = FindVanillaDescriptor(part);
            if (descriptor != null)
            {
                var field = ResolveBackingField(part, descriptor);
                if (field != null) descriptor.AddNewItem(part, field);
                return;
            }

            var genericField = GetGenericListField(part.GetType());
            if (genericField != null)
                AddGenericListItem(part, genericField);
        }

        /// <summary>
        /// Deletes a list item. Uniform across every shape: removal needs nothing beyond the list,
        /// the index, and a label for the announcement.
        /// </summary>
        public static void DeleteListItem(ScenarioBuilderState.PartTreeItem partItem, int listItemIndex)
        {
            var part = partItem.Part;

            System.Collections.IList list = null;
            string itemType = (string)"RimWorldAccess.ScenarioBuilder.ItemType.Item".Translate();

            var descriptor = FindVanillaDescriptor(part);
            if (descriptor != null)
            {
                var field = ResolveBackingField(part, descriptor);
                list = field?.GetValue(part) as System.Collections.IList;
                itemType = (string)descriptor.ItemTypeLabelKey.Translate();
            }
            else
            {
                var genericField = GetGenericListField(part.GetType());
                if (genericField != null)
                {
                    list = genericField.GetValue(part) as System.Collections.IList;
                    itemType = (string)"RimWorldAccess.ScenarioBuilder.ItemType.GenericListEntry".Translate();
                }
            }

            if (list != null && listItemIndex >= 0 && listItemIndex < list.Count)
            {
                list.RemoveAt(listItemIndex);
                ScenarioBuilderState.SetDirty();
                TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.RemovedItemType".Loc(itemType));
            }
        }

        /// <summary>Localized list-item label "{count}x {label}" with an optional "(Required)" suffix.</summary>
        private static string ListItemCountLabel(int count, string label, bool required)
        {
            string baseLabel = (string)"RimWorldAccess.ScenarioBuilder.ListItemCount".Translate(count, label);
            return required
                ? baseLabel + (string)"RimWorldAccess.ScenarioBuilder.RequiredSuffix".Translate()
                : baseLabel;
        }

        #region Vanilla list-based parts

        /// <summary>List items for ScenPart_ConfigPage_ConfigureStartingPawns_KindDefs.</summary>
        private static List<ScenarioBuilderState.ListItemData> ExtractKindDefsListItems(ScenPart part, FieldInfo kindCountsField)
        {
            var listItems = new List<ScenarioBuilderState.ListItemData>();
            var kindCounts = kindCountsField.GetValue(part) as System.Collections.IList;
            if (kindCounts == null) return listItems;

            for (int i = 0; i < kindCounts.Count; i++)
            {
                var item = kindCounts[i];
                var kindDefField = item.GetType().GetField("kindDef", BindingFlags.Public | BindingFlags.Instance);
                var countField = item.GetType().GetField("count", BindingFlags.Public | BindingFlags.Instance);
                var requiredField = item.GetType().GetField("requiredAtStart", BindingFlags.Public | BindingFlags.Instance);

                var kindDef = kindDefField?.GetValue(item) as PawnKindDef;
                int count = countField != null ? (int)countField.GetValue(item) : 1;
                bool required = requiredField != null && (bool)requiredField.GetValue(item);

                string kindLabel = kindDef?.LabelCap ?? "Unknown".Translate().CapitalizeFirst();
                string label = ListItemCountLabel(count, kindLabel, required);

                var listItem = new ScenarioBuilderState.ListItemData
                {
                    Label = label,
                    Index = i,
                    ItemReference = item,
                    IsExpanded = false,
                    Fields = new List<ScenarioBuilderState.PartField>()
                };

                int capturedIndex = i;
                listItem.Fields.Add(new ScenarioBuilderState.PartField
                {
                    Name = (string)"RimWorldAccess.ScenarioBuilder.Field.Count".Translate(),
                    Type = ScenarioBuilderState.FieldType.Quantity,
                    CurrentValue = count.ToString(),
                    Data = new int[] { 1, 10 },
                    SetValue = (val) =>
                    {
                        var list = kindCountsField.GetValue(part) as System.Collections.IList;
                        if (list != null && capturedIndex < list.Count)
                        {
                            var entry = list[capturedIndex];
                            // MUTATION-C: mirrors ScenPart_ConfigPage_ConfigureStartingPawns_KindDefs' per-row count TextFieldNumeric(1,10) (decompiled RimWorld/ScenPart_ConfigPage_ConfigureStartingPawns_KindDefs.cs:25-82).
                            countField.SetValue(entry, Convert.ToInt32(val));
                        }
                    }
                });

                listItem.Fields.Add(new ScenarioBuilderState.PartField
                {
                    Name = (string)"RimWorldAccess.ScenarioBuilder.Field.PawnKind".Translate(),
                    Type = ScenarioBuilderState.FieldType.Dropdown,
                    CurrentValue = kindLabel,
                    Data = ScenarioBuilderState.GetPawnKindDefOptions(),
                    SetValue = (val) =>
                    {
                        var list = kindCountsField.GetValue(part) as System.Collections.IList;
                        if (list != null && capturedIndex < list.Count)
                        {
                            var entry = list[capturedIndex];
                            // MUTATION-C: same file's per-row kindDef ButtonText, FloatMenu over AvailableKinds (decompiled RimWorld/ScenPart_ConfigPage_ConfigureStartingPawns_KindDefs.cs:25-82).
                            kindDefField.SetValue(entry, val);
                        }
                    }
                });

                listItem.Fields.Add(new ScenarioBuilderState.PartField
                {
                    Name = (string)"RimWorldAccess.ScenarioBuilder.Field.RequiredAtStart".Translate(),
                    Type = ScenarioBuilderState.FieldType.Checkbox,
                    CurrentValue = ScenarioBuilderState.BoolDisplay(required),
                    BoolValue = required,
                    Data = null,
                    SetValue = (val) =>
                    {
                        var list = kindCountsField.GetValue(part) as System.Collections.IList;
                        if (list != null && capturedIndex < list.Count)
                        {
                            var entry = list[capturedIndex];
                            bool newVal = val is bool b && b;
                            // MUTATION-C: same file's row-level Required Widgets.Checkbox (decompiled RimWorld/ScenPart_ConfigPage_ConfigureStartingPawns_KindDefs.cs:25-82).
                            requiredField.SetValue(entry, newVal);
                        }
                    }
                });

                listItems.Add(listItem);
            }

            return listItems;
        }

        private static void AddKindDefsItem(ScenPart part, FieldInfo kindCountsField)
        {
            var kindCounts = kindCountsField.GetValue(part) as System.Collections.IList;
            if (kindCounts != null)
            {
                var kindCountType = AccessTools.TypeByName("RimWorld.PawnKindCount");
                var newItem = Activator.CreateInstance(kindCountType);
                var kindDefField = kindCountType.GetField("kindDef", BindingFlags.Public | BindingFlags.Instance);
                var countField = kindCountType.GetField("count", BindingFlags.Public | BindingFlags.Instance);
                // MUTATION-C: mirrors the same file's trailing Add button, which seeds a new PawnKindCount defaulting to Colonist/count 1 (decompiled RimWorld/ScenPart_ConfigPage_ConfigureStartingPawns_KindDefs.cs:25-82).
                kindDefField.SetValue(newItem, PawnKindDefOf.Colonist);
                // MUTATION-C: same Add-button seed (see above).
                countField.SetValue(newItem, 1);
                kindCounts.Add(newItem);
                ScenarioBuilderState.SetDirty();
                TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.AddedPawnKindEntry".Loc());
            }
        }

        /// <summary>List items for ScenPart_ConfigPage_ConfigureStartingPawns_Xenotypes.</summary>
        private static List<ScenarioBuilderState.ListItemData> ExtractXenotypeListItems(ScenPart part, FieldInfo xenotypeCountsField)
        {
            var listItems = new List<ScenarioBuilderState.ListItemData>();
            if (!ModsConfig.BiotechActive) return listItems;

            var xenotypeCounts = xenotypeCountsField.GetValue(part) as System.Collections.IList;
            if (xenotypeCounts == null) return listItems;

            for (int i = 0; i < xenotypeCounts.Count; i++)
            {
                var item = xenotypeCounts[i];
                var xenotypeField = item.GetType().GetField("xenotype", BindingFlags.Public | BindingFlags.Instance);
                var countField = item.GetType().GetField("count", BindingFlags.Public | BindingFlags.Instance);
                var requiredField = item.GetType().GetField("requiredAtStart", BindingFlags.Public | BindingFlags.Instance);

                var xenotype = xenotypeField?.GetValue(item) as XenotypeDef;
                int count = countField != null ? (int)countField.GetValue(item) : 1;
                bool required = requiredField != null && (bool)requiredField.GetValue(item);

                string xenoLabel = xenotype?.LabelCap ?? "Unknown".Translate().CapitalizeFirst();
                string label = ListItemCountLabel(count, xenoLabel, required);

                var listItem = new ScenarioBuilderState.ListItemData
                {
                    Label = label,
                    Index = i,
                    ItemReference = item,
                    IsExpanded = false,
                    Fields = new List<ScenarioBuilderState.PartField>()
                };

                int capturedIndex = i;
                listItem.Fields.Add(new ScenarioBuilderState.PartField
                {
                    Name = (string)"RimWorldAccess.ScenarioBuilder.Field.Count".Translate(),
                    Type = ScenarioBuilderState.FieldType.Quantity,
                    CurrentValue = count.ToString(),
                    Data = new int[] { 1, 10 },
                    SetValue = (val) =>
                    {
                        var list = xenotypeCountsField.GetValue(part) as System.Collections.IList;
                        if (list != null && capturedIndex < list.Count)
                        {
                            var entry = list[capturedIndex];
                            // MUTATION-C: mirrors ScenPart_ConfigPage_ConfigureStartingPawns_Xenotypes' per-row count TextFieldNumeric(1,10) (decompiled RimWorld/ScenPart_ConfigPage_ConfigureStartingPawns_Xenotypes.cs:26-83).
                            countField.SetValue(entry, Convert.ToInt32(val));
                        }
                    }
                });

                listItem.Fields.Add(new ScenarioBuilderState.PartField
                {
                    Name = (string)"RimWorldAccess.ScenarioBuilder.Field.Xenotype".Translate(),
                    Type = ScenarioBuilderState.FieldType.Dropdown,
                    CurrentValue = xenoLabel,
                    Data = ScenarioBuilderState.GetXenotypeDefOptions(),
                    SetValue = (val) =>
                    {
                        var list = xenotypeCountsField.GetValue(part) as System.Collections.IList;
                        if (list != null && capturedIndex < list.Count)
                        {
                            var entry = list[capturedIndex];
                            // MUTATION-C: same file's xenotype ButtonText, FloatMenu over all XenotypeDef (decompiled RimWorld/ScenPart_ConfigPage_ConfigureStartingPawns_Xenotypes.cs:26-83).
                            xenotypeField.SetValue(entry, val);
                        }
                    }
                });

                listItem.Fields.Add(new ScenarioBuilderState.PartField
                {
                    Name = (string)"RimWorldAccess.ScenarioBuilder.Field.RequiredAtStart".Translate(),
                    Type = ScenarioBuilderState.FieldType.Checkbox,
                    CurrentValue = ScenarioBuilderState.BoolDisplay(required),
                    BoolValue = required,
                    Data = null,
                    SetValue = (val) =>
                    {
                        var list = xenotypeCountsField.GetValue(part) as System.Collections.IList;
                        if (list != null && capturedIndex < list.Count)
                        {
                            var entry = list[capturedIndex];
                            bool newVal = val is bool b && b;
                            // MUTATION-C: same file's row-level Required Widgets.Checkbox (decompiled RimWorld/ScenPart_ConfigPage_ConfigureStartingPawns_Xenotypes.cs:26-83).
                            requiredField.SetValue(entry, newVal);
                        }
                    }
                });

                listItems.Add(listItem);
            }

            return listItems;
        }

        private static void AddXenotypeItem(ScenPart part, FieldInfo xenotypeCountsField)
        {
            var xenotypeCounts = xenotypeCountsField.GetValue(part) as System.Collections.IList;
            if (xenotypeCounts != null)
            {
                var xenoCountType = AccessTools.TypeByName("RimWorld.XenotypeCount");
                var newItem = Activator.CreateInstance(xenoCountType);
                var xenotypeField = xenoCountType.GetField("xenotype", BindingFlags.Public | BindingFlags.Instance);
                var countField = xenoCountType.GetField("count", BindingFlags.Public | BindingFlags.Instance);
                // MUTATION-C: mirrors the same file's Add button, seeding a new XenotypeCount defaulting to Baseliner/count 1 (decompiled RimWorld/ScenPart_ConfigPage_ConfigureStartingPawns_Xenotypes.cs:26-83).
                xenotypeField.SetValue(newItem, XenotypeDefOf.Baseliner);
                // MUTATION-C: same Add-button seed (see above).
                countField.SetValue(newItem, 1);
                xenotypeCounts.Add(newItem);
                ScenarioBuilderState.SetDirty();
                TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.AddedXenotypeEntry".Loc());
            }
        }

        /// <summary>List items for ScenPart_ConfigPage_ConfigureStartingPawns_Mutants.</summary>
        private static List<ScenarioBuilderState.ListItemData> ExtractMutantListItems(ScenPart part, FieldInfo mutantCountsField)
        {
            var listItems = new List<ScenarioBuilderState.ListItemData>();
            if (!ModsConfig.AnomalyActive) return listItems;

            var mutantCounts = mutantCountsField.GetValue(part) as System.Collections.IList;
            if (mutantCounts == null) return listItems;

            for (int i = 0; i < mutantCounts.Count; i++)
            {
                var item = mutantCounts[i];
                var mutantField = item.GetType().GetField("mutant", BindingFlags.Public | BindingFlags.Instance);
                var countField = item.GetType().GetField("count", BindingFlags.Public | BindingFlags.Instance);
                var requiredField = item.GetType().GetField("requiredAtStart", BindingFlags.Public | BindingFlags.Instance);

                var mutant = mutantField?.GetValue(item) as MutantDef;
                int count = countField != null ? (int)countField.GetValue(item) : 1;
                bool required = requiredField != null && (bool)requiredField.GetValue(item);

                string mutantLabel = mutant?.LabelCap ?? "None".Translate().CapitalizeFirst();
                string label = ListItemCountLabel(count, mutantLabel, required);

                var listItem = new ScenarioBuilderState.ListItemData
                {
                    Label = label,
                    Index = i,
                    ItemReference = item,
                    IsExpanded = false,
                    Fields = new List<ScenarioBuilderState.PartField>()
                };

                int capturedIndex = i;
                listItem.Fields.Add(new ScenarioBuilderState.PartField
                {
                    Name = (string)"RimWorldAccess.ScenarioBuilder.Field.Count".Translate(),
                    Type = ScenarioBuilderState.FieldType.Quantity,
                    CurrentValue = count.ToString(),
                    Data = new int[] { 1, 10 },
                    SetValue = (val) =>
                    {
                        var list = mutantCountsField.GetValue(part) as System.Collections.IList;
                        if (list != null && capturedIndex < list.Count)
                        {
                            var entry = list[capturedIndex];
                            // MUTATION-C: mirrors ScenPart_ConfigPage_ConfigureStartingPawns_Mutants' per-row count TextFieldNumeric(1,10) (decompiled RimWorld/ScenPart_ConfigPage_ConfigureStartingPawns_Mutants.cs:24-88).
                            countField.SetValue(entry, Convert.ToInt32(val));
                        }
                    }
                });

                listItem.Fields.Add(new ScenarioBuilderState.PartField
                {
                    Name = (string)"RimWorldAccess.ScenarioBuilder.Field.MutantType".Translate(),
                    Type = ScenarioBuilderState.FieldType.Dropdown,
                    CurrentValue = mutantLabel,
                    Data = ScenarioBuilderState.GetMutantDefOptions(),
                    SetValue = (val) =>
                    {
                        var list = mutantCountsField.GetValue(part) as System.Collections.IList;
                        if (list != null && capturedIndex < list.Count)
                        {
                            var entry = list[capturedIndex];
                            // MUTATION-C: same file's mutant ButtonText, FloatMenu None + MutantDefs where showInScenarioEditor (decompiled RimWorld/ScenPart_ConfigPage_ConfigureStartingPawns_Mutants.cs:24-88).
                            mutantField.SetValue(entry, val);
                        }
                    }
                });

                listItem.Fields.Add(new ScenarioBuilderState.PartField
                {
                    Name = (string)"RimWorldAccess.ScenarioBuilder.Field.RequiredAtStart".Translate(),
                    Type = ScenarioBuilderState.FieldType.Checkbox,
                    CurrentValue = ScenarioBuilderState.BoolDisplay(required),
                    BoolValue = required,
                    Data = null,
                    SetValue = (val) =>
                    {
                        var list = mutantCountsField.GetValue(part) as System.Collections.IList;
                        if (list != null && capturedIndex < list.Count)
                        {
                            var entry = list[capturedIndex];
                            bool newVal = val is bool b && b;
                            // MUTATION-C: same file's row-level Required Widgets.Checkbox (decompiled RimWorld/ScenPart_ConfigPage_ConfigureStartingPawns_Mutants.cs:24-88).
                            requiredField.SetValue(entry, newVal);
                        }
                    }
                });

                listItems.Add(listItem);
            }

            return listItems;
        }

        private static void AddMutantItem(ScenPart part, FieldInfo mutantCountsField)
        {
            var mutantCounts = mutantCountsField.GetValue(part) as System.Collections.IList;
            if (mutantCounts != null)
            {
                var mutantCountType = AccessTools.TypeByName("RimWorld.MutantCount");
                var newItem = Activator.CreateInstance(mutantCountType);
                var countField = mutantCountType.GetField("count", BindingFlags.Public | BindingFlags.Instance);
                // MUTATION-C: mirrors the same file's Add button, seeding a new MutantCount with count 1, mutant left null/None (decompiled RimWorld/ScenPart_ConfigPage_ConfigureStartingPawns_Mutants.cs:24-88).
                countField.SetValue(newItem, 1);
                // A null mutant field means "None".
                mutantCounts.Add(newItem);
                ScenarioBuilderState.SetDirty();
                TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.AddedMutantEntry".Loc());
            }
        }

        /// <summary>List items for ScenPart_PlanetLayer connections.</summary>
        private static List<ScenarioBuilderState.ListItemData> ExtractLayerConnectionItems(ScenPart part, FieldInfo connectionsField)
        {
            var listItems = new List<ScenarioBuilderState.ListItemData>();

            var connections = connectionsField.GetValue(part) as System.Collections.IList;
            if (connections == null) return listItems;

            var connectionsList = new List<object>();
            foreach (var conn in connections)
                connectionsList.Add(conn);

            for (int i = 0; i < connections.Count; i++)
            {
                var item = connections[i];
                var tagField = VanillaAccess.GetField(item.GetType(), "tag");
                var zoomModeField = item.GetType().GetField("zoomMode", BindingFlags.Public | BindingFlags.Instance);
                var fuelCostField = item.GetType().GetField("fuelCost", BindingFlags.Public | BindingFlags.Instance);

                string tag = tagField?.GetValue(item) as string ?? "";
                var zoomMode = zoomModeField?.GetValue(item);
                float fuelCost = fuelCostField != null ? (float)fuelCostField.GetValue(item) : 0f;

                // zoomMode.ToString() is the language-independent enum name, used only to decide
                // presence; the spoken zoom word is localized via ZoomModeDisplay.
                string zoomEnum = zoomMode?.ToString() ?? "None";
                string label = zoomEnum != "None"
                    ? (string)"RimWorldAccess.ScenarioBuilder.ConnectionToWithZoom".Translate(tag, ScenarioBuilderState.ZoomModeDisplay(zoomEnum))
                    : (string)"RimWorldAccess.ScenarioBuilder.ConnectionTo".Translate(tag);

                var listItem = new ScenarioBuilderState.ListItemData
                {
                    Label = label,
                    Index = i,
                    ItemReference = item,
                    IsExpanded = false,
                    Fields = new List<ScenarioBuilderState.PartField>()
                };

                int capturedIndex = i;

                listItem.Fields.Add(new ScenarioBuilderState.PartField
                {
                    Name = (string)"RimWorldAccess.ScenarioBuilder.Field.TargetLayer".Translate(),
                    Type = ScenarioBuilderState.FieldType.Dropdown,
                    CurrentValue = tag,
                    Data = ScenarioBuilderState.GetAvailableTagOptions(part, connectionsList),
                    SetValue = (val) =>
                    {
                        if (val?.ToString() == "__REMOVE__")
                        {
                            var list = connectionsField.GetValue(part) as System.Collections.IList;
                            if (list != null && capturedIndex < list.Count)
                            {
                                list.RemoveAt(capturedIndex);
                            }
                        }
                        else
                        {
                            var list = connectionsField.GetValue(part) as System.Collections.IList;
                            if (list != null && capturedIndex < list.Count)
                            {
                                var entry = list[capturedIndex];
                                // MUTATION-C: mirrors ScenPart_PlanetLayer.DoConnections' per-connection tag ButtonText, including its Remove option (decompiled RimWorld/ScenPart_PlanetLayer.cs:238-347).
                                tagField.SetValue(entry, val?.ToString());
                            }
                        }
                    }
                });

                listItem.Fields.Add(new ScenarioBuilderState.PartField
                {
                    Name = (string)"RimWorldAccess.ScenarioBuilder.Field.ZoomMode".Translate(),
                    Type = ScenarioBuilderState.FieldType.Dropdown,
                    CurrentValue = ScenarioBuilderState.ZoomModeDisplay(zoomEnum),
                    Data = ScenarioBuilderState.GetZoomModeOptions(),
                    SetValue = (val) =>
                    {
                        var list = connectionsField.GetValue(part) as System.Collections.IList;
                        if (list == null || capturedIndex >= list.Count) return;
                        var entry = list[capturedIndex];
                        // MUTATION-C: mirrors ScenPart_PlanetLayer.DoConnections' zoomMode ButtonText, None/ZoomIn/ZoomOut (decompiled RimWorld/ScenPart_PlanetLayer.cs:277-300); dedup continues below.
                        zoomModeField.SetValue(entry, val);

                        // MUTATION-C: mirrors ScenPart_PlanetLayer.DoConnections' zoomMode
                        // FloatMenuOption delegates (decompiled RimWorld/ScenPart_PlanetLayer.cs
                        // L277-300) — only one connection may be "the" zoom-in and one "the"
                        // zoom-out, so picking either clears that same mode off every OTHER
                        // connection on this part. Not a callable vanilla method (the dedup is
                        // inline in the delegate, not a method DoConnections calls).
                        string newModeName = val?.ToString();
                        if ((newModeName == "ZoomIn" || newModeName == "ZoomOut") && val.GetType().IsEnum)
                        {
                            object none = Enum.Parse(val.GetType(), "None");
                            for (int j = 0; j < list.Count; j++)
                            {
                                if (j == capturedIndex) continue;
                                var otherEntry = list[j];
                                if (zoomModeField.GetValue(otherEntry)?.ToString() == newModeName)
                                    // MUTATION-C: continuation of the same DoConnections zoomMode dedup (decompiled RimWorld/ScenPart_PlanetLayer.cs:277-300) — only one connection may hold each zoom mode.
                                    zoomModeField.SetValue(otherEntry, none);
                            }
                        }
                    }
                });

                listItem.Fields.Add(new ScenarioBuilderState.PartField
                {
                    Name = (string)"RimWorldAccess.ScenarioBuilder.Field.FuelCost".Translate(),
                    Type = ScenarioBuilderState.FieldType.Quantity,
                    CurrentValue = fuelCost.ToString("F1"),
                    // MUTATION-C: Widgets.TextFieldNumeric with no explicit min/max, so the
                    // widget's own 0f..1E9f default — not the {0, 10000} range hand-picked here.
                    Data = new float[] { 0f, 1_000_000_000f },
                    SetValue = (val) =>
                    {
                        var list = connectionsField.GetValue(part) as System.Collections.IList;
                        if (list != null && capturedIndex < list.Count)
                        {
                            var entry = list[capturedIndex];
                            // MUTATION-C: Widgets.TextFieldNumeric with no explicit min/max (decompiled RimWorld/ScenPart_PlanetLayer.cs:238-347); widget's own 0f..1E9f default.
                            fuelCostField.SetValue(entry, Convert.ToSingle(val));
                        }
                    }
                });

                listItems.Add(listItem);
            }

            return listItems;
        }

        private static void AddLayerConnectionItem(ScenPart part, FieldInfo connectionsField)
        {
            var connections = connectionsField.GetValue(part) as System.Collections.IList;
            if (connections != null)
            {
                var usedTags = new HashSet<string>();
                foreach (var conn in connections)
                {
                    var tagField = VanillaAccess.GetField(conn.GetType(), "tag");
                    var tag = tagField?.GetValue(conn) as string;
                    if (!string.IsNullOrEmpty(tag))
                        usedTags.Add(tag);
                }

                // The first tag available from other PlanetLayer parts.
                string availableTag = null;
                var currentScenario = ScenarioBuilderState.CurrentScenario;
                if (currentScenario != null)
                {
                    foreach (var otherPart in currentScenario.AllParts)
                    {
                        if (otherPart == part || !(otherPart is ScenPart_PlanetLayer)) continue;

                        var tagField = VanillaAccess.GetField(otherPart.GetType(), "tag");
                        var tag = tagField?.GetValue(otherPart) as string;
                        if (!string.IsNullOrEmpty(tag) && !usedTags.Contains(tag))
                        {
                            availableTag = tag;
                            break;
                        }
                    }
                }

                if (availableTag == null)
                {
                    TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.NoLayerTagsAvailable".Loc());
                    return;
                }

                var connectionType = AccessTools.TypeByName("RimWorld.LayerConnection");
                var newItem = Activator.CreateInstance(connectionType);
                var newTagField = VanillaAccess.GetField(connectionType, "tag");
                // MUTATION-C: mirrors ScenPart_PlanetLayer.DoConnections' Add-connection button, seeding a new LayerConnection on the first unused tag (decompiled RimWorld/ScenPart_PlanetLayer.cs:238-347).
                newTagField.SetValue(newItem, availableTag);
                connections.Add(newItem);
                ScenarioBuilderState.SetDirty();
                TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.AddedConnectionTo".Loc(availableTag));
            }
        }

        #endregion

        #region Generic Tier-3 list fallback (modded ScenParts)

        /// <summary>
        /// A MODDED ScenPart's qualifying public List&lt;T&gt; field for the generic list tier, or
        /// null when it has none or the shape cannot be built soundly. Considered only for types
        /// outside the RimWorld namespace, the same gate
        /// ScenarioBuilderState.AddGenericModdedFields uses. Requires exactly one qualifying field:
        /// two candidate lists are left unclaimed rather than guessing which the author meant.
        /// Internal so AddGenericModdedFields can skip a field this tier already owns.
        /// </summary>
        internal static FieldInfo GetGenericListField(Type partType)
        {
            if (partType.Namespace == "RimWorld") return null;

            FieldInfo candidate = null;
            foreach (var fi in partType.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (!fi.FieldType.IsGenericType || fi.FieldType.GetGenericTypeDefinition() != typeof(List<>))
                    continue;

                var elemType = fi.FieldType.GetGenericArguments()[0];
                bool qualifies = typeof(Def).IsAssignableFrom(elemType) || IsGenericCountPairShape(elemType);
                if (!qualifies) continue;

                if (candidate != null) return null; // ambiguous - don't claim either
                candidate = fi;
            }
            return candidate;
        }

        /// <summary>
        /// A wrapper class qualifies for the generic count-pair tier the way
        /// PawnKindCount/XenotypeCount/MutantCount do: a public parameterless constructor, so a new
        /// entry can be built, and at least one public Def-typed field, so the entry is immediately
        /// meaningful and Dropdown-editable rather than a blank read-only row.
        /// </summary>
        private static bool IsGenericCountPairShape(Type elemType)
        {
            if (elemType.IsAbstract || elemType.GetConstructor(Type.EmptyTypes) == null)
                return false;
            return elemType.GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Any(f => typeof(Def).IsAssignableFrom(f.FieldType));
        }

        /// <summary>
        /// List items for a modded part's generic List&lt;T&gt; field. T is either a Def or a wrapper
        /// class satisfying <see cref="IsGenericCountPairShape"/>, the two shapes
        /// <see cref="GetGenericListField"/> restricts the field to.
        /// </summary>
        private static List<ScenarioBuilderState.ListItemData> ExtractGenericListItems(ScenPart part, FieldInfo listField)
        {
            var result = new List<ScenarioBuilderState.ListItemData>();
            var list = listField.GetValue(part) as System.Collections.IList;
            if (list == null) return result;

            var elemType = listField.FieldType.GetGenericArguments()[0];
            bool elementIsDef = typeof(Def).IsAssignableFrom(elemType);

            for (int i = 0; i < list.Count; i++)
            {
                var item = list[i];
                int capturedIndex = i;
                var listItem = new ScenarioBuilderState.ListItemData
                {
                    Index = i,
                    ItemReference = item,
                    IsExpanded = false,
                    Fields = new List<ScenarioBuilderState.PartField>()
                };

                if (elementIsDef)
                {
                    var def = item as Def;
                    listItem.Label = def?.LabelCap ?? (string)"Unknown".Translate().CapitalizeFirst();
                    listItem.Fields.Add(new ScenarioBuilderState.PartField
                    {
                        Name = (string)"RimWorldAccess.ScenarioBuilder.Field.Value".Translate(),
                        Type = ScenarioBuilderState.FieldType.Dropdown,
                        CurrentValue = listItem.Label,
                        Data = ScenarioBuilderState.GetGenericDefOptions(elemType, includeNone: item == null),
                        SetValue = (val) =>
                        {
                            var liveList = listField.GetValue(part) as System.Collections.IList;
                            if (liveList != null && capturedIndex < liveList.Count)
                                liveList[capturedIndex] = val;
                        }
                    });
                }
                else
                {
                    // Wrapper shape: anchor the label on the first Def-typed field's value, then
                    // present every public field as AddGenericModdedFields presents a part's own.
                    var anchorDefField = elemType.GetFields(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(f => typeof(Def).IsAssignableFrom(f.FieldType));
                    var anchorDef = anchorDefField?.GetValue(item) as Def;
                    listItem.Label = anchorDef?.LabelCap ?? (string)"Unknown".Translate().CapitalizeFirst();

                    foreach (var fi in elemType.GetFields(BindingFlags.Public | BindingFlags.Instance))
                    {
                        var capturedField = fi;
                        // Re-fetches the live list entry by index at SetValue time rather than
                        // mutating the captured item, as every vanilla list-item closure above does.
                        var built = ScenarioBuilderState.BuildGenericField(item, capturedField, val =>
                        {
                            var liveList = listField.GetValue(part) as System.Collections.IList;
                            if (liveList != null && capturedIndex < liveList.Count)
                                // MUTATION-C: third-party wrapper-shape list item, no vanilla DoEditInterface exists to cite — generic bool/enum/Def adapter (mirrors ScenarioBuilderState.BuildGenericField's own rules).
                                capturedField.SetValue(liveList[capturedIndex], val);
                        });
                        if (built != null) listItem.Fields.Add(built);
                    }
                }

                result.Add(listItem);
            }

            return result;
        }

        private static void AddGenericListItem(ScenPart part, FieldInfo listField)
        {
            var list = listField.GetValue(part) as System.Collections.IList;
            if (list == null) return;

            var elemType = listField.FieldType.GetGenericArguments()[0];

            if (typeof(Def).IsAssignableFrom(elemType))
            {
                list.Add(GetFirstDefOrNull(elemType));
            }
            else
            {
                var newItem = Activator.CreateInstance(elemType);
                var defField = elemType.GetFields(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(f => typeof(Def).IsAssignableFrom(f.FieldType));
                if (defField != null)
                {
                    object firstDef = GetFirstDefOrNull(defField.FieldType);
                    // MUTATION-C: third-party generic list Add seed, no vanilla vehicle — first DefDatabase entry for a deterministic non-null starting value (see GetGenericListField's doc above).
                    if (firstDef != null) defField.SetValue(newItem, firstDef);
                }
                list.Add(newItem);
            }

            ScenarioBuilderState.SetDirty();
            TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.AddedGenericListEntry".Loc());
        }

        /// <summary>
        /// The first def of an arbitrary Def-derived type via reflection over
        /// DefDatabase&lt;T&gt;.AllDefsListForReading, stopping at the first entry: this needs only a
        /// deterministic non-null seed for a new list entry.
        /// </summary>
        private static object GetFirstDefOrNull(Type defType)
        {
            var dbType = typeof(DefDatabase<>).MakeGenericType(defType);
            var allDefsProp = dbType.GetProperty("AllDefsListForReading", BindingFlags.Public | BindingFlags.Static);
            if (allDefsProp?.GetValue(null) is System.Collections.IEnumerable allDefs)
            {
                foreach (var def in allDefs) return def;
            }
            return null;
        }

        #endregion
    }
}
