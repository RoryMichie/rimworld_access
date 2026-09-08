using System;
using System.Collections.Generic;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Escape behavior for mod windows that disable vanilla's <c>closeOnCancel</c> and keep
    /// their only dismissal on a drawn button (Rimworld Together's DLG_Base family is the
    /// archetype: every dialog sets <c>closeOnCancel = false</c> and draws its own
    /// Cancel/Close/Back). Vanilla's Escape chain is a no-op on such a window, stranding a
    /// keyboard user; a registered entry lets <see cref="GenericWindowScope"/>'s cancel claim
    /// perform the window's own dismissal instead.
    ///
    /// Two entry shapes, first registered match wins:
    /// <list type="bullet">
    /// <item>A LABEL SET: Escape clicks the captured button whose caption equals one of the
    /// given literals — vehicle A, the exact click a mouse user performs. The literals are the
    /// target mod's own hardcoded, untranslated source constants (never .Translate()d text);
    /// a mod update that renames them degrades Escape back to inert, never to a wrong click.</item>
    /// <item>A HANDLER: for dialogs whose dismiss caption is caller-parameterized (Rimworld
    /// Together's DLG_YesNo takes its No text as a constructor argument), the compat module
    /// supplies the dismissal body itself, mirroring the button's own delegate.</item>
    /// </list>
    ///
    /// A matched window with NO dismiss control on screen (Accept-only dialogs) is deliberately
    /// inert on Escape — the same dead end a sighted player has — rather than falling through
    /// to a close the mod's own UI does not offer.
    /// </summary>
    public static class WindowDismissRegistry
    {
        public sealed class Entry
        {
            public Func<Window, bool> Matches;
            public string[] Labels;
            public Action<Window> Handler;
        }

        private static readonly List<Entry> entries = new List<Entry>();

        /// <summary>Escape clicks the first captured button captioned with one of <paramref name="dismissLabels"/>.</summary>
        public static void RegisterLabels(Func<Window, bool> matches, params string[] dismissLabels)
        {
            entries.Add(new Entry { Matches = matches, Labels = dismissLabels });
        }

        /// <summary>Escape runs <paramref name="dismiss"/>, which must mirror the window's own dismiss-button body.</summary>
        public static void RegisterHandler(Func<Window, bool> matches, Action<Window> dismiss)
        {
            entries.Add(new Entry { Matches = matches, Handler = dismiss });
        }

        /// <summary>First registered entry matching <paramref name="window"/>, or null.</summary>
        public static Entry EntryFor(Window window)
        {
            if (window == null)
            {
                return null;
            }
            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                try
                {
                    if (entry.Matches(window))
                    {
                        return entry;
                    }
                }
                catch (Exception ex)
                {
                    ModLogger.LimitedError("Window dismiss registry match", ex);
                }
            }
            return null;
        }
    }
}
