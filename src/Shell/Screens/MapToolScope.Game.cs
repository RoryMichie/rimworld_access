using LudeonTK;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The armed-tool scope: while a <see cref="DebugTools.curTool"/> is armed — a dev-mode
    /// debug tool, or one of Character Editor's placement/teleport tools, which ride the same
    /// vanilla <see cref="DebugTool"/> — aiming and firing outrank whatever screen the player
    /// armed the tool from.
    ///
    /// This exists because vanilla runs <c>DebugTools.DebugToolsOnGUI</c> AFTER
    /// <c>WindowStackOnGUI</c> in <c>UIRoot_Play.UIRootOnGUI</c>: a sighted player with a
    /// non-absorbing window open (Character Editor's <c>EditorUI</c> sets
    /// <c>absorbInputAroundWindow = false</c>, and its own teleport button merely shoves the
    /// window aside with <c>API.EditorMoveRight()</c>) clicks the map with that window still
    /// open. The keyboard equivalent is stack position: <see cref="MapToolScopeMirror"/> pushes
    /// this scope above the screen scope, so Enter fires the tool and the arrows walk the map
    /// cursor even while a modal screen scope sits underneath.
    ///
    /// NON-MODAL, the wave-G targeting law: every key this scope does not claim still reaches
    /// the screen below, so the editor stays usable while the player aims. The arrow claims are
    /// the ones that could not simply fall through — <c>MapScope</c>'s own arrow claims sit
    /// under the screen scope's modal stop, and their <c>ArrowsLive</c> gate reads
    /// <c>MapNavigationState.SuppressMapNavigation</c>, which an open menu sets. They delegate
    /// to the same <see cref="MapArrowKeyHandler"/> entry point <c>MapScope</c> uses, so
    /// aiming sounds and behaves identically to walking the map cursor normally.
    ///
    /// Firing, cancelling and the arm/disarm announcement all live in
    /// <see cref="DevToolTargeting"/>; this scope only decides who hears the keys.
    /// </summary>
    public sealed class MapToolScope : FocusScope
    {
        public MapToolScope()
        {
            Claim("map.devtool.apply", e => DevToolTargeting.FireAtKeyboardCursor());
            Claim("map.devtool.cancel", e => DevToolTargeting.CancelTool());

            Claim("map.cursor.north", e => Arrow(KeyCode.UpArrow, e), when: AimingOnMap);
            Claim("map.cursor.south", e => Arrow(KeyCode.DownArrow, e), when: AimingOnMap);
            Claim("map.cursor.west", e => Arrow(KeyCode.LeftArrow, e), when: AimingOnMap);
            Claim("map.cursor.east", e => Arrow(KeyCode.RightArrow, e), when: AimingOnMap);
            Claim("map.cursor.jumpNorth", e => Arrow(KeyCode.UpArrow, e), when: AimingOnMap);
            Claim("map.cursor.jumpSouth", e => Arrow(KeyCode.DownArrow, e), when: AimingOnMap);
            Claim("map.cursor.jumpWest", e => Arrow(KeyCode.LeftArrow, e), when: AimingOnMap);
            Claim("map.cursor.jumpEast", e => Arrow(KeyCode.RightArrow, e), when: AimingOnMap);
        }

        public override string Name
        {
            get { return "map-tool"; }
        }

        public override bool IsModal
        {
            get { return false; }
        }

        public override bool IsLive
        {
            get { return true; }
        }

        /// <summary>
        /// Both routers stand down for this scope while it is top: the Enter and Escape the
        /// player aims with must not also reach the window underneath — Character Editor's
        /// editor window is a real, non-absorbing <c>Window</c> whose own accept/cancel
        /// handling would otherwise run on the same keystroke.
        /// </summary>
        public override bool OwnsAccept
        {
            get { return true; }
        }

        private static void Arrow(KeyCode key, KeyEventSnapshot e)
        {
            MapArrowKeyHandler.HandleArrowKey(key, e.Ctrl, e.Shift);
        }

        /// <summary>
        /// Map aiming only. On the planet view the tool fires at the selected world tile, whose
        /// navigation <c>WorldScope</c> owns and no screen scope masks.
        /// </summary>
        private static bool AimingOnMap()
        {
            return WorldRendererUtility.DrawingMap
                && Current.ProgramState == ProgramState.Playing
                && Find.CurrentMap != null
                && MapNavigationState.IsInitialized;
        }
    }

    /// <summary>
    /// Keeps <see cref="MapToolScope"/> on the stack for exactly as long as a tool is armed.
    /// Reconciled near the end of <see cref="MirrorReconcileOrder"/> so its push re-floats the
    /// scope above every screen scope and windowless overlay each pass — the whole point is to
    /// outrank the screen the tool was armed from.
    /// </summary>
    internal static class MapToolScopeMirror
    {
        private static readonly MapToolScope scope = new MapToolScope();

        public static void Reconcile()
        {
            if (DebugTools.curTool != null)
            {
                FocusStack.Push(scope);
            }
            else
            {
                FocusStack.Pop(scope);
            }
        }
    }
}
