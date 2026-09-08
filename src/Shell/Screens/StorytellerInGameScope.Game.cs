using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Keyboard focus scope for the in-game storyteller/difficulty page
    /// (<see cref="Page_SelectStorytellerInGame"/>), window-attached via ShellBootstrap. A
    /// <see cref="StorytellerScopeBase"/> subclass sharing the pre-game twin's
    /// Storytellers/Difficulty/CustomDifficulty region core.
    ///
    /// No SaveMode and no Anomaly region: <c>StorytellerUI.DrawStorytellerSelectionInterface</c>
    /// gates BOTH behind <c>ProgramState.Entry</c>, so vanilla never draws either here and
    /// <see cref="RimWorld.Dialog_AnomalySettings"/> is unreachable in-game by any means.
    /// <see cref="StorytellerScopeBase.IsCharGen"/> stays false, matching the shared section
    /// catalog omitting the playstyle picker and override slider in-game.
    ///
    /// Enter applies a selection live and re-announces rather than confirming and closing, exactly
    /// like vanilla's own mouse gesture. The page closes through its captured vanilla "Close"
    /// button or Escape.
    ///
    /// The page's ctor sets only <c>doCloseButton</c>/<c>doCloseX</c>, leaving
    /// <c>closeOnCancel</c>/<c>closeOnAccept</c> at <c>Page</c>'s false, so
    /// <c>OnCancelKeyPressed</c>/<c>OnAcceptKeyPressed</c> are no-ops here. Escape is therefore
    /// claimed unconditionally and runs <see cref="ClosePage"/>'s manual
    /// <c>WindowStack.TryRemove</c>. No window-pass twin guard is needed either: this page's
    /// <c>DoWindowContents</c> only draws, and it never calls <c>DoBottomButtons</c>, so there is
    /// no deferred Accept/Cancel poll.
    ///
    /// <b>Modality.</b> MODAL while no foreign window sits above, so unclaimed keys stop here.
    /// <see cref="OwnsGameInput"/> stays unconditionally true while live regardless.
    /// </summary>
    public sealed class StorytellerInGameScope : StorytellerScopeBase
    {
        private readonly Page_SelectStorytellerInGame page;

        public StorytellerInGameScope(Page_SelectStorytellerInGame page)
        {
            this.page = page;
            // The base's ctor claims search-clear first and wins while a search is active; this
            // fires only with no search, closing the page since it has no closeOnCancel.
            Claim(SharedMenuGrammar.Cancel, e => ClosePage(), when: () => !TypeaheadHasActiveSearch);
        }

        public override string Name
        {
            get { return "storyteller-in-game"; }
        }

        protected internal override Window OwnedWindow
        {
            get { return page; }
        }

        /// <summary>Unconditional: the page has no vanilla close-on-cancel body to fall back on, so this scope must always own Escape.</summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        public override bool IsModal
        {
            get { return !ForeignWindowAbove(); }
        }

        /// <summary>Unconditional while live: input ownership must not lapse when IsModal drops for a foreign window above.</summary>
        public override bool OwnsGameInput
        {
            get { return true; }
        }

        /// <summary>True while an unattached, non-Immediate window sits above this page.</summary>
        private bool ForeignWindowAbove()
        {
            IList<Window> windows = Find.WindowStack.Windows;
            bool above = false;
            for (int i = 0; i < windows.Count; i++)
            {
                Window window = windows[i];
                if (ReferenceEquals(window, page))
                {
                    above = true;
                    continue;
                }
                if (!above || window is ImmediateWindow)
                {
                    continue;
                }
                if (!ScopeForWindow.HasAttachedScope(window))
                {
                    return true;
                }
            }
            return false;
        }

        private void ClosePage()
        {
            ShellFrameStamps.MarkCancelConsumed();
            // Manual removal: the page has no closeOnCancel, so nothing else closes it on Escape.
            Find.WindowStack.TryRemove(typeof(Page_SelectStorytellerInGame));
        }

        // In-game there is a live Storyteller instance, so reads and writes ride
        // Current.Game.storyteller directly.

        protected override StorytellerDef CurrentStorytellerDef
        {
            get { return Current.Game.storyteller.def; }
        }

        protected override DifficultyDef CurrentDifficultyDef
        {
            get { return Current.Game.storyteller.difficultyDef; }
        }

        protected override Difficulty CurrentDifficultyValues
        {
            get { return Current.Game.storyteller.difficulty; }
        }

        protected override void ApplyStorytellerDef(StorytellerDef def)
        {
            Storyteller storyteller = Current.Game.storyteller;
            StorytellerDef oldDef = storyteller.def;
            // MUTATION-C: mirrors StorytellerUI.DrawStorytellerSelectionInterface's
            // portrait ButtonImage branch (decompiled :59-62) — assign the def, then
            // (if it actually changed) fire Notify_DefChanged and the tutor event. No
            // gated setter exists for the bare field.
            storyteller.def = def;
            if (storyteller.def != oldDef)
            {
                storyteller.Notify_DefChanged();
                TutorSystem.Notify_Event("ChooseStoryteller");
            }
        }

        protected override void ApplyDifficultyDef(DifficultyDef def, DifficultyDef previous)
        {
            Storyteller storyteller = Current.Game.storyteller;
            storyteller.difficultyDef = def;
            // BUG FIX, MUTATION-C: mirrors StorytellerUI's difficulty RadioButton
            // branch (decompiled StorytellerUI.cs:100-114) in full — a preset copies
            // its values in; switching TO Custom from a DIFFERENT difficulty seeds
            // from Rough (vanilla's own starting point for a fresh Custom edit). The
            // legacy StorytellerSelectionState.ApplyDifficultySelection omitted the
            // Rough-seed branch entirely (a divergence from vanilla mouse
            // behavior) — this restores parity. CopyFrom is vanilla's own
            // method; the raw difficultyDef field write has no gated setter.
            if (!def.isCustom)
            {
                storyteller.difficulty.CopyFrom(def);
            }
            else if (def != previous)
            {
                storyteller.difficulty.CopyFrom(DifficultyDefOf.Rough);
            }
        }
    }
}
