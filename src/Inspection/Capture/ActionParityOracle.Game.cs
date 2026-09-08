#if DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorldAccess.Shell;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// DEBUG-only write-side parity oracle (the mutation doctrine, CLAUDE.md):
    /// compares the ACTIONS our inspection tree exposes for a target against
    /// the interactive controls vanilla actually draws on the same tabs. An
    /// action of ours with no vanilla counterpart is the signature of a
    /// bypassed vanilla system (the Social-tab role Assign had no vanilla
    /// button — vanilla routes role changes through the ritual dialog).
    /// Advisory: findings are triaged into <see cref="reviewed"/> when a
    /// vanilla vehicle is confirmed under a different label, or fixed.
    ///
    /// Bridge-driven two-step like the read oracle (the harness needs a
    /// Repaint event): eval 1 calls <see cref="Request"/>, the probe's pump
    /// patch captures one tab per Repaint frame, eval 2 reads
    /// <see cref="Report"/>.
    ///
    /// Never activates ItemType.Action rows — collecting an action executes
    /// nothing; only non-Action expandables are expanded (the read oracle's
    /// recipe). Scope: tab-content actions only. Gizmo activation is out of
    /// scope by design — it routes through Gizmo.ProcessInput (Category A),
    /// so vanilla's own branch runs by construction.
    ///
    /// Honesty notes (no silent caps): synthetic categories (Tab == null)
    /// and tabs the harness cannot capture are reported UNAUDITABLE with
    /// their action counts, not skipped. Scroll-culled tabs (Social) may
    /// under-capture vanilla controls and produce false EXTRA-ACTION rows —
    /// triage against the decompiled tab before treating one as real.
    /// Matching is presence-level: vanilla's disabled states are not
    /// distinguishable in the captured stream. Icon-only controls
    /// (Widgets.ButtonImage — the gear tab's per-item drop buttons) have no
    /// capture tap and no label to match, so actions mirroring them read as
    /// false EXTRA-ACTION rows; confirm the vanilla vehicle in the decompiled
    /// tab, then ledger them.
    /// </summary>
    public static class ActionParityOracle
    {
        /// <summary>
        /// Triage ledger: normalized action keys per category confirmed to
        /// ride a vanilla vehicle whose label differs from the action's
        /// (e.g. an action that opens the vanilla dialog a differently-named
        /// vanilla button opens). Entries are added only after reading the
        /// decompiled call chain. English-run keys — the oracle is a DEBUG
        /// triage aid, not a shipped check.
        /// </summary>
        private static readonly Dictionary<string, HashSet<string>> reviewed =
            new Dictionary<string, HashSet<string>>();

        private sealed class Surface
        {
            public string Name;
            public InspectTabBase Tab;
            public List<string> Actions = new List<string>();
            public List<CapturedWidget> Controls;
            public string Error;
        }

        private static object target;
        private static readonly List<Surface> surfaces = new List<Surface>();
        private static int pending;

        /// <summary>
        /// Build the target's tree, collect action rows per tab category, and
        /// queue a vanilla capture of every real tab.
        /// </summary>
        public static string Request(object obj)
        {
            if (obj == null)
            {
                return "null target";
            }
            target = obj;
            surfaces.Clear();
            pending = 0;

            // BuildTree selects the object; tab visibility is computed from
            // the selection, so discovery must run AFTER the build or the two
            // lists disagree. Children build lazily on expansion — expand
            // before reading the category list (never expands Action rows).
            InspectionTreeItem root = InspectionTreeBuilder.BuildTree(new List<object> { obj });
            ExpandNonActions(root, 0);
            var categories = InspectionInfoHelper.GetDynamicCategories(obj);
            InspectionTreeItem objectItem = root.Children.Count == 1 ? root.Children[0] : root;
            var categoryItems = objectItem.Children
                .Where(c => c.Type == InspectionTreeItem.ItemType.Category).ToList();
            if (categoryItems.Count != categories.Count)
            {
                // The zip below pairs tree categories with discovery order; a
                // count mismatch means the builder skipped or synthesized one
                // and the pairing would lie.
                return $"category mismatch: tree has {categoryItems.Count}, discovery has {categories.Count} — oracle needs updating";
            }

            for (int i = 0; i < categories.Count; i++)
            {
                var surface = new Surface
                {
                    Name = categories[i].OriginalCategoryName ?? categories[i].Name,
                    Tab = categories[i].Tab,
                };
                CollectActions(categoryItems[i], surface.Actions);
                surfaces.Add(surface);
                if (surface.Tab != null && surface.Actions.Count > 0)
                {
                    pending++;
                }
            }
            int totalActions = surfaces.Sum(s => s.Actions.Count);
            return $"queued {pending} tab captures, {totalActions} actions across {surfaces.Count} categories";
        }

        internal static void PumpOnRepaint()
        {
            if (target == null || pending == 0
                || Event.current == null || Event.current.type != EventType.Repaint)
            {
                return;
            }
            Surface surface = surfaces.FirstOrDefault(
                s => s.Tab != null && s.Actions.Count > 0 && s.Controls == null && s.Error == null);
            if (surface == null)
            {
                pending = 0;
                return;
            }
            try
            {
                var rows = new List<CapturedWidget>();
                if (InspectTabCaptureHarness.TryCapturePass(surface.Tab, target, rows, out string error))
                {
                    surface.Controls = rows;
                }
                else
                {
                    surface.Error = error;
                }
            }
            catch (Exception ex)
            {
                surface.Error = ex.Message;
            }
            pending--;
        }

        /// <summary>Pending status or the full per-category parity report.</summary>
        public static string Report()
        {
            if (target == null)
            {
                return "(no request)";
            }
            if (pending > 0)
            {
                return $"(pending, {pending} captures left)";
            }

            var sb = new StringBuilder();
            sb.Append("action parity for ").Append(target).Append(':');
            foreach (Surface surface in surfaces)
            {
                if (surface.Actions.Count == 0)
                {
                    continue;
                }
                sb.Append('\n').Append(surface.Name).Append(": ")
                  .Append(surface.Actions.Count).Append(" actions");
                if (surface.Tab == null)
                {
                    sb.Append(" UNAUDITABLE (synthetic category, no vanilla tab)");
                    AppendActionList(sb, surface.Actions, "  ACTION: ");
                    continue;
                }
                if (surface.Error != null)
                {
                    sb.Append(" UNAUDITABLE (capture failed: ").Append(surface.Error).Append(')');
                    AppendActionList(sb, surface.Actions, "  ACTION: ");
                    continue;
                }
                List<string> controlLabels = InteractiveLabels(surface.Controls);
                reviewed.TryGetValue(surface.Name, out HashSet<string> allowed);
                var findings = new List<string>();
                foreach (string action in surface.Actions)
                {
                    string key = Normalize(action);
                    if (key.Length == 0
                        || (allowed != null && allowed.Contains(key))
                        || controlLabels.Any(c => c.Contains(key) || key.Contains(c)))
                    {
                        continue;
                    }
                    findings.Add(action);
                }
                sb.Append(", ").Append(controlLabels.Count).Append(" vanilla controls, ")
                  .Append(findings.Count).Append(" unmatched");
                foreach (string action in findings)
                {
                    sb.Append("\n  EXTRA-ACTION: [").Append(action)
                      .Append("] — no vanilla control on this tab matches");
                }
            }
            return sb.ToString();
        }

        private static void AppendActionList(StringBuilder sb, List<string> actions, string prefix)
        {
            foreach (string action in actions)
            {
                sb.Append('\n').Append(prefix).Append('[').Append(action).Append(']');
            }
        }

        private static List<string> InteractiveLabels(List<CapturedWidget> rows)
        {
            var labels = new List<string>();
            foreach (CapturedWidget row in rows)
            {
                switch (row.Kind)
                {
                    case WidgetKind.Button:
                    case WidgetKind.Checkbox:
                    case WidgetKind.RadioButton:
                    case WidgetKind.Slider:
                        string key = Normalize(row.Label);
                        if (key.Length > 0)
                        {
                            labels.Add(key);
                        }
                        break;
                }
            }
            return labels;
        }

        private static void ExpandNonActions(InspectionTreeItem item, int depth)
        {
            if (depth > 10)
            {
                return;
            }
            if (item.IsExpandable && item.Children.Count == 0
                && item.OnActivate != null && item.Type != InspectionTreeItem.ItemType.Action
                && !item.OpensOverlayMenu)
            {
                try
                {
                    item.OnActivate();
                }
                catch (Exception)
                {
                    // A builder that throws just contributes no actions.
                }
            }
            foreach (InspectionTreeItem child in item.Children)
            {
                ExpandNonActions(child, depth + 1);
            }
        }

        private static void CollectActions(InspectionTreeItem item, List<string> into)
        {
            if (item.Type == InspectionTreeItem.ItemType.Action)
            {
                into.Add(item.Label ?? "(unlabeled)");
            }
            foreach (InspectionTreeItem child in item.Children)
            {
                CollectActions(child, into);
            }
        }

        /// <summary>Case-, tag-, punctuation- and whitespace-insensitive form.</summary>
        private static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }
            text = text.StripTags();
            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(char.ToLowerInvariant(c));
                }
            }
            return sb.ToString();
        }
    }
}
#endif
