using System;
using System.Collections.Generic;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Handler invoked when a claimed action's chord arrives while the owning
    /// scope is visible on the focus stack.
    /// </summary>
    public delegate void ActionHandler(KeyEventSnapshot e);

    /// <summary>
    /// One layer of keyboard focus: a screen, dialog, overlay, or ambient
    /// surface. A scope declares which actions it handles (claims), whether it
    /// masks input from scopes beneath it (modal), and its lifecycle hooks.
    /// Scopes hold their own cursor/tab/search state as instance fields —
    /// popping the scope IS the reset; no static mutable screen state. Scopes
    /// carry no Sync member: presentation lives in the shared Shell/Sync
    /// capture engines plus each scope's own draw-pass patches.
    ///
    /// <see cref="IsLive"/> defaults to false. A shadow scope sits on the stack
    /// for observability (DebugDump, lifecycle) but the dispatcher ignores its
    /// claims and modality, so the legacy keyboard ladder keeps full ownership.
    /// Migrated screens override IsLive to true; their claims then win over
    /// legacy handlers because the dispatcher runs first in the UIRootOnGUI
    /// prefix chain.
    ///
    /// CHOOSING A SCOPE SHAPE — the shapes differ only in how the scope is
    /// pushed and popped:
    ///  - ScreenScope for any screen that is one or more navigable
    ///    lists/tables (most are); its header carries the full
    ///    content/table/Buttons-region contract.
    ///  - Window-attached: a real Window registered once in ShellBootstrap
    ///    via ScopeForWindow, attaching/detaching with the window
    ///    (IdeoLoadScope). Related windows sharing lifecycle use
    ///    ScopeForWindow.RegisterHierarchy — exact-type registration misses
    ///    subclasses the hub opens (IdeoBuilderScreenScope).
    ///  - State-mirrored overlay: a windowless IsActive state machine pairs
    ///    its scope with a *ScopeMirror whose Reconcile() pushes/pops to
    ///    match IsActive every dispatcher pass (IdeoOverlayEditorScopes).
    ///    Mirror pushes land on the NEXT dispatcher pass — never assume
    ///    same-frame scope visibility after flipping IsActive.
    ///  - Ambient claims on the MapScope/WorldScope partials: a chord live
    ///    whenever map/world is the active base, with no scope of its own
    ///    (MapScope.Inspect.Game.cs).
    ///  - Mod-added pawn tables need NO scope: GenericPawnTableScope's
    ///    hierarchy factory covers them, and bespoke exact-type registrations
    ///    beat the hierarchy arm automatically.
    ///
    /// Rules that bite: every claimed action id must already exist in
    /// ShellActionInventory.Part1-8 (dispatch throws on unregistered claims);
    /// singleton scope instances must reset per-session fields in OnPush; a
    /// modal scope may deliberately under-claim, because the dispatcher's
    /// native modal swallow (ShellGuards.MenuOwnsInput) eats what it leaves;
    /// and when migrating a legacy state into a MODAL scope, grep first for
    /// guards written against that state's IsActive — a "no modal open"
    /// guard can invert to always-true once the state itself is modal.
    ///
    /// PURE: links into the test project.
    /// </summary>
    public abstract class FocusScope
    {
        /// <summary>Stable short name for DebugDump and logging (e.g. "map", "schedule").</summary>
        public abstract string Name { get; }

        /// <summary>
        /// Modal scopes mask scopes beneath them: while a live modal scope is
        /// focused, lower claims are not offered (except ids it passes through)
        /// and the legacy suppression guards treat it as "a menu owns input".
        /// Ambient base scopes are non-modal.
        /// </summary>
        public virtual bool IsModal
        {
            get { return true; }
        }

        /// <summary>
        /// False = shadow scope: on the stack for observability only. True:
        /// claims dispatch, modality masks, transition guards fire.
        /// </summary>
        public virtual bool IsLive
        {
            get { return false; }
        }

        /// <summary>
        /// True while this scope, when live, means "an accessibility surface
        /// owns the keyboard" for ShellGuards.MenuOwnsInput. Defaults to
        /// <see cref="IsModal"/>. Non-modal scopes that still own the game's
        /// keys (placement, viewing mode, gizmo navigation) override this to
        /// true — they don't mask lower claims, but map/world ambient openers
        /// and the dispatcher's native modal swallow must still stand down for
        /// them.
        /// </summary>
        public virtual bool OwnsGameInput
        {
            get { return IsModal; }
        }

        /// <summary>
        /// True when this scope, despite owning no window through ScopeForWindow,
        /// puts a surface on screen the viewer can see — it draws its own overlay,
        /// or it opened and anchors a vanilla window it does not formally own.
        /// Consumed only by the DEBUG visual tripwire; gameplay never reads it.
        /// </summary>
        public virtual bool HasOnScreenSurface
        {
            get { return false; }
        }

        /// <summary>
        /// Consulted by the consolidated Window.OnCancelKeyPressed prefix: when
        /// the top live scope owns cancel, vanilla's Escape handling for the
        /// associated window is suppressed and the scope's own Escape claim
        /// acts instead.
        /// </summary>
        public virtual bool OwnsCancel
        {
            get { return true; }
        }

        /// <summary>Same as <see cref="OwnsCancel"/>, for Window.OnAcceptKeyPressed (Enter).</summary>
        public virtual bool OwnsAccept
        {
            get { return false; }
        }

        /// <summary>
        /// True while this scope deliberately hands Unity keyboard focus to a NATIVE vanilla
        /// text field (an edit mode with no shell text session); ShellTextFocus's
        /// dispatcher-level foreign-focus release stands down for it.
        /// </summary>
        public virtual bool OwnsNativeTextFocus
        {
            get { return false; }
        }

        /// <summary>
        /// Character consumer for typeahead/text entry, offered typed
        /// characters before chord matching while this scope is live and
        /// focused. Null when the scope takes no character input.
        /// </summary>
        public virtual ICharSink CharSink
        {
            get { return null; }
        }

        /// <summary>Fired when the scope is added to the stack.</summary>
        public virtual void OnPush()
        {
        }

        /// <summary>Fired when the scope is removed from the stack.</summary>
        public virtual void OnPop()
        {
        }

        private List<Action> popTeardowns;

        /// <summary>
        /// Registers a teardown the focus stack runs immediately after this scope's
        /// OnPop — typically an edit session's CancelIfActive. Register once, from the
        /// constructor. The stack, not the virtual OnPop chain, runs these, so a
        /// subclass override skipping base.OnPop cannot leak a live session.
        /// </summary>
        protected void RegisterPopTeardown(Action teardown)
        {
            if (popTeardowns == null)
            {
                popTeardowns = new List<Action>();
            }
            popTeardowns.Add(teardown);
        }

        internal void RunPopTeardowns()
        {
            if (popTeardowns == null)
            {
                return;
            }
            for (int i = 0; i < popTeardowns.Count; i++)
            {
                popTeardowns[i]();
            }
        }

        /// <summary>
        /// Fired when the scope becomes the top of the stack — on its own push
        /// and again when an overlay above it pops (the re-announce hook).
        /// </summary>
        public virtual void OnFocus()
        {
        }

        /// <summary>
        /// Fired by <see cref="FocusStackCore"/> immediately after an
        /// <see cref="OnFocus"/> dispatch returns — AFTER the most-derived
        /// override's entire body has run, not just the base implementation.
        /// A base class announcing on every focus (ScreenScope's entry item)
        /// does it here so it never preempts a subclass's own title. No-op.
        /// </summary>
        public virtual void AfterFocusDispatch()
        {
        }

        /// <summary>Fired when the scope stops being the top (covered or popped).</summary>
        public virtual void OnUnfocus()
        {
        }

        private readonly List<ClaimEntry> claims = new List<ClaimEntry>();
        private readonly List<ClaimEntry> fallbackClaims = new List<ClaimEntry>();
        private List<ClaimEntry> combinedClaims;
        private bool activateAliasClaimed;
        private HashSet<string> passList;

        /// <summary>
        /// Declare that this scope handles <paramref name="actionId"/> while
        /// focused. <paramref name="propagate"/> runs the handler without
        /// consuming (observer claims); <paramref name="when"/> gates the claim
        /// on a live condition (sub-mode pairs share a chord behind opposite
        /// guards). Claims are offered in registration order.
        ///
        /// Two contract details, both pinned by tests: a live MODAL scope does
        /// not merely mask its own claims — it stops the dispatch walk
        /// outright, so scopes beneath it see nothing except ids it explicitly
        /// <see cref="PassThrough"/>es; and <paramref name="propagate"/> only
        /// exempts a claim from consuming, NOT its scope from that modal stop —
        /// a modal scope whose only matches were propagate claims still
        /// swallows the walk below it.
        /// </summary>
        protected void Claim(string actionId, ActionHandler handler, bool propagate = false, Func<bool> when = null)
        {
            if (string.IsNullOrEmpty(actionId))
            {
                throw new ArgumentException("Claim requires an action id.", "actionId");
            }
            if (handler == null)
            {
                throw new ArgumentNullException("handler");
            }
            Register(claims, actionId, handler, propagate, when);
            activateAliasClaimed |= actionId == SharedMenuGrammar.ActivateAlias;
            if (actionId == SharedMenuGrammar.Activate)
            {
                AutoAliasActivate(handler, propagate, when);
            }
        }

        /// <summary>
        /// Mod-wide Space/Enter parity: a scope claiming
        /// <see cref="SharedMenuGrammar.Activate"/> answers to Space too. Registered as a
        /// fallback, so a scope's own Space meaning still wins where it applies, and it carries
        /// the Activate claim's <c>when</c> guard so both keys are live in the same states.
        ///
        /// Two stand-downs, read per keystroke rather than at registration so a claim made later
        /// in a subclass constructor still counts. A scope stating its OWN Space handling
        /// (<see cref="DeclaresOwnActivateAlias"/>) keeps it. And a scope exposing a
        /// <see cref="CharSink"/> is typing: Space is a character there, and the keyboard
        /// delivers it as twin events whose keyCode half would reach chord dispatch and settle a
        /// search before the ' ' could extend it.
        /// </summary>
        private void AutoAliasActivate(ActionHandler handler, bool propagate, Func<bool> when)
        {
            Register(fallbackClaims, SharedMenuGrammar.ActivateAlias, handler, propagate,
                delegate
                {
                    return !DeclaresOwnActivateAlias && CharSink == null
                        && (when == null || when());
                });
        }

        private void Register(List<ClaimEntry> target, string actionId, ActionHandler handler, bool propagate, Func<bool> when)
        {
            target.Add(new ClaimEntry(actionId, handler, propagate, when));
            combinedClaims = null;
        }

        /// <summary>
        /// Declare a claim offered only after every ordinary <see cref="Claim"/>
        /// on this scope, including a subclass's: "handle this chord unless
        /// someone with a more specific meaning for it already did". A base
        /// constructor's claims otherwise register ahead of the subclass claims
        /// they defer to, and the dispatcher offers claims in registration order.
        ///
        /// Deferring by ORDER rather than by a structural stand-down guard keeps
        /// a contextual chord contextual: the shadowing claim's own <c>when</c>
        /// predicate decides per keystroke whether it applies, and the fallback
        /// fires on every row where it does not.
        /// </summary>
        protected void ClaimFallback(string actionId, ActionHandler handler, bool propagate = false, Func<bool> when = null)
        {
            if (string.IsNullOrEmpty(actionId))
            {
                throw new ArgumentException("ClaimFallback requires an action id.", "actionId");
            }
            if (handler == null)
            {
                throw new ArgumentNullException("handler");
            }
            Register(fallbackClaims, actionId, handler, propagate, when);
            activateAliasClaimed |= actionId == SharedMenuGrammar.ActivateAlias;
        }

        /// <summary>
        /// True once this scope claims <see cref="SharedMenuGrammar.ActivateAlias"/> itself,
        /// which the automatic alias then leaves alone. Read at dispatch time, so a claim
        /// registered after the Activate claim it shadows still counts.
        /// </summary>
        private bool DeclaresOwnActivateAlias
        {
            get { return activateAliasClaimed; }
        }

        /// <summary>
        /// Registers a claim from outside the scope's own declaring class.
        /// Exists for world-view parity: ambient claims registered identically
        /// on the sibling subclasses MapScope and WorldScope from one shared
        /// registrar (MapScope.QuickInfo.Game.cs), which protected access to
        /// <see cref="Claim"/> cannot reach — it only reaches an instance of
        /// the accessing class's own type, not a sibling subclass.
        /// </summary>
        internal void RegisterExternalClaim(string actionId, ActionHandler handler, bool propagate = false, Func<bool> when = null)
        {
            Claim(actionId, handler, propagate, when);
        }

        /// <summary>
        /// Exempt an action from this scope's modal masking: scopes beneath may
        /// still receive it.
        /// </summary>
        protected void PassThrough(string actionId)
        {
            if (string.IsNullOrEmpty(actionId))
            {
                throw new ArgumentException("PassThrough requires an action id.", "actionId");
            }
            if (passList == null)
            {
                passList = new HashSet<string>();
            }
            passList.Add(actionId);
        }

        internal IReadOnlyList<ClaimEntry> Claims
        {
            get
            {
                if (fallbackClaims.Count == 0)
                {
                    return claims;
                }
                if (combinedClaims == null)
                {
                    combinedClaims = new List<ClaimEntry>(claims.Count + fallbackClaims.Count);
                    combinedClaims.AddRange(claims);
                    combinedClaims.AddRange(fallbackClaims);
                }
                return combinedClaims;
            }
        }

        internal HashSet<string> PassList
        {
            get { return passList; }
        }

        internal sealed class ClaimEntry
        {
            public readonly string ActionId;
            public readonly ActionHandler Handler;
            public readonly bool Propagate;
            public readonly Func<bool> When;

            public ClaimEntry(string actionId, ActionHandler handler, bool propagate, Func<bool> when)
            {
                ActionId = actionId;
                Handler = handler;
                Propagate = propagate;
                When = when;
            }
        }

#if DEBUG
        /// <summary>
        /// Dev-bridge screen dump: a snapshot of this scope's navigable surface — every region
        /// and row, described through the same paths the screen reader uses — without moving the
        /// cursor or speaking. The base stub covers every FocusScope that isn't a ScreenScope,
        /// which overrides with the real region/row walk. See
        /// <see cref="RimWorldAccess.Shell.ShellDev.DumpScreen"/>.
        /// </summary>
        internal virtual string DebugDescribeSurface()
        {
            return "(scope " + Name + " exposes no surface dump)";
        }
#endif
    }
}
