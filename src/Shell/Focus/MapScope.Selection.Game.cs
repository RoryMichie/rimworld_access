using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Wave-C ambient map claims: the selection cluster (multi-select pawn
    /// commands, colonist bar navigation, and line-formation placement). These
    /// are always-available map actions with no mode of their own — except
    /// line formation, which is a targeting sub-mode gated on
    /// <see cref="LineFormationState.IsActive"/> but still claimed directly on
    /// <see cref="MapScope"/> rather than through a pushed overlay scope, same
    /// as bookmarks/cursor-hotkeys/scanner-browse. Each handler delegates to
    /// the same logic the retired ladder rung called; each when-guard
    /// replicates that rung's gate verbatim, so only the routing moved.
    /// </summary>
    public sealed partial class MapScope
    {
        /// <summary>
        /// Alt+Space toggle, Ctrl+Alt+Space toggle-all, Shift+Alt+Left/Right
        /// contiguous extend, and the F1-F4 group save/recall digit families.
        /// </summary>
        private void RegisterMultiSelectClaims()
        {
            Claim("map.multiSelect.extendSelectionNext",
                delegate (KeyEventSnapshot e) { MultiSelectState.SelectContiguousNext(); }, when: SelectionClusterLive);
            Claim("map.multiSelect.extendSelectionPrevious",
                delegate (KeyEventSnapshot e) { MultiSelectState.SelectContiguousPrevious(); }, when: SelectionClusterLive);
            Claim("map.multiSelect.togglePawn", OnTogglePawn, when: SelectionClusterLive);
            Claim("map.multiSelect.toggleAll", OnToggleAll, when: SelectionClusterLive);
            Claim("map.multiSelect.saveGroup", OnSaveGroup, when: SaveGroupLive);
            Claim("map.multiSelect.recallGroup", OnRecallGroup, when: SelectionClusterLive);
        }

        /// <summary>
        /// Alt+Left/Right/Up/Down navigate (redirecting to multi-select focus
        /// nav while a multi-selection is active), Ctrl+Alt+arrows reorder
        /// (refused during multi-select), Alt+digit focus/jump, and
        /// Ctrl+Alt+Enter/I open the selected pawn's inspection tree / info
        /// card. The bare-/ focus-by-cursor rung lives here too since it is
        /// the same colonist-bar surface.
        /// </summary>
        private void RegisterColonistBarClaims()
        {
            Claim("map.colonistBar.next", OnColonistBarNext, when: SelectionClusterLive);
            Claim("map.colonistBar.previous", OnColonistBarPrevious, when: SelectionClusterLive);
            Claim("map.colonistBar.pageDown", OnColonistBarPageDown, when: SelectionClusterLive);
            Claim("map.colonistBar.pageUp", OnColonistBarPageUp, when: SelectionClusterLive);
            Claim("map.colonistBar.moveRight", OnColonistBarMoveRight, when: SelectionClusterLive);
            Claim("map.colonistBar.moveLeft", OnColonistBarMoveLeft, when: SelectionClusterLive);
            Claim("map.colonistBar.moveDown", OnColonistBarMoveDown, when: SelectionClusterLive);
            Claim("map.colonistBar.moveUp", OnColonistBarMoveUp, when: SelectionClusterLive);
            Claim("map.colonistBar.focusByNumber", OnColonistBarFocusByNumber, when: SelectionClusterLive);
            Claim("map.colonistBar.inspectSelected", OnColonistBarInspectSelected, when: SelectionClusterLive);
            Claim("map.colonistBar.infoCardSelected", OnColonistBarInfoCardSelected, when: SelectionClusterLive);
            Claim("map.colonistBar.focusByCursor",
                delegate (KeyEventSnapshot e) { ColonistBarState.FocusPawnByCursor(); }, when: FocusByCursorLive);
        }

        /// <summary>
        /// Space places a point, Enter/KeypadEnter confirms (only once both
        /// points are placed), Escape cancels. Arrow keys are intentionally
        /// not claimed here — they must keep flowing to map cursor navigation.
        /// </summary>
        private void RegisterLineFormationClaims()
        {
            Claim("lineFormation.placePoint",
                delegate (KeyEventSnapshot e) { LineFormationState.PlacePoint(); }, when: LineFormationActive);
            Claim("lineFormation.confirm",
                delegate (KeyEventSnapshot e) { LineFormationState.Confirm(); }, when: LineFormationConfirmReady);
            Claim("lineFormation.cancel",
                delegate (KeyEventSnapshot e) { LineFormationState.Cancel(); }, when: LineFormationActive);
        }

        private static void OnTogglePawn(KeyEventSnapshot e)
        {
            Pawn focusedPawn = MultiSelectState.IsMultiSelectMode
                ? MultiSelectState.FocusedPawn ?? ColonistBarState.GetPawnAtCurrentPosition()
                : Find.Selector?.SingleSelectedThing as Pawn ?? ColonistBarState.GetPawnAtCurrentPosition();
            MultiSelectState.TogglePawn(focusedPawn);
        }

        private static void OnToggleAll(KeyEventSnapshot e)
        {
            List<Pawn> allColonists = ColonistBarState.GetColonistsPublic();
            if (allColonists.Count > 0)
            {
                if (MultiSelectState.IsMultiSelectMode)
                {
                    // Already in multi-select -> clear
                    MultiSelectState.ClearMultiSelect();
                }
                else
                {
                    // Not in multi-select -> select all
                    MultiSelectState.SelectAllColonists(allColonists);
                }
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Input.MultiSelect.NoColonistsOnMap".Loc());
            }
        }

        private static void OnSaveGroup(KeyEventSnapshot e)
        {
            int slot = e.Key - KeyCode.F1;
            MultiSelectGroupComponent component = Current.Game?.GetComponent<MultiSelectGroupComponent>();
            if (component != null)
            {
                component.SaveGroup(slot, MultiSelectState.SelectedPawns);
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Input.MultiSelect.CannotSaveGroups".Loc());
            }
        }

        private static void OnRecallGroup(KeyEventSnapshot e)
        {
            int slot = e.Key - KeyCode.F1;
            MultiSelectGroupComponent component = Current.Game?.GetComponent<MultiSelectGroupComponent>();
            if (component != null)
            {
                component.RecallGroup(slot);
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Input.MultiSelect.CannotRecallGroups".Loc());
            }
        }

        private static void OnColonistBarNext(KeyEventSnapshot e)
        {
            if (MultiSelectState.IsMultiSelectMode)
                MultiSelectState.NavigateFocusNext();
            else
                ColonistBarState.NavigateRight();
        }

        private static void OnColonistBarPrevious(KeyEventSnapshot e)
        {
            if (MultiSelectState.IsMultiSelectMode)
                MultiSelectState.NavigateFocusPrevious();
            else
                ColonistBarState.NavigateLeft();
        }

        private static void OnColonistBarPageDown(KeyEventSnapshot e)
        {
            if (MultiSelectState.IsMultiSelectMode)
            {
                Pawn pawn = ColonistBarState.PageFocusDown();
                if (pawn != null)
                {
                    MultiSelectState.SetFocusedPawn(pawn);
                    MultiSelectState.AnnounceFocusedPawn(pawn);
                }
            }
            else
            {
                ColonistBarState.PageDown();
            }
        }

        private static void OnColonistBarPageUp(KeyEventSnapshot e)
        {
            if (MultiSelectState.IsMultiSelectMode)
            {
                Pawn pawn = ColonistBarState.PageFocusUp();
                if (pawn != null)
                {
                    MultiSelectState.SetFocusedPawn(pawn);
                    MultiSelectState.AnnounceFocusedPawn(pawn);
                }
            }
            else
            {
                ColonistBarState.PageUp();
            }
        }

        private static void OnColonistBarMoveRight(KeyEventSnapshot e)
        {
            if (MultiSelectState.IsMultiSelectMode)
            {
                TolkHelper.Speak("RimWorldAccess.Input.MultiSelect.CannotReorderDuringMultiSelect".Loc());
                return;
            }
            ColonistBarState.MoveRight();
        }

        private static void OnColonistBarMoveLeft(KeyEventSnapshot e)
        {
            if (MultiSelectState.IsMultiSelectMode)
            {
                TolkHelper.Speak("RimWorldAccess.Input.MultiSelect.CannotReorderDuringMultiSelect".Loc());
                return;
            }
            ColonistBarState.MoveLeft();
        }

        private static void OnColonistBarMoveDown(KeyEventSnapshot e)
        {
            if (MultiSelectState.IsMultiSelectMode)
            {
                TolkHelper.Speak("RimWorldAccess.Input.MultiSelect.CannotReorderDuringMultiSelect".Loc());
                return;
            }
            ColonistBarState.MoveDown();
        }

        private static void OnColonistBarMoveUp(KeyEventSnapshot e)
        {
            if (MultiSelectState.IsMultiSelectMode)
            {
                TolkHelper.Speak("RimWorldAccess.Input.MultiSelect.CannotReorderDuringMultiSelect".Loc());
                return;
            }
            ColonistBarState.MoveUp();
        }

        /// <summary>
        /// Alpha1-Alpha9 map to positions 0-8; Alpha0 maps to position 9 (the
        /// tenth bar slot) — the legacy handler's mapping, not a plain
        /// e.Key - Alpha0 offset. Double-tap-forces-camera-jump and the
        /// multi-select-focus redirect both live inside HandleAltNumberPress
        /// itself (state class, untouched).
        /// </summary>
        private static void OnColonistBarFocusByNumber(KeyEventSnapshot e)
        {
            int position = e.Key == KeyCode.Alpha0 ? 9 : (e.Key - KeyCode.Alpha1);
            ColonistBarState.HandleAltNumberPress(position);
        }

        private static void OnColonistBarInspectSelected(KeyEventSnapshot e)
        {
            Pawn selectedPawn = Find.Selector?.SingleSelectedThing as Pawn;
            if (selectedPawn != null)
            {
                // This chord is an ambient MapScope claim that passes through an open
                // gizmo menu. Without closing it first, GizmoNavigationState and
                // WindowlessInspectionState coexist and their two per-frame mirrors
                // re-float above each other every frame (a live trace measured 1,894
                // pushes in 4 seconds) while the announcement and the keyboard disagree
                // about which scope is on top. Close() is silent, so the inspection
                // announcement that follows reads as the answer to this chord.
                if (GizmoNavigationState.IsActive)
                {
                    GizmoNavigationState.Close();
                }
                WindowlessInspectionState.OpenForObject(selectedPawn);
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Guard.NoPawnSelected".Loc());
            }
        }

        private static void OnColonistBarInfoCardSelected(KeyEventSnapshot e)
        {
            Pawn selectedPawn = Find.Selector?.SingleSelectedThing as Pawn;
            if (selectedPawn != null)
            {
                Find.WindowStack.Add(new Dialog_InfoCard(selectedPawn));
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Guard.NoPawnSelected".Loc());
            }
        }

        /// <summary>The retired multi-select and colonist-bar rungs' shared
        /// outer gate, verbatim (PRIORITY 6.40 and 6.45 use the identical
        /// condition).</summary>
        private static bool SelectionClusterLive()
        {
            return Current.ProgramState == ProgramState.Playing
                && Find.CurrentMap != null
                && WorldRendererUtility.DrawingMap
                && (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion);
        }

        /// <summary>Save-group additionally requires an active multi-selection
        /// (the legacy handler's inner condition for that one key, verbatim).</summary>
        private static bool SaveGroupLive()
        {
            return SelectionClusterLive() && MultiSelectState.IsMultiSelectActive;
        }

        /// <summary>The retired bare-/ rung's gate, verbatim (PRIORITY 7.14).</summary>
        private static bool FocusByCursorLive()
        {
            return Current.ProgramState == ProgramState.Playing
                && Find.CurrentMap != null
                && (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion)
                && MapNavigationState.IsInitialized;
        }

        /// <summary>The retired line-formation rung's gate, verbatim (PRIORITY 0.195).</summary>
        private static bool LineFormationActive()
        {
            return LineFormationState.IsActive;
        }

        /// <summary>Confirm additionally requires both points placed (the
        /// legacy handler's inner condition for that key, verbatim).</summary>
        private static bool LineFormationConfirmReady()
        {
            return LineFormationState.IsActive && LineFormationState.HasBothPoints;
        }
    }
}
