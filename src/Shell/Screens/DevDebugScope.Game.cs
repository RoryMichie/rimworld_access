using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Keyboard focus scope for RimWorld's dev-mode debug dialog
    /// (<see cref="LudeonTK.Dialog_Debug"/>), registered through <see cref="ScopeForWindow"/>.
    /// Content regions mirror the CURRENT node's visible children grouped by vanilla's own
    /// category breaks, followed by a Tabs region; the automatic Buttons region carries "Back".
    ///
    /// Every location change routes through vanilla's own methods
    /// (<c>DebugActionNode.Enter</c>, <c>Dialog_Debug.SwitchTab</c>). Rather than announce from
    /// each handler, <see cref="RefreshContent"/> detects the resulting node/tab change and
    /// queues one location announcement, so a change vanilla makes itself is reported the same
    /// way; <see cref="SyncLocation"/> flushes it after each action.
    ///
    /// A node leading to another page is described and navigated as a collapsed tree row: Right
    /// drills in, Left goes up a level.
    ///
    /// A leaf node's <c>Enter</c> CLOSES the dialog before running its action, so this scope pops
    /// with the window. Checkbox nodes are leaves whose <c>action</c> is vanilla's own toggle
    /// delegate, so toggling invokes that directly rather than <c>Enter</c>.
    ///
    /// Dialog_Debug force-focuses its "DebugFilter" text box once on open, which would steal
    /// every keystroke; <see cref="OnPush"/> clears the latch before the first GUI pass.
    /// </summary>
    internal sealed class DevDebugScope : ScreenScope
    {
        private static readonly FieldInfo CurrentTabMenuField =
            AccessTools.Field(typeof(Dialog_Debug), "currentTabMenu");
        private static readonly FieldInfo FocusFilterField =
            AccessTools.Field(typeof(Dialog_Debug), "focusFilter");
        private static readonly AccessTools.FieldRef<Dialog_Debug, int> CurrentHighlightIndexRef =
            AccessTools.FieldRefAccess<Dialog_Debug, int>("currentHighlightIndex");
        private static readonly AccessTools.FieldRef<Dialog_Debug, int> PrioritizedHighlightIndexRef =
            AccessTools.FieldRefAccess<Dialog_Debug, int>("prioritizedHighlightedIndex");
        private static readonly MethodInfo VisibleActionsGetter =
            AccessTools.PropertyGetter(typeof(DebugTabMenu), "VisibleActions");

        private sealed class CategoryGroup
        {
            public string Name;
            public readonly List<DebugActionNode> Nodes = new List<DebugActionNode>();
        }

        private readonly Dialog_Debug dialog;
        private readonly List<CategoryGroup> groups = new List<CategoryGroup>();
        private readonly List<DebugTabMenuDef> tabDefs = new List<DebugTabMenuDef>();

        private DebugActionNode lastNode;
        private DebugTabMenuDef lastTab;
        private bool pendingLocationAnnounce;

        public DevDebugScope(Dialog_Debug dialog)
        {
            this.dialog = dialog;

            // Registered AFTER the base's typeahead-first cancel claim, which wins while a
            // search is active; otherwise this goes up one level, or closes at the root.
            Claim(SharedMenuGrammar.Cancel, OnCancel, when: NotSearching);

            // Space pins or unpins the node's action to the dev palette. The when-gate keeps
            // Space falling through on the Tabs and Buttons rows.
            Claim("devDebug.togglePin", delegate { TogglePinCurrentNode(); }, when: OnNodeRow);
        }

        public override string Name => "dev-debug";

        /// <summary>The debug nodes are named items worth searching.</summary>
        protected override bool EnableTypeahead => true;

        /// <summary>
        /// True so <see cref="WindowKeyRouter"/> defers Dialog_Debug's own Escape to this
        /// scope's up-level/close handling, without a Harmony blocker on OnCancelKeyPressed.
        /// </summary>
        public override bool OwnsCancel => true;

        /// <summary>Dialog_Debug draws its rows via DevGUI, not Widgets.ButtonText, so there is nothing to scrape.</summary>
        protected override bool CaptureWindowButtons => false;

        public override void OnPush()
        {
            base.OnPush();

            // Clearing the latch before the first GUI pass hands typing to the shell's typeahead.
            if (FocusFilterField != null)
            {
                // MUTATION-C: mirrors Dialog_Debug.DoWindowContents' one-shot
                // focusFilter latch. No public accessor exists; this is UI focus
                // state, not game state.
                FocusFilterField.SetValue(dialog, false);
            }
            UI.UnfocusCurrentControl();
        }

        public override void OnFocus()
        {
            base.OnFocus();
            FlushLocationAnnounce();
        }

        protected override int ContentRegionCount => groups.Count + 1;

        private int TabsRegion => groups.Count;

        protected override string ContentRegionName(int region)
        {
            if (region == TabsRegion)
            {
                return "RimWorldAccess.Dev.TabsRegion".Translate().ToString();
            }
            if (region >= 0 && region < groups.Count)
            {
                return groups[region].Name;
            }
            return "";
        }

        protected override int ContentItemCount(int region)
        {
            if (region == TabsRegion)
            {
                return tabDefs.Count;
            }
            if (region >= 0 && region < groups.Count)
            {
                return groups[region].Nodes.Count;
            }
            return 0;
        }

        /// <summary>
        /// Rebuilds the category-group snapshot from the CURRENT node's visible children and
        /// detects a node/tab change, queuing one location announcement. Children arrive already
        /// sorted by category, so consecutive same-category nodes group naturally;
        /// <c>TrySetupChildren</c> is deliberately NOT called on them, leaving lazy submenus
        /// unbuilt until entered.
        /// </summary>
        protected override void RefreshContent()
        {
            groups.Clear();
            tabDefs.Clear();

            DebugActionNode node = dialog.CurrentNode;
            if (node != null)
            {
                CategoryGroup current = null;
                foreach (DebugActionNode child in node.children)
                {
                    if (!child.VisibleNow)
                    {
                        continue;
                    }
                    string category = string.IsNullOrEmpty(child.category)
                        ? "RimWorldAccess.Dev.OptionsRegion".Translate().ToString()
                        : child.category;
                    if (current == null || current.Name != category)
                    {
                        current = new CategoryGroup { Name = category };
                        groups.Add(current);
                    }
                    current.Nodes.Add(child);
                }
            }

            tabDefs.AddRange(DefDatabase<DebugTabMenuDef>.AllDefs
                .OrderBy(d => d.displayOrder)
                .ThenBy(d => d.label));

            DebugTabMenuDef tab = CurrentTabDef();
            if (!ReferenceEquals(node, lastNode) || !ReferenceEquals(tab, lastTab))
            {
                lastNode = node;
                lastTab = tab;
                pendingLocationAnnounce = true;
            }
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            if (region == TabsRegion)
            {
                return DescribeTabRow(index);
            }
            DebugActionNode node = NodeAt(region, index);
            return node != null ? DescribeNodeRow(node) : new ElementDescription();
        }

        protected override void ActivateContentItem(int region, int index)
        {
            if (region == TabsRegion)
            {
                if (index >= 0 && index < tabDefs.Count)
                {
                    dialog.SwitchTab(tabDefs[index]);
                    SyncLocation();
                }
                return;
            }
            DebugActionNode node = NodeAt(region, index);
            if (node != null)
            {
                ActivateNode(node);
            }
        }

        private ElementDescription DescribeNodeRow(DebugActionNode node)
        {
            var d = new ElementDescription();
            d.Label = node.LabelNow;
            if (node.settingsField != null)
            {
                d.Role = ElementRole.Checkbox;
                d.Check = (bool)node.settingsField.GetValue(null)
                    ? CheckState.Checked
                    : CheckState.Unchecked;
            }
            else if (node.children.Count > 0 || node.childGetter != null)
            {
                // A node with children drills into its own page rather than expanding in place,
                // so it is never expanded where it is listed.
                d.Role = ElementRole.TreeItem;
                d.Expanded = false;
            }
            else
            {
                d.Role = ElementRole.Button;
            }
            if (!node.ActiveNow)
            {
                d.Disabled = true;
            }
            if (Prefs.DebugActionsPalette.Contains(node.Path))
            {
                // The pin state the sighted menu shows with a filled icon.
                d.Extras = "RimWorldAccess.Dev.Pinned".Translate().ToString();
            }
            return d;
        }

        private ElementDescription DescribeTabRow(int index)
        {
            var d = new ElementDescription();
            d.Role = ElementRole.Tab;
            if (index >= 0 && index < tabDefs.Count)
            {
                DebugTabMenuDef def = tabDefs[index];
                d.Label = def.LabelCap.ToString();
                d.Selected = ReferenceEquals(def, CurrentTabDef());
            }
            return d;
        }

        /// <summary>
        /// Left/Right on a node row: Right drills into a node leading to another page, Left goes
        /// up a level. A row with nowhere to go rejects; the Tabs region keeps the shared meaning.
        /// </summary>
        protected override bool CanAdjustContentItem(int region, int index)
        {
            return region >= 0 && region < groups.Count;
        }

        protected override void AdjustContentItem(int region, int index, int direction)
        {
            DebugActionNode node = NodeAt(region, index);
            if (node == null)
            {
                return;
            }
            TypeaheadReset();
            if (direction > 0)
            {
                node.TrySetupChildren();
                if (!node.children.Any())
                {
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    return;
                }
                GoToNode(node);
                return;
            }
            if (!TryGoUpLevel())
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
            }
        }

        /// <summary>
        /// Puts vanilla's own highlight box on the focused node row by riding the dialog's
        /// highlight index rather than drawing a ring of our own. BOTH fields are written because
        /// <c>HighlightedIndex</c> falls back to <c>currentHighlightIndex</c> whenever the
        /// prioritized one fails the dialog's filter. Off a node row the fields are left alone.
        /// </summary>
        protected override void OnCursorSettled(int region, int index)
        {
            DebugActionNode node = NodeAt(region, index);
            if (node == null)
            {
                return;
            }
            List<DebugActionNode> visible = VisibleActions();
            if (visible == null)
            {
                return;
            }
            for (int i = 0; i < visible.Count; i++)
            {
                if (ReferenceEquals(visible[i], node))
                {
                    CurrentHighlightIndexRef(dialog) = i;
                    PrioritizedHighlightIndexRef(dialog) = i;
                    return;
                }
            }
        }

        /// <summary>
        /// The current tab menu's own visible-action list — what its highlight index counts
        /// against, which diverges from this scope's category groups once a node stops being
        /// visible mid-page.
        /// </summary>
        private List<DebugActionNode> VisibleActions()
        {
            var menu = CurrentTabMenuField?.GetValue(dialog) as DebugTabMenu;
            if (menu == null || VisibleActionsGetter == null)
            {
                return null;
            }
            return VisibleActionsGetter.Invoke(menu, null) as List<DebugActionNode>;
        }

        private DebugActionNode NodeAt(int region, int index)
        {
            if (region < 0 || region >= groups.Count)
            {
                return null;
            }
            List<DebugActionNode> nodes = groups[region].Nodes;
            return index >= 0 && index < nodes.Count ? nodes[index] : null;
        }

        private void ActivateNode(DebugActionNode node)
        {
            if (node.settingsField != null)
            {
                // The node's action is vanilla's own toggle delegate (vehicle A). Enter would
                // close the dialog instead, since the node is a leaf.
                node.action?.Invoke();
                node.DirtyLabelCache();
                RefreshModel();
                bool on = (bool)node.settingsField.GetValue(null);
                string state = (on
                    ? "RimWorldAccess.Shell.State.Checked"
                    : "RimWorldAccess.Shell.State.Unchecked").Translate().ToString();
                TolkHelper.SpeakData(node.LabelNow + ". " + state + ".");
                return;
            }

            node.TrySetupChildren();
            bool drills = node.children.Any();
            if (drills)
            {
                GoToNode(node);
                return;
            }

            // A leaf's Enter closes this dialog, then runs its action or arms a debug tool.
            // RunAndAnnounce stays silent for a tool or a follow-up window, whose own surface
            // speaks, and gives a log-only, no-op, or throwing leaf a voice.
            DevActionOutcome.RunAndAnnounce(node.LabelNow, delegate { node.Enter(dialog); });
        }

        /// <summary>True only while the cursor sits on a debug NODE row, so Space falls through untouched everywhere else.</summary>
        private bool OnNodeRow()
        {
            RefreshModel();
            if (Model.RegionIndex < 0 || Model.RegionIndex >= groups.Count)
            {
                return false;
            }
            ListModel region = Model.CurrentRegion;
            return region != null && !region.IsEmpty;
        }

        /// <summary>
        /// Pins or unpins the current node's action via vanilla's own
        /// <c>Dialog_DevPalette.ToggleAction</c>, the exact call the pin icons make, then
        /// announces the node label and the new state.
        /// </summary>
        private void TogglePinCurrentNode()
        {
            RefreshModel();
            if (Model.RegionIndex < 0 || Model.RegionIndex >= groups.Count)
            {
                return;
            }
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty)
            {
                return;
            }
            DebugActionNode node = NodeAt(Model.RegionIndex, region.Index);
            if (node == null)
            {
                return;
            }
            bool wasPinned = Prefs.DebugActionsPalette.Contains(node.Path);
            Dialog_DevPalette.ToggleAction(node.Path);
            string word = (wasPinned
                ? "RimWorldAccess.Dev.Unpinned"
                : "RimWorldAccess.Dev.Pinned").Translate().ToString();
            TolkHelper.SpeakData(node.LabelNow + ". " + word + ".");
        }

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                DebugActionNode node = dialog.CurrentNode;
                if (node?.parent != null && !node.parent.IsRoot)
                {
                    return new List<ScreenAction>
                    {
                        new ScreenAction("RimWorldAccess.Dev.Back".Translate().ToString(),
                            delegate { TryGoUpLevel(); })
                    };
                }
                return null;
            }
        }

        private bool NotSearching()
        {
            return !TypeaheadHasActiveSearch;
        }

        private void OnCancel(KeyEventSnapshot e)
        {
            // Stamp before the up-level/close: the dialog can leave the stack mid-frame, and the
            // same Escape's deferred re-test must not reach any handling beneath.
            ShellFrameStamps.MarkCancelConsumed();
            if (!TryGoUpLevel())
            {
                dialog.Close();
            }
        }

        /// <summary>Goes up one level, or returns false at the tab's top level.</summary>
        private bool TryGoUpLevel()
        {
            DebugActionNode node = dialog.CurrentNode;
            if (node?.parent == null || node.parent.IsRoot)
            {
                return false;
            }
            GoToNode(node.parent);
            return true;
        }

        /// <summary>
        /// The one page transition every drill path shares: vanilla's own <c>Enter</c> onto the
        /// destination node, the drill tick, and the location announcement.
        /// </summary>
        private void GoToNode(DebugActionNode node)
        {
            node.Enter(dialog);
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            SyncLocation();
        }

        private void SyncLocation()
        {
            RefreshModel();
            FlushLocationAnnounce();
        }

        private void FlushLocationAnnounce()
        {
            if (!pendingLocationAnnounce)
            {
                return;
            }
            pendingLocationAnnounce = false;
            Model.MoveToRegion(0);
            Model.CurrentRegion?.MoveFirst();
            // This bespoke landing path owes the settle notification the shared moves make, or
            // vanilla's highlight box stays on the index the previous page left behind.
            NotifyCursorSettled();
            TolkHelper.SpeakData(CurrentLocationText());
            AnnounceCurrentItem();
        }

        private string CurrentLocationText()
        {
            DebugActionNode node = dialog.CurrentNode;
            if (node == null)
            {
                return "";
            }
            if (node.parent == null || node.parent.IsRoot)
            {
                DebugTabMenuDef tab = CurrentTabDef();
                return tab != null ? tab.LabelCap.ToString() : "";
            }
            return node.Path.Replace("\\", ", ");
        }

        private DebugTabMenuDef CurrentTabDef()
        {
            if (CurrentTabMenuField == null)
            {
                return null;
            }
            DebugTabMenu menu = CurrentTabMenuField.GetValue(dialog) as DebugTabMenu;
            return menu?.def;
        }
    }
}
