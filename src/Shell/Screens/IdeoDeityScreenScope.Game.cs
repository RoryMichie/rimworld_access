using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using DeityType = RimWorld.IdeoFoundation_Deity.Deity;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Windowless overlay for managing a deity-foundation ideoligion's deities, opened the same
    /// three ways as the other overlay editors (see <see cref="IdeoPreceptScreenScope"/> for the
    /// shared windowless/OwnedWindow rationale).
    ///
    /// One flat region: "Randomize deities", then "Add deity" when
    /// <see cref="IdeoDeityListState.CanAddDeity"/> allows it, then one Button row per deity,
    /// expandable only when the deity has a <c>relatedMeme</c>.
    ///
    /// Deity rows are plain Buttons, nothing here cycles a value, so Enter opens the per-deity
    /// action menu — vanilla's own <c>IdeoFoundation_Deity.DoInfo</c> menu, option for option — and
    /// "]" opens the identical menu for muscle memory. Left/Right and "*" ride the shared
    /// <see cref="TreeRegionScope"/> grammar: Randomize/Add are prefix rows, each deity a branch
    /// whose one child is its related-meme detail line.
    ///
    /// Name, title and gender belong to the real <see cref="RimWorld.Dialog_EditDeity"/>, driven by
    /// <see cref="EditDeityDialogScope"/>, so this scope owns no text session.
    ///
    /// <see cref="IdeoDeityListState.RemoveDeity"/> speaks its own confirmation and arms
    /// <see cref="IdeoDeityListState.SuppressNextReturnReannounce"/>, so the float menu's close does
    /// not speak over it. Every other action has nothing else to say, so <see cref="OnFocus"/>'s
    /// re-announce is the only voice — and after an edit it speaks the updated label, which is
    /// exactly the confirmation the player wants.
    /// </summary>
    public sealed class IdeoDeityScreenScope : TreeRegionScope
    {
        private sealed class DeityNode : InspectionTreeItem
        {
            public DeityType Deity;
        }

        private readonly List<ScreenAction> actions = new List<ScreenAction>();
        private bool everFocused;

        public IdeoDeityScreenScope()
        {
            Claim(SharedMenuGrammar.Cancel, e =>
            {
                ShellFrameStamps.MarkCancelConsumed();
                CloseAndReturn();
            }, when: () => !TypeaheadHasActiveSearch);
            Claim(SharedMenuGrammar.Info, OnInfo);
            Claim("ideoOverlayEditor.delete", OnDelete);
            Claim("ideoOverlayEditor.contextMenu", e => OpenEditMenuFor(CurrentDeity()));
            // Expand-all rides the base's tree.expandAllSiblings claim (identical chords).
        }

        public override string Name
        {
            get { return "ideo-deity-list"; }
        }

        /// <summary>Unconditional — see <see cref="IdeoPreceptScreenScope"/>'s class remarks (same posture).</summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        /// <summary>No draw pass of its own — see <see cref="IdeoPreceptScreenScope"/>'s class remarks.</summary>
        protected override bool IncludeCapturedExtrasRegion
        {
            get { return false; }
        }

        protected override bool CaptureWindowButtons
        {
            get { return false; }
        }

        public override void OnPush()
        {
            base.OnPush();
            everFocused = false;
            ResetTree();
            // Windowless overlay: the ring rides the host page's own deity boxes as vanilla draws
            // them, never FocusedContentRect.
            IdeoBoxDrawPatch.AddInterest();
            IdeoBoxDrawPatch.DeityRingProvider = CurrentDeity;
            IdeoBoxDrawPatch.FollowTarget = FollowTargetRect;
        }

        public override void OnPop()
        {
            IdeoBoxDrawPatch.DeityRingProvider = null;
            IdeoBoxDrawPatch.FollowTarget = null;
            IdeoBoxDrawPatch.RemoveInterest();
            base.OnPop();
        }

        /// <summary>The focused deity's box in the host pane's own scroll space, for scroll-follow.</summary>
        private Rect? FollowTargetRect()
        {
            return IdeoBoxDrawPatch.RawRectFor(CurrentDeity());
        }

        protected override void OnCursorSettled(int region, int index)
        {
            base.OnCursorSettled(region, index);
            IdeoBoxDrawPatch.RequestFollow();
        }

        public override void OnFocus()
        {
            base.OnFocus();
            if (!everFocused)
            {
                everFocused = true;
                // This scope is a long-lived singleton whose Model persists across every open/close
                // cycle, unlike a window-attached scope. Force the cursor back to the first content
                // row each time, or a cursor parked on the toolbar in one interaction poisons every
                // later open.
                Model.MoveToRegion(0);
                ListModel firstRegion = Model.CurrentRegion;
                if (firstRegion != null)
                {
                    firstRegion.MoveFirst();
                }
                AnnounceOpening();
                return;
            }
            if (IdeoDeityListState.ShouldReannounceOnReturn())
            {
                AnnounceCurrentItem();
            }
        }

        // ------------------------------------------------------------------
        // Row model.
        // ------------------------------------------------------------------

        protected override void RefreshContent()
        {
            base.RefreshContent();
            if (!IdeoDeityListState.IsActive)
            {
                ResetTree();
                return;
            }
            if (Tree.Root == null)
            {
                SetTreeRoot(BuildDeityRoot());
            }
        }

        /// <summary>Deity labels are read live from the node's Deity, so only membership changes
        /// (add/remove/randomize) need a rebuild.</summary>
        private static InspectionTreeItem BuildDeityRoot()
        {
            var root = new InspectionTreeItem { IsExpandable = true, IsExpanded = true };
            IReadOnlyList<DeityType> deities = IdeoDeityListState.Deities;
            if (deities == null)
            {
                return root;
            }
            for (int i = 0; i < deities.Count; i++)
            {
                DeityType deity = deities[i];
                var node = new DeityNode
                {
                    Deity = deity,
                    Data = deity,
                    LabelProvider = () => IdeoDeityListState.BuildDeityLabel(deity),
                    IsExpandable = deity.relatedMeme != null,
                    IndentLevel = 0,
                    Parent = root,
                };
                if (deity.relatedMeme != null)
                {
                    node.Children.Add(new InspectionTreeItem
                    {
                        LabelProvider = () => (string)"RelatedToMeme".Translate() + ": " + deity.relatedMeme.LabelCap.Resolve(),
                        IndentLevel = 1,
                        Parent = node,
                    });
                }
                root.Children.Add(node);
            }
            return root;
        }

        /// <summary>Rebuilds after membership changes, keeping expansion and the cursor's logical node.</summary>
        private void RebuildTreePreservingState()
        {
            RefreshModel();
            ListModel region = Model.CurrentRegion;
            int before = region != null && !region.IsEmpty ? region.Index : 0;
            int restored = SetTreeRootPreservingState(BuildDeityRoot(), before);
            RefreshModel();
            ListModel after = Model.CurrentRegion;
            if (after != null && !after.IsEmpty)
            {
                after.MoveTo(Mathf.Clamp(restored >= 0 ? restored : before, 0, after.Count - 1));
            }
        }

        protected override string TreeRegionLabel
        {
            get { return (string)"Deities".Translate(); }
        }

        /// <summary>Randomize, then Add while the cap allows it, above the deity tree.</summary>
        protected override int PrefixRowCount
        {
            get { return IdeoDeityListState.IsActive && IdeoDeityListState.CanAddDeity() ? 2 : 1; }
        }

        protected override ElementDescription DescribePrefixRow(int index)
        {
            return new ElementDescription
            {
                Label = index == 0 ? (string)"RandomizeDeities".Translate() : (string)"AddDeity".Translate(),
                Role = ElementRole.Button,
            };
        }

        protected override void ActivatePrefixRow(int index)
        {
            if (index == 0)
            {
                IdeoDeityListState.RandomizeAll();
            }
            else
            {
                IdeoDeityListState.AddDeity();
            }
            RebuildTreePreservingState();
            AnnounceCurrentItem();
        }

        protected override ElementDescription DescribeTreeNode(InspectionTreeItem item)
        {
            if (item is DeityNode deity)
            {
                var d = new ElementDescription { Label = item.Label, Role = ElementRole.Button };
                if (deity.IsExpandable)
                {
                    d.Expanded = deity.IsExpanded;
                }
                return d;
            }
            return new ElementDescription { Label = item.Label, ReadOnly = true };
        }

        protected override void ActivateTreeNode(InspectionTreeItem item)
        {
            if (item is DeityNode deity)
            {
                OpenEditMenuFor(deity.Deity);
                return;
            }
            AnnounceCurrentItem();
        }

        /// <summary>
        /// The deity the cursor rests on, climbing from a focused detail row to its parent deity row.
        /// Null on the Add and Randomize rows, or when nothing is focused.
        /// </summary>
        private DeityType CurrentDeity()
        {
            RefreshModel();
            InspectionTreeItem item = CurrentTreeItem();
            if (item is DeityNode deity)
            {
                return deity.Deity;
            }
            return (item?.Parent as DeityNode)?.Deity;
        }

        private void OpenEditMenuFor(DeityType deity)
        {
            if (deity == null)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("EditDeity".Translate(), () => OpenEditDialogFor(deity)),
                new FloatMenuOption("Regenerate".Translate().CapitalizeFirst(), () => IdeoDeityListState.RegenerateDeity(deity)),
            };
            if (IdeoDeityListState.Deities.Count > IdeoDeityListState.Ideo.DeityCountRange.min)
            {
                options.Add(new FloatMenuOption("Remove".Translate().CapitalizeFirst(), () =>
                {
                    if (IdeoDeityListState.RemoveDeity(deity))
                    {
                        RebuildTreePreservingState();
                    }
                }));
            }
            WindowlessFloatMenuState.Open(options, colonistOrders: false, titleText: deity.name);
        }

        /// <summary>Vehicle A: vanilla's own IdeoFoundation_Deity expression.</summary>
        private void OpenEditDialogFor(DeityType deity)
        {
            // Arms the suppression window: the edit menu pops back to this scope's OnFocus the same
            // frame the dialog opens, and the dialog's own opening is the only voice.
            IdeoDeityListState.SuppressNextReturnReannounce();
            Find.WindowStack.Add(new Dialog_EditDeity(deity, IdeoDeityListState.Ideo));
        }

        // ------------------------------------------------------------------
        // Delete.
        // ------------------------------------------------------------------

        private void OnDelete(KeyEventSnapshot e)
        {
            DeityType deity = CurrentDeity();
            if (deity == null)
            {
                return;
            }
            if (IdeoDeityListState.RemoveDeity(deity))
            {
                // RemoveDeity already spoke the confirmation or rejection.
                RebuildTreePreservingState();
            }
        }

        // ------------------------------------------------------------------
        // Info card drill-in. A Deity is not a Def, so there is nothing to inspect.
        // ------------------------------------------------------------------

        private void OnInfo(KeyEventSnapshot e)
        {
            InfoCardState.SpeakNoInfoCardAvailable();
        }

        // ------------------------------------------------------------------
        // Buttons region: Back closes the overlay.
        // ------------------------------------------------------------------

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                actions.Add(new ScreenAction("Back".Translate(), CloseAndReturn, SharedMenuGrammar.Cancel));
                return actions;
            }
        }

        private void CloseAndReturn()
        {
            // Speaks nothing of its own: this overlay is host-blind and reachable from more than
            // one host, so it must not hardcode either host's name; the host beneath re-announces
            // on regaining focus.
            IdeoDeityListState.Close();
            SoundDefOf.TabClose.PlayOneShotOnCamera();
        }

        // ------------------------------------------------------------------
        // Opening announcement.
        // ------------------------------------------------------------------

        private void AnnounceOpening()
        {
            string tabCount = TabCountFragment();
            string title = (string)"Deities".Translate();
            int count = IdeoDeityListState.Deities != null ? IdeoDeityListState.Deities.Count : 0;
            string opening = string.IsNullOrEmpty(tabCount) ? title : tabCount + ". " + title;
            opening += ". " + count;
            TolkHelper.SpeakData(opening, SpeechPriority.High);
            AnnounceCurrentItem();
        }
    }
}
