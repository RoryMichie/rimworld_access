using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    internal sealed partial class CharacterEditorScope
    {
        // Appearance section: Hair, Head, Torso, Apparel, Weapons. Every def-list picker rides
        // the mod's OWN label-click handler, which opens a real vanilla FloatMenu already caught
        // by WindowlessFloatMenuState/DialogInterceptionPatch, so no candidate list or apply
        // callback is reimplemented here. Hair/Skin/Eye color rows push CharEditorColorScope
        // directly. The mod's handlers refresh the portrait through their own UpdateGraphics()
        // chain, so nothing here calls it.

        private void BuildAppearanceSection(InspectionTreeItem section, Pawn pawn)
        {
            if (pawn.story != null)
            {
                BuildHairSubsection(section, pawn);
                BuildHeadSubsection(section, pawn);
                BuildTorsoSubsection(section, pawn);
            }
            if (pawn.apparel != null && !pawn.apparel.WornApparel.NullOrEmpty())
            {
                BuildApparelSubsection(section, pawn);
            }
            if (pawn.equipment != null && !pawn.equipment.AllEquipmentListForReading.NullOrEmpty())
            {
                BuildWeaponsSubsection(section, pawn);
            }
            BuildPreviewSubsection(section);
        }

        /// <summary>
        /// The three portrait-view controls from the mod's utility icon strip: show clothes, show
        /// hats, rotate. They change the DRAWN PORTRAIT rather than the pawn, so they belong to the
        /// appearance section rather than the mutating-actions toolbar.
        /// </summary>
        private void BuildPreviewSubsection(InspectionTreeItem section)
        {
            InspectionTreeItem sub = AddSubsection(section, "RimWorldAccess.CharEd.Appearance.PreviewSection".Translate());
            AddRow(sub, ActionRowKind.UtilityNude, "RimWorldAccess.CharEd.Actions.Nude".Translate());
            AddRow(sub, ActionRowKind.UtilityHats, "RimWorldAccess.CharEd.Actions.Hats".Translate());
            AddRow(sub, ActionRowKind.UtilityRotate, "RimWorldAccess.CharEd.Actions.RotatePortrait".Translate());
        }

        // ---- Hair. ----

        private void BuildHairSubsection(InspectionTreeItem section, Pawn pawn)
        {
            InspectionTreeItem sub = AddSubsection(section, "RimWorldAccess.CharEd.Appearance.HairSection".Translate());
            AddRow(sub, AppearanceRowKind.HairStyle, "RimWorldAccess.CharEd.Appearance.HairSection".Translate());
            if (pawn.style != null)
            {
                AddRow(sub, AppearanceRowKind.Beard, "Beard".Translate());
            }
            // The "A" suffix only means something when a channel-B row exists (Gradient Hair); a
            // lone row speaks plainly as "Hair color".
            AddRow(sub, AppearanceRowKind.HairColorA, CharEditorCompat.GradientHairActive
                ? "RimWorldAccess.CharEd.Appearance.HairColorA".Translate()
                : "RimWorldAccess.CharEd.Appearance.HairColor".Translate());
            if (CharEditorCompat.GradientHairActive)
            {
                AddRow(sub, AppearanceRowKind.HairColorB, "RimWorldAccess.CharEd.Appearance.HairColorB".Translate());
                AddRow(sub, AppearanceRowKind.GradientMask, "RimWorldAccess.CharEd.Appearance.GradientMask".Translate());
            }
        }

        // ---- Head. ----

        private void BuildHeadSubsection(InspectionTreeItem section, Pawn pawn)
        {
            InspectionTreeItem sub = AddSubsection(section, "RimWorldAccess.CharEd.Appearance.HeadSection".Translate());
            if (pawn.style != null)
            {
                AddRow(sub, AppearanceRowKind.FaceTattoo, "RimWorldAccess.CharEd.Appearance.FaceTattoo".Translate());
            }
            if (CharEditorCompat.HasFacialHeadController(pawn))
            {
                AddRow(sub, AppearanceRowKind.FacialHead, "RimWorldAccess.CharEd.Appearance.FacialHead".Translate());
                AddRow(sub, AppearanceRowKind.FacialEye, "RimWorldAccess.CharEd.Appearance.FacialEye".Translate());
                AddRow(sub, AppearanceRowKind.EyeColor1, "RimWorldAccess.CharEd.Appearance.EyeColor1".Translate());
                AddRow(sub, AppearanceRowKind.EyeColor2, "RimWorldAccess.CharEd.Appearance.EyeColor2".Translate());
                AddRow(sub, AppearanceRowKind.FacialLid, "RimWorldAccess.CharEd.Appearance.FacialLid".Translate());
                AddRow(sub, AppearanceRowKind.FacialBrow, "RimWorldAccess.CharEd.Appearance.FacialBrow".Translate());
                AddRow(sub, AppearanceRowKind.FacialMouth, "RimWorldAccess.CharEd.Appearance.FacialMouth".Translate());
                AddRow(sub, AppearanceRowKind.FacialSkin, "RimWorldAccess.CharEd.Appearance.FacialSkin".Translate());
            }
            else
            {
                AddRow(sub, AppearanceRowKind.HeadVanilla, BodyPartDefOf.Head.LabelCap.ToString().CapitalizeFirst());
            }
            // Alien-race-only, matching the mod's isAlien gate on the "bheadaddon" icon, and
            // gated on its Ready flag so a rename in DialogChangeHeadAddons cannot take down the
            // rest of the Head subsection.
            if (CharEditorCompat.IsAlienRace(window) && CharEditorHeadAddonsCompat.Ready)
            {
                AddRow(sub, AppearanceRowKind.HeadAddons, "RimWorldAccess.CharEd.Appearance.HeadAddons".Translate());
            }
        }

        /// <summary>The key FacialCurrentDefName/OpenFacialPicker/RandomizeFacial share for one of the six controllers.</summary>
        private static string FacialKey(AppearanceRowKind kind)
        {
            switch (kind)
            {
                case AppearanceRowKind.FacialHead: return "Head";
                case AppearanceRowKind.FacialEye: return "Eye";
                case AppearanceRowKind.FacialLid: return "Lid";
                case AppearanceRowKind.FacialBrow: return "Brow";
                case AppearanceRowKind.FacialMouth: return "Mouth";
                default: return "Skin";
            }
        }

        // ---- Torso. ----

        private void BuildTorsoSubsection(InspectionTreeItem section, Pawn pawn)
        {
            InspectionTreeItem sub = AddSubsection(section, "RimWorldAccess.CharEd.Appearance.TorsoSection".Translate());
            AddRow(sub, AppearanceRowKind.Body, BodyPartDefOf.Torso.LabelCap.ToString().CapitalizeFirst());
            if (CharEditorCompat.IsAlienRace(window))
            {
                AddRow(sub, AppearanceRowKind.SkinColorA, "RimWorldAccess.CharEd.Appearance.SkinColorA".Translate());
                AddRow(sub, AppearanceRowKind.SkinColorB, "RimWorldAccess.CharEd.Appearance.SkinColorB".Translate());
            }
            else
            {
                AddRow(sub, AppearanceRowKind.SkinColorSingle, "RimWorldAccess.CharEd.Appearance.SkinColor".Translate());
            }
            if (pawn.style != null)
            {
                AddRow(sub, AppearanceRowKind.BodyTattoo, "RimWorldAccess.CharEd.Appearance.BodyTattoo".Translate());
            }
        }

        // ---- Apparel. ----

        private void BuildApparelSubsection(InspectionTreeItem section, Pawn pawn)
        {
            InspectionTreeItem sub = AddSubsection(section, "Apparel".Translate());
            PopulateApparelRows(sub, pawn);
        }

        private void PopulateApparelRows(InspectionTreeItem sub, Pawn pawn)
        {
            foreach (Apparel apparel in CharEditorCompat.OrderedWornApparel(window, pawn))
            {
                AddRow(sub, AppearanceRowKind.ApparelEntry, apparel.def.label.CapitalizeFirst(), apparel);
            }
        }

        /// <summary>
        /// Rebuilds the Apparel subsection in place after a cycle or randomize replaces the worn
        /// item, since the old Apparel INSTANCE is gone. <paramref name="announce"/> is false only
        /// for the return-from-Replace-dialog call, which must stay silent when the Replace was
        /// cancelled.
        /// </summary>
        private void RebuildApparelAfterMutation(InspectionTreeItem anyApparelRowItem, bool announce = true)
        {
            InspectionTreeItem sub = anyApparelRowItem?.Parent;
            if (sub == null)
            {
                return;
            }
            Pawn pawn = CharEditorCompat.CurrentPawn;
            int cursorIndex = IndexOfVisible(Tree.Visible, anyApparelRowItem);
            sub.Children.Clear();
            PopulateApparelRows(sub, pawn);
            Tree.Reflatten();
            ResetBoundaryTracking();
            RefreshModel();
            if (Tree.Visible.Count == 0)
            {
                return;
            }
            int clamped = Math.Min(Math.Max(cursorIndex, 0), Tree.Visible.Count - 1);
            Tree.SetSelectedIndex(clamped);
            SyncRegionFromCurrentTree();
            if (announce)
            {
                AnnounceCurrentItem();
            }
        }

        // ---- Weapons. ----

        private void BuildWeaponsSubsection(InspectionTreeItem section, Pawn pawn)
        {
            InspectionTreeItem sub = AddSubsection(section, "RimWorldAccess.CharEd.Appearance.WeaponsSection".Translate());
            PopulateWeaponRows(sub, pawn);
        }

        private static void PopulateWeaponRows(InspectionTreeItem sub, Pawn pawn)
        {
            foreach (ThingWithComps weapon in pawn.equipment.AllEquipmentListForReading)
            {
                AddRow(sub, AppearanceRowKind.WeaponEntry, weapon.def.label.CapitalizeFirst(), weapon);
            }
        }

        /// <summary>Rebuilds the Weapons subsection in place after a cycle or randomize re-equips a different weapon INSTANCE; WeaponTool.Reequip destroys the old one.</summary>
        /// <summary>See RebuildApparelAfterMutation's remarks on <paramref name="announce"/>.</summary>
        private void RebuildWeaponsAfterMutation(InspectionTreeItem anyWeaponRowItem, bool announce = true)
        {
            InspectionTreeItem sub = anyWeaponRowItem?.Parent;
            if (sub == null)
            {
                return;
            }
            Pawn pawn = CharEditorCompat.CurrentPawn;
            int cursorIndex = IndexOfVisible(Tree.Visible, anyWeaponRowItem);
            sub.Children.Clear();
            PopulateWeaponRows(sub, pawn);
            Tree.Reflatten();
            ResetBoundaryTracking();
            RefreshModel();
            if (Tree.Visible.Count == 0)
            {
                return;
            }
            int clamped = Math.Min(Math.Max(cursorIndex, 0), Tree.Visible.Count - 1);
            Tree.SetSelectedIndex(clamped);
            SyncRegionFromCurrentTree();
            if (announce)
            {
                AnnounceCurrentItem();
            }
        }

        /// <summary>
        /// The Objects browser's "Replace..." flow (DialogObjects) is a real child dialog pushed
        /// above this scope. A confirmed replace swaps the worn Apparel/equipped ThingWithComps
        /// INSTANCE the cursor's row still points at, which RefreshCharacterSectionAfterChildDialog
        /// does not cover. Reuses the same in-place rebuild the cycle/randomize drill-ins call,
        /// keyed off whichever Apparel/Weapon row the cursor sits on when focus returns; harmless
        /// even when Replace was cancelled.
        /// </summary>
        private void RefreshApparelOrWeaponRowAfterChildDialog()
        {
            InspectionTreeItem current = CurrentTreeItem();
            if (!(current?.Data is RegionRow<AppearanceRowKind> row))
            {
                return;
            }
            if (row.Kind == AppearanceRowKind.ApparelEntry)
            {
                RebuildApparelAfterMutation(current, announce: false);
            }
            else if (row.Kind == AppearanceRowKind.WeaponEntry)
            {
                RebuildWeaponsAfterMutation(current, announce: false);
            }
        }


        // ---- Shared Appearance-row dispatch (Describe/Activate/Adjust). ----

        private void DescribeAppearanceRow(RegionRow<AppearanceRowKind> row, ElementDescription d)
        {
            Pawn pawn = CharEditorCompat.CurrentPawn;
            switch (row.Kind)
            {
                case AppearanceRowKind.HairStyle:
                    d.Label = "RimWorldAccess.CharEd.Appearance.HairSection".Translate();
                    d.Role = ElementRole.ComboBox;
                    d.Value = pawn?.story?.hairDef != null ? pawn.story.hairDef.LabelCap.ToString() : "None".Translate().ToString();
                    d.Extras = StyleRowDescription(pawn?.story?.hairDef);
                    break;
                case AppearanceRowKind.Beard:
                    d.Label = "Beard".Translate();
                    d.Role = ElementRole.ComboBox;
                    d.Value = pawn?.style?.beardDef != null ? pawn.style.beardDef.LabelCap.ToString() : "None".Translate().ToString();
                    d.Extras = StyleRowDescription(pawn?.style?.beardDef);
                    break;
                case AppearanceRowKind.HairColorA:
                    // Plain "Hair color" unless a channel-B row exists (Gradient Hair) — see BuildHairSubsection.
                    d.Label = CharEditorCompat.GradientHairActive
                        ? "RimWorldAccess.CharEd.Appearance.HairColorA".Translate()
                        : "RimWorldAccess.CharEd.Appearance.HairColor".Translate();
                    d.Role = ElementRole.ComboBox;
                    d.Value = ColorNameHelper.NameForColor(CharEditorCompat.GetHairColor(pawn, true));
                    break;
                case AppearanceRowKind.HairColorB:
                    d.Label = "RimWorldAccess.CharEd.Appearance.HairColorB".Translate();
                    d.Role = ElementRole.ComboBox;
                    d.Value = ColorNameHelper.NameForColor(CharEditorCompat.GetHairColor(pawn, false));
                    break;
                case AppearanceRowKind.GradientMask:
                    d.Label = "RimWorldAccess.CharEd.Appearance.GradientMask".Translate();
                    d.Role = ElementRole.ComboBox;
                    d.Value = CharEditorCompat.GradientMaskLabel(pawn);
                    break;
                case AppearanceRowKind.FaceTattoo:
                    d.Label = "RimWorldAccess.CharEd.Appearance.FaceTattoo".Translate();
                    d.Role = ElementRole.ComboBox;
                    d.Value = pawn?.style?.FaceTattoo != null ? pawn.style.FaceTattoo.LabelCap.ToString() : "None".Translate().ToString();
                    d.Extras = StyleRowDescription(pawn?.style?.FaceTattoo);
                    break;
                case AppearanceRowKind.HeadVanilla:
                    d.Label = BodyPartDefOf.Head.LabelCap.ToString().CapitalizeFirst();
                    d.Role = ElementRole.ComboBox;
                    d.Value = pawn?.story?.headType != null
                        ? (pawn.story.headType.label.NullOrEmpty() ? pawn.story.headType.defName : pawn.story.headType.label)
                        : "None".Translate().ToString();
                    break;
                case AppearanceRowKind.FacialHead:
                case AppearanceRowKind.FacialEye:
                case AppearanceRowKind.FacialLid:
                case AppearanceRowKind.FacialBrow:
                case AppearanceRowKind.FacialMouth:
                case AppearanceRowKind.FacialSkin:
                    d.Label = FacialRowLabel(row.Kind);
                    d.Role = ElementRole.ComboBox;
                    d.Value = CharEditorCompat.FacialCurrentDefName(pawn, FacialKey(row.Kind));
                    break;
                case AppearanceRowKind.EyeColor1:
                    d.Label = "RimWorldAccess.CharEd.Appearance.EyeColor1".Translate();
                    d.Role = ElementRole.ComboBox;
                    d.Value = ColorNameHelper.NameForColor(CharEditorCompat.GetEyeColor(pawn, true));
                    break;
                case AppearanceRowKind.EyeColor2:
                    d.Label = "RimWorldAccess.CharEd.Appearance.EyeColor2".Translate();
                    d.Role = ElementRole.ComboBox;
                    d.Value = ColorNameHelper.NameForColor(CharEditorCompat.GetEyeColor(pawn, false));
                    break;
                case AppearanceRowKind.Body:
                    d.Label = BodyPartDefOf.Torso.LabelCap.ToString().CapitalizeFirst();
                    d.Role = ElementRole.ComboBox;
                    // Mirrors the mod's own FloatMenu label exactly. BodyTypeDef has no friendly
                    // label field, only defName, which the mod runs through the translation system
                    // — so with no injection for that key it reads as a bare def name here too.
                    d.Value = pawn?.story?.bodyType != null ? pawn.story.bodyType.defName.Translate().ToString() : "None".Translate().ToString();
                    break;
                case AppearanceRowKind.SkinColorSingle:
                    d.Label = "RimWorldAccess.CharEd.Appearance.SkinColor".Translate();
                    d.Role = ElementRole.ComboBox;
                    d.Value = ColorNameHelper.NameForColor(CharEditorCompat.GetSkinColor(pawn, true));
                    break;
                case AppearanceRowKind.SkinColorA:
                    d.Label = "RimWorldAccess.CharEd.Appearance.SkinColorA".Translate();
                    d.Role = ElementRole.ComboBox;
                    d.Value = ColorNameHelper.NameForColor(CharEditorCompat.GetSkinColor(pawn, true));
                    break;
                case AppearanceRowKind.SkinColorB:
                    d.Label = "RimWorldAccess.CharEd.Appearance.SkinColorB".Translate();
                    d.Role = ElementRole.ComboBox;
                    d.Value = ColorNameHelper.NameForColor(CharEditorCompat.GetSkinColor(pawn, false));
                    break;
                case AppearanceRowKind.BodyTattoo:
                    d.Label = "RimWorldAccess.CharEd.Appearance.BodyTattoo".Translate();
                    d.Role = ElementRole.ComboBox;
                    d.Value = pawn?.style?.BodyTattoo != null ? pawn.style.BodyTattoo.LabelCap.ToString() : "None".Translate().ToString();
                    d.Extras = StyleRowDescription(pawn?.style?.BodyTattoo);
                    break;
                case AppearanceRowKind.ApparelEntry:
                {
                    var apparel = row.Payload as Apparel;
                    d.Label = apparel?.def?.label.CapitalizeFirst() ?? "";
                    d.Role = ElementRole.ComboBox;
                    d.Extras = apparel != null ? apparel.GetTooltip().text : null;
                    break;
                }
                case AppearanceRowKind.WeaponEntry:
                {
                    var weapon = row.Payload as ThingWithComps;
                    d.Label = weapon?.def?.label.CapitalizeFirst() ?? "";
                    d.Role = ElementRole.ComboBox;
                    d.Extras = weapon != null ? weapon.GetTooltip().text : null;
                    break;
                }
                case AppearanceRowKind.HeadAddons:
                    d.Label = "RimWorldAccess.CharEd.Appearance.HeadAddons".Translate();
                    d.Role = ElementRole.Button;
                    break;
            }
        }

        /// <summary>The style's own description, or null so the row speaks nothing extra.</summary>
        private static string StyleRowDescription(StyleItemDef def)
        {
            string description = def?.description;
            return description.NullOrEmpty() ? null : description;
        }

        private static string FacialRowLabel(AppearanceRowKind kind)
        {
            switch (kind)
            {
                case AppearanceRowKind.FacialHead: return "RimWorldAccess.CharEd.Appearance.FacialHead".Translate();
                case AppearanceRowKind.FacialEye: return "RimWorldAccess.CharEd.Appearance.FacialEye".Translate();
                case AppearanceRowKind.FacialLid: return "RimWorldAccess.CharEd.Appearance.FacialLid".Translate();
                case AppearanceRowKind.FacialBrow: return "RimWorldAccess.CharEd.Appearance.FacialBrow".Translate();
                case AppearanceRowKind.FacialMouth: return "RimWorldAccess.CharEd.Appearance.FacialMouth".Translate();
                default: return "RimWorldAccess.CharEd.Appearance.FacialSkin".Translate();
            }
        }

        /// <summary>Runs a mod handler that opens its OWN vanilla FloatMenu (vehicle A) under the menu-redirect flag so WindowlessFloatMenuState/DialogInterceptionPatch catch it -- the menu announces itself, so nothing further is spoken here.</summary>
        private void OpenModFloatMenu(Action invoke)
        {
            CharEditorCompat.RunWithMenuRedirect(invoke);
        }

        private void ActivateAppearanceRow(RegionRow<AppearanceRowKind> row, InspectionTreeItem item)
        {
            switch (row.Kind)
            {
                case AppearanceRowKind.HairStyle:
                    OpenModFloatMenu(() => CharEditorCompat.OpenHairPicker(window));
                    break;
                case AppearanceRowKind.Beard:
                    OpenModFloatMenu(() => CharEditorCompat.OpenBeardPicker(window));
                    break;
                case AppearanceRowKind.HairColorA:
                    CharEditorColorPickerCompat.Open(CharEditorColorPickerCompat.Mode.HairColor, true);
                    break;
                case AppearanceRowKind.HairColorB:
                    CharEditorColorPickerCompat.Open(CharEditorColorPickerCompat.Mode.HairColor, false);
                    break;
                case AppearanceRowKind.GradientMask:
                    OpenModFloatMenu(() => CharEditorCompat.OpenGradientPicker(window));
                    break;
                case AppearanceRowKind.FaceTattoo:
                    OpenModFloatMenu(() => CharEditorCompat.OpenFaceTattooPicker(window));
                    break;
                case AppearanceRowKind.HeadVanilla:
                    OpenModFloatMenu(() => CharEditorCompat.OpenHeadPicker(window));
                    break;
                case AppearanceRowKind.FacialHead:
                case AppearanceRowKind.FacialEye:
                case AppearanceRowKind.FacialLid:
                case AppearanceRowKind.FacialBrow:
                case AppearanceRowKind.FacialMouth:
                case AppearanceRowKind.FacialSkin:
                    OpenModFloatMenu(() => CharEditorCompat.OpenFacialPicker(window, FacialKey(row.Kind)));
                    break;
                case AppearanceRowKind.EyeColor1:
                    CharEditorColorPickerCompat.Open(CharEditorColorPickerCompat.Mode.EyeColor, true);
                    break;
                case AppearanceRowKind.EyeColor2:
                    CharEditorColorPickerCompat.Open(CharEditorColorPickerCompat.Mode.EyeColor, false);
                    break;
                case AppearanceRowKind.Body:
                    OpenModFloatMenu(() => CharEditorCompat.OpenBodyPicker(window));
                    break;
                case AppearanceRowKind.SkinColorSingle:
                case AppearanceRowKind.SkinColorA:
                    CharEditorColorPickerCompat.Open(CharEditorColorPickerCompat.Mode.SkinColor, true);
                    break;
                case AppearanceRowKind.SkinColorB:
                    CharEditorColorPickerCompat.Open(CharEditorColorPickerCompat.Mode.SkinColor, false);
                    break;
                case AppearanceRowKind.BodyTattoo:
                    OpenModFloatMenu(() => CharEditorCompat.OpenBodyTattooPicker(window));
                    break;
                case AppearanceRowKind.ApparelEntry:
                    OpenApparelDrillIn(row.Payload as Apparel, item);
                    break;
                case AppearanceRowKind.WeaponEntry:
                    OpenWeaponDrillIn(row.Payload as ThingWithComps, item);
                    break;
                case AppearanceRowKind.HeadAddons:
                    CharEditorCompat.OpenHeadAddons(window);
                    break;
            }
        }

        // Every interactive Appearance row is a combo box: Enter/Space open the mod's own picker
        // and Left/Right never step a variant in place — a combo box is a dropdown, not a slider.
        // The apparel and weapon rows follow the same rule; their Enter drill-in is where an item
        // gets swapped. So there is no AdjustAppearanceRow arm and Left/Right fall through to the
        // base's tree expand/collapse.

        // ---- Apparel/Weapon drill-in menus (Enter): Edit color plus the modifier variants. ----

        private void OpenApparelDrillIn(Apparel apparel, InspectionTreeItem item)
        {
            if (apparel == null)
            {
                return;
            }
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("RimWorldAccess.CharEd.Appearance.EditColor".Translate(), delegate
                {
                    // Vehicle A: AChangeApparelUI's own Preselect already runs the make-colorable
                    // confirm for an uncolorable item, so this option is offered for every item.
                    CharEditorCompat.OpenApparelColorDialog(window, apparel);
                }),
                new FloatMenuOption("RimWorldAccess.CharEd.Appearance.NextKeepColor".Translate(), delegate
                {
                    CharEditorCompat.StepApparelWithColorVariant(window, apparel, true, CharEditorCompat.ApparelColorVariant.KeepColor);
                    RebuildApparelAfterMutation(item);
                }),
                new FloatMenuOption("RimWorldAccess.CharEd.Appearance.NextRandomColor".Translate(), delegate
                {
                    CharEditorCompat.StepApparelWithColorVariant(window, apparel, true, CharEditorCompat.ApparelColorVariant.RandomColor);
                    RebuildApparelAfterMutation(item);
                }),
                new FloatMenuOption("RimWorldAccess.CharEd.Appearance.NextRandomAlpha".Translate(), delegate
                {
                    CharEditorCompat.StepApparelWithColorVariant(window, apparel, true, CharEditorCompat.ApparelColorVariant.RandomAlpha);
                    RebuildApparelAfterMutation(item);
                }),
            };
            if (CharEditorCompat.CreationMode)
            {
                options.Add(new FloatMenuOption("Randomize".Translate(), delegate
                {
                    CharEditorCompat.RandomizeApparel(window, apparel);
                    RebuildApparelAfterMutation(item);
                }));
            }
            // Opens DialogObjects preloaded in Apparel mode via the row's own texture-button
            // opener (vehicle A); the Objects browser adapter drives the rest.
            options.Add(new FloatMenuOption("RimWorldAccess.CharEd.Appearance.Replace".Translate(), delegate
            {
                CharEditorCompat.OpenReplaceApparelDialog(window, apparel);
            }));
            WindowlessFloatMenuState.OpenTitled(apparel.def.label.CapitalizeFirst(), options);
        }

        private void OpenWeaponDrillIn(ThingWithComps weapon, InspectionTreeItem item)
        {
            if (weapon == null)
            {
                return;
            }
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("RimWorldAccess.CharEd.Appearance.EditColor".Translate(), delegate
                {
                    // Vehicle A: AChangeWeaponUI IS the make-colorable prompt path, never the
                    // eager every-frame config-panel path.
                    CharEditorCompat.OpenWeaponColorDialog(window, weapon);
                }),
            };
            // Weapon cycling has NO modifier or color variants at all.
            if (CharEditorCompat.CreationMode)
            {
                options.Add(new FloatMenuOption("Randomize".Translate(), delegate
                {
                    CharEditorCompat.RandomizeWeapon(window, weapon);
                    RebuildWeaponsAfterMutation(item);
                }));
            }
            // Opens DialogObjects preloaded in Weapon mode for this item.
            options.Add(new FloatMenuOption("RimWorldAccess.CharEd.Appearance.Replace".Translate(), delegate
            {
                CharEditorCompat.OpenReplaceWeaponDialog(window, weapon);
            }));
            WindowlessFloatMenuState.OpenTitled(weapon.def.label.CapitalizeFirst(), options);
        }

        /// <summary>
        /// An Inventory thing row's Alt+I menu: BlockInventory.DrawThingRow's five per-row buttons
        /// (info, drop, transfer, random, ingest), each promoted to a discrete option. Info card is
        /// vanilla <c>Dialog_InfoCard</c>; Transfer/Drop/Destroy/Ingest ride
        /// <see cref="CharEditorCompat"/>'s own vehicles. Drop is absent in world generation, where
        /// its content would duplicate Destroy.
        /// </summary>
        private void OpenInventoryThingDrillIn(InventoryThingEntry entry, InspectionTreeItem item)
        {
            Thing thing = entry?.Thing;
            if (thing == null)
            {
                return;
            }
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("RimWorldAccess.CharEd.Inventory.InfoCard".Translate(), delegate
                {
                    Find.WindowStack.Add(new Dialog_InfoCard(thing));
                }),
                new FloatMenuOption("RimWorldAccess.CharEd.Inventory.Transfer".Translate(), delegate
                {
                    CharEditorCompat.TransferThing(window, thing);
                    RefreshInventorySectionInPlace(silent: false);
                }),
            };
            if (!CharEditorCompat.InStartingScreen)
            {
                options.Add(new FloatMenuOption("RimWorldAccess.CharEd.Inventory.Drop".Translate(), delegate
                {
                    CharEditorCompat.DropThing(window, thing);
                    RefreshInventorySectionInPlace(silent: false);
                }));
            }
            options.Add(new FloatMenuOption("RimWorldAccess.CharEd.Inventory.Destroy".Translate(), delegate
            {
                CharEditorCompat.DestroyThing(window, thing);
                RefreshInventorySectionInPlace(silent: false);
            }));
            Pawn pawn = CharEditorCompat.CurrentPawn;
            if (pawn != null && CanIngestInventoryThing(pawn, thing))
            {
                options.Add(new FloatMenuOption("RimWorldAccess.CharEd.Inventory.Ingest".Translate(), delegate
                {
                    CharEditorCompat.IngestThing(window, thing);
                    RefreshInventorySectionInPlace(silent: false);
                }));
            }
            if (CharEditorCompat.CreationMode)
            {
                options.Add(new FloatMenuOption("Randomize".Translate(), delegate
                {
                    CharEditorCompat.RandomizeThing(window, thing, entry.Mode);
                    RefreshInventorySectionInPlace(silent: false);
                }));
            }
            WindowlessFloatMenuState.OpenTitled(thing.LabelCap.ToString(), options);
        }

        // ---- Alt+I unified drill-in (Randomize, and the Apparel/Weapon menu). ----

        private bool OnAppearanceEntryRow()
        {
            RefreshModel();
            InspectionTreeItem item = CurrentTreeItem();
            if (item?.Data is RegionRow<AppearanceRowKind>)
            {
                return true;
            }
            // The same claim serves an Inventory thing row's own menu: Alt+I is the one drill-in
            // entry point, never a second Info-key claim.
            return item?.Data is RegionRow<InventoryRowKind> inventoryRow && inventoryRow.Kind == InventoryRowKind.ThingEntry;
        }

        private void PerformAppearanceDrillIn()
        {
            InspectionTreeItem item = CurrentTreeItem();
            if (item?.Data is RegionRow<InventoryRowKind> inventoryRow && inventoryRow.Kind == InventoryRowKind.ThingEntry)
            {
                OpenInventoryThingDrillIn(inventoryRow.Payload as InventoryThingEntry, item);
                return;
            }
            if (!(item?.Data is RegionRow<AppearanceRowKind> row))
            {
                return;
            }

            if (row.Kind == AppearanceRowKind.ApparelEntry)
            {
                OpenApparelDrillIn(row.Payload as Apparel, item);
                return;
            }
            if (row.Kind == AppearanceRowKind.WeaponEntry)
            {
                OpenWeaponDrillIn(row.Payload as ThingWithComps, item);
                return;
            }

            // Every other row's only drill-in action is Randomize, and only in creation mode,
            // mirroring the mod's dice button, which NavSelectorImageBox draws solely under
            // CEditor.IsRandom.
            if (!CharEditorCompat.CreationMode)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.SpeakData("RimWorldAccess.CharEd.Appearance.NoDrillIn".Translate().ToString());
                return;
            }

            switch (row.Kind)
            {
                case AppearanceRowKind.HairStyle:
                    OpenRandomizeVariantMenu(item, new (string, Action)[]
                    {
                        ("RimWorldAccess.CharEd.Browser.Randomize".Translate(), () => CharEditorCompat.RandomizeHair(window, false, false)),
                        ("RimWorldAccess.CharEd.Appearance.RandomizeAlsoColor".Translate(), () => CharEditorCompat.RandomizeHair(window, true, false)),
                        ("RimWorldAccess.CharEd.Appearance.RandomizeAlsoColorAlpha".Translate(), () => CharEditorCompat.RandomizeHair(window, true, true)),
                    });
                    return;
                case AppearanceRowKind.Beard:
                    RandomizeAndAnnounce(() => CharEditorCompat.RandomizeBeard(window), item);
                    return;
                case AppearanceRowKind.HairColorA:
                    OpenRandomizeVariantMenu(item, new (string, Action)[]
                    {
                        ("RimWorldAccess.CharEd.Browser.Randomize".Translate(), () => CharEditorCompat.RandomizeHairColorChannel(window, true, false)),
                        ("RimWorldAccess.CharEd.Appearance.RandomizeAlpha".Translate(), () => CharEditorCompat.RandomizeHairColorChannel(window, true, true)),
                    });
                    return;
                case AppearanceRowKind.HairColorB:
                    OpenRandomizeVariantMenu(item, new (string, Action)[]
                    {
                        ("RimWorldAccess.CharEd.Browser.Randomize".Translate(), () => CharEditorCompat.RandomizeHairColorChannel(window, false, false)),
                        ("RimWorldAccess.CharEd.Appearance.RandomizeAlpha".Translate(), () => CharEditorCompat.RandomizeHairColorChannel(window, false, true)),
                    });
                    return;
                case AppearanceRowKind.GradientMask:
                    RandomizeAndAnnounce(() => CharEditorCompat.RandomizeGradient(window), item);
                    return;
                case AppearanceRowKind.FaceTattoo:
                    RandomizeAndAnnounce(() => CharEditorCompat.RandomizeFaceTattoo(window), item);
                    return;
                case AppearanceRowKind.HeadVanilla:
                    RandomizeAndAnnounce(() => CharEditorCompat.RandomizeHead(window), item);
                    return;
                case AppearanceRowKind.FacialHead:
                case AppearanceRowKind.FacialEye:
                case AppearanceRowKind.FacialLid:
                case AppearanceRowKind.FacialBrow:
                case AppearanceRowKind.FacialMouth:
                case AppearanceRowKind.FacialSkin:
                    RandomizeAndAnnounce(() => CharEditorCompat.RandomizeFacial(window, FacialKey(row.Kind)), item);
                    return;
                case AppearanceRowKind.EyeColor1:
                    RandomizeAndAnnounce(() => CharEditorCompat.RandomizeEyeColor(window, true), item);
                    return;
                case AppearanceRowKind.EyeColor2:
                    RandomizeAndAnnounce(() => CharEditorCompat.RandomizeEyeColor(window, false), item);
                    return;
                case AppearanceRowKind.Body:
                    RandomizeAndAnnounce(() => CharEditorCompat.RandomizeBody(window), item);
                    return;
                case AppearanceRowKind.SkinColorSingle:
                    // Non-alien: the mod's plain-click branch is the only one that does anything,
                    // so act directly rather than offering a menu of one.
                    RandomizeAndAnnounce(() => CharEditorCompat.RandomizeSkinColorChannel(window, null, false), item);
                    return;
                case AppearanceRowKind.SkinColorA:
                    OpenRandomizeVariantMenu(item, new (string, Action)[]
                    {
                        ("RimWorldAccess.CharEd.Browser.Randomize".Translate(), () => CharEditorCompat.RandomizeSkinColorChannel(window, true, false)),
                        ("RimWorldAccess.CharEd.Appearance.RandomizeAlpha".Translate(), () => CharEditorCompat.RandomizeSkinColorChannel(window, true, true)),
                    });
                    return;
                case AppearanceRowKind.SkinColorB:
                    OpenRandomizeVariantMenu(item, new (string, Action)[]
                    {
                        ("RimWorldAccess.CharEd.Browser.Randomize".Translate(), () => CharEditorCompat.RandomizeSkinColorChannel(window, false, false)),
                        ("RimWorldAccess.CharEd.Appearance.RandomizeAlpha".Translate(), () => CharEditorCompat.RandomizeSkinColorChannel(window, false, true)),
                    });
                    return;
                case AppearanceRowKind.BodyTattoo:
                    RandomizeAndAnnounce(() => CharEditorCompat.RandomizeBodyTattoo(window), item);
                    return;
            }
        }

        /// <summary>A single-option drill-in: acts directly rather than opening a picker.</summary>
        private void RandomizeAndAnnounce(Action randomize, InspectionTreeItem item)
        {
            randomize();
            SoundDefOf.Click.PlayOneShotOnCamera();
            AnnounceAdjustedRow<AppearanceRowKind>(item, DescribeAppearanceRow);
        }

        /// <summary>A 2-3 option drill-in: a flat WindowlessFloatMenuState picker, each row naming what it will do.</summary>
        private void OpenRandomizeVariantMenu(InspectionTreeItem item, (string label, Action action)[] variants)
        {
            var options = new List<FloatMenuOption>();
            foreach (var (label, action) in variants)
            {
                options.Add(new FloatMenuOption(label, delegate
                {
                    action();
                    AnnounceAdjustedRow<AppearanceRowKind>(item, DescribeAppearanceRow);
                }));
            }
            WindowlessFloatMenuState.OpenTitled("Randomize".Translate(), options);
        }
    }
}
