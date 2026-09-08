using System;
using System.Collections.Generic;

namespace RimWorldAccess
{
    /// <summary>
    /// Registration point for "an ideoligion edit just happened, refresh whichever host is
    /// showing it." Replaces the hardcoded IsActive-chain <see cref="IdeoSymbolEditState"/>'s
    /// <c>AfterEdit</c> used to walk (IdeoSectionEditorState -> IdeoReformState ->
    /// IdeoBuilderScreenScope's static <c>active</c> field) — every ideo-editing host now
    /// registers its own refresh callback while it is live and unregisters when it stops being
    /// live, and <see cref="Notify"/> invokes whichever one is currently on top.
    ///
    /// A stack, not a single slot: the worldgen builder hub's own Fluid-development-points row
    /// can open <c>Dialog_ReformIdeo</c> ON TOP of itself (the hub scope stays pushed underneath,
    /// never popped), so both the hub and the reform host can be simultaneously registered. The
    /// original chain's fixed priority (section editor, then reform, then hub) is reproduced
    /// because in every reachable state a host only ever registers AFTER whichever host it can be
    /// opened on top of has already registered, so "most recently registered" and "the original
    /// chain's priority order" always agree. <see cref="Register"/> is idempotent (re-registering
    /// the same callback just keeps it at the top, no duplicate entries) so a defensive re-open
    /// call can never leave a stale duplicate behind.
    /// </summary>
    public static class IdeoEditNotifyHub
    {
        private static readonly List<Action<bool>> hosts = new List<Action<bool>>();

        /// <summary>Marks <paramref name="onIdeoEdited"/> as the current top-of-stack host. Call once when a host becomes the live ideo-editing surface.</summary>
        public static void Register(Action<bool> onIdeoEdited)
        {
            if (onIdeoEdited == null)
            {
                return;
            }
            hosts.Remove(onIdeoEdited);
            hosts.Add(onIdeoEdited);
        }

        /// <summary>Removes <paramref name="onIdeoEdited"/> from the stack. Call once when a host stops being the live ideo-editing surface.</summary>
        public static void Unregister(Action<bool> onIdeoEdited)
        {
            if (onIdeoEdited == null)
            {
                return;
            }
            hosts.Remove(onIdeoEdited);
        }

        /// <summary>
        /// Invokes the top-of-stack host's refresh callback, if any host is currently registered.
        /// <paramref name="announce"/> false refreshes the host's model WITHOUT re-announcing its
        /// current row: the caller is an edit that immediately re-opens its own menu (the styles
        /// slot picker returning to the styles submenu), and that menu's first row is the single
        /// utterance the announcement law allows for the action — the host beneath re-announces on
        /// its own when it eventually regains focus.
        /// </summary>
        public static void Notify(bool announce = true)
        {
            if (hosts.Count == 0)
            {
                return;
            }
            hosts[hosts.Count - 1]?.Invoke(announce);
        }
    }
}
