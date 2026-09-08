using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Registry mapping vanilla <see cref="Window"/> types to the <see cref="FocusScope"/>
    /// representing them on the focus stack, plus the Harmony postfixes that mirror the
    /// game's window stack with no per-dialog wiring.
    /// <see cref="WindowStack.Add"/> and the <c>(Window, bool)</c> overload of
    /// <see cref="WindowStack.TryRemove(Window, bool)"/> are the only window-lifecycle choke
    /// points — <c>TryRemove(Type, bool)</c>, <c>TryRemoveAssignableFromType</c>,
    /// <see cref="Window.Close"/> and Add's own onlyOneOfTypeAllowed eviction all route through
    /// that overload.
    /// </summary>
    public static class ScopeForWindow
    {
        /// <summary>
        /// Exact-type lookup (<c>window.GetType() == key</c>), consulted first; a subclass does NOT
        /// match. Families opt in via <see cref="RegisterHierarchy"/>/<see cref="RegisterGenericHierarchy"/>.
        /// </summary>
        private static readonly Dictionary<Type, Func<Window, FocusScope>> factories =
            new Dictionary<Type, Func<Window, FocusScope>>();

        private struct HierarchyEntry
        {
            public Type BaseType;
            public bool OpenGeneric;
            public Func<Window, FocusScope> Factory;
        }

        /// <summary>
        /// Family registrations, consulted in registration order only after the exact-type lookup
        /// missed. A list, not a dictionary: resolution is a per-entry type test.
        /// </summary>
        private static readonly List<HierarchyEntry> hierarchyFactories =
            new List<HierarchyEntry>();

        /// <summary>The scope currently pushed for each live window, so the TryRemove postfix knows what to pop.</summary>
        private static readonly Dictionary<Window, FocusScope> attachedScopes =
            new Dictionary<Window, FocusScope>();

        /// <summary>
        /// Per-window cap on bespoke re-attach attempts by <see cref="ReconcileAttachments"/>,
        /// bounding a scope that keeps dying after re-attach. Pruned by the liveness sweep once the
        /// window closes; <see cref="Detach"/> must NOT clear them — the desync sweep routes through
        /// Detach while the window is still open, so clearing there re-arms the cap mid-loop.
        /// </summary>
        private static readonly Dictionary<Window, int> bespokeReattachAttempts =
            new Dictionary<Window, int>();

        private const int MaxBespokeReattachAttempts = 4;

        /// <summary>
        /// Register the focus scope factory for a vanilla window type, once per type. Throws on a
        /// duplicate rather than letting one of two claimants silently win.
        /// </summary>
        public static void Register(Type windowType, Func<Window, FocusScope> factory)
        {
            if (windowType == null)
            {
                throw new ArgumentNullException("windowType");
            }
            if (factory == null)
            {
                throw new ArgumentNullException("factory");
            }
            if (factories.ContainsKey(windowType))
            {
                throw new InvalidOperationException(
                    "A focus scope factory is already registered for window type '" + windowType.FullName + "'.");
            }
            factories.Add(windowType, factory);
        }

        /// <summary>
        /// Opts a third-party window, named at runtime, into the generic reader even though its
        /// posture (no <see cref="Window.absorbInputAroundWindow"/>) would make
        /// <see cref="GenericReaderEligible"/> refuse it. By name, so the mod's assembly is never
        /// referenced. Returns false rather than throwing when the type cannot be resolved or is
        /// already registered; third-party types legitimately come and go.
        /// </summary>
        public static bool TryRegisterGenericReaderForWindow(string windowTypeName)
        {
            if (string.IsNullOrEmpty(windowTypeName))
            {
                return false;
            }
            Type windowType = AccessTools.TypeByName(windowTypeName);
            if (windowType == null || !typeof(Window).IsAssignableFrom(windowType)
                || factories.ContainsKey(windowType))
            {
                return false;
            }
            factories.Add(windowType, w => new GenericWindowScope(w));
            return true;
        }

        /// <summary>
        /// Register a factory for a window type and every subclass, modded ones included.
        /// Exact-type registrations win, so a family can coexist with a bespoke scope for one member.
        /// </summary>
        public static void RegisterHierarchy(Type baseType, Func<Window, FocusScope> factory)
        {
            if (baseType == null)
            {
                throw new ArgumentNullException("baseType");
            }
            if (factory == null)
            {
                throw new ArgumentNullException("factory");
            }
            EnsureNotAlreadyInHierarchy(baseType);
            HierarchyEntry entry;
            entry.BaseType = baseType;
            entry.OpenGeneric = false;
            entry.Factory = factory;
            hierarchyFactories.Add(entry);
        }

        /// <summary>
        /// Register a factory for every closed construction of an open generic window base (e.g.
        /// <c>Dialog_Rename&lt;T&gt;</c>) and their subclasses. IsAssignableFrom cannot express that
        /// relation, so the match walks the BaseType chain comparing generic type definitions.
        /// </summary>
        public static void RegisterGenericHierarchy(Type openGenericBase, Func<Window, FocusScope> factory)
        {
            if (openGenericBase == null)
            {
                throw new ArgumentNullException("openGenericBase");
            }
            if (!openGenericBase.IsGenericTypeDefinition)
            {
                throw new ArgumentException(
                    "'" + openGenericBase.FullName + "' is not an open generic type definition.", "openGenericBase");
            }
            if (factory == null)
            {
                throw new ArgumentNullException("factory");
            }
            EnsureNotAlreadyInHierarchy(openGenericBase);
            HierarchyEntry entry;
            entry.BaseType = openGenericBase;
            entry.OpenGeneric = true;
            entry.Factory = factory;
            hierarchyFactories.Add(entry);
        }

        private static void EnsureNotAlreadyInHierarchy(Type baseType)
        {
            for (int i = 0; i < hierarchyFactories.Count; i++)
            {
                if (hierarchyFactories[i].BaseType == baseType)
                {
                    throw new InvalidOperationException(
                        "A hierarchy scope factory is already registered for base type '" + baseType.FullName + "'.");
                }
            }
        }

        private static Func<Window, FocusScope> ResolveHierarchyFactory(Type windowType)
        {
            for (int i = 0; i < hierarchyFactories.Count; i++)
            {
                HierarchyEntry entry = hierarchyFactories[i];
                if (entry.OpenGeneric)
                {
                    for (Type t = windowType; t != null; t = t.BaseType)
                    {
                        if (t.IsGenericType && t.GetGenericTypeDefinition() == entry.BaseType)
                        {
                            return entry.Factory;
                        }
                    }
                }
                else if (entry.BaseType.IsAssignableFrom(windowType))
                {
                    return entry.Factory;
                }
            }
            return null;
        }

        /// <summary>
        /// The window a live scope was pushed for, or null for an overlay scope or any scope this
        /// registry never attached. Lets <c>WindowKeyRouter</c> tell "this window's own scope owns
        /// cancel" from "an overlay above it owns cancel".
        /// </summary>
        internal static Window WindowOf(FocusScope scope)
        {
            foreach (KeyValuePair<Window, FocusScope> pair in attachedScopes)
            {
                if (ReferenceEquals(pair.Value, scope))
                {
                    return pair.Key;
                }
            }
            return null;
        }

        /// <summary>
        /// Whether the mirror pushed a scope for this window. Windows with a scope take the
        /// focus-stack top themselves, so only scopeless ones count as foreign overlays.
        /// </summary>
        internal static bool HasAttachedScope(Window window)
        {
            return window != null && attachedScopes.ContainsKey(window);
        }

        /// <summary>
        /// Drop all attached-window bookkeeping, re-attach counters and eligibility verdicts at a
        /// session boundary. Does NOT clear <see cref="factories"/>: registrations are a
        /// startup-time constant, not session state.
        /// </summary>
        internal static void Reset()
        {
            attachedScopes.Clear();
            bespokeReattachAttempts.Clear();
            genericEligibility.Clear();
        }

        /// <summary>
        /// Attach and push the registered scope for <paramref name="window"/>, if it has one, else
        /// the generic reader.
        /// </summary>
        /// <summary>Whether this scope is currently attached to a live window.</summary>
        internal static bool IsAttachedScope(FocusScope scope)
        {
            return scope != null && attachedScopes.ContainsValue(scope);
        }

        internal static void Attach(Window window)
        {
            Func<Window, FocusScope> factory;
            if (!factories.TryGetValue(window.GetType(), out factory))
            {
                factory = ResolveHierarchyFactory(window.GetType());
            }
            FocusScope scope;
            if (factory == null)
            {
                scope = TryCreateGenericReader(window);
            }
            else
            {
                scope = factory(window);
            }
            if (scope == null)
            {
                return;
            }
            attachedScopes[window] = scope;
            // FocusStackCore's own "push <scope>" line can't name the window; this one does.
            FlightRecorder.Record("scope", "attach " + scope.Name + " for " + window.GetType().FullName);

            // When an outer window's PostOpen synchronously opens an inner one (Add calls PostOpen
            // last), the inner Add completes first and the outer postfix runs last, so a plain push
            // would mask the inner scope. Keep the focus stack ordered to match WindowStack z-order.
            FocusScope insertReference = FindLowestScopeAboveWindow(window);
            if (insertReference != null)
            {
                FocusStack.InsertBelow(scope, insertReference);
            }
            else
            {
                FocusStack.Push(scope);
            }
        }

        /// <summary>
        /// Replace the attached scope of an open window (a screen with two views). One reconcile
        /// bracket, so only the net top-of-stack change fires focus events.
        /// </summary>
        internal static void Swap(Window window, Func<Window, FocusScope> factory)
        {
            FocusScope old;
            if (window == null || factory == null || !attachedScopes.TryGetValue(window, out old))
            {
                return;
            }
            FocusScope fresh = factory(window);
            if (fresh == null)
            {
                return;
            }
            FocusStack.BeginReconcile();
            try
            {
                FocusStack.Pop(old);
                attachedScopes[window] = fresh;
                FocusStack.Push(fresh);
            }
            finally
            {
                FocusStack.EndReconcile();
            }
            FlightRecorder.Record("scope", "swap " + old.Name + " -> " + fresh.Name + " for " + window.GetType().FullName);
        }

        /// <summary>
        /// Replays <see cref="Attach"/> for an already-open window, for factories that only yield a
        /// scope once armed. No-op when a scope is already attached.
        /// </summary>
        internal static void AttachOnDemand(Window window)
        {
            if (window == null || attachedScopes.ContainsKey(window))
            {
                return;
            }
            Attach(window);
        }

        /// <summary>
        /// The generic fallback factory, consulted only after both registry lookups miss; null when
        /// the window is not a modal dialog surface. Eligibility is the window's own posture rather
        /// than a type list: <see cref="Window.absorbInputAroundWindow"/> is how a window declares
        /// the modal contract GenericWindowScope assumes, and attaching a modal scope to a
        /// coexisting surface masks what the player is really driving.
        /// </summary>
        private static FocusScope TryCreateGenericReader(Window window)
        {
            return GenericReaderEligible(window) ? new GenericWindowScope(window) : null;
        }

        /// <summary>
        /// The eligibility half of <see cref="TryCreateGenericReader"/>, exposed so announce-only
        /// fallback tiers can stand down for a window the reader owns without asking
        /// <see cref="HasAttachedScope"/>, which is transient: a window draws one more teardown pass
        /// after its scope detaches, and a fallback gated on attach state speaks into that gap. The
        /// verdict is decided once per window and held — see <see cref="genericEligibility"/>.
        /// </summary>
        internal static bool GenericReaderEligible(Window window)
        {
            if (window == null)
            {
                return false;
            }
            if (window is ImmediateWindow || window is FloatMenu)
            {
                // Never cached: minted and discarded per frame, and neither is ever eligible.
                return false;
            }
            bool cached;
            if (genericEligibility.TryGetValue(window, out cached))
            {
                return cached;
            }
            bool verdict = ComputeGenericReaderEligible(window);
            PruneEligibilityCache();
            genericEligibility[window] = verdict;
            return verdict;
        }

        /// <summary>
        /// Ownership verdicts already handed out, keyed by window. A verdict never changes ON
        /// PURPOSE: the underlying posture is not stable — a window may rewrite its own
        /// <see cref="Window.absorbInputAroundWindow"/> mid-draw, as mod-settings pages do to stay
        /// clickable over the map — and the verdict must survive the extra draw pass a window makes
        /// after its scope detaches.
        /// </summary>
        private static readonly Dictionary<Window, bool> genericEligibility = new Dictionary<Window, bool>();

        /// <summary>
        /// Drops verdicts for windows that have left the stack. Never evicts an
        /// open window, so the teardown-pass reader still finds its verdict.
        /// </summary>
        private static void PruneEligibilityCache()
        {
            const int PruneThreshold = 16;
            if (genericEligibility.Count < PruneThreshold)
            {
                return;
            }
            IList<Window> live = Find.WindowStack != null ? Find.WindowStack.Windows : null;
            if (live == null)
            {
                genericEligibility.Clear();
                return;
            }
            List<Window> stale = new List<Window>();
            foreach (KeyValuePair<Window, bool> entry in genericEligibility)
            {
                if (!live.Contains(entry.Key))
                {
                    stale.Add(entry.Key);
                }
            }
            for (int i = 0; i < stale.Count; i++)
            {
                genericEligibility.Remove(stale[i]);
            }
        }

        private static bool ComputeGenericReaderEligible(Window window)
        {
            if (window.absorbInputAroundWindow)
            {
                return true;
            }
            // Refused outright: the debug log auto-opens on errors and could race into the watch
            // below, and dev windows have their own armed scopes.
            if (window is LudeonTK.Window_Dev)
            {
                return false;
            }
            // The deliberate-attach watch: a non-absorbing window the player just asked for IS the
            // surface they are driving (Character Editor's EditorUI coexists with the map, leaving
            // it scopeless). Vanilla-assembly windows are excluded — the watch is armed only by
            // activating a MOD main tab, so a vanilla window arriving inside it is a side effect.
            return DeliberateGenericWatchActive
                && window.GetType().Assembly != typeof(Window).Assembly;
        }

        /// <summary>
        /// Frame deadline of the deliberate-attach watch. <see cref="Time.frameCount"/> is
        /// app-lifetime monotonic, so a stale deadline needs no session reset.
        /// </summary>
        private static int deliberateGenericDeadline = -1;

        /// <summary>
        /// How long an armed watch accepts non-absorbing windows. The window can arrive passes AFTER
        /// the activation returns (Character Editor opens a 1x1 bridge MainTabWindow whose first
        /// DoWindowContents spawns the real editor), so a synchronous flag in the opener cannot see
        /// it. Two seconds covers any such trampoline and is too short for the player to move on.
        /// </summary>
        private const int DeliberateAttachWindowFrames = 120;

        private static bool DeliberateGenericWatchActive
        {
            get { return Time.frameCount <= deliberateGenericDeadline; }
        }

        /// <summary>
        /// Arms <see cref="GenericReaderEligible"/> to also accept non-absorbing windows for the next
        /// <see cref="DeliberateAttachWindowFrames"/> frames; call immediately before deliberately
        /// opening a surface on the player's behalf. Every qualifying window inside the watch gets
        /// the reader — deliberately not first-match-consumed, or a trampoline window would eat the
        /// watch before the real editor appears.
        /// </summary>
        internal static void ArmDeliberateGenericAttach()
        {
            deliberateGenericDeadline = Time.frameCount + DeliberateAttachWindowFrames;
        }

        /// <summary>
        /// The attached scope whose window sits above <paramref name="window"/> in z-order and is
        /// lowest on the focus stack — the reference to insert beneath, or null to plain-push.
        /// </summary>
        private static FocusScope FindLowestScopeAboveWindow(Window window)
        {
            IList<Window> windows = Find.WindowStack != null ? Find.WindowStack.Windows : null;
            if (windows == null)
            {
                return null;
            }
            int thisIndex = windows.IndexOf(window);
            if (thisIndex < 0)
            {
                return null;
            }

            IReadOnlyList<FocusScope> stack = FocusStack.ScopesBottomUp;
            FocusScope reference = null;
            int referenceStackPos = int.MaxValue;
            foreach (KeyValuePair<Window, FocusScope> pair in attachedScopes)
            {
                if (ReferenceEquals(pair.Key, window))
                {
                    continue;
                }
                int otherIndex = windows.IndexOf(pair.Key);
                if (otherIndex <= thisIndex)
                {
                    continue; // not above this window in z-order
                }
                int stackPos = IndexInStack(stack, pair.Value);
                if (stackPos >= 0 && stackPos < referenceStackPos)
                {
                    referenceStackPos = stackPos;
                    reference = pair.Value;
                }
            }
            return reference;
        }

        private static int IndexInStack(IReadOnlyList<FocusScope> stack, FocusScope scope)
        {
            for (int i = 0; i < stack.Count; i++)
            {
                if (ReferenceEquals(stack[i], scope))
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// Pop and detach the scope attached to <paramref name="window"/>, if any. A scopeless window
        /// closing is the foreign-close case: see <see cref="NotifyForeignWindowClosed"/>.
        /// </summary>
        internal static void Detach(Window window)
        {
            FocusScope scope;
            if (!attachedScopes.TryGetValue(window, out scope))
            {
                NotifyForeignWindowClosed(window);
                return;
            }
            attachedScopes.Remove(window);
            FocusStack.Pop(scope);
        }

        /// <summary>
        /// A scopeless window closed (storyteller page, mod settings, key bindings).
        /// A live scope underneath just became the player's surface again with no
        /// pop to fire OnFocus, so nudge it. Gated on the top scope reporting itself
        /// modal — one that released modality (drill-in overlay, another foreign
        /// window above) is not the surface returned to — and stands down while a
        /// legacy keyboard overlay is active, since its own close path nudges.
        /// <see cref="ImmediateWindow"/>s are excluded: they never own keyboard
        /// focus, and refocusing on their churn double-announces every row on
        /// tooltip-rich screens. Not filtered by WindowLayer.Super — real FloatMenus
        /// are Super-layer and their closes must keep refocusing.
        /// </summary>
        private static void NotifyForeignWindowClosed(Window window)
        {
            if (window is ImmediateWindow)
            {
                return;
            }
            FocusScope top = FocusStack.Top;
            if (top == null || !top.IsLive || !top.IsModal)
            {
                return;
            }
            if (ShellDispatcherPatch.LegacyKeyboardOverlayActive())
            {
                return;
            }
            FocusStack.RefocusTop();
        }

        /// <summary>
        /// Safety-net sweep detaching any attached scope whose window has left
        /// <see cref="Find.WindowStack"/>. Attach/Detach observe only the two choke
        /// points, and a window can leave through a path that defeats the postfixes'
        /// ordering: a PostOpen that synchronously removes itself inside
        /// <c>WindowStack.Add</c>'s own body (<c>Dialog_FormCaravan</c> via
        /// <c>WorldRoutePlanner.Start</c>), or an explicit <c>TryRemove</c> racing a
        /// removal already in flight so <c>__result</c> comes back false and
        /// <see cref="ScopeForWindowRemovePatch"/> skips Detach. Both bypass the same
        /// choke points from different directions, so no per-call-site fix closes
        /// them. Called every OnGUI pass from
        /// <see cref="ShellDispatcherPatch.Prefix"/> before dispatch and ahead of the
        /// per-screen mirror reconciles. Snapshots orphans before detaching so
        /// <see cref="attachedScopes"/> is never mutated mid-enumeration.
        /// </summary>
        internal static void ReconcileLiveness()
        {
            if (attachedScopes.Count == 0)
            {
                return;
            }

            IList<Window> liveWindows = Find.WindowStack?.Windows;
            List<Window> windowGone = null;
            List<Window> scopeDesynced = null;
            foreach (KeyValuePair<Window, FocusScope> pair in attachedScopes)
            {
                if (liveWindows == null || !liveWindows.Contains(pair.Key))
                {
                    (windowGone ?? (windowGone = new List<Window>())).Add(pair.Key);
                }
                else if (!FocusStack.Contains(pair.Value))
                {
                    // Window still open, scope popped without Detach: the stale
                    // entry silently blocks re-attachment, since
                    // ReconcileAttachments reads a present entry as "handled".
                    // Comes from a session-boundary ClearToBase unpaired with
                    // Reset (AmbientScopeSelector's Entry->Playing teardown firing
                    // after a mod opened a window during the load drain). Drop it
                    // so the next sweep re-attaches. Silent: an expected,
                    // self-healed transient, not a leaked scope.
                    (scopeDesynced ?? (scopeDesynced = new List<Window>())).Add(pair.Key);
                }
            }

            if (windowGone != null)
            {
                for (int i = 0; i < windowGone.Count; i++)
                {
                    Window orphan = windowGone[i];
                    Log.Warning("[RimWorld Access] ScopeForWindow: reconcile swept an orphaned scope for " +
                        orphan.GetType() + " (window left the stack without a matching Detach).");
                    Detach(orphan);
                }
            }

            if (scopeDesynced != null)
            {
                for (int i = 0; i < scopeDesynced.Count; i++)
                {
                    Detach(scopeDesynced[i]);
                }
            }

            // The ONLY per-window cleanup for the re-attach counters. Detach must
            // not clear them (the desync sweep above routes through Detach while
            // the window is still open, re-arming the cap mid-loop), and windows
            // whose arming-gated factory always yields null accrue a counter
            // without ever gaining an attachedScopes entry to detach.
            if (bespokeReattachAttempts.Count > 0)
            {
                List<Window> staleCounters = null;
                foreach (KeyValuePair<Window, int> pair in bespokeReattachAttempts)
                {
                    if (liveWindows == null || !liveWindows.Contains(pair.Key))
                    {
                        (staleCounters ?? (staleCounters = new List<Window>())).Add(pair.Key);
                    }
                }
                if (staleCounters != null)
                {
                    for (int i = 0; i < staleCounters.Count; i++)
                    {
                        bespokeReattachAttempts.Remove(staleCounters[i]);
                    }
                }
            }
        }

        private static int loggedReattachErrors;

        /// <summary>
        /// The attach half of the reconcile, symmetric to
        /// <see cref="ReconcileLiveness"/>: every open window that should own a
        /// scope gets one, converging the focus stack back to the window stack each
        /// OnGUI pass rather than chasing every open path that can race the
        /// session-boundary reset (VEF's Dialog_NewFactionSpawning, opened from
        /// <c>GameComponentUtility.LoadedGame</c> on the first save-load after boot,
        /// has its Add-time scope gone by the first dispatched frame).
        /// The generic-reader arm is uncapped: eligible windows with no bespoke
        /// registration have no legitimate self-pop path, so replaying forever is
        /// safe. The bespoke arm is capped per window instance via
        /// <see cref="bespokeReattachAttempts"/> — re-attaching is itself safe (no
        /// window-attached scope here self-pops while its window stays open; standing
        /// down means releasing <c>IsModal</c>), so the cap exists only to bound a
        /// scope that keeps dying after each re-attach, which would churn
        /// construction and OnFocus announcements every frame. A deliberate arm goes
        /// through <see cref="AttachOnDemand"/>, which the cap never gates.
        /// Runs immediately after <see cref="ReconcileLiveness"/> so a stale scope is
        /// swept before a fresh one is replayed. Idempotent; skips attached windows.
        /// </summary>
        internal static void ReconcileAttachments()
        {
            IList<Window> liveWindows = Find.WindowStack?.Windows;
            if (liveWindows == null)
            {
                return;
            }

            for (int i = 0; i < liveWindows.Count; i++)
            {
                Window window = liveWindows[i];
                if (window == null || attachedScopes.ContainsKey(window))
                {
                    continue;
                }
                if (window is ImmediateWindow)
                {
                    continue; // per-frame chrome; cheap pre-filter for the common case
                }
                bool bespoke = factories.ContainsKey(window.GetType())
                    || ResolveHierarchyFactory(window.GetType()) != null;
                if (bespoke)
                {
                    int attempts;
                    bespokeReattachAttempts.TryGetValue(window, out attempts);
                    if (attempts >= MaxBespokeReattachAttempts)
                    {
                        continue;
                    }
                    // Silent: an arming-gated factory that yields null every pass
                    // reaches the cap by design (EditWindow_TweakValues sits open
                    // unattached until armed), so a warning would be routine noise.
                    bespokeReattachAttempts[window] = attempts + 1;
                }
                else if (!GenericReaderEligible(window))
                {
                    continue;
                }
                try
                {
                    Attach(window);
                }
                catch (Exception ex)
                {
                    if (loggedReattachErrors < 10)
                    {
                        loggedReattachErrors++;
                        Log.Error("[RimWorld Access] ScopeForWindow: reconcile failed to attach a scope for " +
                            window.GetType() + ": " + ex);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Mirrors <see cref="WindowStack.Add"/> onto the focus stack, as a postfix so
    /// it only reacts once the window has actually been inserted.
    /// </summary>
    [HarmonyPatch(typeof(WindowStack), "Add")]
    public static class ScopeForWindowAddPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Window window, bool __runOriginal)
        {
            if (!__runOriginal)
            {
                // DialogInterceptionPatch (Priority.First) swallowed this window
                // before Add's body ran, so it never entered the stack.
                return;
            }
            if (window == null)
            {
                return;
            }
            FlightRecorder.RecordWindowLifecycle("add", window);
            try
            {
                ScopeForWindow.Attach(window);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("ScopeForWindow: failed to attach a focus scope for " + window.GetType(), ex);
            }
        }
    }

    /// <summary>
    /// Mirrors the <c>(Window, bool)</c> overload of
    /// <see cref="WindowStack.TryRemove(Window, bool)"/> onto the focus stack; see
    /// <see cref="ScopeForWindow"/> for why that one overload covers every removal.
    /// </summary>
    [HarmonyPatch(typeof(WindowStack), "TryRemove", new Type[] { typeof(Window), typeof(bool) })]
    public static class ScopeForWindowRemovePatch
    {
        [HarmonyPostfix]
        public static void Postfix(Window window, bool __result)
        {
            if (!__result)
            {
                // Nothing removed: not on the stack, or OnCloseRequest declined.
                return;
            }
            if (window == null)
            {
                return;
            }
            FlightRecorder.RecordWindowLifecycle("remove", window);
            try
            {
                ScopeForWindow.Detach(window);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("ScopeForWindow: failed to detach the focus scope for " + window.GetType(), ex);
            }
        }
    }
}
