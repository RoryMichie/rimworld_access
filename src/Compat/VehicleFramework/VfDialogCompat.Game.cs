using HarmonyLib;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Registers Vehicle Framework's dialogs against their keyboard scopes, and mirrors the two
    /// vanilla transfer dialogs' own close-announcement pattern (<see cref="TransportPodPatch"/>'s
    /// Window_PostClose_LoadTransporters_Patch) so a cancelled load/stash is announced the same
    /// way. Dialog_AssignSeats needs no such patch -- see its scope's own class remarks.
    /// </summary>
    internal static class VfDialogCompat
    {
        /// <summary>Each dialog's scope registers independently, so one dialog's absence or broken reflection surface never blocks the others.</summary>
        public static void RegisterDialogScopes()
        {
            if (VfCargoDialogAdapter.DialogType != null && VfCargoDialogAdapter.ReflectionReady)
            {
                ScopeForWindow.Register(VfCargoDialogAdapter.DialogType, delegate (Window w)
                {
                    return new TransportPodLoadingScope(w, new VfCargoDialogAdapter(w));
                });
            }

            // RegisterHierarchy, not Register: modded VF subclasses of these dialogs must inherit
            // the scope too.
            if (VfStashDialogAdapter.DialogType != null && VfStashDialogAdapter.ReflectionReady)
            {
                ScopeForWindow.RegisterHierarchy(VfStashDialogAdapter.DialogType, delegate (Window w)
                {
                    return new TransportPodLoadingScope(w, new VfStashDialogAdapter(w));
                });
            }

            if (VfAssignSeatsCompat.Ready)
            {
                ScopeForWindow.RegisterHierarchy(VfAssignSeatsCompat.DialogType, delegate (Window w)
                {
                    return new VfAssignSeatsScope(w);
                });
            }

            if (VfRenameDialogCompat.Ready)
            {
                ScopeForWindow.RegisterHierarchy(VfRenameDialogCompat.DialogType, delegate (Window w)
                {
                    return new VfRenameDialogScope(w);
                });
                VfRenameDialogCompat.PatchDrawPass();
            }

            if (VfPainterCompat.Ready)
            {
                ScopeForWindow.RegisterHierarchy(VfPainterCompat.DialogType, delegate (Window w)
                {
                    return new VfPainterScope(w);
                });
            }

            if (VfVehicleSelectorCompat.Ready)
            {
                ScopeForWindow.Register(VfVehicleSelectorCompat.SelectorType, delegate (Window w)
                {
                    return new VfVehicleSelectorScope(w);
                });
                VfSelectorRowDrawPatch.Register();
            }
        }

        /// <summary>Applied by <see cref="VehicleFrameworkModule"/> rather than declaratively, so an absent VF costs no Window.PostClose postfixes.</summary>
        public static void ApplyPostClosePatches(Harmony harmony)
        {
            var postClose = AccessTools.Method(typeof(Window), "PostClose");
            harmony.Patch(postClose, postfix: new HarmonyMethod(typeof(Window_PostClose_VfCargo_Patch), nameof(Window_PostClose_VfCargo_Patch.Postfix)));
            harmony.Patch(postClose, postfix: new HarmonyMethod(typeof(Window_PostClose_VfStash_Patch), nameof(Window_PostClose_VfStash_Patch.Postfix)));
        }

        /// <summary>
        /// Cleans up accessibility state and announces cancellation when Dialog_LoadCargo closes.
        /// Patches the base Window.PostClose rather than Dialog_LoadCargo.PostClose because the
        /// latter is a plain base.PostClose() call with no extra logic -- either site observes the
        /// same close, but this one needs no VF type reference at all.
        /// </summary>
        public static class Window_PostClose_VfCargo_Patch
        {
            public static void Postfix(Window __instance)
            {
                if (VfCargoDialogAdapter.DialogType == null || !VfCargoDialogAdapter.DialogType.IsInstanceOfType(__instance))
                    return;

                // Capture accept state before Close() resets it.
                bool wasAccepted = TransportPodLoadingState.AcceptAttempted;

                TransportPodLoadingState.Close();

                // Accept already speaks its own confirmation via VfCargoDialogAdapter.TriggerAccept.
                if (!wasAccepted)
                {
                    TolkHelper.Speak("RimWorldAccess.Compat.Vf.LoadCargoCancelled".Loc(), SpeechPriority.Normal);
                }
            }
        }

        /// <summary>The same treatment for Dialog_StashVehicle; edits here and in <see cref="Window_PostClose_VfCargo_Patch"/> must stay in step.</summary>
        public static class Window_PostClose_VfStash_Patch
        {
            public static void Postfix(Window __instance)
            {
                if (VfStashDialogAdapter.DialogType == null || !VfStashDialogAdapter.DialogType.IsInstanceOfType(__instance))
                    return;

                bool wasAccepted = TransportPodLoadingState.AcceptAttempted;

                TransportPodLoadingState.Close();

                if (!wasAccepted)
                {
                    TolkHelper.Speak("RimWorldAccess.Compat.Vf.StashCancelled".Loc(), SpeechPriority.Normal);
                }
            }
        }
    }
}
