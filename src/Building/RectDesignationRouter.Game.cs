using System;
using System.Collections.Generic;
using Verse;

namespace RimWorldAccess
{
    /// <summary>The observable effect of a rect designation, measured rather than predicted.</summary>
    public readonly struct RectDesignationResult
    {
        public bool Handled { get; }

        /// <summary>How many things the operation added to the selection, or 0.</summary>
        public int SelectionAdded { get; }

        /// <summary>True when the handled designator emitted its own game message, so we must not speak a count over it.</summary>
        public bool SpokeForItself { get; }

        /// <summary>
        /// True when the designator's product is a selection (Allow Tool's Select Similar
        /// family). Such designators are mute on failure, so a zero count must still be
        /// announced as "nothing selected" rather than as a generic area application.
        /// </summary>
        public bool IsSelection { get; }

        public RectDesignationResult(bool handled, int selectionAdded, bool spokeForItself)
            : this(handled, selectionAdded, spokeForItself, false)
        {
        }

        public RectDesignationResult(bool handled, int selectionAdded, bool spokeForItself, bool isSelection)
        {
            Handled = handled;
            SelectionAdded = selectionAdded;
            SpokeForItself = spokeForItself;
            IsSelection = isSelection;
        }

        public static readonly RectDesignationResult NotHandled = new RectDesignationResult(false, 0, false);
    }

    /// <summary>
    /// A designator whose unit of work is a RECTANGLE, not a cell. Vanilla has none:
    /// every vanilla designator either accepts cells through CanDesignateCell or is a
    /// zone. Mods that hand-roll their own area drag (Allow Tool's
    /// Designator_UnlimitedDragger family) do, and for those the cell-by-cell placement
    /// path designates nothing at all, because their CanDesignateCell rejects everything
    /// by design.
    ///
    /// A handler owns exactly one question -- "is this my designator, and if so, apply it
    /// over this rectangle" -- and answers it by invoking the designator's OWN entry
    /// points. It never designates on its own behalf.
    /// </summary>
    public interface IRectDesignationHandler
    {
        bool Handles(Designator designator);

        /// <summary>
        /// Applies <paramref name="designator"/> over <paramref name="rect"/>. Called only
        /// when <see cref="Handles"/> returned true. Must not throw; a handler that cannot
        /// proceed returns <see cref="RectDesignationResult.NotHandled"/>.
        /// </summary>
        RectDesignationResult Designate(Designator designator, CellRect rect, IntVec3 firstCorner,
            IReadOnlyList<IntVec3> cells);
    }

    public static class RectDesignationRouter
    {
        // The eyedropper is the one vanilla designator whose click selects another tool.
        private static readonly List<IRectDesignationHandler> handlers = new List<IRectDesignationHandler>
        {
            new DesignatorHandoffRectHandler(typeof(RimWorld.Designator_Eyedropper)),
        };

        /// <summary>
        /// Registers a handler for the process lifetime. Handler registration is not per-game
        /// state, so it deliberately has no <see cref="StateResetRegistry"/> hook: a save-load
        /// must not unregister it.
        /// </summary>
        public static void Register(IRectDesignationHandler handler)
        {
            if (handler == null || handlers.Contains(handler))
                return;

            handlers.Add(handler);
        }

        public static bool IsRectDesignator(Designator designator)
        {
            return designator != null && FindHandler(designator) != null;
        }

        public static RectDesignationResult Designate(Designator designator, CellRect rect,
            IntVec3 firstCorner, IReadOnlyList<IntVec3> cells)
        {
            IRectDesignationHandler handler = designator == null ? null : FindHandler(designator);
            if (handler == null)
                return RectDesignationResult.NotHandled;

            try
            {
                return handler.Designate(designator, rect, firstCorner, cells);
            }
            catch (Exception ex)
            {
                // A mod's internal failure must never take down the key handler that got here.
                ModLogger.Error($"[RectDesignationRouter] {handler.GetType().Name} threw: {ex}");
                return RectDesignationResult.NotHandled;
            }
        }

        private static IRectDesignationHandler FindHandler(Designator designator)
        {
            for (int i = 0; i < handlers.Count; i++)
            {
                try
                {
                    if (handlers[i].Handles(designator))
                        return handlers[i];
                }
                catch (Exception ex)
                {
                    ModLogger.Error($"[RectDesignationRouter] {handlers[i].GetType().Name}.Handles threw: {ex}");
                }
            }

            return null;
        }
    }
}
