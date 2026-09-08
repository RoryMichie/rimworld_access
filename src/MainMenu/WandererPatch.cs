using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    [HarmonyPatch(typeof(Dialog_ChooseNewWanderers))]
    [HarmonyPatch("PreOpen")]
    public static class WandererPreOpenPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Dialog_ChooseNewWanderers __instance)
        {
            try
            {
                // Close the notification menu if it was open (user activated letter button)
                if (NotificationMenuState.IsActive)
                    NotificationMenuState.Close();

                WandererPatch.SetInstance(__instance);
                PawnFilterData.Initialize();
                StartingPawnState.Open(PawnEditorContext.Wanderer);
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimWorld Access] Error in WandererPreOpenPatch: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(Dialog_ChooseNewWanderers))]
    [HarmonyPatch("PostClose")]
    public static class WandererPostClosePatch
    {
        [HarmonyPostfix]
        public static void Postfix(Dialog_ChooseNewWanderers __instance)
        {
            try
            {
                // Shared teardown with the chargen page: the
                // wanderer flow can open the filter editor / presets /
                // reroll too, and their flags must not leak into Playing.
                StartingPawnPatch.CloseOverlayStates();
                StartingPawnState.Close();
                WandererPatch.SetInstance(null);
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimWorld Access] Error in WandererPostClosePatch: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(Dialog_ChooseNewWanderers))]
    [HarmonyPatch("DoWindowContents")]
    public static class WandererDoWindowContentsPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Dialog_ChooseNewWanderers __instance)
        {
            try
            {
                // Process reroll batch each frame
                RerollState.ProcessBatch();

                if (!StartingPawnState.IsActive) return;

                StartingPawnState.CheckPendingRenameRebuild();

                // Keep a live name-edit session's buffer mirrored (see
                // StartingPawnScreenScope.OnHostDrawPass's remarks).
                Shell.StartingPawnScreenScope.Active?.OnHostDrawPass();

                int pawnIdx = StartingPawnState.GetSelectedPawnIndex();
                AccessTools.Field(typeof(Dialog_ChooseNewWanderers), "curPawnIndex")
                    .SetValue(__instance, pawnIdx);
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimWorld Access] Error in WandererDoWindowContentsPatch: {ex}");
            }
        }
    }

    // RETIRED: WandererCancelBlockPatch and
    // WandererAcceptBlockPatch, the Window.OnCancelKeyPressed /
    // OnAcceptKeyPressed prefixes. Gates verbatim (identical shape):
    //   if (__instance is Dialog_ChooseNewWanderers && StartingPawnState.IsActive)
    //       return false;
    // The dialog is a REAL window with Window-default closeOnCancel/
    // closeOnAccept (it declares neither override — decompiled-verified), so
    // vanilla's Escape/Enter would close it under the accessibility state.
    // StartingPawnScope now owns both keys structurally (the I3
    // anomaly-dialog precedent): OwnsCancel/OwnsAccept are TRUE, so the
    // consolidated Window router twins (WindowKeyRouter.Game.cs) suppress
    // this dialog's own handlers whenever the scope is Top — which covers
    // every frame the retired unconditional gate did, because the scope
    // stays Top under all WINDOWLESS overlays (their scopes' Escape/Enter
    // claims stamp Cancel/AcceptConsumed, which the twins also honor), and
    // under REAL windows the topmost window received the cancel/accept
    // instead of this dialog in both worlds.

    public static class WandererPatch
    {
        private static Dialog_ChooseNewWanderers instance;
        private static FieldInfo generationIndexField;
        private static PropertyInfo defaultRequestProperty;

        public static void SetInstance(Dialog_ChooseNewWanderers dialog)
        {
            instance = dialog;
        }

        public static Dialog_ChooseNewWanderers GetInstance()
        {
            return instance;
        }

        public static void ConfirmWanderers()
        {
            if (instance == null) return;

            try
            {
                // MUTATION-C: mirrors Dialog_ChooseNewWanderers.DoWindowContents'
                // Confirm-button branch verbatim (IncidentParms fire, letter
                // removal, gameEnding=false, newWanderersCreatedTick stamp); no
                // gated vehicle exists, vanilla's own button writes the fields.
                var parms = new IncidentParms();
                parms.target = Find.AnyPlayerHomeMap;
                parms.forced = true;
                Find.Storyteller.TryFire(new FiringIncident(
                    IncidentDefOf.GameEndedWanderersJoin, null, parms));

                Find.LetterStack.RemoveLetter(
                    Find.LetterStack.LettersListForReading.Find(
                        letter => letter.def == LetterDefOf.GameEnded));

                Find.GameEnder.gameEnding = false;
                Find.GameEnder.newWanderersCreatedTick = Find.TickManager.TicksGame;
                instance.Close();
                Current.Game.InitData = null;
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimWorld Access] Error confirming wanderers: {ex}");
            }
        }

        public static void CloseDialog()
        {
            if (instance == null) return;
            instance.Close();
        }

        public static void AddPawn()
        {
            if (instance == null) return;

            try
            {
                if (generationIndexField == null)
                    generationIndexField = AccessTools.Field(typeof(Dialog_ChooseNewWanderers), "generationIndex");
                if (defaultRequestProperty == null)
                    defaultRequestProperty = AccessTools.Property(typeof(Dialog_ChooseNewWanderers), "DefaultStartingPawnRequest");

                // MUTATION-C: mirrors Dialog_ChooseNewWanderers.DrawPawnList's
                // "+" button branch (startingPawnCount++, AddNewPawn,
                // generationIndex++); the SetGenerationRequest call is a
                // harmless superset — StartingPawnUtility.EnsureGenerationRequestInRangeOf
                // would default it identically if omitted.
                Find.GameInitData.startingPawnCount++;

                int genIdx = (int)generationIndexField.GetValue(instance);
                var request = (PawnGenerationRequest)defaultRequestProperty.GetValue(null);
                StartingPawnUtility.SetGenerationRequest(genIdx, request);
                StartingPawnUtility.AddNewPawn(genIdx);
                genIdx++;
                generationIndexField.SetValue(instance, genIdx);
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimWorld Access] Error adding wanderer pawn: {ex}");
            }
        }

        // MUTATION-C: mirrors Dialog_ChooseNewWanderers.DoPawnRow's delete
        // branch (startingPawnCount--, remove pawn); vanilla also clamps its
        // own curPawnIndex field here, but that field is synced every frame
        // from StartingPawnState's tree selection (WandererDoWindowContentsPatch),
        // and StartingPawnState.RemoveWandererPawn clamps treeNav.SelectedIndex
        // itself after calling this — same clamp, different owner.
        public static void RemovePawn(int pawnIndex)
        {
            var pawns = Find.GameInitData.startingAndOptionalPawns;
            if (pawnIndex < 0 || pawnIndex >= pawns.Count) return;

            Find.GameInitData.startingPawnCount--;
            pawns.RemoveAt(pawnIndex);
        }
    }
}
