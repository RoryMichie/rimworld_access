using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The scenario editor's windowless add-part overlay; its backing state
    /// (<see cref="ScenarioBuilderAddPartState"/>) carries lifecycle, data and mutation vehicles
    /// only. Save and Load are the real <c>Dialog_ScenarioList_Save/Load</c> windows on
    /// <see cref="FileListScope"/>, not overlays here.
    ///
    /// <b>Windowless and MODAL.</b> The host beneath is
    /// <see cref="ScenarioEditorScreenScope"/>, a full row/region model where Left/Right expands
    /// a part, Delete removes one and Tab switches regions — every one of which would silently
    /// fire on the WRONG screen if an unclaimed key leaked through. So this scope keeps
    /// <see cref="FocusScope.IsModal"/>'s base default (true) and its <see cref="OwnsCancel"/> is
    /// unconditional. It has no draw pass of its own to bracket, so capture is off; the editor
    /// page beneath keeps its own pass.
    ///
    /// <see cref="ScenarioScopeGuards.WindowAboveScenarioEditor"/> is what lets the guard twins on
    /// <see cref="ScenarioEditorScreenScope"/> recognize any real window above the editor page and
    /// stand down.
    /// </summary>
    public sealed class ScenarioAddPartScreenScope : ScreenScope
    {
        private readonly List<ScreenAction> actions = new List<ScreenAction>();
        private bool announcedOpen;

        public ScenarioAddPartScreenScope()
        {
            Claim(SharedMenuGrammar.Cancel, e =>
            {
                ShellFrameStamps.MarkCancelConsumed();
                CloseAndCancel();
            }, when: () => !TypeaheadHasActiveSearch);
        }

        public override string Name
        {
            get { return "scenario-add-part"; }
        }

        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        /// <summary>Unconditional — see the class remarks on modality.</summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

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
            announcedOpen = false;
        }

        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen)
            {
                return;
            }
            announcedOpen = true;
            // A long-lived singleton whose Model persists across every open/close cycle, and
            // SetCount only CLAMPS the cursor on refresh rather than resetting it — so without
            // this the region cursor would stay wherever a previous session left it.
            Model.MoveToRegion(0);
            ListModel firstRegion = Model.CurrentRegion;
            if (firstRegion != null)
            {
                firstRegion.MoveFirst();
            }
            AnnounceOpening();
        }

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        protected override string ContentRegionName(int region)
        {
            return (string)"AddPart".Translate();
        }

        protected override int ContentItemCount(int region)
        {
            return ScenarioBuilderAddPartState.AvailableParts.Count;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            IReadOnlyList<ScenPartDef> parts = ScenarioBuilderAddPartState.AvailableParts;
            if (index < 0 || index >= parts.Count)
            {
                return new ElementDescription();
            }
            ScenPartDef def = parts[index];
            var d = new ElementDescription { Label = def.LabelCap, Role = ElementRole.Button };
            if (!string.IsNullOrEmpty(def.description))
            {
                d.Extras = def.description;
            }
            return d;
        }

        protected override void ActivateContentItem(int region, int index)
        {
            IReadOnlyList<ScenPartDef> parts = ScenarioBuilderAddPartState.AvailableParts;
            if (index < 0 || index >= parts.Count)
            {
                return;
            }
            ShellFrameStamps.MarkAcceptConsumed();
            ScenarioBuilderAddPartState.Confirm(parts[index]);
        }

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                actions.Add(new ScreenAction("Back".Translate(), CloseAndCancel, SharedMenuGrammar.Cancel));
                return actions;
            }
        }

        private void CloseAndCancel()
        {
            ScenarioBuilderAddPartState.Cancel();
        }

        private void AnnounceOpening()
        {
            int count = ScenarioBuilderAddPartState.AvailableParts.Count;
            TolkHelper.SpeakData(
                "RimWorldAccess.ScenarioBuilder.AddPart.OpenInstructions".Translate(count),
                SpeechPriority.High);
            AnnounceCurrentItem();
        }
    }

    /// <summary>
    /// Reconciles the add-part overlay scope (the save/load pickers are the real
    /// <c>Dialog_ScenarioList_Save/Load</c> windows on <see cref="FileListScope"/> now), sharing
    /// the <see cref="ScenarioScopeGuards.WindowAboveScenarioEditor"/> stand-down.
    /// </summary>
    internal static class ScenarioOverlayScopeMirror
    {
        private static readonly ScenarioAddPartScreenScope addPart = new ScenarioAddPartScreenScope();

        public static void Reconcile()
        {
            bool standDown = ScenarioScopeGuards.WindowAboveScenarioEditor();

            ReconcileOne(addPart, ScenarioBuilderAddPartState.IsActive && !standDown);
        }

        // Push ONLY when not already on the stack: an unconditional Push would re-float the scope
        // and re-fire OnFocus every frame while a real window temporarily masks it.
        private static void ReconcileOne(FocusScope scope, bool live)
        {
            if (live)
            {
                if (!FocusStack.Contains(scope))
                {
                    FocusStack.Push(scope);
                }
            }
            else
            {
                FocusStack.Pop(scope);
            }
        }
    }

    /// <summary>
    /// Shared stand-down signal for the overlay mirror above: true while a real (non-Immediate)
    /// window sits above the editor page, so the mirror never re-floats over MessageBoxScope on
    /// the unsaved-changes, version-mismatch or workshop-confirm boxes, or the load picker's own
    /// delete confirmation — and vanilla's scopeless dialogs get untouched vanilla routing.
    /// </summary>
    internal static class ScenarioScopeGuards
    {
        internal static bool WindowAboveScenarioEditor()
        {
            WindowStack stack = Find.WindowStack;
            if (stack == null)
            {
                return false;
            }
            IList<Window> windows = stack.Windows;
            bool above = false;
            for (int i = 0; i < windows.Count; i++)
            {
                Window window = windows[i];
                if (window is Page_ScenarioEditor)
                {
                    above = true;
                    continue;
                }
                if (above && !(window is ImmediateWindow))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
