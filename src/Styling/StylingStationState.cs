using System;
using System.Collections.Generic;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Lifecycle and pure data/mutation backend for vanilla's
    /// <c>Dialog_StylingStation</c> (Ideology). Navigation, region/typeahead
    /// state, and announcement composition all live on
    /// <see cref="RimWorldAccess.Shell.StylingStationScope"/> — this class keeps only
    /// what has no ScreenModel equivalent: the pawn/dialog references, the
    /// dev-edit-mode flag, and the dynamically rebuilt tab set. All dialog
    /// field/method access still goes through <see cref="StylingStationHelper"/>.
    /// </summary>
    public static class StylingStationState
    {
        private static bool isActive;
        private static Window currentDialog;
        private static Pawn pawn;

        private static readonly List<StylingTabKind> tabs = new List<StylingTabKind>();

        // Mirrors Dialog_StylingStation.devEditMode ("DEV: Show all"): reveals every
        // style item and the two dev-only tabs. Reset per open to match vanilla's
        // per-window-instance default.
        private static bool devEditMode;

        public static bool IsActive => isActive;
        public static Window Dialog => currentDialog;
        public static Pawn Pawn => pawn;
        public static bool DevEditMode => devEditMode;

        public static int TabCount => tabs.Count;
        public static StylingTabKind TabKind(int region) => tabs[region];

        // ===== Lifecycle =====

        public static void Open(Window dialog)
        {
            if (dialog == null) return;

            try
            {
                currentDialog = dialog;
                pawn = StylingStationHelper.GetPawn(dialog);
                if (pawn == null) { Close(); return; }

                devEditMode = false;
                RebuildTabs();
                isActive = true;

                // Opening sound/title/first-region announcement live on
                // StylingStationScope.OnFocus (the ScreenScope push/focus hook),
                // matching AutoSlaughterState's own split with AutoSlaughterScope.
            }
            catch (Exception ex)
            {
                Log.Error($"[StylingStationState] Error opening: {ex.Message}");
                Close();
            }
        }

        public static void Close()
        {
            isActive = false;
            currentDialog = null;
            pawn = null;
            devEditMode = false;
            tabs.Clear();
        }

        /// <summary>
        /// Flips the "DEV: Show all" edit mode (mirrors
        /// <see cref="StylingStationHelper.SetDevEditMode"/>'s own MUTATION-C write
        /// to the dialog's field) and rebuilds the tab set — Beard and the two dev
        /// tabs may appear or disappear. The scope re-syncs the focused region and
        /// re-announces afterward.
        /// </summary>
        internal static void SetDevEditMode(bool value)
        {
            devEditMode = value;
            StylingStationHelper.SetDevEditMode(currentDialog, value);
            RebuildTabs();
        }

        /// <summary>
        /// Rebuilds the tab set. Mirrors Dialog_StylingStation.DrawTabs (decompiled
        /// :278-315): the Beard tab appears when the pawn can want a beard OR
        /// devEditMode is on; the dev-only Body type/Head type tabs (decompiled
        /// :304-314) appear together, gated purely on devEditMode with no
        /// per-pawn condition — matching the rule that dev mode
        /// means full parity, every button, so this migration closes the gap the
        /// pre-ScreenScope surface had left open.
        /// </summary>
        private static void RebuildTabs()
        {
            tabs.Clear();
            tabs.Add(StylingTabKind.Hair);
            if ((pawn.style != null && pawn.style.CanWantBeard) || devEditMode)
                tabs.Add(StylingTabKind.Beard);
            tabs.Add(StylingTabKind.FaceTattoo);
            tabs.Add(StylingTabKind.BodyTattoo);
            tabs.Add(StylingTabKind.ApparelColor);
            if (devEditMode)
            {
                tabs.Add(StylingTabKind.BodyType);
                tabs.Add(StylingTabKind.HeadType);
            }
        }
    }
}
