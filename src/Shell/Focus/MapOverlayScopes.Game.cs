namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Overlay scope for the "Go To" coordinate entry (Ctrl+G). The mod owns
    /// this surface — there is no vanilla window — so it rides the focus stack
    /// via <see cref="MapOverlayScopeMirror"/> instead of a WindowStack mirror.
    ///
    /// It is a mirror-mode text overlay: the coordinate digits arrive as layout-aware characters
    /// through the <see cref="CharSink"/>, never gated on a KeyCode.Alpha range, while the structural
    /// keys are chord claims. The scope is NON-modal, so the map cursor arrows still fall through to
    /// <see cref="MapScope"/> while typing.
    ///
    /// A consume-only claim swallows the bare digit KEYCODES. Normally the CharSink consumes the
    /// character before chord dispatch and this never fires; it exists for the "twin" delivery where
    /// the keycode arrives as a separate event, which would otherwise leak to the tile-info digits or
    /// vanilla's bare-digit time-speed bindings.
    ///
    /// <b>Element-uniformity EXCEPTION.</b> A single active text buffer, not a list of focusable
    /// rows: GoToState speaks the buffer's own contents and prompts, and the row grammar has no row
    /// to attach to. Same shape as <see cref="TextSessionScope"/>.
    /// </summary>
    public sealed class GoToScope : FocusScope
    {
        private static readonly GoToCharSink sink = new GoToCharSink();

        public GoToScope()
        {
            Claim("goTo.confirm", delegate (KeyEventSnapshot e) { GoToState.ConfirmGoTo(); }, when: NotYielding);
            Claim("goTo.cancel", delegate (KeyEventSnapshot e) { GoToState.Cancel(); }, when: NotYielding);
            Claim("goTo.switchField", delegate (KeyEventSnapshot e) { GoToState.HandleFieldSeparator(); });
            Claim("goTo.backspace", delegate (KeyEventSnapshot e) { GoToState.HandleBackspace(); });
            Claim("goTo.plus", delegate (KeyEventSnapshot e) { GoToState.HandleCharacter('+'); });
            Claim("goTo.minus", delegate (KeyEventSnapshot e) { GoToState.HandleCharacter('-'); });
            // Consume-only twin block; see class remarks. No-op handler.
            Claim("goTo.blockDigit", delegate (KeyEventSnapshot e) { });
        }

        public override string Name
        {
            get { return "goto"; }
        }

        public override bool IsModal
        {
            get { return false; }
        }

        public override bool IsLive
        {
            get { return true; }
        }

        public override ICharSink CharSink
        {
            get { return sink; }
        }

        /// <summary>
        /// Go To yields its Enter/Escape to a real overlay menu on top of it (architect tree, float
        /// menu, shape picker) but NOT to placement mode. This guard is the ONLY thing covering the
        /// tree and shape menus, which are not modal scopes over this one; the float-menu term is
        /// redundant with FloatMenuOverlayScope's modal masking but kept as documentation.
        /// </summary>
        private static bool NotYielding()
        {
            return !GoToState.ShouldYieldToOverlayMenu();
        }

        private sealed class GoToCharSink : ICharSink
        {
            public bool HandleChar(char c)
            {
                if (c >= '0' && c <= '9')
                {
                    GoToState.HandleCharacter(c);
                    return true;
                }
                return false;
            }
        }
    }

    /// <summary>
    /// Overlay scope for the colony-map scanner's name search (Z). Like
    /// <see cref="GoToScope"/> it is a non-modal mirror-mode text overlay: the
    /// filter characters (letters and digits, layout-aware) arrive through the
    /// <see cref="CharSink"/>, the structural keys (Enter to confirm, Escape to
    /// cancel, Backspace) are chord claims, and a consume-only block swallows
    /// the letter/digit KEYCODE twins so they cannot leak to the ladder's
    /// bare-letter menu openers (route planner R, notifications L, settlement
    /// browser S, ...) exactly as the legacy handler's letter/digit consume did.
    ///
    /// ScannerSearchState is SHARED with the world scanner and the pre-game world-gen screen. The
    /// mirror pushes this scope for both the colony and the in-game world session; the claims are
    /// identical for either, since none of ScannerSearchState's handlers branch on IsOnWorldMap.
    ///
    /// <b>Element-uniformity EXCEPTION.</b> Same reasoning as <see cref="GoToScope"/>: a single
    /// active text buffer, not a list of focusable rows.
    /// </summary>
    public sealed class ScannerSearchScope : FocusScope
    {
        private static readonly ScannerSearchCharSink sink = new ScannerSearchCharSink();

        public ScannerSearchScope()
        {
            Claim("scannerSearch.confirm", delegate (KeyEventSnapshot e) { ScannerSearchState.ConfirmSearch(); });
            Claim("scannerSearch.cancel", delegate (KeyEventSnapshot e) { ScannerSearchState.CancelSearch(); });
            Claim("scannerSearch.backspace", delegate (KeyEventSnapshot e) { ScannerSearchState.HandleBackspace(); });
            // Consume-only twin block; see class remarks. No-op handler.
            Claim("scannerSearch.blockChar", delegate (KeyEventSnapshot e) { });
        }

        public override string Name
        {
            get { return "scanner-search"; }
        }

        public override bool IsModal
        {
            get { return false; }
        }

        public override bool IsLive
        {
            get { return true; }
        }

        public override ICharSink CharSink
        {
            get { return sink; }
        }

        private sealed class ScannerSearchCharSink : ICharSink
        {
            public bool HandleChar(char c)
            {
                if (char.IsLetterOrDigit(c))
                {
                    ScannerSearchState.HandleCharacter(c);
                    return true;
                }
                return false;
            }
        }
    }

    /// <summary>
    /// Keeps the mod-owned map overlay scopes (Go To, and colony plus in-game-world scanner search)
    /// in lockstep with their IsActive state, reconciled every OnGUI pass. The state classes stay
    /// ignorant of the focus stack.
    /// This mirror is reconciled AFTER TargetingScopeMirror, and Push re-floats a scope already on
    /// the stack, so whenever a targeting session and a scanner search are both live the search wins
    /// the shared keys. TargetingScopeMirror is the single mirror for every TargetingScope variant,
    /// world ones included, so no targeting path can miss that ordering.
    /// </summary>
    internal static class MapOverlayScopeMirror
    {
        private static readonly GoToScope goToScope = new GoToScope();
        private static readonly ScannerSearchScope scannerSearchScope = new ScannerSearchScope();

        public static void Reconcile()
        {
            if (GoToState.IsActive)
            {
                FocusStack.Push(goToScope);
            }
            else
            {
                FocusStack.Pop(goToScope);
            }

            // Bare IsActive: every search session rides this scope, world-gen included. The claims
            // and CharSink are context-neutral — none of ScannerSearchState's handlers branch on
            // IsOnWorldMap.
            if (ScannerSearchState.IsActive)
            {
                FocusStack.Push(scannerSearchScope);
            }
            else
            {
                FocusStack.Pop(scannerSearchScope);
            }
        }
    }
}
