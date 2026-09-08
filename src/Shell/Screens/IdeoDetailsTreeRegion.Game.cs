using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The ideology details tree as one region of a multi-region <see cref="ScreenScope"/>,
    /// following the <see cref="GeneTreeRegion"/> shape (a composed component rather than a
    /// <see cref="TreeRegionScope"/> subclass, since every host has regions of its own).
    /// Owns the <see cref="TreeModel{T}"/>, the section-jump math, the per-row
    /// <see cref="ElementDescription"/>, the info-card walk, and the ideology-specific
    /// activation branches; the host decides sounds and speaks navigation.
    /// Expansion state rides the label channel: <see cref="Describe"/> bakes "expanded, 3
    /// items" into the Label and leaves Role/Expanded unset, because the child count is
    /// informative here and the two channels must never carry the same fact.
    /// The ritual-sound <see cref="Sustainer"/> is a class-level static: a preview started
    /// under one host must be stoppable from another.
    /// </summary>
    public sealed class IdeoDetailsTreeRegion
    {
        public readonly TreeModel<InspectionTreeItem> Tree =
            new TreeModel<InspectionTreeItem>(new InspectionTreeItemShape());

        private static Sustainer ritualSoundPreview;

        /// <summary>
        /// Rebuilds the tree from a fresh root, sampling SubmenuTreeNavigation here rather
        /// than per keypress (the <see cref="TreeRegionScope.SetTreeRoot"/> convention).
        /// </summary>
        public void SetRoot(InspectionTreeItem root, bool wrap, int initialIndex = 0)
        {
            Tree.Wrap = wrap;
            Tree.SubmenuMode = RimWorldAccessMod_Settings.Settings != null
                && RimWorldAccessMod_Settings.Settings.SubmenuTreeNavigation;
            Tree.SetRoot(root, initialIndex);
        }

        /// <summary>
        /// Replaces the root while preserving perceptible state: surviving expanded nodes
        /// are re-expanded and the cursor lands on the same logical node (matched by Data
        /// reference, else by label path), not the same flat index. Returns that node's flat
        /// visible index, or -1 when it is gone — callers need a numeric-clamp fallback.
        /// Hosts rebuild from scratch after any row activation that opened a window, since an
        /// in-place mutation of the same <see cref="Ideo"/> escapes the staleness check.
        /// </summary>
        public int SetRootPreservingState(InspectionTreeItem newRoot, bool wrap, int currentFlatIndex)
        {
            return TreeStatePreserve.SetRootPreservingState(Tree, newRoot, r => SetRoot(r, wrap), currentFlatIndex);
        }

        /// <summary>The visible row at <paramref name="index"/>, or null when out of range.</summary>
        public InspectionTreeItem ItemAt(int index)
        {
            IReadOnlyList<InspectionTreeItem> visible = Tree.Visible;
            return index >= 0 && index < visible.Count ? visible[index] : null;
        }

        /// <summary>
        /// Page Up/Down: moves the cursor to the previous/next level-0 node (this tree's
        /// section headers). Pure cursor move, no wrap; the caller sounds and announces.
        /// </summary>
        public bool JumpToAdjacentSection(bool forward)
        {
            IReadOnlyList<InspectionTreeItem> visible = Tree.Visible;
            if (visible.Count == 0)
            {
                return false;
            }
            int step = forward ? 1 : -1;
            for (int i = Tree.SelectedIndex + step; i >= 0 && i < visible.Count; i += step)
            {
                if (visible[i].IndentLevel == 0)
                {
                    Tree.SetSelectedIndex(i);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The row's typeahead haystack: the smart label <see cref="Describe"/> speaks,
        /// WITHOUT the expansion suffix — searching the spoken label would make "c" match
        /// every collapsed row and "e" every expanded one.
        /// </summary>
        public string SearchText(InspectionTreeItem item)
        {
            if (item == null)
            {
                return "";
            }
            string raw = item.Label ?? "";
            if (item.IsExpandable && item.IsExpanded)
            {
                int sepIdx = raw.IndexOf(". ");
                return sepIdx > 0 ? raw.Substring(0, sepIdx) : raw;
            }
            return raw.TrimEnd('.', '!', '?');
        }

        /// <summary>
        /// One row's announcement fields: the smart label (an expanded node speaks only the
        /// part before its first sentence break) plus the expansion suffix, Level and sibling
        /// Position, and the "Inspectable" hint when the subtree carries an inspectable Def.
        /// </summary>
        public ElementDescription Describe(InspectionTreeItem item)
        {
            var d = new ElementDescription();
            if (item == null)
            {
                return d;
            }
            string raw = item.Label ?? "";
            string label;
            if (item.IsExpandable && item.IsExpanded)
            {
                int sepIdx = raw.IndexOf(". ");
                label = sepIdx > 0 ? raw.Substring(0, sepIdx) : raw;
            }
            else
            {
                label = raw.TrimEnd('.', '!', '?');
            }
            d.Label = label + TreeNavigationHelper.FormatExpansionSuffix(item, includeChildCount: true);
            var siblingPosition = Tree.GetSiblingPosition(item);
            d.PositionIndex = siblingPosition.position;
            d.PositionCount = siblingPosition.total;
            // Unconditional, never change-gated: Describe also runs during the typeahead
            // haystack build, which would corrupt a stateful "last level spoken" tracker.
            d.Level = item.IndentLevel + 1;
            // Enter's behavior here lives outside the role channel (the host's precedence:
            // TryActivateSpecial, OnActivate, then the expand/collapse toggle), so rows that
            // own Enter must say so or the double-press proceed confirm steals the key.
            d.KeepsAccept = item.Data is SoundDef
                || item.Data is IdeoReformState.ReformActionMarker
                || item.OnActivate != null
                || item.IsExpandable;
            if (InspectableDefs(item).Count > 0)
            {
                d.Extras = (string)"RimWorldAccess.InfoCard.Inspectable".Translate();
            }
            return d;
        }

        /// <summary>
        /// The two ideology-specific Enter branches. Returns false for every other
        /// row, leaving the host to run the item's own <c>OnActivate</c> or the
        /// expand/collapse toggle.
        /// </summary>
        public bool TryActivateSpecial(InspectionTreeItem item)
        {
            if (item == null)
            {
                return false;
            }
            if (item.Data is SoundDef soundDef)
            {
                ToggleRitualSound(soundDef);
                return true;
            }
            // Opens the reform dialog over whichever host shows this tree (vehicle A). The tree
            // is host-agnostic, so nothing here may close a host's own window or state; stopping
            // the sound preview is the one side effect correct for every host.
            if (item.Data is IdeoReformState.ReformActionMarker reformMarker)
            {
                StopRitualSound();
                Find.WindowStack.Add(new RimWorld.Dialog_ReformIdeo(reformMarker.Ideo));
                return true;
            }
            return false;
        }

        /// <summary>Alt+I: no defs speaks the shared "nothing to inspect" line, one opens its card directly, several offer the picker.</summary>
        public void OpenInfoCard(InspectionTreeItem item)
        {
            List<Def> defs = InspectableDefs(item);
            if (defs.Count == 0)
            {
                InfoCardState.SpeakNoInfoCardAvailable();
                return;
            }
            if (defs.Count == 1)
            {
                InfoCardState.OpenInfoCardForDef(defs[0]);
                return;
            }
            var options = new List<FloatMenuOption>();
            foreach (Def def in defs)
            {
                Def captured = def;
                string label = def.label != null ? def.label.CapitalizeFirst() : def.defName;
                options.Add(new FloatMenuOption(label, delegate { InfoCardState.OpenInfoCardForDef(captured); }));
            }
            TolkHelper.Speak("RimWorldAccess.InfoCard.ChooseItemToInspect".Loc());
            WindowlessFloatMenuState.Open(options, false);
        }

        /// <summary>
        /// Walks up from <paramref name="item"/> to the root for inspectable Defs, accepting
        /// either a single Def or a <c>List&lt;Def&gt;</c> in Data. Takes the row rather than
        /// reading the cursor, so each row's "Inspectable" hint reports its own defs.
        /// MemeDef is excluded like SoundDef: vanilla never opens Dialog_InfoCard for one, and
        /// the meme's detail content already has its own row.
        /// </summary>
        public List<Def> InspectableDefs(InspectionTreeItem item)
        {
            InspectionTreeItem node = item;
            InspectionTreeItem root = Tree.Root;
            while (node != null && node != root)
            {
                if (node.Data is Def def && !(def is SoundDef) && !(def is MemeDef))
                    return new List<Def> { def };
                if (node.Data is List<Def> defs && defs.Count > 0)
                    return defs;
                node = node.Parent;
            }
            return new List<Def>();
        }

        // Ritual sound preview, shared across every host.

        /// <summary>Internal, not private: the legacy <c>IdeologyTreeNavigation</c> wrapper driving the landing dialog routes its ritual-sound row here so one Sustainer serves every host.</summary>
        internal static void ToggleRitualSound(SoundDef soundDef)
        {
            if (ritualSoundPreview != null)
            {
                ritualSoundPreview.End();
                ritualSoundPreview = null;
                TolkHelper.Speak("RimWorldAccess.Ideology.RitualSound.Stopped".Loc("RitualAmbienceSound".Translate().Resolve()));
            }
            else
            {
                SoundInfo info = SoundInfo.OnCamera(MaintenanceType.PerFrame);
                info.forcedPlayOnCamera = true;
                info.testPlay = true;
                ritualSoundPreview = soundDef.TrySpawnSustainer(info);
                TolkHelper.Speak("RimWorldAccess.Ideology.RitualSound.Playing".Loc("RitualAmbienceSound".Translate().Resolve()));
            }
        }

        public static void MaintainRitualSound()
        {
            if (ritualSoundPreview != null)
            {
                if (ritualSoundPreview.Ended)
                {
                    ritualSoundPreview = null;
                    return;
                }
                ritualSoundPreview.Maintain();
                Find.MusicManagerPlay?.ForceSilenceFor(0.1f);
            }
        }

        public static void StopRitualSound()
        {
            if (ritualSoundPreview != null)
            {
                ritualSoundPreview.End();
                ritualSoundPreview = null;
            }
        }
    }
}
