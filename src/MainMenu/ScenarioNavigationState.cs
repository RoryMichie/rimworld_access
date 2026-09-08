using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.Steam;

namespace RimWorldAccess
{
    /// <summary>
    /// Data and mutation vehicles for the scenario selection page; all keyboard navigation,
    /// list/detail state, and typeahead live in
    /// <see cref="RimWorldAccess.Shell.ScenarioSelectScreenScope"/>.
    /// <see cref="BuildDetailTree"/> restructures a scenario's <c>GetFullInformationText</c>
    /// content into StartWith/MapScatteredWith/CreateIncident/DisableIncident/
    /// PermanentGameCondition sections, which the scope flattens into its Details region.
    /// <see cref="DeleteSelectedScenario"/> and <see cref="UnsubscribeSelectedScenario"/> are
    /// vanilla-mirrored confirm-then-mutate flows, scoped to a scenario plus a callback.
    /// Nothing here is static state: the scope rebuilds its rows from
    /// <see cref="ScenarioLister"/> on every RefreshContent.
    /// </summary>
    public static class ScenarioNavigationState
    {
        // ScenPart_CreateIncident is internal in Assembly-CSharp, so it cannot be named in an
        // `is` pattern the way the other branches are; matched by reference equality instead.
        private static readonly System.Type ScenPartCreateIncidentType = AccessTools.TypeByName("RimWorld.ScenPart_CreateIncident");

        /// <summary>
        /// Builds the read-only detail tree for <paramref name="selected"/>: its description,
        /// then its ScenPart summaries as expandable sections (start-with, map-scattered,
        /// create/disable incident, permanent game condition) with everything else flat — the
        /// one run-on block <see cref="Scenario.GetFullInformationText"/> shows, made navigable.
        /// </summary>
        internal static InspectionTreeItem BuildDetailTree(Scenario selected)
        {
            var root = new InspectionTreeItem
            {
                Label = "RimWorldAccess.ScenarioSelect.DetailsLabel".Translate(),
                IndentLevel = -1,
                IsExpanded = true,
                IsExpandable = false
            };

            if (selected == null) return root;

            var addedContent = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

            // The description, with any "Note:" portion split into its own row.
            string noteFromDescription = null;
            if (!string.IsNullOrWhiteSpace(selected.description))
            {
                string desc = selected.description.Trim();

                int noteIndex = desc.IndexOf("\nNote:", System.StringComparison.OrdinalIgnoreCase);
                if (noteIndex < 0)
                    noteIndex = desc.IndexOf("\n\nNote:", System.StringComparison.OrdinalIgnoreCase);

                if (noteIndex >= 0)
                {
                    string mainDesc = desc.Substring(0, noteIndex).Trim();
                    noteFromDescription = desc.Substring(noteIndex).Trim();

                    root.Children.Add(new InspectionTreeItem
                    {
                        Label = "RimWorldAccess.ScenarioSelect.DescriptionLine".Translate(mainDesc),
                        IndentLevel = 0,
                        IsExpandable = false,
                        Parent = root
                    });
                    addedContent.Add(mainDesc);

                    root.Children.Add(new InspectionTreeItem
                    {
                        Label = noteFromDescription,
                        IndentLevel = 0,
                        IsExpandable = false,
                        Parent = root
                    });
                    addedContent.Add(noteFromDescription);
                }
                else
                {
                    root.Children.Add(new InspectionTreeItem
                    {
                        Label = "RimWorldAccess.ScenarioSelect.DescriptionLine".Translate(desc),
                        IndentLevel = 0,
                        IsExpandable = false,
                        Parent = root
                    });
                    addedContent.Add(desc);
                }
            }

            var startWithItems = new List<string>();
            var mapScatteredItems = new List<string>();
            var createIncidentItems = new List<string>();
            var disableIncidentItems = new List<string>();
            var permaGameConditionItems = new List<string>();
            string pawnCountLine = null;
            var researchLines = new List<string>();
            var otherSummaries = new List<string>();

            foreach (ScenPart part in selected.AllParts)
            {
                if (!part.visible)
                    continue;

                // Structured entries, never parsed out of the summary text.
                foreach (string entry in part.GetSummaryListEntries("PlayerStartsWith"))
                {
                    if (!string.IsNullOrWhiteSpace(entry))
                        startWithItems.Add(entry);
                }

                foreach (string entry in part.GetSummaryListEntries("MapScatteredWith"))
                {
                    if (!string.IsNullOrWhiteSpace(entry))
                        mapScatteredItems.Add(entry);
                }

                foreach (string entry in part.GetSummaryListEntries("CreateIncident"))
                {
                    if (!string.IsNullOrWhiteSpace(entry))
                        createIncidentItems.Add(entry);
                }

                foreach (string entry in part.GetSummaryListEntries("DisableIncident"))
                {
                    if (!string.IsNullOrWhiteSpace(entry))
                        disableIncidentItems.Add(entry);
                }

                foreach (string entry in part.GetSummaryListEntries("PermaGameCondition"))
                {
                    if (!string.IsNullOrWhiteSpace(entry))
                        permaGameConditionItems.Add(entry);
                }

                System.Type partType = part.GetType();

                // Parts whose content already came from GetSummaryListEntries.
                if (part is ScenPart_StartingThing_Defined ||
                    part is ScenPart_StartingAnimal ||
                    part is ScenPart_StartingMech ||
                    part is ScenPart_ScatterThings ||
                    partType == ScenPartCreateIncidentType ||
                    part is ScenPart_DisableIncident ||
                    part is ScenPart_GameCondition ||
                    part is ScenPart_PermaGameCondition)
                {
                    continue;
                }

                string summary = part.Summary(selected);
                if (string.IsNullOrWhiteSpace(summary))
                    continue;

                if (part is ScenPart_ConfigPage_ConfigureStartingPawnsBase)
                {
                    pawnCountLine = summary.Trim();
                }
                else if (part is ScenPart_StartingResearch)
                {
                    researchLines.Add(summary.Trim());
                }
                else if (part is ScenPart_PlayerFaction)
                {
                    // Read the faction off the live ScenPart: string surgery on vanilla's
                    // "Your faction will be a {0}." sentence would be English-only.
                    var factionDef = AccessTools.Field(typeof(ScenPart_PlayerFaction), "factionDef").GetValue(part) as FactionDef;
                    if (factionDef != null)
                    {
                        string line = "RimWorldAccess.ScenarioSelect.PlayerFactionWillBe".Translate(factionDef.LabelCap);
                        if (!addedContent.Contains(line))
                        {
                            otherSummaries.Add(line);
                            addedContent.Add(line);
                        }
                    }
                }
                else
                {
                    foreach (string line in summary.Split('\n'))
                    {
                        string trimmed = line.Trim();
                        if (string.IsNullOrWhiteSpace(trimmed))
                            continue;

                        if (addedContent.Contains(trimmed))
                            continue;

                        // List items and section headers: already structured above.
                        if (trimmed.StartsWith("-") || line.StartsWith("   -"))
                            continue;

                        if (IsListHeader(trimmed))
                            continue;

                        otherSummaries.Add(trimmed);
                        addedContent.Add(trimmed);
                    }
                }
            }

            foreach (string line in otherSummaries)
            {
                root.Children.Add(new InspectionTreeItem
                {
                    Label = line,
                    IndentLevel = 0,
                    IsExpandable = false,
                    Parent = root
                });
            }

            bool hasStartWithContent = pawnCountLine != null ||
                                       startWithItems.Count > 0 ||
                                       researchLines.Count > 0;
            if (hasStartWithContent)
            {
                var startWithSection = new InspectionTreeItem
                {
                    Label = "RimWorldAccess.ScenarioSelect.StartWith".Translate(),
                    IndentLevel = 0,
                    IsExpandable = true,
                    IsExpanded = false,
                    Parent = root
                };

                if (pawnCountLine != null)
                {
                    startWithSection.Children.Add(new InspectionTreeItem
                    {
                        Label = pawnCountLine,
                        IndentLevel = 1,
                        IsExpandable = false,
                        Parent = startWithSection
                    });
                }

                foreach (string label in startWithItems)
                {
                    startWithSection.Children.Add(new InspectionTreeItem
                    {
                        Label = label,
                        IndentLevel = 1,
                        IsExpandable = false,
                        Parent = startWithSection
                    });
                }

                foreach (string research in researchLines)
                {
                    startWithSection.Children.Add(new InspectionTreeItem
                    {
                        Label = research,
                        IndentLevel = 1,
                        IsExpandable = false,
                        Parent = startWithSection
                    });
                }

                root.Children.Add(startWithSection);
            }

            if (mapScatteredItems.Count > 0)
            {
                var mapScatteredSection = new InspectionTreeItem
                {
                    Label = "RimWorldAccess.ScenarioSelect.MapScatteredWith".Translate(),
                    IndentLevel = 0,
                    IsExpandable = true,
                    IsExpanded = false,
                    Parent = root
                };
                foreach (string label in mapScatteredItems)
                {
                    mapScatteredSection.Children.Add(new InspectionTreeItem
                    {
                        Label = label,
                        IndentLevel = 1,
                        IsExpandable = false,
                        Parent = mapScatteredSection
                    });
                }
                root.Children.Add(mapScatteredSection);
            }

            if (createIncidentItems.Count > 0)
            {
                var createIncidentSection = new InspectionTreeItem
                {
                    Label = "RimWorldAccess.ScenarioSelect.CreateIncident".Translate(),
                    IndentLevel = 0,
                    IsExpandable = true,
                    IsExpanded = false,
                    Parent = root
                };
                foreach (string label in createIncidentItems)
                {
                    createIncidentSection.Children.Add(new InspectionTreeItem
                    {
                        Label = label,
                        IndentLevel = 1,
                        IsExpandable = false,
                        Parent = createIncidentSection
                    });
                }
                root.Children.Add(createIncidentSection);
            }

            if (disableIncidentItems.Count > 0)
            {
                var disableIncidentSection = new InspectionTreeItem
                {
                    Label = "RimWorldAccess.ScenarioSelect.DisableIncident".Translate(),
                    IndentLevel = 0,
                    IsExpandable = true,
                    IsExpanded = false,
                    Parent = root
                };
                foreach (string label in disableIncidentItems)
                {
                    disableIncidentSection.Children.Add(new InspectionTreeItem
                    {
                        Label = label,
                        IndentLevel = 1,
                        IsExpandable = false,
                        Parent = disableIncidentSection
                    });
                }
                root.Children.Add(disableIncidentSection);
            }

            if (permaGameConditionItems.Count > 0)
            {
                var permaGameConditionSection = new InspectionTreeItem
                {
                    Label = "RimWorldAccess.ScenarioSelect.PermanentGameCondition".Translate(),
                    IndentLevel = 0,
                    IsExpandable = true,
                    IsExpanded = false,
                    Parent = root
                };
                foreach (string label in permaGameConditionItems)
                {
                    permaGameConditionSection.Children.Add(new InspectionTreeItem
                    {
                        Label = label,
                        IndentLevel = 1,
                        IsExpandable = false,
                        Parent = permaGameConditionSection
                    });
                }
                root.Children.Add(permaGameConditionSection);
            }

            return root;
        }

        /// <summary>
        /// Whether a line is one of the list-section headers ScenSummaryList.SummaryWithList
        /// embeds for a tag already extracted structurally. Compared against vanilla's own
        /// translated header text, never an English fragment.
        /// </summary>
        private static bool IsListHeader(string line)
        {
            foreach (string header in KnownSummaryListHeaders())
            {
                if (string.Equals(line, header, System.StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// The header lines vanilla's ScenSummaryList.SummaryWithList produces for every tag
        /// collected structurally above.
        /// </summary>
        private static IEnumerable<string> KnownSummaryListHeaders()
        {
            yield return ScenPart_StartingThing_Defined.PlayerStartWithIntro + ":";
            yield return "ScenPart_MapScatteredWith".Translate() + ":";
            yield return "ScenPart_CreateIncident".Translate() + ":";
            yield return "ScenPart_DisableIncident".Translate() + ":";
            yield return "ScenPart_PermaGameCondition".Translate() + ":";
        }

        /// <summary>
        /// Confirms then deletes a CustomLocal scenario file, mirroring
        /// <c>Page_SelectScenario.DoScenarioListEntry</c>'s Delete icon branch.
        /// <paramref name="onDeleted"/> runs after the announcement, so the scope can rebuild
        /// its rows and restore the cursor.
        /// </summary>
        public static void DeleteSelectedScenario(Scenario selected, System.Action onDeleted)
        {
            if (selected == null || selected.Category != ScenarioCategory.CustomLocal)
            {
                Log.Warning("[RimWorld Access] DeleteSelectedScenario called for a non-CustomLocal scenario.");
                return;
            }

            string fileName = selected.File?.Name ?? selected.name;
            string scenarioName = selected.name;

            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "ConfirmDelete".Translate(fileName),
                delegate
                {
                    // Vehicle A: the vanilla Delete icon's own action.
                    selected.File.Delete();
                    ScenarioLister.MarkDirty();
                    TolkHelper.Speak("RimWorldAccess.ScenarioSelect.Deleted".Loc(scenarioName));
                    onDeleted?.Invoke();
                },
                destructive: true
            ));
        }

        /// <summary>
        /// Confirms then unsubscribes from a SteamWorkshop scenario, mirroring
        /// <c>Page_SelectScenario.DoScenarioListEntry</c>'s Unsubscribe icon branch.
        /// </summary>
        public static void UnsubscribeSelectedScenario(Scenario selected, System.Action onUnsubscribed)
        {
            if (selected == null || selected.Category != ScenarioCategory.SteamWorkshop)
            {
                Log.Warning("[RimWorld Access] UnsubscribeSelectedScenario called for a non-Workshop scenario.");
                return;
            }

            string fileName = selected.File?.Name ?? selected.name;
            string scenarioName = selected.name;

            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "ConfirmUnsubscribeFrom".Translate(fileName),
                delegate
                {
                    // MUTATION-C: mirrors the vanilla Unsubscribe icon's own field write
                    // (decompiled Page_SelectScenario.cs:165) — no gated setter exists for the
                    // enabled bool.
                    selected.enabled = false;

                    // Vehicle A: Workshop.Unsubscribe, internal so reflected.
                    var workshopType = AccessTools.TypeByName("Verse.Steam.Workshop");
                    if (workshopType != null)
                    {
                        var unsubscribeMethod = AccessTools.Method(workshopType, "Unsubscribe", new[] { typeof(WorkshopUploadable) });
                        unsubscribeMethod?.Invoke(null, new object[] { selected });
                    }

                    ScenarioLister.MarkDirty();
                    TolkHelper.Speak("RimWorldAccess.ScenarioSelect.UnsubscribedFrom".Loc(scenarioName));
                    onUnsubscribed?.Invoke();
                },
                destructive: true
            ));
        }
    }
}
