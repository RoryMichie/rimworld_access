using System.Collections.Generic;
using UnityEngine;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// One surface family's record of (row identity, on-screen rect) for the draw pass in
    /// flight, fed by a Harmony postfix on the vanilla method that draws every row of that
    /// surface and read back by the surface's scope in <c>FocusedContentRect</c>. Where
    /// vanilla funnels each row through one method carrying both the row's identity and its
    /// rect, this is all the geometry a focus ring needs — no layout math to mirror, no
    /// constants to keep in step with a future vanilla change.
    ///
    /// One instance per family (a static field on each recording patch), never one global:
    /// two families never draw in the same window pass, but the isolation costs nothing and
    /// rules out cross-window bleed. Recording is gated by each patch on its own scope being
    /// live, so the postfixes are one static check in normal play.
    ///
    /// The pass boundary is implicit: a record arriving under a different frame or event type
    /// than the last one starts a new pass. That keeps the engine self-contained (the letter
    /// stack, which draws outside every window, has no draw bracket to hang off) and leaves
    /// <see cref="Items"/> holding exactly the rows the pass now running has drawn — the
    /// reader runs after them, inside the same pass.
    /// </summary>
    internal sealed class RowDrawCapture
    {
        internal struct Entry
        {
            /// <summary>The row's own object, def, or ordinal — never its label.</summary>
            public object Identity;

            /// <summary>Absolute UI points, already clipped to what is visible (empty when scrolled out).</summary>
            public Rect VisibleScreenRect;
        }

        private readonly List<Entry> entries = new List<Entry>();
        private int passFrame = -1;
        private EventType passEvent = EventType.Ignore;

        /// <summary>Rows recorded by the pass in flight, in draw order.</summary>
        internal IReadOnlyList<Entry> Items
        {
            get { return entries; }
        }

        /// <summary>Called from the row method's postfix, inside the row's own coordinate space.</summary>
        internal void Record(object identity, Rect guiRect)
        {
            Event ev = Event.current;
            EventType type = ev == null ? EventType.Ignore : ev.type;
            if (Time.frameCount != passFrame || type != passEvent)
            {
                entries.Clear();
                passFrame = Time.frameCount;
                passEvent = type;
            }
            entries.Add(new Entry
            {
                Identity = identity,
                VisibleScreenRect = GuiSpace.VisibleScreenRect(guiRect),
            });
        }

        /// <summary>The FIRST rect this pass drew for <paramref name="identity"/>, or empty.</summary>
        internal Rect FindFirst(object identity)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (Equals(entries[i].Identity, identity))
                {
                    return entries[i].VisibleScreenRect;
                }
            }
            return default(Rect);
        }

        /// <summary>The LAST rect this pass drew for <paramref name="identity"/>, or empty.</summary>
        internal Rect FindLast(object identity)
        {
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                if (Equals(entries[i].Identity, identity))
                {
                    return entries[i].VisibleScreenRect;
                }
            }
            return default(Rect);
        }
    }
}
