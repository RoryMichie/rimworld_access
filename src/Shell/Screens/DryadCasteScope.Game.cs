using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The keyboard focus scope for the real <see cref="Dialog_ChangeDryadCaste"/>
    /// window (the Gauranlen tree caste picker), registered through
    /// <see cref="ScopeForWindow"/> — the Shape-A pattern, alongside
    /// <see cref="EntityCodexScope"/>.
    ///
    /// Promoted to <see cref="ScreenScope"/>: one content region over the dialog's own <c>allDryadModes</c> list,
    /// plus the automatic Buttons region captured from the dialog's own draw
    /// (vanilla's Close button, and its Accept button whenever vanilla draws
    /// one). <see cref="DryadCasteState"/> keeps the dialog's data, its opening
    /// announcement and the Enter mutation, and sheds the cursor/typeahead/
    /// announcement plumbing the chassis now provides — the same S4 promotion
    /// the scenario overlay editors got. The rung-0.34 inventory note ("a
    /// genuine selection list... all standard grammar") is now literally true:
    /// every claim below is shared menu grammar.
    ///
    /// <b>Wave-law 2 (per-mode retired-tail verification).</b> The retired
    /// HandleInput had one mode and its final line unconditionally
    /// <c>return true</c> — every key, recognized or not, was consumed while
    /// the dialog was active (the leading <c>if (ev.control ||
    /// KeyboardHelper.IsAltHeld) return true;</c> swallow included). No leak
    /// to preserve or close: the modal backstop faithfully reproduces this
    /// "consume everything" tail for any key not explicitly claimed here.
    ///
    /// <b>Child windows.</b> The only window this screen can open is a real
    /// <see cref="Dialog_MessageBox"/> confirmation from
    /// <see cref="DryadCasteState.SelectCaste"/> (Enter on an available caste),
    /// which gets its own MessageBoxScope pushed on top via the same
    /// ScopeForWindow mirror — ordinary window-stack layering handles this, no
    /// gate needed (the same D4/F1-EntityCodex precedent for a real child
    /// window).
    /// </summary>
    public sealed class DryadCasteScope : ScreenScope
    {
        private readonly Dialog_ChangeDryadCaste dialog;
        private bool seededCursor;

        public DryadCasteScope(Dialog_ChangeDryadCaste dialog)
        {
            this.dialog = dialog;
            // Escape closes the dialog itself, as the retired handler's own
            // Escape branch did: DryadCastePatch's OnCancelKeyPressed prefix
            // blocks vanilla's close path while this state is active (kept
            // untouched — see OwnsCancel), so nothing else would. The base's
            // typeahead Escape claim registers first and clears a live search
            // ahead of this one.
            Claim(SharedMenuGrammar.Cancel, e => dialog.Close(), when: () => !TypeaheadHasActiveSearch);
        }

        public override string Name
        {
            get { return "dryad-caste"; }
        }

        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        /// <summary>
        /// Decompiled-source verified: Dialog_ChangeDryadCaste never
        /// overrides OnAcceptKeyPressed/OnCancelKeyPressed. It sets
        /// closeOnAccept = false in its own constructor (vanilla's base Enter
        /// handling already no-ops regardless — no ad hoc Accept blocker
        /// exists for this dialog, matching that), so OwnsAccept = true (the
        /// chassis default) is documentation of the real owner rather than a
        /// behavior change. closeOnCancel stays Window's true default, so
        /// OwnsCancel is held at the original unconditional true — the
        /// chassis narrows it to "only while searching", which would hand
        /// Escape back to a vanilla close path DryadCastePatch's existing
        /// OnCancelKeyPressed blocker refuses (kept untouched — see
        /// InfoCardScope/AutoSlaughterScope for the identical rationale).
        /// </summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        /// <summary>
        /// The caste under the keyboard cursor, for the focus ring below. Not
        /// vanilla's <c>selectedMode</c>, which the state writes only on commit.
        /// </summary>
        internal GauranlenTreeModeDef FocusedMode
        {
            get
            {
                if (Model.RegionIndex != 0)
                {
                    return null;
                }
                ListModel region = Model.CurrentRegion;
                IReadOnlyList<GauranlenTreeModeDef> modes = DryadCasteState.AllModes;
                if (region == null || region.IsEmpty || region.Index >= modes.Count)
                {
                    return null;
                }
                return modes[region.Index];
            }
        }

        /// <summary>
        /// Lands the cursor on the tree's current caste on the first focus, so
        /// the entry announcement (which fires after this override's body — see
        /// <see cref="FocusScope.AfterFocusDispatch"/>) reads the caste the
        /// player actually has, exactly as the retired state's own index seed did.
        /// </summary>
        public override void OnFocus()
        {
            base.OnFocus();
            if (seededCursor)
            {
                return;
            }
            seededCursor = true;
            int index = DryadCasteState.CurrentModeIndex;
            ListModel region = Model.CurrentRegion;
            if (index > 0 && region != null && Model.RegionIndex == 0 && index < region.Count)
            {
                region.MoveTo(index);
            }
        }

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        protected override string ContentRegionName(int region)
        {
            return (string)"ChangeMode".Translate();
        }

        protected override int ContentItemCount(int region)
        {
            return DryadCasteState.AllModes.Count;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            IReadOnlyList<GauranlenTreeModeDef> modes = DryadCasteState.AllModes;
            if (index < 0 || index >= modes.Count)
            {
                return new ElementDescription();
            }
            return DryadCasteState.DescribeMode(modes[index]);
        }

        protected override void ActivateContentItem(int region, int index)
        {
            IReadOnlyList<GauranlenTreeModeDef> modes = DryadCasteState.AllModes;
            DryadCasteState.SelectCaste(index >= 0 && index < modes.Count ? modes[index] : null);
        }
    }

    /// <summary>
    /// Paints the shared focus ring on vanilla's own caste box.
    ///
    /// The rect <c>DrawDryadStage</c> receives is the whole right panel, not the
    /// row (decompiled Dialog_ChangeDryadCaste.cs:262), so the geometry is taken
    /// the way <see cref="ThingFilterTreeSync"/> takes it: the prefix opens a
    /// bracket on the stage vanilla is about to paint, and the box vanilla itself
    /// draws first inside that bracket (:266) supplies the rect. Nothing here calls
    /// <c>GetPosition</c> or reads <c>OptionSize</c>.
    ///
    /// Identity is the <see cref="GauranlenTreeModeDef"/> vanilla hands its own
    /// painter, matched by reference against <see cref="DryadCasteScope.FocusedMode"/>,
    /// which reads the same <c>allDryadModes</c> list the loop walks (:176).
    /// </summary>
    internal static class DryadStageRingPatch
    {
        private static bool bracketOpen;
        private static Rect stageRect;
        private static bool haveRect;

        /// <summary>Opens and closes the bracket, and draws once the box inside it has supplied the rect.</summary>
        [HarmonyPatch(typeof(Dialog_ChangeDryadCaste), "DrawDryadStage")]
        internal static class DrawDryadStagePatch
        {
            [HarmonyPrefix]
            public static void Prefix(GauranlenTreeModeDef stage)
            {
                try
                {
                    DryadCasteScope scope = FocusStackLookup.TopmostOfType<DryadCasteScope>();
                    bracketOpen = stage != null && scope != null && stage == scope.FocusedMode;
                    haveRect = false;
                }
                catch (Exception ex)
                {
                    bracketOpen = false;
                    ModLogger.LimitedError("Dryad caste ring error", ex);
                }
            }

            [HarmonyPostfix]
            public static void Postfix()
            {
                try
                {
                    if (bracketOpen && haveRect && Event.current.type == EventType.Repaint)
                    {
                        FocusRing.Draw(stageRect.ContractedBy(1f));
                    }
                }
                catch (Exception ex)
                {
                    ModLogger.LimitedError("Dryad caste ring error", ex);
                }
                finally
                {
                    bracketOpen = false;
                    haveRect = false;
                }
            }
        }

        /// <summary>The geometry tap: inert unless the bracket above is open, which is true only inside one dialog's row painter.</summary>
        [HarmonyPatch(typeof(Widgets), nameof(Widgets.DrawBoxSolidWithOutline))]
        internal static class DrawBoxSolidWithOutlinePatch
        {
            [HarmonyPostfix]
            public static void Postfix(Rect rect)
            {
                if (!bracketOpen || haveRect)
                {
                    return;
                }
                stageRect = rect;
                haveRect = true;
            }
        }
    }
}
