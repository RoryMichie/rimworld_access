using System.Collections.Generic;
using Verse;
using RimWorld;
using RimWorldAccess.Shell;

namespace RimWorldAccess
{
    /// <summary>
    /// The windowless shape selection menu for building placement: which shapes the active
    /// designator offers, and the two ways out of the menu (confirm into shape placement, or
    /// cancel).
    ///
    /// Navigation, typeahead and per-row announcements belong to
    /// <see cref="ShapeSelectionScope"/>'s <see cref="ScreenScope"/> chassis (the ScreenScope
    /// migration), which reads <see cref="AvailableShapes"/> and calls
    /// <see cref="ConfirmAt"/>/<see cref="Cancel"/>. What used to live here — a
    /// <c>FlatListCursor</c> plus two announcement formats and the per-key routers that chose
    /// between them — is gone; the legacy handler's search-aware Up/Down/Home/End behavior is now
    /// the shared typeahead engine's.
    /// </summary>
    public static class ShapeSelectionMenuState
    {
        /// <summary>
        /// Gets whether the shape selection menu is currently active.
        /// </summary>
        public static bool IsActive { get; private set; }

        /// <summary>The shapes the active designator offers, in ShapeHelper's own order.</summary>
        internal static IReadOnlyList<ShapeType> AvailableShapes => availableShapes;

        private static List<ShapeType> availableShapes = new List<ShapeType>();
        private static Designator currentDesignator = null;

        /// <summary>
        /// Opens the shape selection menu for the given designator.
        /// </summary>
        /// <param name="designator">The designator to get shapes for</param>
        public static void Open(Designator designator)
        {
            if (designator == null)
            {
                Log.Error("Cannot open shape selection menu: designator is null");
                return;
            }

            currentDesignator = designator;
            availableShapes = ShapeHelper.GetAvailableShapes(designator);
            IsActive = true;

        }

        /// <summary>
        /// Closes the shape selection menu.
        /// </summary>
        public static void Close()
        {
            IsActive = false;
            currentDesignator = null;
        }

        /// <summary>
        /// Enter on a shape row: confirms the selection and enters shape placement mode with the
        /// chosen shape, capturing the designator before <see cref="Confirm"/> clears it (the
        /// legacy handler's own Enter branch).
        /// </summary>
        internal static void ConfirmAt(int index)
        {
            Designator designatorForPlacement = currentDesignator;
            ShapeType selectedShape = Confirm(index);
            if (designatorForPlacement != null)
            {
                ShapePlacementState.Enter(designatorForPlacement, selectedShape);
            }
        }

        /// <summary>
        /// Escape: cancels and closes the menu without selecting, the legacy handler's own
        /// Escape branch (its search-clearing first tier is now the chassis's).
        /// </summary>
        internal static void Cancel()
        {
            TolkHelper.Speak("RimWorldAccess.Building.ShapeSelect.Cancelled".Loc());
            Close();
        }

        /// <summary>
        /// Confirms the selection and returns the selected shape type.
        /// Announces "{shape name} selected" and closes the menu.
        /// </summary>
        /// <returns>The selected ShapeType</returns>
        private static ShapeType Confirm(int index)
        {
            if (index < 0 || index >= availableShapes.Count)
            {
                Close();
                return ShapeType.Manual;
            }

            ShapeType selected = availableShapes[index];
            string shapeName = ShapeHelper.GetShapeName(selected);

            TolkHelper.Speak("RimWorldAccess.Building.ShapeSelect.ShapeSelected".Loc(shapeName));

            Close();
            return selected;
        }
    }
}
