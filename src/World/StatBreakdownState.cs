using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Data facade for the stat breakdown tree viewer: parses a vanilla explanation string into a
    /// navigable tree and owns the open/close lifecycle. Opened by Alt+I on a stat row; navigation and
    /// announcement live in <see cref="RimWorldAccess.Shell.StatBreakdownScope"/>. <see cref="Open"/>
    /// hands the parsed root over through <see cref="TakePendingRoot"/> rather than seeding the scope:
    /// the mirror does not push until the reconcile pass after <see cref="Open"/> returns.
    /// </summary>
    public static class StatBreakdownState
    {
        private static bool isActive = false;
        private static string statName = "";
        private static InspectionTreeItem pendingRoot;

        public static bool IsActive => isActive;

        /// <summary>The stat this breakdown describes; the scope's content-region name.</summary>
        internal static string StatName => statName;

        /// <summary>Hands the freshly parsed tree to the scope once per <see cref="Open"/>. Returns null
        /// on a mere re-push, leaving the scope's existing tree and cursor untouched.</summary>
        internal static InspectionTreeItem TakePendingRoot()
        {
            InspectionTreeItem root = pendingRoot;
            pendingRoot = null;
            return root;
        }

        /// <summary>Opens the viewer over a stat's explanation text; announces and stays closed when empty or factorless.</summary>
        public static void Open(string name, string explanation)
        {
            if (string.IsNullOrEmpty(explanation))
            {
                TolkHelper.Speak("RimWorldAccess.StatBreakdown.NoBreakdownFor".Loc(name));
                return;
            }

            isActive = true;
            statName = name;

            var root = ParseExplanation(explanation);

            if (root.Children.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.StatBreakdown.NoFactorsFor".Loc(name));
                Close();
                return;
            }

            pendingRoot = root;

            bool hasExpandableNodes = root.Children.Exists(item => item.IsExpandable);
            TolkHelper.Speak((hasExpandableNodes
                ? "RimWorldAccess.StatBreakdown.HeaderTreeview"
                : "RimWorldAccess.StatBreakdown.Header").Loc(name, root.Children.Count));
        }

        public static void Close()
        {
            isActive = false;
            statName = "";
            pendingRoot = null;
            TolkHelper.Speak("RimWorldAccess.StatBreakdown.Closed".Loc());
        }

        /// <summary>Silent session-boundary reset. This state has no other deterministic close, so the
        /// session-start resets must call it.</summary>
        internal static void ResetHard()
        {
            isActive = false;
            statName = "";
            pendingRoot = null;
        }

        /// <summary>Parses the explanation into a tree, deriving hierarchy from indentation (two spaces
        /// per level, tabs four). Result lines ("= ") become their parent's tooltip, not rows.</summary>
        private static InspectionTreeItem ParseExplanation(string explanation)
        {
            var root = new InspectionTreeItem
            {
                Label = "Root",
                IndentLevel = -1,
                IsExpanded = true,
                IsExpandable = false
            };

            if (string.IsNullOrEmpty(explanation))
                return root;

            string[] lines = explanation.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            InspectionTreeItem currentParent = null;
            int lastIndentLevel = 0;
            List<InspectionTreeItem> allItems = new List<InspectionTreeItem>();

            foreach (string rawLine in lines)
            {
                if (string.IsNullOrWhiteSpace(rawLine))
                    continue;

                int leadingSpaces = 0;
                foreach (char c in rawLine)
                {
                    if (c == ' ') leadingSpaces++;
                    else if (c == '\t') leadingSpaces += 4;
                    else break;
                }

                int indentLevel = leadingSpaces / 2;

                string line = rawLine.Trim();

                if (string.IsNullOrEmpty(line))
                    continue;

                bool isResultLine = line.StartsWith("= ");
                if (isResultLine)
                {
                    string resultValue = line.Substring(2).Trim();

                    InspectionTreeItem targetParent = currentParent;
                    while (targetParent != null && targetParent.IndentLevel >= indentLevel)
                    {
                        targetParent = targetParent.Parent;
                    }

                    if (targetParent != null && targetParent != root)
                    {
                        targetParent.Tooltip = resultValue;
                    }
                    else if (root.Children.Count > 0)
                    {
                        root.Children[root.Children.Count - 1].Tooltip = resultValue;
                    }
                    continue;
                }

                if (line.StartsWith("- "))
                {
                    line = line.Substring(2);
                    indentLevel = Math.Max(1, indentLevel);
                }

                bool isSection = line.EndsWith(":");
                if (isSection)
                {
                    line = line.Substring(0, line.Length - 1);
                }

                var item = new InspectionTreeItem
                {
                    Label = line,
                    IndentLevel = indentLevel,
                    IsExpandable = false,
                    IsExpanded = false
                };

                allItems.Add(item);

                if (indentLevel == 0)
                {
                    item.Parent = root;
                    root.Children.Add(item);
                    currentParent = item;
                }
                else if (currentParent == null)
                {
                    // No parent to attach to yet; treat as top-level.
                    item.Parent = root;
                    root.Children.Add(item);
                    currentParent = item;
                }
                else if (indentLevel > lastIndentLevel)
                {
                    item.Parent = currentParent;
                    currentParent.Children.Add(item);
                    currentParent.IsExpandable = true;
                    currentParent = item;
                }
                else if (indentLevel <= lastIndentLevel)
                {
                    InspectionTreeItem parent = currentParent;
                    while (parent != null && parent != root && parent.IndentLevel >= indentLevel)
                    {
                        parent = parent.Parent;
                    }

                    if (parent != null && parent != root)
                    {
                        item.Parent = parent;
                        parent.Children.Add(item);
                        parent.IsExpandable = true;
                    }
                    else
                    {
                        item.Parent = root;
                        root.Children.Add(item);
                    }
                    currentParent = item;
                }

                lastIndentLevel = indentLevel;
            }

            InlineSingleChildren(root.Children);

            // Match vanilla's resolved prefixes, never English words: these labels are translated.
            string finalValuePrefix = "StatsReport_FinalValue".Translate().ToString().Trim();

            // The caravan speed builders append ":" after the translated label.
            string[] speedLinePrefixes =
            {
                "CaravanMovementSpeedFull".Translate().ToString().Trim() + ":",
                "FinalCaravanPawnsMovementSpeed".Translate().ToString().Trim() + ":",
                "FinalCaravanMovementSpeed".Translate().ToString().Trim() + ":"
            };

            foreach (var item in allItems)
            {
                if (item.IsExpandable && string.IsNullOrEmpty(item.Tooltip) && item.Children.Count > 0)
                {
                    for (int i = item.Children.Count - 1; i >= 0; i--)
                    {
                        var child = item.Children[i];
                        string childText = child.Label;

                        var equalsMatch = Regex.Match(childText, @"=\s*([\d.]+\s*\S+.*?)$");
                        if (equalsMatch.Success)
                        {
                            item.Tooltip = equalsMatch.Groups[1].Value.Trim();
                            break;
                        }

                        if (childText.StartsWith(finalValuePrefix, StringComparison.Ordinal) ||
                            Array.Exists(speedLinePrefixes, prefix => childText.StartsWith(prefix, StringComparison.Ordinal)))
                        {
                            var colonMatch = Regex.Match(childText, @":\s*(.+)$");
                            if (colonMatch.Success)
                            {
                                item.Tooltip = colonMatch.Groups[1].Value.Trim();
                                break;
                            }
                        }
                    }
                }
            }

            return root;
        }

        /// <summary>Collapses only-child chains: the child's text is appended to the parent and the
        /// grandchildren promoted, recursively.</summary>
        private static void InlineSingleChildren(List<InspectionTreeItem> items)
        {
            foreach (var item in items)
            {
                while (item.Children.Count == 1)
                {
                    var singleChild = item.Children[0];

                    item.Label += ", " + singleChild.Label;

                    if (!string.IsNullOrEmpty(singleChild.Tooltip) && string.IsNullOrEmpty(item.Tooltip))
                    {
                        item.Tooltip = singleChild.Tooltip;
                    }

                    item.Children = singleChild.Children;
                    foreach (var grandchild in item.Children)
                    {
                        grandchild.Parent = item;
                    }

                    item.IsExpandable = item.Children.Count > 0;
                }

                if (item.Children.Count > 0)
                {
                    InlineSingleChildren(item.Children);
                }
            }
        }

        /// <summary>Tab: always closes (not a standard tree key).</summary>
        internal static void CloseFromTab() => Close();

        /// <summary>Escape when the tree had no active search to clear: close.</summary>
        internal static void HandleEscape() => Close();
    }
}
