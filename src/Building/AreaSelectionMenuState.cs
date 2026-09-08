using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Opens the area-selection menu for an area designator (Allowed Area expand/clear) as a
    /// <see cref="WindowlessFloatMenuState"/> menu, the same replacement vanilla's own
    /// <c>Verse.AreaUtility.MakeAllowedAreaListFloatMenu</c> builds a real <c>FloatMenu</c>
    /// for (decompiled Verse/AreaUtility.cs:10-40). Riding WindowlessFloatMenuState makes
    /// the menu visible through the FloatMenuTwin for free and retires the bespoke
    /// cursor/typeahead code this class used to own; keyboard navigation, typeahead,
    /// activation and Escape now all come from WindowlessFloatMenuState +
    /// <see cref="RimWorldAccess.Shell.FloatMenuOverlayScope"/>.
    /// </summary>
    public static class AreaSelectionMenuState
    {
        // The exact List&lt;FloatMenuOption&gt; instance handed to WindowlessFloatMenuState.Open,
        // so IsActive can tell "our area menu" apart from any OTHER menu also riding
        // WindowlessFloatMenuState (gear equip, quantity, ideoligion pickers, ...).
        private static List<FloatMenuOption> ourOptions;

        // Index-aligned with ourOptions; the Manage Areas slot is null. Lets
        // CurrentAreaForDisplay report the browsed area without re-deriving it from
        // WindowlessFloatMenuState's own option list.
        private static List<Area> areaSlots;

        public static bool IsActive =>
            WindowlessFloatMenuState.IsActive && ReferenceEquals(WindowlessFloatMenuState.CurrentOptions, ourOptions);

        /// <summary>
        /// The area the cursor is currently browsing, for the visual-parity area-cells driver, null on
        /// the Manage Areas row or when this isn't the active menu.
        /// </summary>
        internal static Area CurrentAreaForDisplay
        {
            get
            {
                if (!IsActive) return null;
                int index = WindowlessFloatMenuState.SelectedIndex;
                return index >= 0 && index < areaSlots.Count ? areaSlots[index] : null;
            }
        }

        public static void Open(Designator designator, Action<Area> callback)
        {
            if (designator == null || callback == null)
                return;

            Map map = Find.CurrentMap;
            if (map?.areaManager == null)
            {
                TolkHelper.Speak("RimWorldAccess.Guard.NoMapLoaded".Loc());
                return;
            }

            List<Area> mutableAreas = map.areaManager.AllAreas.Where(a => a.Mutable).ToList();
            if (mutableAreas.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Building.AreaSelect.NoAreasAvailable".Loc());
            }

            string designatorLabel = designator.Label ?? (string)"RimWorldAccess.Building.AreaSelect.OpenFallbackName".Translate();

            var options = new List<FloatMenuOption>();
            var slots = new List<Area>();
            foreach (Area area in mutableAreas)
            {
                Area localArea = area;
                string label = localArea.Label + ", " + "RimWorldAccess.Building.AreaSelect.CellCount".Translate(localArea.TrueCount);
                options.Add(new FloatMenuOption(label, delegate
                {
                    TolkHelper.Speak("RimWorldAccess.Building.AreaSelect.Selected".Loc(localArea.Label));
                    callback(localArea);
                }, mouseoverGuiAction: delegate { localArea.MarkForDraw(); }));
                slots.Add(localArea);
            }
            options.Add(new FloatMenuOption("RimWorldAccess.Building.AreaSelect.ManageAreas".Translate(), delegate
            {
                OpenManageAreas(designator, callback);
            }));
            slots.Add(null);

            ourOptions = options;
            areaSlots = slots;

            WindowlessFloatMenuState.Open(
                options,
                colonistOrders: false,
                announceSelection: false,
                titleText: "RimWorldAccess.Building.AreaSelect.OpenPrompt".Loc(designatorLabel).ToString(),
                onClose: cancelled =>
                {
                    if (!cancelled) return;
                    TolkHelper.Speak("RimWorldAccess.Building.AreaSelect.Cancelled".Loc());
                    Find.DesignatorManager.Deselect();
                });
        }

        /// <summary>
        /// Silent hygiene close: only closes the shared windowless menu if it is still ours, and never
        /// runs the cancel callback (a stale Deselect against a session that has already moved on).
        /// </summary>
        internal static void Close()
        {
            if (IsActive)
            {
                WindowlessFloatMenuState.Close();
            }
            ourOptions = null;
            areaSlots = null;
        }

        private static void OpenManageAreas(Designator designator, Action<Area> callback)
        {
            Map map = Find.CurrentMap;
            if (map == null) return;

            // The real dialog, exactly vanilla's own Manage areas... delegate (AreaUtility);
            // Window.PreOpen deselects the pending designator, as it would for a mouse user.
            Find.WindowStack.Add(new RimWorld.Dialog_ManageAreas(map));
        }
    }
}
