using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// The caravan gear equip/swap menu: pawns with their current equipment in the relevant slot.
    ///
    /// Data/mutation backend only. The rows go to <see cref="WindowlessFloatMenuState"/>, whose
    /// visual twin draws them and whose scope owns navigation, typeahead and Escape. Each row's
    /// action calls back into <see cref="ExecuteSelected"/>, the single execution path; a pawn who
    /// cannot take the item gets a disabled option, so that method's CanEquip gate is reached only
    /// through a direct call.
    /// </summary>
    public static class GearEquipMenuState
    {
        /// <summary>
        /// True while OUR option list is the one the windowless menu is running. Identity, not a
        /// retained flag, so a menu closed by any other path can never leave this stale.
        /// </summary>
        public static bool IsActive =>
            menuOptions != null && ReferenceEquals(WindowlessFloatMenuState.CurrentOptions, menuOptions);

        private static Caravan currentCaravan = null;
        private static Thing itemToEquip = null;
        private static Pawn sourceOwner = null; // Pawn who currently has the item (null if from inventory)
        private static bool isWeapon = false;
        private static List<PawnEquipOption> options = new List<PawnEquipOption>();
        private static List<FloatMenuOption> menuOptions = null;

        /// <summary>A pawn option in the equip menu.</summary>
        internal class PawnEquipOption
        {
            public Pawn Pawn { get; set; }
            public bool CanEquip { get; set; }
            public string CantEquipReason { get; set; }
            public string CurrentEquipmentLabel { get; set; }
            public Thing CurrentEquipment { get; set; } // For swap functionality
            public bool IsUnequipOption { get; set; } // Special "Unequip to inventory" option

            public string GetDisplayLabel()
            {
                if (IsUnequipOption)
                {
                    return "RimWorldAccess.Gear.UnequipToInventory".Translate();
                }
                if (!CanEquip)
                {
                    return "RimWorldAccess.Gear.PawnCantEquip".Translate(Pawn.LabelShortCap, CantEquipReason);
                }
                return "RimWorldAccess.Gear.PawnWithCurrent".Translate(Pawn.LabelShortCap, CurrentEquipmentLabel);
            }
        }

        /// <summary>
        /// Opens the equip menu for a gear item. <paramref name="currentOwner"/> is null when the
        /// item comes from caravan inventory rather than off a pawn.
        /// </summary>
        public static void Open(Caravan caravan, Thing item, Pawn currentOwner)
        {
            if (caravan == null || item == null)
            {
                TolkHelper.Speak("RimWorldAccess.Gear.CannotOpenEquipMenu".Loc(), SpeechPriority.High);
                return;
            }

            currentCaravan = caravan;
            itemToEquip = item;
            sourceOwner = currentOwner;
            isWeapon = item.def.IsWeapon;

            BuildOptions();

            if (options.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Gear.NoPawnsAvailable".Loc(), SpeechPriority.High);
                return;
            }

            menuOptions = BuildMenuOptions();

            string itemName = item.LabelCap;
            string actionType = currentOwner != null
                ? "RimWorldAccess.Gear.ActionGive".Translate()
                : "RimWorldAccess.Gear.ActionEquip".Translate();
            // The menu speaks its title and first row as one utterance and stays silent after an
            // option runs: ExecuteSelected speaks the outcome.
            WindowlessFloatMenuState.Open(
                menuOptions,
                colonistOrders: false,
                announceSelection: false,
                titleText: "RimWorldAccess.Gear.OpenInstructions".Loc(actionType, itemName).ToString());
        }

        /// <summary>
        /// Wraps each built row as a float-menu option. A pawn who cannot take the item becomes a
        /// disabled option in vanilla's own null-action shape, so the menu rejects it as it would
        /// any unusable entry.
        /// </summary>
        private static List<FloatMenuOption> BuildMenuOptions()
        {
            var built = new List<FloatMenuOption>(options.Count);
            for (int i = 0; i < options.Count; i++)
            {
                PawnEquipOption option = options[i];
                string label = option.GetDisplayLabel();
                // Vanilla draws separate Colonists/Prisoners sections; this flat list carries the
                // distinction on the row label instead, since reordering would change typeahead
                // ranking and the Home/End targets.
                if (!option.IsUnequipOption && option.Pawn != null && option.Pawn.IsPrisoner)
                {
                    label = "RimWorldAccess.Gear.PrisonerLabelWrapper".Translate(label);
                }

                if (!option.CanEquip)
                {
                    built.Add(new FloatMenuOption(label, null));
                    continue;
                }
                int index = i;
                built.Add(new FloatMenuOption(label, delegate { ExecuteSelected(index); }));
            }
            return built;
        }

        /// <summary>The pawn options for equipping.</summary>
        private static void BuildOptions()
        {
            options.Clear();

            if (sourceOwner != null)
            {
                options.Add(new PawnEquipOption
                {
                    IsUnequipOption = true,
                    CanEquip = true
                });
            }

            var canEquipList = new List<PawnEquipOption>();
            var cantEquipList = new List<PawnEquipOption>();

            // Every pawn vanilla's Gear tab would draw a row for: IsColonist or IsPrisoner only.
            // IsColonist excludes an insecure slave, so an unsecured slave is never a target
            // vanilla's own UI can reach either.
            var pawns = currentCaravan.PawnsListForReading
                .Where(p => (p.IsColonist || p.IsPrisoner) && !p.Dead && !p.Downed)
                .OrderBy(p => p.LabelShortCap);

            foreach (Pawn pawn in pawns)
            {
                if (pawn == sourceOwner)
                    continue;

                var option = new PawnEquipOption { Pawn = pawn };

                if (!EquipmentUtility.CanEquip(itemToEquip, pawn, out string cantReason))
                {
                    option.CanEquip = false;
                    option.CantEquipReason = cantReason ?? (string)"RimWorldAccess.Gear.CantReason.Unknown".Translate();
                    cantEquipList.Add(option);
                    continue;
                }

                if (isWeapon)
                {
                    if (pawn.guest?.IsPrisoner == true)
                    {
                        option.CanEquip = false;
                        option.CantEquipReason = (string)"RimWorldAccess.Gear.CantReason.Prisoner".Translate();
                        cantEquipList.Add(option);
                        continue;
                    }
                    if (pawn.WorkTagIsDisabled(WorkTags.Violent))
                    {
                        option.CanEquip = false;
                        option.CantEquipReason = (string)"RimWorldAccess.Gear.CantReason.Pacifist".Translate();
                        cantEquipList.Add(option);
                        continue;
                    }
                    if (pawn.WorkTagIsDisabled(WorkTags.Shooting) && itemToEquip.def.IsRangedWeapon)
                    {
                        option.CanEquip = false;
                        option.CantEquipReason = (string)"RimWorldAccess.Gear.CantReason.CantShoot".Translate();
                        cantEquipList.Add(option);
                        continue;
                    }
                    if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
                    {
                        option.CanEquip = false;
                        option.CantEquipReason = (string)"RimWorldAccess.Gear.CantReason.CantManipulate".Translate();
                        cantEquipList.Add(option);
                        continue;
                    }
                }

                if (itemToEquip is Apparel apparel)
                {
                    if (!ApparelUtility.HasPartsToWear(pawn, apparel.def))
                    {
                        option.CanEquip = false;
                        option.CantEquipReason = (string)"RimWorldAccess.Gear.CantReason.MissingBodyParts".Translate();
                        cantEquipList.Add(option);
                        continue;
                    }
                    if (pawn.apparel?.WouldReplaceLockedApparel(apparel) == true)
                    {
                        option.CanEquip = false;
                        option.CantEquipReason = (string)"RimWorldAccess.Gear.CantReason.WouldReplaceLockedApparel".Translate();
                        cantEquipList.Add(option);
                        continue;
                    }
                }

                option.CanEquip = true;
                Thing currentEquip;
                string currentLabel;
                GetCurrentEquipmentInSlot(pawn, out currentEquip, out currentLabel);
                option.CurrentEquipment = currentEquip;
                option.CurrentEquipmentLabel = currentLabel;
                canEquipList.Add(option);
            }

            options.AddRange(canEquipList);
            options.AddRange(cantEquipList);
        }

        /// <summary>What the pawn currently has in the slot this item would use.</summary>
        private static void GetCurrentEquipmentInSlot(Pawn pawn, out Thing currentEquipment, out string label)
        {
            currentEquipment = null;
            label = "";

            if (isWeapon)
            {
                var weapon = pawn.equipment?.Primary;
                if (weapon != null)
                {
                    currentEquipment = weapon;
                    label = weapon.LabelCap;
                }
                else
                {
                    label = (string)"RimWorldAccess.Gear.SlotUnarmed".Translate();
                }
            }
            else if (itemToEquip is Apparel apparel)
            {
                var conflicting = GetConflictingApparel(pawn, apparel);
                if (conflicting != null && conflicting.Count > 0)
                {
                    currentEquipment = conflicting[0];
                    if (conflicting.Count == 1)
                    {
                        label = conflicting[0].LabelCap;
                    }
                    else
                    {
                        label = (string)"RimWorldAccess.Gear.SlotConflictingPlusMore".Translate(conflicting[0].LabelCap, conflicting.Count - 1);
                    }
                }
                else
                {
                    label = (string)"RimWorldAccess.Gear.SlotNone".Translate();
                }
            }
        }

        /// <summary>Apparel that would conflict with wearing the given apparel.</summary>
        private static List<Apparel> GetConflictingApparel(Pawn pawn, Apparel newApparel)
        {
            var conflicting = new List<Apparel>();
            if (pawn.apparel?.WornApparel == null)
                return conflicting;

            foreach (var worn in pawn.apparel.WornApparel)
            {
                if (!ApparelUtility.CanWearTogether(newApparel.def, worn.def, pawn.RaceProps.body))
                {
                    conflicting.Add(worn);
                }
            }
            return conflicting;
        }

        /// <summary>Closes the equip menu.</summary>
        public static void Close()
        {
            // Only ours to close: after an option runs the menu has closed itself, and something
            // else may own the windowless menu by now.
            if (IsActive)
            {
                WindowlessFloatMenuState.Close();
            }
            menuOptions = null;
            currentCaravan = null;
            itemToEquip = null;
            sourceOwner = null;
            options.Clear();
        }

        /// <summary>
        /// Executes the option at <paramref name="index"/>, the single execution path every menu
        /// row's action calls back into.
        /// </summary>
        // MUTATION-C: the weapon path below defers its mutation behind
        // EquipmentUtility.GetPersonaWeaponConfirmationText + Dialog_MessageBox, mirroring
        // WITab_Caravan_Gear.TryEquipDraggedItem (decompiled lines 483-497) — a real
        // confirmation may be pending when this method returns, so the close+refresh is
        // deferred to the shared `finish` callback rather than always running inline.
        public static void ExecuteSelected(int index)
        {
            if (options.Count == 0 || index < 0 || index >= options.Count)
                return;

            var option = options[index];

            if (!option.CanEquip)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("RimWorldAccess.Gear.CannotEquipReason".Loc(option.CantEquipReason));
                return;
            }

            string itemName = itemToEquip.LabelCap;

            if (option.IsUnequipOption)
            {
                try
                {
                    if (PerformUnequipToInventory())
                    {
                        TolkHelper.Speak("RimWorldAccess.Gear.UnequippedToInventory".Loc(itemName));
                        SoundDefOf.Click.PlayOneShotOnCamera();
                    }
                    else
                    {
                        SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[RimWorldAccess] Error equipping gear: {ex}");
                    TolkHelper.Speak("RimWorldAccess.Gear.FailedToEquip".Loc());
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                }

                Close();
                CaravanInspectState.RefreshTree();
                return;
            }

            bool isSwap = option.CurrentEquipment != null;
            string targetName = option.Pawn.LabelShortCap;
            Thing displacedItem = option.CurrentEquipment;

            // Runs once the outcome is known: immediately for apparel or an unconfirmed weapon,
            // else from the persona-weapon dialog's Yes callback. Announces only genuine success;
            // PerformEquip already speaks each specific rejection.
            void Finish(bool success)
            {
                try
                {
                    if (success)
                    {
                        if (isSwap)
                        {
                            string swappedItem = displacedItem?.LabelCap ?? (string)"RimWorldAccess.Gear.SwappedItemFallback".Translate();
                            if (sourceOwner != null)
                            {
                                TolkHelper.Speak("RimWorldAccess.Gear.SwappedTwoOwners".Loc(targetName, itemName, sourceOwner.LabelShortCap, swappedItem));
                            }
                            else
                            {
                                TolkHelper.Speak("RimWorldAccess.Gear.SwappedToInventory".Loc(targetName, itemName, swappedItem));
                            }
                        }
                        else
                        {
                            TolkHelper.Speak("RimWorldAccess.Gear.EquippedTo".Loc(itemName, targetName));
                        }

                        SoundDefOf.Click.PlayOneShotOnCamera();
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[RimWorldAccess] Error equipping gear: {ex}");
                    TolkHelper.Speak("RimWorldAccess.Gear.FailedToEquip".Loc());
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                }

                Close();
                CaravanInspectState.RefreshTree();
            }

            try
            {
                PerformEquip(option.Pawn, displacedItem, Finish);
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorldAccess] Error equipping gear: {ex}");
                TolkHelper.Speak("RimWorldAccess.Gear.FailedToEquip".Loc());
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                Close();
                CaravanInspectState.RefreshTree();
            }
        }

        /// <summary>
        /// Unequips the item from its owner into caravan inventory. False means it did not
        /// happen, and callers must not announce success — the rejection already spoke its reason.
        /// </summary>
        // MUTATION-C: mirrors WITab_Caravan_Gear.MoveDraggedItemToInventory's locked-apparel
        // gate (decompiled lines 384-389) -- vanilla refuses with MessageCantUnequipLockedApparel
        // before ever moving a locked apparel item to inventory. No reusable A/B vehicle exists
        // for this mod-invented non-drag unequip flow, so the check is hand-copied verbatim,
        // matching the same gate PerformApparelEquip already applies below.
        private static bool PerformUnequipToInventory()
        {
            if (sourceOwner == null)
                return false;

            if (isWeapon)
            {
                ThingWithComps weapon = itemToEquip as ThingWithComps;
                if (weapon != null && sourceOwner.equipment?.Primary == weapon)
                {
                    sourceOwner.equipment.Remove(weapon);
                    Pawn carrier = CaravanInventoryUtility.FindPawnToMoveInventoryTo(weapon, currentCaravan.PawnsListForReading, null);
                    if (carrier != null)
                    {
                        carrier.inventory.innerContainer.TryAdd(weapon);
                    }
                }
            }
            else if (itemToEquip is Apparel apparel)
            {
                if (sourceOwner.apparel?.WornApparel?.Contains(apparel) == true)
                {
                    if (sourceOwner.apparel.IsLocked(apparel))
                    {
                        TolkHelper.Speak("RimWorldAccess.Gear.CannotRemoveLockedApparel".Loc());
                        return false;
                    }
                    sourceOwner.apparel.Remove(apparel);
                    Pawn carrier = CaravanInventoryUtility.FindPawnToMoveInventoryTo(apparel, currentCaravan.PawnsListForReading, null);
                    if (carrier != null)
                    {
                        carrier.inventory.innerContainer.TryAdd(apparel);
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Performs the equip/swap, calling <paramref name="onDone"/> once the outcome is known:
        /// synchronously for apparel, and for weapons only after any persona/bladelink
        /// confirmation resolves.
        /// </summary>
        private static void PerformEquip(Pawn targetPawn, Thing targetCurrentEquipment, Action<bool> onDone)
        {
            if (isWeapon)
            {
                PerformWeaponEquip(targetPawn, onDone);
            }
            else if (itemToEquip is Apparel apparel)
            {
                onDone(PerformApparelEquip(targetPawn, apparel));
            }
            else
            {
                onDone(false);
            }
        }

        /// <summary>Equips a weapon to the target pawn, gated by vanilla's persona/bladelink confirmation.</summary>
        // MUTATION-C: mirrors WITab_Caravan_Gear.TryEquipDraggedItem's weapon branch
        // (decompiled lines 483-522) verbatim. No reusable A/B vehicle exists — the vanilla
        // method is private and coupled to the WITab's own drag-drop fields — so the
        // persona-weapon confirmation gate (rides EquipmentUtility.GetPersonaWeaponConfirmationText
        // + Dialog_MessageBox, Category A once opened) and the displacement handling are
        // hand-copied here. The whole mutation is gated behind the confirmation, matching
        // vanilla: nothing about the source pawn or the target's existing equipment is touched
        // until the player accepts.
        private static void PerformWeaponEquip(Pawn targetPawn, Action<bool> onDone)
        {
            ThingWithComps weaponToEquip = itemToEquip as ThingWithComps;
            if (weaponToEquip == null)
            {
                onDone(false);
                return;
            }

            string personaWeaponConfirmationText = EquipmentUtility.GetPersonaWeaponConfirmationText(weaponToEquip, targetPawn);
            if (!personaWeaponConfirmationText.NullOrEmpty())
            {
                Find.WindowStack.Add(new Dialog_MessageBox(personaWeaponConfirmationText, "Yes".Translate(), delegate
                {
                    DoWeaponEquip(targetPawn, weaponToEquip);
                    onDone(true);
                }, "No".Translate()));
                return;
            }

            DoWeaponEquip(targetPawn, weaponToEquip);
            onDone(true);
        }

        /// <summary>
        /// The weapon-equip mutation itself, run once any persona/bladelink confirmation has been
        /// accepted.
        /// </summary>
        private static void DoWeaponEquip(Pawn targetPawn, ThingWithComps weaponToEquip)
        {
            if (sourceOwner != null && sourceOwner.equipment?.Primary == weaponToEquip)
            {
                sourceOwner.equipment.Remove(weaponToEquip);
            }
            else
            {
                foreach (Pawn p in currentCaravan.PawnsListForReading)
                {
                    if (p.inventory?.innerContainer?.Contains(weaponToEquip) == true)
                    {
                        p.inventory.innerContainer.Remove(weaponToEquip);
                        break;
                    }
                }
            }

            // Displace every equipped item, not just Primary, mirroring vanilla's AddEquipment
            // loop over a snapshot of AllEquipmentListForReading: a modded or mechanoid pawn can
            // carry more than one weapon.
            if (targetPawn.equipment != null)
            {
                var existingEquipment = new List<ThingWithComps>(targetPawn.equipment.AllEquipmentListForReading);
                bool grantedSwapToSource = false;
                foreach (var displaced in existingEquipment)
                {
                    targetPawn.equipment.Remove(displaced);

                    // The swap affordance applies to the first displaced item only. Vanilla's
                    // drag-drop flow has no source-pawn concept — every displaced item goes to a
                    // carrier or is destroyed — so this swap is an addition for the common
                    // single-weapon case.
                    if (sourceOwner != null && !grantedSwapToSource)
                    {
                        sourceOwner.equipment.AddEquipment(displaced);
                        grantedSwapToSource = true;
                        continue;
                    }

                    Pawn carrier = CaravanInventoryUtility.FindPawnToMoveInventoryTo(displaced, currentCaravan.PawnsListForReading, null);
                    if (carrier != null)
                    {
                        carrier.inventory.innerContainer.TryAdd(displaced);
                    }
                    else
                    {
                        Log.Warning("[RimWorldAccess] Could not find any pawn to move " + displaced + " to.");
                        displaced.Destroy();
                    }
                }
            }

            targetPawn.equipment.AddEquipment(weaponToEquip);
        }

        /// <summary>
        /// Equips apparel to the target pawn. False means it did not happen, and callers must not
        /// announce success — the rejection already spoke its reason.
        /// </summary>
        // MUTATION-C: mirrors WITab_Caravan_Gear.TryEquipDraggedItem's apparel branch
        // (decompiled lines 438-482). Vanilla has no confirmation gate for apparel (only
        // weapons get one), but no reusable A/B vehicle exists for this mod-invented
        // non-drag equip flow either, so the locked-apparel checks and displacement
        // handling are hand-copied verbatim.
        private static bool PerformApparelEquip(Pawn targetPawn, Apparel apparel)
        {
            Apparel apparelToEquip = apparel;

            if (sourceOwner != null && sourceOwner.apparel?.WornApparel?.Contains(apparel) == true)
            {
                if (sourceOwner.apparel.IsLocked(apparel))
                {
                    TolkHelper.Speak("RimWorldAccess.Gear.CannotRemoveLockedApparel".Loc());
                    return false;
                }
                sourceOwner.apparel.Remove(apparel);
            }
            else
            {
                foreach (Pawn p in currentCaravan.PawnsListForReading)
                {
                    if (p.inventory?.innerContainer?.Contains(apparel) == true)
                    {
                        // SplitOff(1) handles stacked items, matching WITab_Caravan_Gear.
                        apparelToEquip = (Apparel)apparel.SplitOff(1);
                        break;
                    }
                }
            }

            var conflicting = GetConflictingApparel(targetPawn, apparelToEquip);
            foreach (var worn in conflicting)
            {
                if (targetPawn.apparel.IsLocked(worn))
                {
                    TolkHelper.Speak("RimWorldAccess.Gear.CannotRemoveLockedFromPawn".Loc(worn.LabelCap, targetPawn.LabelShortCap));
                    if (apparelToEquip != apparel)
                    {
                        apparel.TryAbsorbStack(apparelToEquip, respectStackLimit: true);
                    }
                    return false;
                }
                targetPawn.apparel.Remove(worn);

                if (sourceOwner != null && ApparelUtility.HasPartsToWear(sourceOwner, worn.def))
                {
                    bool sourceCanWear = true;
                    if (sourceOwner.apparel != null)
                    {
                        foreach (var sourceWorn in sourceOwner.apparel.WornApparel)
                        {
                            if (!ApparelUtility.CanWearTogether(worn.def, sourceWorn.def, sourceOwner.RaceProps.body))
                            {
                                sourceCanWear = false;
                                break;
                            }
                        }
                    }

                    if (sourceCanWear)
                    {
                        sourceOwner.apparel.Wear(worn, dropReplacedApparel: false);
                        continue;
                    }
                }

                // Otherwise move to inventory, mirroring TryEquipDraggedItem's displacement loop:
                // destroy the item when no caravan pawn can carry it rather than orphaning it.
                Pawn carrier = CaravanInventoryUtility.FindPawnToMoveInventoryTo(worn, currentCaravan.PawnsListForReading, null);
                if (carrier != null)
                {
                    carrier.inventory.innerContainer.TryAdd(worn);
                }
                else
                {
                    Log.Warning("[RimWorldAccess] Could not find any pawn to move " + worn + " to.");
                    worn.Destroy();
                }
            }

            targetPawn.apparel.Wear(apparelToEquip, dropReplacedApparel: false);

            if (targetPawn.outfits != null)
            {
                targetPawn.outfits.forcedHandler.SetForced(apparel, forced: true);
            }

            return true;
        }

    }
}
