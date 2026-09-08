using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// keyboard parity for the RimTalk TTS addon's overlay-adjacent
    /// controls -- the ones a sighted player reaches with a mouse click on the map view or the
    /// PlaySettings row, outside any of the addon's own windows. Two
    /// surfaces, each gated exactly as the addon gates its own drawing so a
    /// keyboard action never appears when the matching mouse control would not:
    ///
    /// SURFACE 1 -- <c>RimTalk.TTS.Patch.RimTalkPatches.TogglePatch</c> (postfix on
    /// <c>PlaySettings.DoPlaySettingsGlobalControls</c>): a brand-new toggleable icon (not a
    /// modifier variant of an existing one, so <see cref="ToolbarModifierRegistry"/> does not
    /// apply), gated on <c>TTSSettings.ButtonDisplay</c>. Its click just flips
    /// <c>TTSSettings.isOnButton</c> (a session-only field -- confirmed NOT Scribe'd in
    /// <c>TTSSettings.ExposeData</c>, matching its own tooltip "its setting won't be saved") and,
    /// when turning off, <c>Update_PendingToggleExecutor</c> calls <c>TTSService.StopAll()</c> on
    /// the next tick. <see cref="TrySetOnState"/> reproduces both effects synchronously rather than
    /// replaying the addon's private next-tick queue (MUTATION-C: TogglePatch.Postfix itself needs
    /// a live WidgetRow from an in-progress PlaySettings draw, which cannot be constructed outside
    /// one, so there is no A/B vehicle beyond the field-set-plus-StopAll pair the addon's own
    /// executor performs).
    ///
    /// SURFACE 2 -- <c>RimTalk.TTS.Patch.OverlayButtonPatch</c>'s four map-view buttons (Reset,
    /// Generate, Ignore, Display), gated on <c>TTSSettings.ControlButtonDisplay</c>. Each button's
    /// handler is a private static method on that patch class; <see cref="TryResetAudio"/>,
    /// <see cref="TryGenerateDialogue"/>, <see cref="TryIgnoreAllDialogue"/>, and
    /// <see cref="TryDisplayNextDialogue"/> invoke those exact methods by reflection (vehicle A --
    /// the same delegate the mouse click runs, including the addon's own Messages.Message calls).
    ///
    /// Independent reflection cache from <see cref="RimTalkNarrativeCompat"/>'s TTS-backoff probe:
    /// the TTSConfig/TTSSettings types come from <see cref="RimTalkSettingsFacade"/>, but every
    /// member stays bound on this class's own surface -- per that class's own header, each concern
    /// keeps its own cache so a member drift in one never takes another down.
    ///
    /// POISONED ASSEMBLY: only <c>RimTalk.TTS.Data.TTSConfig</c>, <c>RimTalk.TTS.Data.TTSSettings</c>,
    /// <c>RimTalk.TTS.Service.TTSService</c>, and <c>RimTalk.TTS.Patch.OverlayButtonPatch</c> are
    /// resolved here, each by exact name -- never <c>RimTalk.TTS.Service.SiliconFlowClient</c> and
    /// never a type sweep (CLAUDE.md gotcha).
    /// </summary>
    internal static class RimTalkTtsOverlayCompat
    {
        private const string TtsPackageId = "nitoritech.rimtalk.tts";

        private static readonly LazyReflectionGate gate =
            new LazyReflectionGate("RimTalk TTS compat (overlay-adjacent controls)", ResolveReflection);

        private static PropertyInfo ttsConfigIsEnabledProperty;
        private static PropertyInfo ttsConfigSettingsProperty;
        private static FieldInfo buttonDisplayField;
        private static FieldInfo controlButtonDisplayField;
        private static FieldInfo isOnButtonField;
        private static MethodInfo ttsServiceStopAllMethod;
        private static MethodInfo resetButtonFuncMethod;
        private static MethodInfo generateButtonFuncMethod;
        private static MethodInfo ignoreButtonFuncMethod;
        private static MethodInfo displayButtonFuncMethod;

        private static bool ResolveReflection(ReflectionSurface surface)
        {
            Type ttsConfigType = surface.Supplied("RimTalk.TTS.Data.TTSConfig", RimTalkSettingsFacade.TtsConfigType);
            Type ttsSettingsType = surface.Supplied("RimTalk.TTS.Data.TTSSettings", RimTalkSettingsFacade.TtsSettingsType);
            Type ttsServiceType = surface.Type("RimTalk.TTS.Service.TTSService");
            Type overlayButtonPatchType = surface.Type("RimTalk.TTS.Patch.OverlayButtonPatch");

            PropertyInfo isEnabledProperty = surface.Property(ttsConfigType, "IsEnabled");
            PropertyInfo settingsProperty = surface.Property(ttsConfigType, "Settings");
            FieldInfo buttonDisplay = surface.Field(ttsSettingsType, "ButtonDisplay");
            FieldInfo controlButtonDisplay = surface.Field(ttsSettingsType, "ControlButtonDisplay");
            FieldInfo isOnButton = surface.Field(ttsSettingsType, "isOnButton");
            MethodInfo stopAllMethod = surface.Method(ttsServiceType, "StopAll", Type.EmptyTypes);
            MethodInfo resetMethod = surface.Method(overlayButtonPatchType, "ResetButtonFunc", Type.EmptyTypes);
            MethodInfo generateMethod = surface.Method(overlayButtonPatchType, "generateButtonFunc", Type.EmptyTypes);
            MethodInfo ignoreMethod = surface.Method(overlayButtonPatchType, "ignoreButtonFunc", Type.EmptyTypes);
            MethodInfo displayMethod = surface.Method(overlayButtonPatchType, "displayButtonFunc", Type.EmptyTypes);

            if (!surface.Ready)
            {
                return false;
            }

            ttsConfigIsEnabledProperty = isEnabledProperty;
            ttsConfigSettingsProperty = settingsProperty;
            buttonDisplayField = buttonDisplay;
            controlButtonDisplayField = controlButtonDisplay;
            isOnButtonField = isOnButton;
            ttsServiceStopAllMethod = stopAllMethod;
            resetButtonFuncMethod = resetMethod;
            generateButtonFuncMethod = generateMethod;
            ignoreButtonFuncMethod = ignoreMethod;
            displayButtonFuncMethod = displayMethod;
            return true;
        }

        /// <summary>The live TTSSettings instance, or null if the addon isn't installed/active/reflectable -- shared by every probe below.</summary>
        private static object GetSettingsIfActive()
        {
            if (!ModsConfig.IsActive(TtsPackageId) || !gate.Ensure())
            {
                return null;
            }
            try
            {
                if (!(ttsConfigIsEnabledProperty.GetValue(null) is bool enabled) || !enabled)
                {
                    return null;
                }
                return ttsConfigSettingsProperty.GetValue(null);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"RimTalk TTS compat: settings probe failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>Mirrors TogglePatch.Postfix's own gate exactly: settings.ButtonDisplay must be on. Out param is the current isOnButton state.</summary>
        internal static bool TryGetOnState(out bool isOn)
        {
            isOn = false;
            object settings = GetSettingsIfActive();
            if (settings == null)
            {
                return false;
            }
            try
            {
                if (!(buttonDisplayField.GetValue(settings) is bool buttonDisplay) || !buttonDisplay)
                {
                    return false;
                }
                isOn = isOnButtonField.GetValue(settings) is bool on && on;
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"RimTalk TTS compat: on-state read failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// MUTATION-C (see class header): sets the session-only isOnButton field directly, then
        /// reproduces Update_PendingToggleExecutor's own effect (TTSService.StopAll() when turning
        /// off) synchronously rather than waiting a tick for the addon's private pending-toggle queue.
        /// </summary>
        internal static bool TrySetOnState(bool value)
        {
            object settings = GetSettingsIfActive();
            if (settings == null)
            {
                return false;
            }
            try
            {
                // MUTATION-C: mirrors TogglePatch's own field set (isOnButton is never Scribe'd,
                // so no Write() pairs with it) plus Update_PendingToggleExecutor's StopAll() call.
                isOnButtonField.SetValue(settings, value);
                if (!value)
                {
                    ttsServiceStopAllMethod.Invoke(null, null);
                }
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"RimTalk TTS compat: on-state write failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>Mirrors OverlayButtonPatch's own draw gate: settings.ControlButtonDisplay must be on.</summary>
        internal static bool ControlButtonsAvailable()
        {
            object settings = GetSettingsIfActive();
            if (settings == null)
            {
                return false;
            }
            try
            {
                return controlButtonDisplayField.GetValue(settings) is bool controlButtonDisplay && controlButtonDisplay;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"RimTalk TTS compat: control-buttons gate read failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>Vehicle A: the exact delegate the map view's "Audio clear" button invokes.</summary>
        internal static bool TryResetAudio()
        {
            return InvokeButtonFunc(resetButtonFuncMethod, "Reset");
        }

        /// <summary>Vehicle A: the exact delegate the map view's "Generate dialogue" button invokes.</summary>
        internal static bool TryGenerateDialogue()
        {
            return InvokeButtonFunc(generateButtonFuncMethod, "Generate");
        }

        /// <summary>Vehicle A: the exact delegate the map view's "Ignore existing dialogues" button invokes.</summary>
        internal static bool TryIgnoreAllDialogue()
        {
            return InvokeButtonFunc(ignoreButtonFuncMethod, "Ignore");
        }

        /// <summary>Vehicle A: the exact delegate the map view's "Display next" button invokes.</summary>
        internal static bool TryDisplayNextDialogue()
        {
            return InvokeButtonFunc(displayButtonFuncMethod, "Display");
        }

        private static bool InvokeButtonFunc(MethodInfo method, string name)
        {
            if (!ControlButtonsAvailable())
            {
                return false;
            }
            try
            {
                method.Invoke(null, null);
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"RimTalk TTS compat: {name} control invoke failed: {ex.Message}");
                return false;
            }
        }
    }
}
