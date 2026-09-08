using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The mod's action buttons. Everything that ACTS on the pawn or the roster is screen-global and
    /// lives in the toolbar (see DeclaredActions); only the four rows that belong to what they
    /// describe stay tree nodes -- Gender under Character/Identity, the three portrait-view toggles
    /// under Appearance/Preview. Row grammar, confirms and pickers live here; the MUTATION-C sites
    /// and the modifier-simulation idiom are documented on CharEditorCompat.Actions.Game.cs.
    /// </summary>
    internal sealed partial class CharacterEditorScope
    {
        // ---- Describe/Activate/Adjust dispatch. ----

        private void DescribeActionRow(RegionRow<ActionRowKind> row, ElementDescription d)
        {
            Pawn pawn = CharEditorCompat.CurrentPawn;
            switch (row.Kind)
            {
                case ActionRowKind.UtilityGender:
                    d.Label = "RimWorldAccess.CharEd.Actions.Gender".Translate();
                    d.Role = ElementRole.ComboBox;
                    d.Value = GenderLabel(pawn?.gender ?? Gender.None);
                    break;
                case ActionRowKind.UtilityNude:
                    d.Label = "RimWorldAccess.CharEd.Actions.Nude".Translate();
                    d.Role = ElementRole.Checkbox;
                    d.Check = CharEditorCompat.ClothesShown(window) ? CheckState.Unchecked : CheckState.Checked;
                    d.Extras = "RimWorldAccess.CharEd.Actions.NudeDesc".Translate();
                    break;
                case ActionRowKind.UtilityHats:
                    d.Label = "RimWorldAccess.CharEd.Actions.Hats".Translate();
                    d.Role = ElementRole.Checkbox;
                    d.Check = CharEditorCompat.HatsShown(window) ? CheckState.Checked : CheckState.Unchecked;
                    d.Extras = "RimWorldAccess.CharEd.Actions.HatsDesc".Translate();
                    break;
                case ActionRowKind.UtilityRotate:
                    d.Label = "RimWorldAccess.CharEd.Actions.RotatePortrait".Translate();
                    d.Role = ElementRole.Button;
                    break;
            }
        }

        private static string GenderLabel(Gender gender)
        {
            switch (gender)
            {
                case Gender.Male:
                    return "Male".Translate();
                case Gender.Female:
                    return "Female".Translate();
                default:
                    return "None".Translate();
            }
        }

        private void ActivateActionRow(RegionRow<ActionRowKind> row, InspectionTreeItem item)
        {
            Pawn pawn = CharEditorCompat.CurrentPawn;
            switch (row.Kind)
            {
                case ActionRowKind.UtilityGender:
                    OpenGenderPicker(pawn, item);
                    break;
                case ActionRowKind.UtilityNude:
                    CharEditorCompat.ToggleNude(window);
                    AnnounceAdjustedRow<CharRowKind>(item, DescribeCharRow);
                    break;
                case ActionRowKind.UtilityHats:
                    CharEditorCompat.ToggleHats(window);
                    AnnounceAdjustedRow<CharRowKind>(item, DescribeCharRow);
                    break;
                case ActionRowKind.UtilityRotate:
                {
                    string facing = CharEditorCompat.RotatePortrait(window);
                    TolkHelper.SpeakData(facing.NullOrEmpty()
                        ? "RimWorldAccess.CharEd.Actions.RotatePortrait".Translate().ToString()
                        : "RimWorldAccess.CharEd.Actions.RotatedTo".Translate(facing).ToString());
                    break;
                }
            }
        }

        // ---- Toolbar handlers. One method per entry, so the entry's own ScreenAction and its Alt
        // chord's claim run the SAME body. ----

        private void PerformPreviousPawn()
        {
            CharEditorCompat.PreviousPawn(window);
            AnnouncePawnSwitch();
        }

        private void PerformNextPawn()
        {
            CharEditorCompat.NextPawn(window);
            AnnouncePawnSwitch();
        }

        private void PerformAddPawn()
        {
            CharEditorCompat.AddPawn(window);
            AnnounceActionsMutation("RimWorldAccess.CharEd.Actions.PawnAdded");
        }

        private void PerformDeletePawn()
        {
            ConfirmDeletePawn(CharEditorCompat.CurrentPawn);
        }

        private void PerformClonePawn()
        {
            CharEditorCompat.ClonePawn(window);
            AnnounceActionsMutation("RimWorldAccess.CharEd.Actions.PawnCloned");
        }

        private void PerformRandomizePawn()
        {
            CharEditorCompat.RandomizePawn(window);
            // ARandomizePawn deletes API.Pawn's CURRENT target and creates a fresh pawn without
            // confirmed re-assignment back into API.Pawn -- force the rebuild rather than trust
            // RefreshContent's reference-identity check, which a stale reference to the now-deleted
            // pawn would silently defeat.
            BuildTree(preserveCursor: true);
            AnnounceActionsMutation("RimWorldAccess.CharEd.Actions.PawnRandomized");
        }

        private void PerformRandomizeEquipment()
        {
            OpenRandomizeEquipmentPicker(CharEditorCompat.CurrentPawn);
        }

        private void PerformRandomizeBio()
        {
            CharEditorCompat.RandomizeBio(window);
            RefreshCharacterSectionAfterChildDialog();
            AnnounceActionsMutation("RimWorldAccess.CharEd.Actions.BioRandomized");
        }

        private void PerformFindPawn()
        {
            CharEditorBrowserCompat.OpenFindPawn();
        }

        private void PerformMoveUp()
        {
            Pawn pawn = CharEditorCompat.CurrentPawn;
            CharEditorCompat.MoveUpInList(window);
            TolkHelper.SpeakData(DescribeNewRosterPosition(pawn, "RimWorldAccess.CharEd.Actions.MovedUpTo", "RimWorldAccess.CharEd.Actions.MovedUp"));
        }

        private void PerformMoveDown()
        {
            Pawn pawn = CharEditorCompat.CurrentPawn;
            CharEditorCompat.MoveDownInList(window);
            TolkHelper.SpeakData(DescribeNewRosterPosition(pawn, "RimWorldAccess.CharEd.Actions.MovedDownTo", "RimWorldAccess.CharEd.Actions.MovedDown"));
        }

        private void PerformJumpToPawn()
        {
            CharEditorCompat.JumpToPawn(window);
            TolkHelper.SpeakData("RimWorldAccess.CharEd.Actions.JumpedToPawn".Translate().ToString());
        }

        private void PerformTeleportPawn()
        {
            // The editor stays open, as it does for a mouse user: MapToolScope floats above this
            // scope while the tool is armed. No speech -- DevToolTargeting's mirror announces it.
            CharEditorCompat.MoveEditorAside();
            CharEditorCompat.ArmTeleport(CharEditorCompat.CurrentPawn);
        }

        private void PerformTeleportAnyPawn()
        {
            CharEditorCompat.MoveEditorAside();
            CharEditorCompat.ArmTeleportAnyPawn();
        }

        private void PerformOpenCapsule()
        {
            CharEditorCompat.OpenCapsule(window);
            // Dialog opened; its own scope announces on focus.
        }

        /// <summary>The chord twin of a disabled toolbar row's Enter: creation-strip entries stay
        /// present but disabled while creation mode is off, and an Alt chord reaches the handler
        /// directly, so it must refuse in the same words the shell speaks for their rows.</summary>
        private void RunCreationAction(string labelKey, Action perform)
        {
            if (!CharEditorCompat.CreationMode)
            {
                TolkHelper.SpeakData(
                    "RimWorldAccess.Shell.GenericWindow.Disabled".Loc(labelKey.Translate()).ToString()
                    + " " + "RimWorldAccess.CharEd.Actions.CreationModeOffHint".Translate());
                return;
            }
            perform();
        }

        // ---- Creation: confirm and modifier-chord pickers. ----

        /// <summary>AMoveUp/AMoveDown reorder <c>API.ListOf&lt;Pawn&gt;(EType.Pawns)</c> in place -- the
        /// same live list <see cref="RimWorldAccess.CharEditorCompat.PawnList"/> reads, so the pawn's
        /// 1-based rank there is its new roster position. Falls back to a bare "Moved up/down."</summary>
        private static string DescribeNewRosterPosition(Pawn pawn, string positionKey, string fallbackKey)
        {
            List<Pawn> list = CharEditorCompat.PawnList();
            int rank = pawn != null ? list.IndexOf(pawn) + 1 : 0;
            return rank > 0
                ? positionKey.Translate(rank, list.Count).ToString()
                : fallbackKey.Translate().ToString();
        }

        /// <summary>Our own confirm; the mod's delete button carries none. A real Dialog_MessageBox, so MessageBoxScope drives it unwired.</summary>
        private void ConfirmDeletePawn(Pawn pawn)
        {
            if (pawn == null)
            {
                return;
            }
            string text = "RimWorldAccess.CharEd.Actions.ConfirmDeletePawn".Translate(DescribePawn(pawn)).ToString();
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(text, delegate
            {
                CharEditorCompat.RemovePawn(window);
                // Force the rebuild: a reference-identity check against the now-deleted Pawn would
                // silently pass.
                BuildTree(preserveCursor: true);
                AnnounceActionsMutation("RimWorldAccess.CharEd.Actions.PawnDeleted");
            }, destructive: true));
        }

        private void OpenRandomizeKeepRacePicker()
        {
            var options = new List<FloatMenuOption>();
            options.Add(new FloatMenuOption("RimWorldAccess.CharEd.Actions.AnyGender".Translate(), delegate
            {
                CharEditorCompat.RunWithMenuRedirect(() => CharEditorCompat.RandomizePawnKeepRace(window, EventModifiers.None));
                AnnounceActionsMutation("RimWorldAccess.CharEd.Actions.RandomizedKeepingRace");
            }));
            options.Add(new FloatMenuOption("RimWorldAccess.CharEd.Actions.ForceFemale".Translate(), delegate
            {
                CharEditorCompat.RunWithMenuRedirect(() => CharEditorCompat.RandomizePawnKeepRace(window, EventModifiers.Alt));
                AnnounceActionsMutation("RimWorldAccess.CharEd.Actions.RandomizedKeepingRace");
            }));
            options.Add(new FloatMenuOption("RimWorldAccess.CharEd.Actions.ForceMale".Translate(), delegate
            {
                CharEditorCompat.RunWithMenuRedirect(() => CharEditorCompat.RandomizePawnKeepRace(window, EventModifiers.CapsLock));
                AnnounceActionsMutation("RimWorldAccess.CharEd.Actions.RandomizedKeepingRace");
            }));
            WindowlessFloatMenuState.OpenTitled("RimWorldAccess.CharEd.Actions.RandomizeKeepRace".Translate(), options);
        }

        private void OpenRandomizeBodyPartsPicker()
        {
            var options = new List<FloatMenuOption>();
            options.Add(new FloatMenuOption("RimWorldAccess.CharEd.Actions.AnyGender".Translate(), delegate
            {
                CharEditorCompat.RunWithMenuRedirect(() => CharEditorCompat.RandomizeBodyParts(window, EventModifiers.None));
                AnnounceAppearanceAndCharacterMutation("RimWorldAccess.CharEd.Actions.BodyPartsRandomized");
            }));
            options.Add(new FloatMenuOption("Female".Translate(), delegate
            {
                CharEditorCompat.RunWithMenuRedirect(() => CharEditorCompat.RandomizeBodyParts(window, EventModifiers.Alt));
                AnnounceAppearanceAndCharacterMutation("RimWorldAccess.CharEd.Actions.BodyPartsRandomized");
            }));
            options.Add(new FloatMenuOption("Male".Translate(), delegate
            {
                CharEditorCompat.RunWithMenuRedirect(() => CharEditorCompat.RandomizeBodyParts(window, EventModifiers.CapsLock));
                AnnounceAppearanceAndCharacterMutation("RimWorldAccess.CharEd.Actions.BodyPartsRandomized");
            }));
            WindowlessFloatMenuState.OpenTitled("RimWorldAccess.CharEd.Actions.RandomizeBodyParts".Translate(), options);
        }

        /// <summary>Recolor apparel rides <see cref="CharEditorCompat.RecolorApparelOnly"/> (MUTATION-C,
        /// see its remarks) rather than a Control-alone simulated modifier: in ARandomizeEquip Control
        /// alone still leaves `flag` true, bundling a full Redress+Reequip.</summary>
        private void OpenRandomizeEquipmentPicker(Pawn pawn)
        {
            var options = new List<FloatMenuOption>();
            options.Add(new FloatMenuOption("RimWorldAccess.CharEd.Actions.Everything".Translate(), delegate
            {
                CharEditorCompat.RunWithMenuRedirect(() => CharEditorCompat.RandomizeEquip(window, EventModifiers.None));
                AnnounceAppearanceMutation();
            }));
            options.Add(new FloatMenuOption("RimWorldAccess.CharEd.Actions.ApparelOnly".Translate(), delegate
            {
                CharEditorCompat.RunWithMenuRedirect(() => CharEditorCompat.RandomizeEquip(window, EventModifiers.Alt));
                AnnounceAppearanceMutation();
            }));
            options.Add(new FloatMenuOption("RimWorldAccess.CharEd.Actions.WeaponsOnly".Translate(), delegate
            {
                CharEditorCompat.RunWithMenuRedirect(() => CharEditorCompat.RandomizeEquip(window, EventModifiers.Shift));
                AnnounceAppearanceMutation();
            }));
            options.Add(new FloatMenuOption("RimWorldAccess.CharEd.Actions.RecolorApparel".Translate(), delegate
            {
                CharEditorCompat.RecolorApparelOnly(pawn);
                AnnounceAppearanceMutation();
            }));
            WindowlessFloatMenuState.OpenTitled("RimWorldAccess.CharEd.Actions.RandomizeEquipment".Translate(), options);
        }

        private void OpenQuickRestorePicker()
        {
            var options = new List<FloatMenuOption>();
            options.Add(new FloatMenuOption("RimWorldAccess.CharEd.Actions.ResurrectAndHeal".Translate(), delegate
            {
                CharEditorCompat.RunWithMenuRedirect(() => CharEditorCompat.QuickRestore(window, EventModifiers.None));
                AnnounceActionsMutation("RimWorldAccess.CharEd.Actions.RestoreApplied");
            }));
            options.Add(new FloatMenuOption("RimWorldAccess.CharEd.Actions.HealInjuriesRestoreLegs".Translate(), delegate
            {
                CharEditorCompat.RunWithMenuRedirect(() => CharEditorCompat.QuickRestore(window, EventModifiers.Alt));
                AnnounceActionsMutation("RimWorldAccess.CharEd.Actions.RestoreApplied");
            }));
            options.Add(new FloatMenuOption("RimWorldAccess.CharEd.Actions.Medicate".Translate(), delegate
            {
                CharEditorCompat.RunWithMenuRedirect(() => CharEditorCompat.QuickRestore(window, EventModifiers.Shift));
                AnnounceActionsMutation("RimWorldAccess.CharEd.Actions.RestoreApplied");
            }));
            options.Add(new FloatMenuOption("RimWorldAccess.CharEd.Actions.Anaesthetize".Translate(), delegate
            {
                CharEditorCompat.RunWithMenuRedirect(() => CharEditorCompat.QuickRestore(window, EventModifiers.Control));
                AnnounceActionsMutation("RimWorldAccess.CharEd.Actions.RestoreApplied");
            }));
            options.Add(new FloatMenuOption("RimWorldAccess.CharEd.Actions.DamageUntilDeath".Translate(), delegate
            {
                CharEditorCompat.RunWithMenuRedirect(() => CharEditorCompat.QuickRestore(window, EventModifiers.CapsLock));
                AnnounceActionsMutation("RimWorldAccess.CharEd.Actions.RestoreApplied");
            }));
            WindowlessFloatMenuState.OpenTitled("RimWorldAccess.CharEd.Actions.QuickRestore".Translate(), options);
        }

        // ---- Presets: slot pickers. ----

        private void OpenSaveToSlotPicker()
        {
            int total = CharEditorCompat.NumSlots;
            var options = new List<FloatMenuOption>();
            for (int i = 0; i < total; i++)
            {
                int slot = i;
                string label = CharEditorCompat.SlotDisplayLabel(window, slot, true);
                options.Add(new FloatMenuOption(label, delegate
                {
                    // MUTATION-C (see CharEditorCompat.SaveToSlot remarks): an empty slot saves
                    // directly; an occupied one rides the mod's own overwrite-confirm dialog. Only
                    // the direct save is announced -- an occupied slot's outcome is decided inside
                    // that dialog, not observable from this closure.
                    bool directSave = CharEditorCompat.GetSlot(slot).NullOrEmpty();
                    CharEditorCompat.SaveToSlot(window, slot);
                    RefreshModel();
                    if (directSave)
                    {
                        TolkHelper.SpeakData("RimWorldAccess.CharEd.Actions.SlotSaved".Translate(label).ToString());
                    }
                }));
            }
            WindowlessFloatMenuState.OpenTitled(SlotPickerTitle("RimWorldAccess.CharEd.Actions.SaveToSlot"), options);
        }

        /// <summary>Two-level picker: pick a slot, then Load or Show required mods -- promoting ALoadPawn's Alt-click peek to a discrete option.</summary>
        private void OpenLoadFromSlotPicker()
        {
            int total = CharEditorCompat.NumSlots;
            var options = new List<FloatMenuOption>();
            for (int i = 0; i < total; i++)
            {
                int slot = i;
                string label = CharEditorCompat.SlotDisplayLabel(window, slot, false);
                options.Add(new FloatMenuOption(label, delegate
                {
                    OpenLoadSlotActionPicker(slot, label);
                }));
            }
            WindowlessFloatMenuState.OpenTitled(SlotPickerTitle("RimWorldAccess.CharEd.Actions.LoadFromSlot"), options);
        }

        /// <summary>A slot picker's spoken title, carrying the occupancy summary the player needs before choosing.</summary>
        private string SlotPickerTitle(string titleKey)
        {
            int total = CharEditorCompat.NumSlots;
            int occupied = 0;
            for (int i = 0; i < total; i++)
            {
                if (!CharEditorCompat.GetSlot(i).NullOrEmpty())
                {
                    occupied++;
                }
            }
            return "RimWorldAccess.CharEd.Actions.SlotPickerTitle"
                .Translate(titleKey.Translate(), occupied, total).ToString();
        }

        private void OpenLoadSlotActionPicker(int slot, string slotLabel)
        {
            var options = new List<FloatMenuOption>();
            options.Add(new FloatMenuOption("RimWorldAccess.CharEd.Actions.LoadSlot".Translate(), delegate
            {
                Pawn loaded = CharEditorCompat.LoadFromSlot(slot);
                SpeakActionsOutcome(loaded != null
                    ? "RimWorldAccess.CharEd.Actions.SlotLoadedFrom".Translate(DescribePawn(loaded), slotLabel).ToString()
                    : "RimWorldAccess.CharEd.Actions.SlotLoadFailed".Translate().ToString());
            }));
            options.Add(new FloatMenuOption("RimWorldAccess.CharEd.Actions.ShowRequiredMods".Translate(), delegate
            {
                string mods = CharEditorCompat.PeekSlotMods(slot);
                TolkHelper.SpeakData(mods.NullOrEmpty()
                    ? "RimWorldAccess.CharEd.Actions.ShowRequiredMods".Translate().ToString()
                    : mods);
            }));
            WindowlessFloatMenuState.OpenTitled(slotLabel, options);
        }

        /// <summary>Occupied slots only.</summary>
        private void OpenClearSlotPicker()
        {
            int total = CharEditorCompat.NumSlots;
            var options = new List<FloatMenuOption>();
            for (int i = 0; i < total; i++)
            {
                if (CharEditorCompat.GetSlot(i).NullOrEmpty())
                {
                    continue;
                }
                int slot = i;
                string label = CharEditorCompat.SlotDisplayLabel(window, slot, true);
                options.Add(new FloatMenuOption(label, delegate
                {
                    ConfirmClearSlot(slot, label);
                }));
            }
            WindowlessFloatMenuState.OpenTitled(SlotPickerTitle("RimWorldAccess.CharEd.Actions.ClearSlot"), options,
                "RimWorldAccess.CharEd.Actions.NoOccupiedSlots");
        }

        /// <summary>Our own confirm; the mod's Ctrl-click clear carries none.</summary>
        private void ConfirmClearSlot(int slot, string slotLabel)
        {
            string text = "RimWorldAccess.CharEd.Actions.ConfirmClearSlot".Translate(slotLabel).ToString();
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(text, delegate
            {
                CharEditorCompat.ClearSlot(window, slot);
                RefreshModel();
                TolkHelper.SpeakData("RimWorldAccess.CharEd.Actions.SlotCleared".Translate().ToString());
            }, destructive: true));
        }

        // ---- Utilities: gender picker. ----

        /// <summary>The mod's own cycle order; it reaches Gender.None only for genderless races, which never see this hasGenders-gated row.</summary>
        private static readonly Gender[] GenderCycleOrder = { Gender.Male, Gender.Female };

        private void OpenGenderPicker(Pawn pawn, InspectionTreeItem item)
        {
            if (pawn == null)
            {
                return;
            }
            var options = new List<FloatMenuOption>();
            foreach (Gender gender in GenderCycleOrder)
            {
                Gender captured = gender;
                options.Add(new FloatMenuOption(GenderLabel(captured), delegate
                {
                    CharEditorCompat.SetGender(pawn, captured);
                    AnnounceAdjustedRow<CharRowKind>(item, DescribeCharRow);
                }));
            }
            WindowlessFloatMenuState.OpenTitled("RimWorldAccess.CharEd.Actions.Gender".Translate(), options);
        }

        // ---- Shared refresh/announce helpers. ----

        /// <summary>One outcome utterance per action. Refreshes the model first: Add/Delete/Clone/Randomize
        /// change the edited pawn's identity, which RefreshContent's pawn-reference check catches and
        /// rebuilds the tree for; unchanged identity makes the refresh a cheap no-op.</summary>
        private void AnnounceActionsMutation(string translationKey)
        {
            RefreshModel();
            TolkHelper.SpeakData(translationKey.Translate().ToString());
        }

        /// <summary>Same refresh, for a caller that already has a composed sentence rather than a bare translation key.</summary>
        private void SpeakActionsOutcome(string finalText)
        {
            RefreshModel();
            TolkHelper.SpeakData(finalText);
        }

        /// <summary>RandomizeBodyParts/RandomizeKeepRace change appearance values in place, which every Appearance row reads live -- no structural rebuild needed.</summary>
        private void AnnounceAppearanceAndCharacterMutation(string translationKey)
        {
            AnnounceActionsMutation(translationKey);
        }

        /// <summary>RandomizeEquip/RecolorApparel can change WHICH apparel/weapons are worn, so the Appearance section's Apparel/Weapons subsections must rebuild.</summary>
        private void AnnounceAppearanceMutation()
        {
            RefreshAppearanceSectionAfterCreationAction();
            AnnounceActionsMutation("RimWorldAccess.CharEd.Actions.EquipmentRandomized");
        }

        /// <summary>Rebuilds the Appearance section in place after a mutation that can add or remove
        /// worn apparel or equipped weapons. No-op when the section is collapsed or there is no pawn.</summary>
        private void RefreshAppearanceSectionAfterCreationAction()
        {
            InspectionTreeItem section = FindTopLevelSection(SectionKind.Appearance);
            if (section == null || !section.IsExpanded)
            {
                staleSections.Add(SectionKind.Appearance);
                return;
            }
            Pawn pawn = CharEditorCompat.CurrentPawn;
            if (pawn == null)
            {
                return;
            }

            var expandedSubs = new HashSet<string>();
            foreach (InspectionTreeItem child in section.Children)
            {
                if (child.IsExpanded && !string.IsNullOrEmpty(child.ExpandedLabel ?? child.Label))
                {
                    expandedSubs.Add(child.ExpandedLabel ?? child.Label);
                }
            }
            InspectionTreeItem current = CurrentTreeItem();
            int cursorIndex = current != null ? IndexOfVisible(Tree.Visible, current) : -1;

            section.Children.Clear();
            BuildAppearanceSection(section, pawn);
            foreach (InspectionTreeItem child in section.Children)
            {
                if (expandedSubs.Contains(child.ExpandedLabel ?? child.Label))
                {
                    child.IsExpanded = true;
                }
            }
            Tree.Reflatten();
            ResetBoundaryTracking();
            RefreshModel();
            // cursorIndex is -1 whenever the cursor was NOT in the tree region — every toolbar and
            // pawn-selection entry reaching this refresh is that case. Moving the tree selection then
            // would silently drag it to row 0 behind the player's back.
            if (cursorIndex < 0 || Tree.Visible.Count == 0)
            {
                return;
            }
            int target = Math.Min(cursorIndex, Tree.Visible.Count - 1);
            Tree.SetSelectedIndex(target);
            SyncRegionFromCurrentTree();
        }
    }
}
