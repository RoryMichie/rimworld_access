using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The Shape-B mirror scope for the factions ADD MENU overlay on
    /// Page_CreateWorldParams (<see cref="FactionsNavigationState.IsAddMenuOpen"/>).
    /// Relocated verbatim from the retired WorldParamsScope host file when the
    /// page's anchor scope became <see cref="WorldParamsScreenScope"/>; the modal
    /// add-menu overlay itself is unchanged this slice.
    ///
    /// Promoted to <see cref="ScreenScope"/> (Recipe B): one content region
    /// over the state's addable-faction options, no
    /// Buttons region (this overlay draws nothing of its own to capture), and the
    /// windowless Escape pattern. <see cref="FactionAddMenuState"/> keeps its
    /// lifecycle, its option list and its add mutation and sheds the cursor,
    /// typeahead and announcement plumbing the chassis provides.
    ///
    /// <b>MODAL, deliberately.</b> The retired in-pass handler ended its add-menu
    /// branch in an unconditional consume-all, so no key reached the page handling
    /// or the deferred polls while the menu was open. A modal scope reproduces both
    /// halves: claimed keys route here; unclaimed keys stop at the modal boundary
    /// and the ladder's catch-all (AnyLiveModal) consumes the main pass, while the
    /// Enter/Escape stamps shield the deferred DoBottomButtons polls the
    /// dispatcher's main-pass consume cannot reach (the I1 stamp guard) — without
    /// the Enter stamp, confirming a faction would ALSO queue vanilla's async world
    /// generation. Enter is stamped by the chassis (<c>ScreenScope</c>'s Activate
    /// claim stamps the accept frame unconditionally, before any row runs); Escape
    /// is stamped by this scope's own Cancel claim below, both the typeahead-clear
    /// tier (the chassis's, which stamps too) and the close tier. The
    /// <see cref="WorldParamsPatch_CanDoNext"/> guard additionally blocks the raw
    /// Accept poll while this overlay is open (its IsAddMenuOpen term), and
    /// <see cref="WorldParamsPatch_CanDoBack"/> holds Back the same way.
    /// </summary>
    public sealed class WorldParamsAddFactionScope : ScreenScope
    {
        public WorldParamsAddFactionScope()
        {
            Claim(SharedMenuGrammar.Cancel, e =>
            {
                ShellFrameStamps.MarkCancelConsumed();
                FactionsNavigationState.CloseAddMenu();
            }, when: () => !TypeaheadHasActiveSearch);
        }

        public override string Name
        {
            get { return "world-params-add-faction"; }
        }

        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        /// <summary>Unconditional — the windowless pattern: the page beneath must never see this overlay's Escape.</summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        protected override bool IncludeActionsRegion
        {
            get { return false; }
        }

        /// <summary>
        /// This scope is a long-lived singleton (<see cref="WorldParamsAddFactionScopeMirror"/>'s
        /// static readonly field) whose Model outlives every open, and SetCount only clamps the
        /// cursor rather than resetting it — so each open starts at the first option again (the
        /// state's own index-0 reset on Open) and re-arms the opening announcement.
        /// </summary>
        public override void OnPush()
        {
            base.OnPush();
            ResetOpenAnnouncement();
            Model.MoveToRegion(0);
            ListModel region = Model.CurrentRegion;
            if (region != null)
            {
                region.MoveFirst();
            }
        }

        protected override string ComposeOpenAnnouncement()
        {
            return "RimWorldAccess.Factions.AddMenuOpened".Translate().ToString();
        }

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        protected override string ContentRegionName(int region)
        {
            return "RimWorldAccess.Factions.AddMenuOpened".Translate().ToString();
        }

        protected override int ContentItemCount(int region)
        {
            return FactionAddMenuState.Options.Count;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            IReadOnlyList<FactionAddMenuState.AddMenuOption> options = FactionAddMenuState.Options;
            if (index < 0 || index >= options.Count)
            {
                return new ElementDescription();
            }
            FactionAddMenuState.AddMenuOption option = options[index];
            var d = new ElementDescription
            {
                Label = option.Faction.LabelCap.ToString(),
                Role = ElementRole.MenuItem,
                Disabled = option.IsDisabled,
            };
            // Count and reason are mutually exclusive, as the legacy label was: a
            // disabled faction speaks only why, never how many are already in the list.
            var extras = new List<string>();
            if (option.IsDisabled)
            {
                if (!string.IsNullOrEmpty(option.DisabledReason))
                {
                    extras.Add(option.DisabledReason);
                }
            }
            else if (option.Count > 0)
            {
                extras.Add("RimWorldAccess.Factions.CountInList".Translate(option.Count).ToString());
            }
            if (!string.IsNullOrEmpty(option.Faction.Description))
            {
                extras.Add(option.Faction.Description.StripTags());
            }
            if (extras.Count > 0)
            {
                d.Extras = string.Join(". ", extras);
            }
            return d;
        }

        protected override void ActivateContentItem(int region, int index)
        {
            FactionsNavigationState.AddMenuConfirm(index);
        }
    }

    /// <summary>
    /// Keeps <see cref="WorldParamsAddFactionScope"/> in lockstep with
    /// <see cref="FactionsNavigationState.IsAddMenuOpen"/> (a plain flag,
    /// Entry-only by construction — opened solely from the factions section,
    /// reset by WorldParamsPatch's PreOpen and PreClose patches). Unlike the
    /// I3 custom-difficulty state, real windows CAN stack while this
    /// windowless overlay is open — the page stays fully mouse-interactive
    /// (planet-coverage/faction FloatMenus, Dialog_AdvancedGameConfig, info
    /// cards) — and a mirror's per-pass Push would re-float this MODAL scope
    /// above their window-attached scopes every frame, so the mirror stands
    /// down while any real window sits above the page (the I2
    /// ScenarioScopeGuards posture). The same walk doubles as the law-7
    /// stale-flag backstop: if the page is not on the stack at all, the
    /// scope is popped regardless of the flag.
    /// </summary>
    internal static class WorldParamsAddFactionScopeMirror
    {
        private static readonly WorldParamsAddFactionScope scope = new WorldParamsAddFactionScope();

        public static void Reconcile()
        {
            if (FactionsNavigationState.IsAddMenuOpen && PageDrivable())
            {
                FocusStack.Push(scope);
            }
            else
            {
                FocusStack.Pop(scope);
            }
        }

        /// <summary>
        /// True while Page_CreateWorldParams is on the window stack with no
        /// real (non-immediate) window above it. Tooltips render as
        /// Super-layer ImmediateWindows every frame and are skipped (the
        /// OptionsScope foreign-guard precedent).
        /// </summary>
        private static bool PageDrivable()
        {
            WindowStack stack = Find.WindowStack;
            if (stack == null)
            {
                return false;
            }
            IList<Window> windows = stack.Windows;
            bool found = false;
            for (int i = 0; i < windows.Count; i++)
            {
                Window window = windows[i];
                if (window is Page_CreateWorldParams)
                {
                    found = true;
                    continue;
                }
                if (found && !(window is ImmediateWindow))
                {
                    return false;
                }
            }
            return found;
        }
    }
}
