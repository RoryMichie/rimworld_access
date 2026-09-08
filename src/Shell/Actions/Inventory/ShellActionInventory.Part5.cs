using System.Collections.Generic;
using UnityEngine;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Action inventory, part 5: assign/storage/plant/mech/gizmo/health/prisoner/inspection/
    /// inventory/float menus, world tile info, time speed, draft.
    /// </summary>
    internal static class ShellActionInventoryPart5
    {
        internal static void Register(ActionCatalog c)
        {
            // ---- assign menu -> AssignScope ----
            // Policy-column shortcuts; live in both the main table and the submenu.
            c.Register(InputAction.ForScreen("assign", "assign.policyNew",
                new List<KeyChord> { KeyChord.Of(KeyCode.N, alt: true) }));
            c.Register(InputAction.ForScreen("assign", "assign.policyRename",
                new List<KeyChord> { KeyChord.Of(KeyCode.R, alt: true) }));
            c.Register(InputAction.ForScreen("assign", "assign.policyCopy",
                new List<KeyChord> { KeyChord.Of(KeyCode.C, alt: true) }));
            c.Register(InputAction.ForScreen("assign", "assign.policyEdit",
                new List<KeyChord> { KeyChord.Of(KeyCode.E, alt: true) }));
            c.Register(InputAction.ForScreen("assign", "assign.policyDelete",
                new List<KeyChord> { KeyChord.Of(KeyCode.Delete) }));
            // Paint the current cell value onto neighbouring pawns.
            c.Register(InputAction.ForScreen("assign", "assign.paintDown",
                new List<KeyChord> { KeyChord.Of(KeyCode.DownArrow, shift: true) }));
            c.Register(InputAction.ForScreen("assign", "assign.paintUp",
                new List<KeyChord> { KeyChord.Of(KeyCode.UpArrow, shift: true) }));
            c.Register(InputAction.ForScreen("assign", "assign.paintToFirst",
                new List<KeyChord> { KeyChord.Of(KeyCode.Home, shift: true) }));
            c.Register(InputAction.ForScreen("assign", "assign.paintToLast",
                new List<KeyChord> { KeyChord.Of(KeyCode.End, shift: true) }));
            // Both chords paint the entire column; the Home/End form only differs in fill direction.
            c.Register(InputAction.ForScreen("assign", "assign.paintEntireColumn",
                new List<KeyChord>
                {
                    KeyChord.Of(KeyCode.Home, ctrl: true, shift: true),
                    KeyChord.Of(KeyCode.End, ctrl: true, shift: true)
                }));
            // ] opens the cell context menu in both the table and the submenu.
            c.Register(InputAction.ForScreen("assign", "assign.contextMenu",
                new List<KeyChord> { KeyChord.Of(KeyCode.RightBracket) }));
            c.Register(InputAction.ForScreen("assign", "assign.infoCard",
                new List<KeyChord> { KeyChord.Of(KeyCode.I, alt: true) }));

            // ---- storage settings menu ("storageSettings") ----
            // No registrations: Alt+I is the shared filterTree.infoCard id (Part7.cs).

            // ---- plant selection menu ----
            // No screen-specific actions: typeahead char sink + typeahead-erase only.

            // ---- mech control group menu -> MechControlGroupScope ----
            c.Register(InputAction.ForScreen("mech", "mech.nextPage",
                new List<KeyChord> { KeyChord.Of(KeyCode.Tab) }));
            c.Register(InputAction.ForScreen("mech", "mech.previousPage",
                new List<KeyChord> { KeyChord.Of(KeyCode.Tab, shift: true) }));
            c.Register(InputAction.ForScreen("mech", "mech.reassignMech",
                new List<KeyChord> { KeyChord.Of(KeyCode.RightBracket) }));
            // Range-editor sub-mode (isEditingRange); arrows are repurposed while it is open.
            c.Register(InputAction.ForScreen("mech", "mech.range.toggleBound",
                new List<KeyChord> { KeyChord.Of(KeyCode.UpArrow), KeyChord.Of(KeyCode.DownArrow) }));
            // Shifted and bare chords share one action id; MechControlGroupScope reads
            // KeyEventSnapshot.Shift to pick the step size.
            c.Register(InputAction.ForScreen("mech", "mech.range.increase",
                new List<KeyChord> { KeyChord.Of(KeyCode.RightArrow), KeyChord.Of(KeyCode.RightArrow, shift: true) }));
            c.Register(InputAction.ForScreen("mech", "mech.range.decrease",
                new List<KeyChord> { KeyChord.Of(KeyCode.LeftArrow), KeyChord.Of(KeyCode.LeftArrow, shift: true) }));
            // The keyboard path to Dialog_RechargeSettings' own "Reset" button.
            c.Register(InputAction.ForScreen("mech", "mech.range.reset",
                new List<KeyChord> { KeyChord.Of(KeyCode.R) }));

            // ---- gizmo navigation -> GizmoScope ----
            c.Register(InputAction.ForScreen("gizmos", "gizmos.infoCard",
                new List<KeyChord> { KeyChord.Of(KeyCode.I, alt: true) }));
            c.Register(InputAction.ForScreen("gizmos", "gizmos.rightClickOptions",
                new List<KeyChord> { KeyChord.Of(KeyCode.RightBracket) }));
            // Reverse designation gizmos call DesignateThing directly and mods (Allow Tool)
            // re-read the physical modifier keys inside it, so every modifier variant is claimed.
            // Ctrl+Alt is excluded: Ctrl+Alt+Enter falls through to colonistBar.inspectSelected.
            var activateModified = new List<KeyChord>();
            activateModified.AddRange(KeyChord.ModifierHeldVariants(KeyCode.Return));
            activateModified.AddRange(KeyChord.ModifierHeldVariants(KeyCode.KeypadEnter));
            c.Register(InputAction.ForScreen("gizmos", "gizmos.activateModified",
                activateModified));
            // Horizontal arrows adjust the focused slider gizmo live, no sub-mode; the claims
            // stand down for non-slider gizmos so the map cursor keeps the arrows.
            c.Register(InputAction.ForScreen("gizmos", "gizmos.slider.decrease",
                new List<KeyChord> { KeyChord.Of(KeyCode.LeftArrow) }));
            c.Register(InputAction.ForScreen("gizmos", "gizmos.slider.increase",
                new List<KeyChord> { KeyChord.Of(KeyCode.RightArrow) }));
            c.Register(InputAction.ForScreen("gizmos", "gizmos.slider.decreaseBig",
                new List<KeyChord> { KeyChord.Of(KeyCode.LeftArrow, shift: true) }));
            c.Register(InputAction.ForScreen("gizmos", "gizmos.slider.increaseBig",
                new List<KeyChord> { KeyChord.Of(KeyCode.RightArrow, shift: true) }));
            // Consume-only block of the bare letter/digit KEYCODES so a keycode twin of a
            // typeahead character cannot leak to other bare-letter openers; the character itself
            // goes to the scope's CharSink. Ctrl/Alt letters still reach the ambient claims.
            c.Register(InputAction.ForScreen("gizmos", "gizmos.blockChar",
                new List<KeyChord>
                {
                    KeyChord.Of(KeyCode.A), KeyChord.Of(KeyCode.B), KeyChord.Of(KeyCode.C),
                    KeyChord.Of(KeyCode.D), KeyChord.Of(KeyCode.E), KeyChord.Of(KeyCode.F),
                    KeyChord.Of(KeyCode.G), KeyChord.Of(KeyCode.H), KeyChord.Of(KeyCode.I),
                    KeyChord.Of(KeyCode.J), KeyChord.Of(KeyCode.K), KeyChord.Of(KeyCode.L),
                    KeyChord.Of(KeyCode.M), KeyChord.Of(KeyCode.N), KeyChord.Of(KeyCode.O),
                    KeyChord.Of(KeyCode.P), KeyChord.Of(KeyCode.Q), KeyChord.Of(KeyCode.R),
                    KeyChord.Of(KeyCode.S), KeyChord.Of(KeyCode.T), KeyChord.Of(KeyCode.U),
                    KeyChord.Of(KeyCode.V), KeyChord.Of(KeyCode.W), KeyChord.Of(KeyCode.X),
                    KeyChord.Of(KeyCode.Y), KeyChord.Of(KeyCode.Z),
                    KeyChord.Of(KeyCode.Alpha0), KeyChord.Of(KeyCode.Alpha1),
                    KeyChord.Of(KeyCode.Alpha2), KeyChord.Of(KeyCode.Alpha3),
                    KeyChord.Of(KeyCode.Alpha4), KeyChord.Of(KeyCode.Alpha5),
                    KeyChord.Of(KeyCode.Alpha6), KeyChord.Of(KeyCode.Alpha7),
                    KeyChord.Of(KeyCode.Alpha8), KeyChord.Of(KeyCode.Alpha9),
                }));

            // ---- health tab -> HealthTabScope ----
            // No registrations: surgery reordering rides the shared
            // SharedMenuGrammar.ReorderUp/ReorderDown ids, and blockChar claims are reserved for
            // NON-modal scopes -- this screen's modality already swallows unclaimed keys.

            // ---- prisoner tab ----
            // Left/Right switch between tabbable sections (TabSection enum).
            c.Register(InputAction.ForScreen("prisoner", "prisoner.previousSection",
                new List<KeyChord> { KeyChord.Of(KeyCode.LeftArrow) }));
            c.Register(InputAction.ForScreen("prisoner", "prisoner.nextSection",
                new List<KeyChord> { KeyChord.Of(KeyCode.RightArrow) }));
            c.Register(InputAction.ForScreen("prisoner", "prisoner.toggleCheckbox",
                new List<KeyChord> { KeyChord.Of(KeyCode.Space) }));

            // ---- inspection menu -> InspectionScope ----
            // Space toggles/activates Action items (e.g. food checkboxes).
            c.Register(InputAction.ForScreen("inspection", "inspection.toggleItem",
                new List<KeyChord> { KeyChord.Of(KeyCode.Space) }));
            c.Register(InputAction.ForScreen("inspection", "inspection.infoCard",
                new List<KeyChord> { KeyChord.Of(KeyCode.I, alt: true) }));
            c.Register(InputAction.ForScreen("inspection", "inspection.delete",
                new List<KeyChord> { KeyChord.Of(KeyCode.Delete) }));
            // No tree-grammar ids here: InspectionScope is a TreeRegionScope, so Left/Right ride
            // the base shared menus.previousHorizontal/nextHorizontal claims and Ctrl+Home,
            // Ctrl+End, Page Up/Down and '*' ride the "tree" pseudo-screen set in Part7.cs.

            // ---- inventory menu -> InventoryScope ----
            c.Register(InputAction.ForScreen("inventory", "inventory.contextMenu",
                new List<KeyChord> { KeyChord.Of(KeyCode.RightBracket) }));
            c.Register(InputAction.ForScreen("inventory", "inventory.jumpTo",
                new List<KeyChord> { KeyChord.Of(KeyCode.J, alt: true) }));
            // Expand every category node recursively; '*' is Numpad-Star or Shift+8 (US layout).
            c.Register(InputAction.ForScreen("inventory", "inventory.expandAll",
                new List<KeyChord>
                {
                    KeyChord.Of(KeyCode.KeypadMultiply),
                    KeyChord.Of(KeyCode.Alpha8, shift: true)
                }));
            c.Register(InputAction.ForScreen("inventory", "inventory.delete",
                new List<KeyChord> { KeyChord.Of(KeyCode.Delete) }));
            c.Register(InputAction.ForScreen("inventory", "inventory.announce",
                new List<KeyChord> { KeyChord.Of(KeyCode.Space) }));
            // No tree-grammar ids here: InventoryScope is a TreeRegionScope, so Left/Right and
            // the Ctrl+Home/Ctrl+End/Page Up/Down set come from the base and the "tree"
            // pseudo-screen (Part7.cs). inventory.expandAll above stays local on purpose: '*'
            // expands every Category in the WHOLE tree here, not just siblings.

            // ---- order float menu -> floatMenu ----
            c.Register(InputAction.ForScreen("floatMenu", "floatMenu.infoCard",
                new List<KeyChord> { KeyChord.Of(KeyCode.I, alt: true) }));

            // ---- world map tile info keys 1-5 ----
            c.Register(new InputAction("world.tileInfo.growing",
                ActionCategory.World,
                new List<KeyChord> { KeyChord.Of(KeyCode.Alpha1), KeyChord.Of(KeyCode.Keypad1) }));
            c.Register(new InputAction("world.tileInfo.movement",
                ActionCategory.World,
                new List<KeyChord> { KeyChord.Of(KeyCode.Alpha2), KeyChord.Of(KeyCode.Keypad2) }));
            c.Register(new InputAction("world.tileInfo.health",
                ActionCategory.World,
                new List<KeyChord> { KeyChord.Of(KeyCode.Alpha3), KeyChord.Of(KeyCode.Keypad3) }));
            c.Register(new InputAction("world.tileInfo.location",
                ActionCategory.World,
                new List<KeyChord> { KeyChord.Of(KeyCode.Alpha4), KeyChord.Of(KeyCode.Keypad4) }));
            c.Register(new InputAction("world.tileInfo.features",
                ActionCategory.World,
                new List<KeyChord> { KeyChord.Of(KeyCode.Alpha5), KeyChord.Of(KeyCode.Keypad5) }));

            // ---- time control Shift+1/2/3 ----
            // Vanilla's TimeSpeed_* on Shift+digit; the mod blocks the vanilla bare-digit
            // binding. These share physical chords with the Work tab's work.setPriorityAll1-4:
            // Map is ambient and Work a Screen scope, so CanCoexist treats them as exclusive
            // contexts and scope layering resolves them at dispatch. Ultrafast is additionally
            // gated to Prefs.DevMode, mirroring vanilla.
            c.Register(new InputAction("map.timeSpeed.normal",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.Alpha1, shift: true), KeyChord.Of(KeyCode.Keypad1, shift: true) }));
            c.Register(new InputAction("map.timeSpeed.fast",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.Alpha2, shift: true), KeyChord.Of(KeyCode.Keypad2, shift: true) }));
            c.Register(new InputAction("map.timeSpeed.superfast",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.Alpha3, shift: true), KeyChord.Of(KeyCode.Keypad3, shift: true) }));
            c.Register(new InputAction("map.timeSpeed.ultrafast",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.Alpha4, shift: true), KeyChord.Of(KeyCode.Keypad4, shift: true) }));

            // ---- toggle draft with R -> map.draft.toggle ----
            // Drafts or undrafts single and multi-selected colonists alike. Bare R only:
            // modifier variants are not claimed, per exact-chord matching.
            c.Register(new InputAction("map.draft.toggle",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.R) }));
        }
    }
}
