using System;
using System.Collections.Generic;

namespace RimWorldAccess
{
    /// <summary>
    /// Generic registry for a mod's own on-screen HUD button strip — the icon-only corner
    /// toolbars mods draw straight onto the map/world UI outside any window (Rimworld Together's
    /// seven <c>Widgets.ButtonImageWithBG</c> buttons are the archetype). Such a strip has no
    /// window for a scope to attach to and its buttons carry no labels or tooltips to capture,
    /// so the keyboard mirror is DECLARED here instead: a compat module registers one toolbar
    /// (its visibility gate mirroring the strip's own draw gate) and one entry per button (each
    /// activation mirroring that button's own click body).
    ///
    /// The MACHINERY is generic, matching <see cref="ToolbarModifierRegistry"/>'s split: any
    /// compat module may register a toolbar, and the shared presentation — one pause-menu item
    /// per visible toolbar, opening the windowless <c>ModHudToolbarScope</c> menu over the live
    /// game (the play-settings-menu shape, this project's vanilla analogue for a global control
    /// strip) — lives in the shell and never learns mod names. A future mod with its own HUD
    /// strip needs only its own Register calls, never a change here or in the scope.
    /// </summary>
    internal static class ModHudToolbarRegistry
    {
        /// <summary>
        /// One button of a registered strip. <see cref="Activate"/> null marks a read-only
        /// readout row (Rimworld Together's ping label); <see cref="Available"/> null means the
        /// entry is offered whenever its toolbar is visible. <see cref="Label"/> is resolved
        /// fresh on every read so a state-dependent caption (open/close toggles, live readouts)
        /// stays current.
        /// </summary>
        internal sealed class Entry
        {
            public readonly Func<string> Label;
            public readonly Func<bool> Available;
            public readonly Action Activate;

            public Entry(Func<string> label, Action activate, Func<bool> available)
            {
                Label = label;
                Activate = activate;
                Available = available;
            }
        }

        /// <summary>One registered strip: a stable per-mod id chosen by the registering compat, a title resolver (typically the mod's own listed name), and the strip's own visibility gate.</summary>
        internal sealed class Toolbar
        {
            public readonly string Id;
            public readonly Func<string> Title;
            public readonly Func<bool> Visible;
            public readonly List<Entry> Entries = new List<Entry>();

            public Toolbar(string id, Func<string> title, Func<bool> visible)
            {
                Id = id;
                Title = title;
                Visible = visible;
            }
        }

        private static readonly List<Toolbar> toolbars = new List<Toolbar>();

        public static IReadOnlyList<Toolbar> Toolbars
        {
            get { return toolbars; }
        }

        /// <summary>Declares a toolbar. Registering an id twice replaces the earlier registration (re-entrant activation safety); entries are added with <see cref="AddEntry"/>.</summary>
        public static void Register(string id, Func<string> title, Func<bool> visible)
        {
            if (string.IsNullOrEmpty(id) || title == null || visible == null)
            {
                return;
            }
            toolbars.RemoveAll(t => t.Id == id);
            toolbars.Add(new Toolbar(id, title, visible));
        }

        /// <summary>Appends one entry to a registered toolbar, in the strip's own presentation order. Pass a null <paramref name="activate"/> for a read-only readout row.</summary>
        public static void AddEntry(string id, Func<string> label, Action activate, Func<bool> available = null)
        {
            Toolbar toolbar = Find(id);
            if (toolbar == null || label == null)
            {
                return;
            }
            toolbar.Entries.Add(new Entry(label, activate, available));
        }

        public static Toolbar Find(string id)
        {
            for (int i = 0; i < toolbars.Count; i++)
            {
                if (toolbars[i].Id == id)
                {
                    return toolbars[i];
                }
            }
            return null;
        }
    }
}
