using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.Steam;

namespace RimWorldAccess
{
    /// <summary>
    /// Content-line data source for the selected mod's Details region: the author, package id,
    /// version, warning, requirement and description lines, including the manual chunking of long
    /// descriptions, plus the classification of which lines carry a clickable requirement.
    /// Details is region 1 of ModListScreenScope, a live view of whichever mod region 0's cursor
    /// rests on, rebuilt fresh every RefreshContent — there is no list/detail mode and no cache.
    /// </summary>
    internal static class ModListDetailView
    {
        private static List<string> contentLines = new List<string>();

        /// <summary>
        /// Parallel to contentLines: the ModRequirement whose actionable OnClicked a line
        /// represents, null for every other line, inert requirement rows included.
        /// </summary>
        private static List<ModRequirement> lineRequirements = new List<ModRequirement>();

        internal static int ContentLineCount => contentLines.Count;

        internal static string ContentLine(int index) =>
            index >= 0 && index < contentLines.Count ? contentLines[index] : "";

        /// <summary>The requirement a content line represents, or null for a plain line.</summary>
        internal static ModRequirement RequirementAt(int index) =>
            index >= 0 && index < lineRequirements.Count ? lineRequirements[index] : null;

        internal static void Reset()
        {
            contentLines.Clear();
            lineRequirements.Clear();
        }

        /// <summary>What a requirement row's OnClicked will actually do, so this class knows what to announce.</summary>
        private enum RequirementAction
        {
            None,
            OpenUrl,
            SelectMod
        }

        /// <summary>
        /// Vanilla exposes no accessor for "would this row's click do anything", so this mirrors the
        /// private read-only lookups behind ModDependency and ModIncompatibility, through the same
        /// public ModLister call they use, purely to decide what to SAY. The click itself always runs
        /// through req.OnClicked, never through here.
        /// </summary>
        private static RequirementAction ClassifyRequirement(ModRequirement req, out ModMetaData targetMod)
        {
            targetMod = null;

            ModDependency dependency = req as ModDependency;
            if (dependency != null)
            {
                ModMetaData found = ModLister.GetModWithIdentifier(dependency.packageId, ignorePostfix: true);
                if (found == null && !dependency.alternativePackageIds.NullOrEmpty())
                {
                    foreach (string altId in dependency.alternativePackageIds)
                    {
                        found = ModLister.GetModWithIdentifier(altId, ignorePostfix: true);
                        if (found != null) break;
                    }
                }

                if (found == null)
                {
                    return !dependency.Url.NullOrEmpty() ? RequirementAction.OpenUrl : RequirementAction.None;
                }
                if (!found.Active)
                {
                    targetMod = found;
                    return RequirementAction.SelectMod;
                }
                return RequirementAction.None;
            }

            if (req is ModIncompatibility)
            {
                ModMetaData conflicting = ModLister.GetModWithIdentifier(req.packageId, ignorePostfix: true);
                if (conflicting != null && conflicting.Active)
                {
                    targetMod = conflicting;
                    return RequirementAction.SelectMod;
                }
                return RequirementAction.None;
            }

            return RequirementAction.None;
        }

        /// <summary>The affordance sentence appended to a requirement's content line, or null if its OnClicked does nothing.</summary>
        private static string GetRequirementAffordanceSuffix(ModRequirement req)
        {
            ModMetaData ignored;
            switch (ClassifyRequirement(req, out ignored))
            {
                case RequirementAction.OpenUrl:
                    return "RimWorldAccess.ModList.ReqEnterToOpenPage".Translate().ToString();
                case RequirementAction.SelectMod:
                    return "RimWorldAccess.ModList.ReqEnterToSelect".Translate().ToString();
                default:
                    return null;
            }
        }

        /// <summary>
        /// Runs a requirement's vanilla OnClicked and announces what happened. When the click
        /// selected a different mod, mirrors the navigation column and index onto it and reports it
        /// through <paramref name="selectedMod"/>, so the caller can move its own cursor there.
        /// </summary>
        internal static void ActivateRequirement(ModRequirement req, out ModMetaData selectedMod)
        {
            selectedMod = null;
            if (req == null) return;

            ModMetaData targetMod;
            RequirementAction action = ClassifyRequirement(req, out targetMod);
            if (action == RequirementAction.None) return;

            req.OnClicked(ModListState.CurrentPage);

            if (action == RequirementAction.OpenUrl)
            {
                TolkHelper.Speak("RimWorldAccess.ModList.OpeningWebsiteFor".Loc(req.displayName));
            }
            else if (action == RequirementAction.SelectMod && targetMod != null)
            {
                SyncColumnAndIndexTo(targetMod);
                selectedMod = targetMod;
            }
        }

        /// <summary>
        /// Mirrors the navigation column and index onto the mod a requirement's OnClicked just
        /// selected on the real page, so the accessible cursor and vanilla's selection never diverge.
        /// </summary>
        private static void SyncColumnAndIndexTo(ModMetaData targetMod)
        {
            var activeList = ModListVanillaBridge.GetFilteredActiveModList() ?? new List<ModMetaData>();
            int activeIndex = activeList.IndexOf(targetMod);
            if (activeIndex >= 0)
            {
                ModListNavigation.SetColumn(ModListColumn.Active);
                ModListNavigation.SetSelectedIndex(activeIndex);
            }
            else
            {
                var inactiveList = ModListVanillaBridge.GetFilteredInactiveModList() ?? new List<ModMetaData>();
                int inactiveIndex = inactiveList.IndexOf(targetMod);
                ModListNavigation.SetColumn(ModListColumn.Inactive);
                ModListNavigation.SetSelectedIndex(inactiveIndex >= 0 ? inactiveIndex : 0);
            }
        }

        /// <summary>
        /// Builds the detail lines for the row the caller's own region-0 cursor rests on; this class
        /// never reads the navigation state's selected row itself.
        /// </summary>
        internal static void BuildContentLines(ModMetaData mod, WorkshopItem_Downloading downloadingItem)
        {
            contentLines.Clear();
            lineRequirements.Clear();

            if (mod == null)
            {
                // The downloading placeholder row, matching vanilla's own.
                if (downloadingItem != null)
                {
                    AddLine("Downloading".Translate().ToString());
                }
                return;
            }

            if (!mod.AuthorsString.NullOrEmpty())
            {
                AddLine($"{"Author".Translate()}: {mod.AuthorsString}");
            }

            AddLine($"{"ModPackageId".Translate()}: {mod.packageIdLowerCase}");

            if (mod.SupportedVersionsReadOnly != null && mod.SupportedVersionsReadOnly.Any())
            {
                string versions = mod.SupportedVersionsReadOnly
                    .Select(v => v.Major + "." + v.Minor)
                    .ToCommaList();
                string compat = mod.VersionCompatible
                    ? "compatible"
                    : (mod.MadeForNewerVersion
                        ? "ModNotMadeForThisVersionShort".Translate().ToString()
                        : "ModNotMadeForThisVersionShort".Translate().ToString());
                AddLine($"{"ModTargetVersion".Translate()}: {versions} - {compat}");
            }

            if (!mod.ModVersion.NullOrEmpty())
            {
                AddLine($"{"ModVersion".Translate()}: {mod.ModVersion}");
            }

            if (!mod.VersionCompatible)
            {
                string warning = mod.MadeForNewerVersion
                    ? "ModNotMadeForThisVersion_Newer".Translate().ToString()
                    : "ModNotMadeForThisVersion".Translate().ToString();
                AddLine($"{"Warning".Translate()}: {warning}");
            }

            var warnings = ModListVanillaBridge.GetModWarningsCached();
            if (warnings != null && warnings.TryGetValue(mod.PackageId, out string errorText) && !errorText.NullOrEmpty())
            {
                AddLine($"Error: {errorText}");
            }

            // An actionable requirement row gets an affordance sentence appended and carries its
            // ModRequirement in lineRequirements for the Enter handler.
            var requirements = mod.GetRequirements();
            if (requirements != null)
            {
                foreach (var req in requirements)
                {
                    // RequirementTypeLabel is a vanilla format key resolved with an empty argument,
                    // so it already ends in its separator; another colon would double it.
                    string reqType = req.RequirementTypeLabel.TrimEnd();
                    string status = (req.IsSatisfied ? "RimWorldAccess.ModList.ReqSatisfied" : "RimWorldAccess.ModList.ReqNotSatisfied").Translate();
                    string line = $"{reqType} {req.displayName} - {status}";

                    string affordance = GetRequirementAffordanceSuffix(req);
                    if (!string.IsNullOrEmpty(affordance))
                    {
                        AddLine(line + ". " + affordance, req);
                    }
                    else
                    {
                        AddLine(line);
                    }
                }
            }

            if (mod.Active && ModsConfig.ModHasAnyOrderingIssues(mod))
            {
                AddLine("ModOrderingWarning".Translate().ToString());
            }

            if (!mod.Description.NullOrEmpty())
            {
                string desc = System.Net.WebUtility.HtmlDecode(mod.Description).Trim();
                // Long descriptions are chunked so they stay navigable.
                if (desc.Length > 300)
                {
                    var chunks = SplitDescription(desc, 300);
                    foreach (var chunk in chunks)
                    {
                        AddLine(chunk);
                    }
                }
                else
                {
                    AddLine(desc);
                }
            }
        }

        /// <summary>Appends a content line and its parallel requirement slot (null for every non-requirement line) in lockstep.</summary>
        private static void AddLine(string text, ModRequirement req = null)
        {
            contentLines.Add(text);
            lineRequirements.Add(req);
        }

        private static List<string> SplitDescription(string desc, int maxChunkLength)
        {
            var chunks = new List<string>();
            int start = 0;
            while (start < desc.Length)
            {
                int length = Math.Min(maxChunkLength, desc.Length - start);
                if (start + length < desc.Length)
                {
                    int breakAt = desc.LastIndexOf('\n', start + length, length);
                    if (breakAt <= start)
                        breakAt = desc.LastIndexOf(". ", start + length, length);
                    if (breakAt > start)
                        length = breakAt - start + 1;
                }
                chunks.Add(desc.Substring(start, length).Trim());
                start += length;
            }
            return chunks;
        }
    }
}
