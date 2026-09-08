using System.Collections.Generic;
using UnityEngine;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Action inventory, part 8 — the pre-game Page chain, the main menu, and the
    /// ideology-building flows (IdeoBuilder/IdeoReform, the shared tree viewer, the overlay
    /// editors, and the Archonexus endgame reform, which reuses the same overlay family).
    /// Anything reached from the map or world view during ordinary play belongs in Part7 instead.
    /// Each header names the scope that owns its keys; defaults are today's keys.
    /// </summary>
    internal static class ShellActionInventoryPart8
    {
        internal static void Register(ActionCatalog c)
        {
            // ---- ArchonexusIdeoScreenScope (Dialog_ConfigureIdeo) ----
            // Alt+N is the mod-wide "Next" chord; this screen has no Save, so no Alt+S.
            c.Register(InputAction.ForScreen("archonexusReformIdeo", "archonexusReformIdeo.confirmAndProceed",
                new List<KeyChord> { KeyChord.Of(KeyCode.N, alt: true) })); // "Next" toolbar row (ConfirmAndProceed vehicle)
            // DORMANT — base region cycling owns Tab. Kept registered so the rebindable default
            // survives.
            c.Register(InputAction.ForScreen("archonexusReformIdeo", "archonexusReformIdeo.toggleDetailTab",
                new List<KeyChord> { KeyChord.Of(KeyCode.Tab), KeyChord.Of(KeyCode.Tab, shift: true) })); // DORMANT — base region cycling
            // The Details region delegates read-only to the shared IdeoDetailsTreeRegion and
            // editable to IdeoEditorRegionCore. Entries below are claimed by
            // ArchonexusIdeoScreenScope, gated on Details-region tree mode, unless marked DORMANT.
            c.Register(InputAction.ForScreen("archonexusReformIdeo", "archonexusReformIdeo.randomizeAll",
                new List<KeyChord> { KeyChord.Of(KeyCode.R, alt: true) })); // "Randomize all" toolbar row, only while an editable ideo exists
            // DORMANT — IdeoEditorRegionCore has no context menu; its rows and toolbar are
            // directly reachable.
            c.Register(InputAction.ForScreen("archonexusReformIdeo", "archonexusReformIdeo.contextMenu",
                new List<KeyChord> { KeyChord.Of(KeyCode.RightBracket) })); // DORMANT
            // DORMANT — each overlay editor claims its own ideoOverlayEditor.delete and masks this
            // one by ordinary stack layering.
            c.Register(InputAction.ForScreen("archonexusReformIdeo", "archonexusReformIdeo.delete",
                new List<KeyChord> { KeyChord.Of(KeyCode.Delete) })); // DORMANT
            c.Register(InputAction.ForScreen("archonexusReformIdeo", "archonexusReformIdeo.jumpToFirstAbsolute",
                new List<KeyChord> { KeyChord.Of(KeyCode.Home, ctrl: true) })); // Details-region tree mode only
            c.Register(InputAction.ForScreen("archonexusReformIdeo", "archonexusReformIdeo.jumpToLastAbsolute",
                new List<KeyChord> { KeyChord.Of(KeyCode.End, ctrl: true) })); // Details-region tree mode only
            c.Register(InputAction.ForScreen("archonexusReformIdeo", "archonexusReformIdeo.jumpToPreviousSection",
                new List<KeyChord> { KeyChord.Of(KeyCode.PageUp) })); // Details-region tree mode only
            c.Register(InputAction.ForScreen("archonexusReformIdeo", "archonexusReformIdeo.jumpToNextSection",
                new List<KeyChord> { KeyChord.Of(KeyCode.PageDown) })); // Details-region tree mode only
            c.Register(InputAction.ForScreen("archonexusReformIdeo", "archonexusReformIdeo.expandAllSiblings",
                new List<KeyChord> { KeyChord.Of(KeyCode.KeypadMultiply), KeyChord.Of(KeyCode.Alpha8, shift: true) })); // Details-region tree mode only

            // ---- ArchonexusConvertColonistsScope (Dialog_ChooseColonistsForIdeo) ----
            // Enter is the shared menus.activate grammar; Space still needs its own action even
            // though it is functionally identical.
            c.Register(InputAction.ForScreen("archonexusConvertColonists", "archonexusConvertColonists.toggleItem",
                new List<KeyChord> { KeyChord.Of(KeyCode.Space) }));
            // Everything else is generic: the screen is a flat toggle list.

            // ---- ArchonexusColonyScope (Dialog_ChooseThingsForNewColony) ----
            c.Register(InputAction.ForScreen("archonexusColony", "archonexusColony.previousSection",
                new List<KeyChord> { KeyChord.Of(KeyCode.LeftArrow), KeyChord.Of(KeyCode.Tab, shift: true) }));
            c.Register(InputAction.ForScreen("archonexusColony", "archonexusColony.nextSection",
                new List<KeyChord> { KeyChord.Of(KeyCode.RightArrow), KeyChord.Of(KeyCode.Tab) }));
            c.Register(InputAction.ForScreen("archonexusColony", "archonexusColony.accept",
                new List<KeyChord> { KeyChord.Of(KeyCode.S, alt: true) }));
            c.Register(InputAction.ForScreen("archonexusColony", "archonexusColony.announceStatus",
                new List<KeyChord> { KeyChord.Of(KeyCode.T) }));
            c.Register(InputAction.ForScreen("archonexusColony", "archonexusColony.infoCard",
                new List<KeyChord> { KeyChord.Of(KeyCode.I, alt: true) }));
            // Pawn info quintet via the shared CaravanInputHelper.HandlePawnInfoShortcuts.
            c.Register(InputAction.ForScreen("archonexusColony", "archonexusColony.healthInfo",
                new List<KeyChord> { KeyChord.Of(KeyCode.H, alt: true) }));
            c.Register(InputAction.ForScreen("archonexusColony", "archonexusColony.moodInfo",
                new List<KeyChord> { KeyChord.Of(KeyCode.M, alt: true) }));
            c.Register(InputAction.ForScreen("archonexusColony", "archonexusColony.needsInfo",
                new List<KeyChord> { KeyChord.Of(KeyCode.N, alt: true) }));
            c.Register(InputAction.ForScreen("archonexusColony", "archonexusColony.gearInfo",
                new List<KeyChord> { KeyChord.Of(KeyCode.G, alt: true) }));
            c.Register(InputAction.ForScreen("archonexusColony", "archonexusColony.skillsInfo",
                new List<KeyChord> { KeyChord.Of(KeyCode.K, alt: true) }));
            c.Register(InputAction.ForScreen("archonexusColony", "archonexusColony.toggleItem",
                new List<KeyChord> { KeyChord.Of(KeyCode.Space) }));

            // ---- IdeoBuilderScreenScope (Page_ConfigureIdeo / Page_ConfigureFluidIdeo) ----
            // togglePanel is DORMANT: the base ctor claims Tab/Shift+Tab for region cycling across
            // all three regions. Kept registered so its rebindable default survives.
            c.Register(InputAction.ForScreen("ideoBuilder", "ideoBuilder.togglePanel",
                new List<KeyChord> { KeyChord.Of(KeyCode.Tab), KeyChord.Of(KeyCode.Tab, shift: true) })); // DORMANT — base region cycling
            // Alt+N is the mod-wide "Next" chord; Alt+S is ideoBuilder.saveToFile here.
            c.Register(InputAction.ForScreen("ideoBuilder", "ideoBuilder.continueNext",
                new List<KeyChord> { KeyChord.Of(KeyCode.N, alt: true) }));
            c.Register(InputAction.ForScreen("ideoBuilder", "ideoBuilder.randomizeAll",
                new List<KeyChord> { KeyChord.Of(KeyCode.R, alt: true) }));
            c.Register(InputAction.ForScreen("ideoBuilder", "ideoBuilder.saveToFile",
                new List<KeyChord> { KeyChord.Of(KeyCode.S, alt: true) }));
            // IdeoBuilderHubPatch_DoBackGuard / _DoNextGuard prefix Page.DoBack/DoNext, which is
            // where Pages wire Accept/Cancel — not Window.OnCancelKeyPressed/OnAcceptKeyPressed.

            // ---- Shared component: the read-only ideo detail tree (IdeoDetailsTreeRegion) ----
            c.Register(InputAction.ForScreen("ideologyTreeViewer", "ideologyTreeViewer.infoCard",
                new List<KeyChord> { KeyChord.Of(KeyCode.I, alt: true) }));
            // ---- IdeoReformScreenScope (Dialog_ReformIdeo) ----
            // Alt+N is the mod-wide "Next" chord; this screen has no Save, so no Alt+S.
            c.Register(InputAction.ForScreen("ideoReform", "ideoReform.advanceOrApply",
                new List<KeyChord> { KeyChord.Of(KeyCode.N, alt: true) })); // stage 1: Next; stage 2: Apply changes (DoneButton)
            c.Register(InputAction.ForScreen("ideoReform", "ideoReform.resetOrRandomize",
                new List<KeyChord> { KeyChord.Of(KeyCode.R, alt: true) })); // stage 1: reset pending changes; stage 2: randomize
            // Escape is stage-aware back/close, treated as generic back. This scope claims none of
            // the ideoOverlayEditor.* family: the overlay editors are full ScreenScopes that mask
            // it by ordinary stack layering.

            // ---- Shared component: the overlay editors — IdeoPreceptScreenScope,
            //      IdeoTypedPreceptScreenScope, IdeoDeityScreenScope, and StyleItemsDialogScope
            //      over Dialog_EditIdeoStyleItems. All claim these ids whichever host is live.
            //      contextMenu is the expand/collapse toggle on StyleItemsDialogScope (it has no
            //      edit-actions menu) and a second binding for the edit-actions menu on the typed
            //      precept and deity scopes. expandAllSiblings is claimed only by
            //      StyleItemsDialogScope and IdeoDeityScreenScope; the two TreeRegionScopes get
            //      the identical "*" chords from the base tree grammar. ----
            c.Register(InputAction.ForScreen("ideoOverlayEditor", "ideoOverlayEditor.contextMenu",
                new List<KeyChord> { KeyChord.Of(KeyCode.RightBracket) }));
            c.Register(InputAction.ForScreen("ideoOverlayEditor", "ideoOverlayEditor.delete",
                new List<KeyChord> { KeyChord.Of(KeyCode.Delete) }));
            c.Register(InputAction.ForScreen("ideoOverlayEditor", "ideoOverlayEditor.jumpToFirstAbsolute",
                new List<KeyChord> { KeyChord.Of(KeyCode.Home, ctrl: true) })); // DORMANT everywhere; kept for rebinds
            c.Register(InputAction.ForScreen("ideoOverlayEditor", "ideoOverlayEditor.jumpToLastAbsolute",
                new List<KeyChord> { KeyChord.Of(KeyCode.End, ctrl: true) })); // DORMANT everywhere; kept for rebinds
            c.Register(InputAction.ForScreen("ideoOverlayEditor", "ideoOverlayEditor.jumpToPreviousSection",
                new List<KeyChord> { KeyChord.Of(KeyCode.PageUp) })); // StyleItemsDialogScope only
            c.Register(InputAction.ForScreen("ideoOverlayEditor", "ideoOverlayEditor.jumpToNextSection",
                new List<KeyChord> { KeyChord.Of(KeyCode.PageDown) }));
            c.Register(InputAction.ForScreen("ideoOverlayEditor", "ideoOverlayEditor.expandAllSiblings",
                new List<KeyChord> { KeyChord.Of(KeyCode.KeypadMultiply), KeyChord.Of(KeyCode.Alpha8, shift: true) })); // toggle-all on StyleItemsDialogScope

            // ---- IdeoPreceptScreenScope, a TreeRegionScope over IdeoPreceptSelectionState ----
            c.Register(InputAction.ForScreen("ideoPreceptSelection", "ideoPreceptSelection.removePrecept",
                new List<KeyChord> { KeyChord.Of(KeyCode.Delete) }));
            // Expand/collapse, the absolute edges, "*" and the section jumps ride the tree.* set
            // registered in Part7.

            // ---- IdeoMemeScreenScope (Dialog_ChooseMemes), a TreeRegionScope ----
            // accept/randomize are both global chords AND Buttons-region entries (Done/Randomize).
            c.Register(InputAction.ForScreen("ideoMemeSelection", "ideoMemeSelection.accept",
                new List<KeyChord> { KeyChord.Of(KeyCode.S, alt: true) }));
            c.Register(InputAction.ForScreen("ideoMemeSelection", "ideoMemeSelection.randomize",
                new List<KeyChord> { KeyChord.Of(KeyCode.R, alt: true) }));
            // The tree extras come from the shared tree.* ids, registered exactly once in Part7:
            // action ids are globally unique in ActionCatalog, so per-screen re-registration
            // throws, and dispatch resolves claims by bare id.

            // ---- IdeoLoadPatch (Dialog_FileList.DoWindowContents, filtered to Dialog_IdeoList_Load) ----
            c.Register(InputAction.ForScreen("ideoLoad", "ideoLoad.delete",
                new List<KeyChord> { KeyChord.Of(KeyCode.Delete) }));
            // ---- WorldParamsScreenScope (Page_CreateWorldParams) ----
            // toggleSection / decreaseValue / increaseValue are DORMANT — the base grammar owns
            // Tab and Left/Right — but stay registered so their rebindable defaults survive.
            // There is no bare-R randomizeSeed id: it collided with typeahead everywhere on the
            // page, so Randomize Seed activates on Enter only.
            c.Register(InputAction.ForScreen("worldParams", "worldParams.toggleSection",
                new List<KeyChord> { KeyChord.Of(KeyCode.Tab), KeyChord.Of(KeyCode.Tab, shift: true) })); // DORMANT — base region cycling
            c.Register(InputAction.ForScreen("worldParams", "worldParams.decreaseValue",
                new List<KeyChord> { KeyChord.Of(KeyCode.LeftArrow) })); // DORMANT — base Left/Right adjust
            c.Register(InputAction.ForScreen("worldParams", "worldParams.increaseValue",
                new List<KeyChord> { KeyChord.Of(KeyCode.RightArrow) })); // DORMANT — base Left/Right adjust
            c.Register(InputAction.ForScreen("worldParams", "worldParams.deleteFaction",
                new List<KeyChord> { KeyChord.Of(KeyCode.Delete) })); // Factions section
            c.Register(InputAction.ForScreen("worldParams", "worldParams.openAddFactionMenu",
                new List<KeyChord> { KeyChord.Of(KeyCode.A, alt: true) }));
            // Alt+G: the page's proceed action, first-letter mnemonic on "WorldGenerate".
            c.Register(InputAction.ForScreen("worldParams", "worldParams.generate",
                new List<KeyChord> { KeyChord.Of(KeyCode.G, alt: true) }));
            // Alt+R / Shift+Alt+R follow the catalog's reset convention.
            c.Register(InputAction.ForScreen("worldParams", "worldParams.resetAll",
                new List<KeyChord> { KeyChord.Of(KeyCode.R, alt: true) }));
            c.Register(InputAction.ForScreen("worldParams", "worldParams.resetFactions",
                new List<KeyChord> { KeyChord.Of(KeyCode.R, shift: true, alt: true) })); // page-wide, like resetAll

            // ---- StartingSitePatch (Page_SelectStartingSite.DoWindowContents) ----
            // Registers nothing: its key handling duplicates the ambient world-scanner handling
            // as a defensive fallback for when GUI.Window focus is not established.

            // ---- StorytellerScreenScope (Page_SelectStoryteller) ----
            // The storytellerSelect.* / customDifficulty.* ids below are DORMANT — the scope uses
            // only the shared grammar — but stay registered so their rebindable defaults survive.
            // The in-game StorytellerSelectionState is unaffected.
            c.Register(InputAction.ForScreen("storytellerSelect", "storytellerSelect.nextMode",
                new List<KeyChord> { KeyChord.Of(KeyCode.Tab) })); // Storyteller -> Difficulty -> Permadeath -> [Anomaly]
            c.Register(InputAction.ForScreen("storytellerSelect", "storytellerSelect.previousMode",
                new List<KeyChord> { KeyChord.Of(KeyCode.Tab, shift: true) }));
            // DORMANT — the shared grammar's Enter covers it; kept registered per the note above.
            c.Register(InputAction.ForScreen("storytellerSelect", "storytellerSelect.activateSecondary",
                new List<KeyChord> { KeyChord.Of(KeyCode.Space) }));
            c.Register(InputAction.ForScreen("storytellerSelect", "storytellerSelect.customDifficulty.decrease",
                new List<KeyChord> { KeyChord.Of(KeyCode.LeftArrow) }));
            c.Register(InputAction.ForScreen("storytellerSelect", "storytellerSelect.customDifficulty.increase",
                new List<KeyChord> { KeyChord.Of(KeyCode.RightArrow) }));
            c.Register(InputAction.ForScreen("storytellerSelect", "storytellerSelect.customDifficulty.jumpToResetSection",
                new List<KeyChord> { KeyChord.Of(KeyCode.R, alt: true) }));
            // ---- ScenarioSelectScreenScope (Page_SelectScenario) ----
            // toggleDetailPanel and the two detailJump* ids are DORMANT: the base grammar owns Tab,
            // and the flat Details region has no within-level vs absolute distinction left.
            // deleteOrUnsubscribe is DORMANT too, replaced by the "]" context menu, which offers
            // only the actions that actually apply. All stay registered for their rebinds.
            c.Register(InputAction.ForScreen("scenarioSelect", "scenarioSelect.toggleDetailPanel",
                new List<KeyChord> { KeyChord.Of(KeyCode.Tab) })); // DORMANT — base region cycling
            c.Register(InputAction.ForScreen("scenarioSelect", "scenarioSelect.editScenario",
                new List<KeyChord> { KeyChord.Of(KeyCode.E, alt: true) }));
            // Alt+N, the mod-wide "Next" toolbar chord.
            c.Register(InputAction.ForScreen("scenarioSelect", "scenarioSelect.next",
                new List<KeyChord> { KeyChord.Of(KeyCode.N, alt: true) }));
            c.Register(InputAction.ForScreen("scenarioSelect", "scenarioSelect.deleteOrUnsubscribe",
                new List<KeyChord> { KeyChord.Of(KeyCode.Delete) })); // DORMANT — replaced by scenarioSelect.contextMenu
            c.Register(InputAction.ForScreen("scenarioSelect", "scenarioSelect.detailJumpToFirstAbsolute",
                new List<KeyChord> { KeyChord.Of(KeyCode.Home, ctrl: true) })); // DORMANT
            c.Register(InputAction.ForScreen("scenarioSelect", "scenarioSelect.detailJumpToLastAbsolute",
                new List<KeyChord> { KeyChord.Of(KeyCode.End, ctrl: true) })); // DORMANT
            // Per-row secondary actions: Delete/Unsubscribe/Open workshop page.
            c.Register(InputAction.ForScreen("scenarioSelect", "scenarioSelect.contextMenu",
                new List<KeyChord> { KeyChord.Of(KeyCode.RightBracket) }));
            // ---- ModListScreenScope, window-attached to Page_ModsConfig; real logic in the
            //      ModList* family under src/MainMenu/ ----
            // Reordering rides the shared SharedMenuGrammar.ReorderUp/ReorderDown ids.
            c.Register(InputAction.ForScreen("modList", "modList.autoSort",
                new List<KeyChord> { KeyChord.Of(KeyCode.R, alt: true) }));
            c.Register(InputAction.ForScreen("modList", "modList.saveChanges",
                new List<KeyChord> { KeyChord.Of(KeyCode.S, alt: true) }));
            // DoBottomButtons' three WidgetRow buttons (Page_ModsConfig.cs:830-889).
            c.Register(InputAction.ForScreen("modList", "modList.getMods",
                new List<KeyChord> { KeyChord.Of(KeyCode.G, alt: true) }));
            c.Register(InputAction.ForScreen("modList", "modList.unsubscribeMultiple",
                new List<KeyChord> { KeyChord.Of(KeyCode.U, alt: true) }));
            c.Register(InputAction.ForScreen("modList", "modList.saveLoadList",
                new List<KeyChord> { KeyChord.Of(KeyCode.L, alt: true) }));
            // Consume-only: swallows '*' in list view to prevent passthrough.
            c.Register(InputAction.ForScreen("modList", "modList.blockStar",
                new List<KeyChord> { KeyChord.Of(KeyCode.KeypadMultiply), KeyChord.Of(KeyCode.Alpha8, shift: true) }));
            // Escape is covered by the base ScreenScope's state-based OwnsCancel
            // (TypeaheadHasActiveSearch); see ModListScreenScope's class remarks.

            // ---- MainMenuScope (MainMenuDrawer.DoMainMenuControls; ActionCategory.MainMenu) ----
            // A two-region ScreenScope (Menu/Links) on pure shared grammar. blockChar is a
            // consume-only block of the bare letter/digit KEYCODES while the menu draws, so a
            // keycode twin of a typeahead character cannot leak past the non-modal base scope; the
            // character itself is captured by MainMenuScope's CharSink. The KeypadMultiply /
            // Shift+Alpha8 chords swallow '*'. Ctrl/Alt letters and digits fall through.
            c.Register(new InputAction("mainMenu.blockChar",
                ActionCategory.MainMenu,
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
                    KeyChord.Of(KeyCode.KeypadMultiply),
                    KeyChord.Of(KeyCode.Alpha8, shift: true),
                }));

            // ---- IdeoPresetScreenScope (Page_ChooseIdeoPreset, the initial preset choice) ----
            // Every ideologySelect.* id below is DORMANT — the scope uses only the shared grammar
            // — but stays registered so its rebindable default survives.
            c.Register(InputAction.ForScreen("ideologySelect", "ideologySelect.switchTab",
                new List<KeyChord> { KeyChord.Of(KeyCode.Tab), KeyChord.Of(KeyCode.Tab, shift: true) })); // DORMANT — base region cycling
            c.Register(InputAction.ForScreen("ideologySelect", "ideologySelect.structureMenu",
                new List<KeyChord> { KeyChord.Of(KeyCode.S, alt: true) })); // DORMANT — Structure is now a combo row (Enter opens the picker)
            c.Register(InputAction.ForScreen("ideologySelect", "ideologySelect.styleMenu",
                new List<KeyChord> { KeyChord.Of(KeyCode.Y, alt: true) })); // DORMANT — style slots are now combo rows (Enter opens the picker)
            // The Presets region is a flat RadioButton list, so the tree extras below claim
            // nothing.
            c.Register(InputAction.ForScreen("ideologySelect", "ideologySelect.firstAbsolute",
                new List<KeyChord> { KeyChord.Of(KeyCode.Home, ctrl: true) }));
            c.Register(InputAction.ForScreen("ideologySelect", "ideologySelect.lastAbsolute",
                new List<KeyChord> { KeyChord.Of(KeyCode.End, ctrl: true) }));
            c.Register(InputAction.ForScreen("ideologySelect", "ideologySelect.jumpToPreviousSection",
                new List<KeyChord> { KeyChord.Of(KeyCode.PageUp) }));
            c.Register(InputAction.ForScreen("ideologySelect", "ideologySelect.jumpToNextSection",
                new List<KeyChord> { KeyChord.Of(KeyCode.PageDown) }));
            c.Register(InputAction.ForScreen("ideologySelect", "ideologySelect.expandAllSiblings",
                new List<KeyChord>
                {
                    KeyChord.Of(KeyCode.KeypadMultiply),
                    KeyChord.Of(KeyCode.Alpha8, shift: true)
                }));
            // Alt+N, the mod-wide "Next" toolbar chord. Its own scope key, since every
            // ideologySelect id is dormant.
            c.Register(InputAction.ForScreen("ideoPreset", "ideoPreset.next",
                new List<KeyChord> { KeyChord.Of(KeyCode.N, alt: true) }));

            // ---- TextBufferScope ----
            // The shared big-text reader's section jumps, registered ONCE under the "textBuffer"
            // pseudo-screen key: ids are globally unique and dispatch resolves by bare id, so every
            // subclass reaches this pair. Do NOT re-register these per screen.
            c.Register(InputAction.ForScreen("textBuffer", "textBuffer.jumpToPreviousSection",
                new List<KeyChord> { KeyChord.Of(KeyCode.PageUp) }));
            c.Register(InputAction.ForScreen("textBuffer", "textBuffer.jumpToNextSection",
                new List<KeyChord> { KeyChord.Of(KeyCode.PageDown) }));

            // ---- CreditsScope (Screen_Credits) ----
            // No chord: Escape runs the skip and Enter reaches it through the shared proceed offer
            // (ScreenScope.DefaultAcceptActionId). Registered so that offer can resolve it.
            c.Register(InputAction.ForScreen("credits", "credits.skip",
                new List<KeyChord>()));

            // ---- Dialog_MessageBox is a real window driven by MessageBoxScope on the shared
            //      Menus grammar; the windowless interception covers only its SUBCLASSES. ----

            // ---- IsekaiStatWindowScope: the mod reads Shift/Ctrl off the physical keys for its
            //      5/20/100 bulk steps, so modifier-held Left/Right must reach the same handler. ----
            c.Register(InputAction.ForScreen("isekaiStats", "isekaiStats.increaseBulk",
                new List<KeyChord>
                {
                    KeyChord.Of(KeyCode.RightArrow, shift: true), KeyChord.Of(KeyCode.RightArrow, ctrl: true),
                    KeyChord.Of(KeyCode.RightArrow, ctrl: true, shift: true),
                }));
            c.Register(InputAction.ForScreen("isekaiStats", "isekaiStats.decreaseBulk",
                new List<KeyChord>
                {
                    KeyChord.Of(KeyCode.LeftArrow, shift: true), KeyChord.Of(KeyCode.LeftArrow, ctrl: true),
                    KeyChord.Of(KeyCode.LeftArrow, ctrl: true, shift: true),
                }));
        }
    }
}
