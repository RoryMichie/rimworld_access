using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Narrative Feed producers P2 and P3, plus the shared TTS-backoff probe P1
    /// (<see cref="BubblesNarrativeCompat"/>) also uses. Everything resolves by name, so neither
    /// the RimTalk core assembly nor its TTS addon is referenced at compile time. Each concern
    /// keeps its own reflection cache and failure path, so drift in one never takes the others
    /// down.
    ///
    /// Concern 1, <see cref="TtsWillVocalize"/>: whether the TTS addon is about to speak the line
    /// just created. Callable unconditionally — it does its own ModsConfig.IsActive check.
    ///
    /// Concern 2 (P2): a postfix on <c>Verse.PlayLog.Add</c> at <see cref="Priority.Low"/> so it
    /// runs after Bubbles' own default-priority postfix, letting P1 observe and publish first. It
    /// catches RimTalk lines whose bubble was suppressed but that remain visible in RimTalk's
    /// overlay; P1's dedupe key makes it a silent no-op otherwise.
    ///
    /// Concern 3 (P3): a subscription to <c>RimTalk.UI.Overlay.OnLogUpdated</c>, the family's only
    /// public event. Lines the player types as their own persona never reach <c>Verse.PlayLog</c>
    /// — Cache.GetPlayer() is a synthetic, unspawned pawn — and exist only in <c>ApiHistory</c>
    /// under <c>Channel.User</c>. That channel is not exclusively player-typed, though:
    /// <c>CustomDialogueService.ExecuteDialogue</c> uses it when the player scripts a line FOR an
    /// NPC, and those flow through PlayLog where P1/P2 catch them under a different dedupe key.
    /// So this producer also requires the ApiLog's <c>TalkRequest.Initiator</c> to be
    /// reference-equal to <c>Cache.GetPlayer()</c>, the same test <c>PawnUtil.IsPlayer()</c> makes.
    /// </summary>
    internal static class RimTalkNarrativeCompat
    {
        private const string RimTalkPackageId = "cj.rimtalk";
        private const string TtsPackageId = "nitoritech.rimtalk.tts";

        public static void Register(Harmony harmony)
        {
            if (!ModsConfig.IsActive(RimTalkPackageId))
            {
                return;
            }

            RegisterP2(harmony);
            RegisterP3();
            RegisterOverlayToggleAnnouncement(harmony);

            // The SHIFT variant of RimTalk's PlaySettings toggle icon, registered generically so
            // DialogueLogScope's button row can offer it with no RimTalk-specific code. The gate
            // resolves lazily, so registration order among the compats never matters.
            ToolbarModifierRegistry.Register("rimtalk.toggle",
                "RimWorldAccess.Narrative.DialogueLog.OpenSettings",
                ActivateOpenModSettings,
                modTypeGate.Ensure);
        }

        // Concern 1: TTS backoff detection, used by both P1 and P2.

        private static readonly LazyReflectionGate ttsGate =
            new LazyReflectionGate("RimTalk TTS compat (backoff detection)", ResolveTtsReflection);

        private static PropertyInfo ttsConfigIsEnabledProperty;
        private static PropertyInfo ttsConfigSettingsProperty;
        private static PropertyInfo ttsSettingsSupplierProperty;
        private static MethodInfo audioPlaybackIsCurrentlyPlayingMethod;

        /// <summary>
        /// True only when the TTS addon is installed, enabled, has a real supplier, AND has just
        /// started or is still playing audio for the line this call concerns. Safe to call
        /// unconditionally: it does its own <c>ModsConfig.IsActive</c> check and never throws.
        ///
        /// DELIBERATELY decides on <c>AudioPlaybackService.IsCurrentlyPlaying()</c> rather than
        /// the static settings alone. The addon prefixes the same
        /// <c>TalkService.CreateInteraction</c> call that produces the entry, and that prefix
        /// synchronously calls <c>PlayAudio</c> before letting the original — and therefore
        /// <c>PlayLog.Add</c> — run, so TTS has already decided by the time a producer fires. That
        /// also covers the per-pawn "voice model == NONE" skip and the mute toggle for free: when
        /// either suppresses audio, PlayAudio never runs and the flag stays false. The
        /// EnableTTS/supplier checks remain as defense in depth.
        /// </summary>
        internal static bool TtsWillVocalize()
        {
            if (!ModsConfig.IsActive(TtsPackageId))
            {
                return false;
            }

            if (!ttsGate.Ensure())
            {
                return false;
            }

            try
            {
                if (!(ttsConfigIsEnabledProperty.GetValue(null) is bool enabled) || !enabled)
                {
                    return false;
                }

                object settings = ttsConfigSettingsProperty.GetValue(null);
                if (settings == null)
                {
                    return false;
                }

                object supplier = ttsSettingsSupplierProperty.GetValue(settings);
                if (supplier == null || string.Equals(supplier.ToString(), "None", StringComparison.Ordinal))
                {
                    return false;
                }

                return audioPlaybackIsCurrentlyPlayingMethod.Invoke(null, null) is bool playing && playing;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"RimTalk TTS compat: TtsWillVocalize probe failed: {ex.Message}");
                return false;
            }
        }

        private static bool ResolveTtsReflection(ReflectionSurface surface)
        {
            Type ttsConfigType = surface.Supplied("RimTalk.TTS.Data.TTSConfig", RimTalkSettingsFacade.TtsConfigType);
            Type ttsSettingsType = surface.Supplied("RimTalk.TTS.Data.TTSSettings", RimTalkSettingsFacade.TtsSettingsType);
            Type audioPlaybackServiceType = surface.Type("RimTalk.TTS.Service.AudioPlaybackService");

            PropertyInfo isEnabledProperty = surface.Property(ttsConfigType, "IsEnabled");
            PropertyInfo settingsProperty = surface.Property(ttsConfigType, "Settings");
            PropertyInfo supplierProperty = surface.Property(ttsSettingsType, "Supplier");
            MethodInfo isCurrentlyPlayingMethod = surface.Method(audioPlaybackServiceType, "IsCurrentlyPlaying", Type.EmptyTypes);

            if (!surface.Ready)
            {
                return false;
            }

            ttsConfigIsEnabledProperty = isEnabledProperty;
            ttsConfigSettingsProperty = settingsProperty;
            ttsSettingsSupplierProperty = supplierProperty;
            audioPlaybackIsCurrentlyPlayingMethod = isCurrentlyPlayingMethod;
            return true;
        }

        // Shared: RimTalk's overlay-visibility flag, read through Settings.Get() the way its own
        // Overlay/DebugWindow read it. A Channel.User line is only ever visible in the overlay.

        private static readonly LazyReflectionGate settingsGate =
            new LazyReflectionGate("RimTalk compat (overlay-visibility probe)", ResolveSettingsReflection);

        private static FieldInfo overlayEnabledField;

        private static bool ResolveSettingsReflection(ReflectionSurface surface)
        {
            FieldInfo overlayField = surface.Field(surface.Type("RimTalk.RimTalkSettings"), "OverlayEnabled");

            if (!surface.Ready || !RimTalkSettingsFacade.Ready)
            {
                return false;
            }

            overlayEnabledField = overlayField;
            return true;
        }

        /// <summary>
        /// Fail-safe false: if this reflection breaks, lines still reach the ring buffer through
        /// <see cref="NarrativeFeed.Publish"/> but are never auto-announced, rather than guessing
        /// that a sighted player saw something unconfirmed.
        /// </summary>
        private static bool IsOverlayEnabled()
        {
            if (!settingsGate.Ensure())
            {
                return false;
            }

            try
            {
                object settingsInstance = RimTalkSettingsFacade.Get();
                return settingsInstance != null && overlayEnabledField.GetValue(settingsInstance) is bool enabled && enabled;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"RimTalk compat: overlay-enabled probe failed: {ex.Message}");
                return false;
            }
        }

        // Dialogue Log button support: reuses the cached Settings.Get()/OverlayEnabled reflection
        // above for "Toggle chat overlay", plus a standalone probe for "Open RimTalk settings".

        /// <summary>Public twin of <see cref="IsOverlayEnabled"/> for the Dialogue Log's toggle row; false means the reflection failed and the caller hides the button.</summary>
        internal static bool TryGetOverlayEnabled(out bool enabled)
        {
            enabled = false;
            if (!settingsGate.Ensure())
            {
                return false;
            }
            try
            {
                object settingsInstance = RimTalkSettingsFacade.Get();
                if (settingsInstance == null)
                {
                    return false;
                }
                enabled = overlayEnabledField.GetValue(settingsInstance) is bool b && b;
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"RimTalk compat: overlay-enabled read failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Sets <c>RimTalkSettings.OverlayEnabled</c> and persists through its own <c>Write()</c>,
        /// the same field-then-Write() pair TogglePatch's plain-click branch and the gear
        /// dropdown's checkbox use. <c>RimTalkSettings</c> extends the compile-time-referenceable
        /// <c>Verse.ModSettings</c>, so <c>Write()</c> needs no reflection once the instance is in
        /// hand.
        /// </summary>
        internal static bool TrySetOverlayEnabled(bool value)
        {
            if (!settingsGate.Ensure())
            {
                return false;
            }
            try
            {
                object settingsInstance = RimTalkSettingsFacade.Get();
                if (settingsInstance == null)
                {
                    return false;
                }
                // MUTATION-C: mirrors TogglePatch.cs's own plain-click branch verbatim
                // (rimTalkSettings.OverlayEnabled = overlayEnabled; ((ModSettings)rimTalkSettings).Write();,
                // Overlay.cs's gear-dropdown IsEnabled checkbox does the identical field-then-Write()
                // for a different field). RimTalkSettings.OverlayEnabled is a bare public field with
                // no gated setter method, so no A/B vehicle exists beyond reproducing that exact pair.
                overlayEnabledField.SetValue(settingsInstance, value);
                if (settingsInstance is ModSettings modSettings)
                {
                    modSettings.Write();
                }
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"RimTalk compat: overlay-enabled write failed: {ex.Message}");
                return false;
            }
        }

        private static readonly LazyReflectionGate modTypeGate =
            new LazyReflectionGate("RimTalk compat (mod-settings opener)", ResolveModType);

        private static Type rimTalkModType;

        private static bool ResolveModType(ReflectionSurface surface)
        {
            Type modType = surface.Supplied("RimTalk.Settings", RimTalkSettingsFacade.SettingsType);

            if (!surface.Ready)
            {
                ModLogger.Warning("RimTalk compat: RimTalk.Settings (the Mod subclass) not found, mod-settings opener disabled.");
                return false;
            }

            rimTalkModType = modType;
            return true;
        }

        /// <summary>
        /// Opens RimTalk's mod-settings dialog as TogglePatch's shift-click and the gear
        /// dropdown's "Mod Settings" button do: resolve the <c>RimTalk.Settings</c> Mod instance
        /// through the non-generic <see cref="LoadedModManager.GetMod(Type)"/> overload and add a
        /// real <see cref="Dialog_ModSettings"/>.
        /// </summary>
        internal static bool TryOpenModSettings()
        {
            if (!modTypeGate.Ensure())
            {
                return false;
            }
            try
            {
                Mod mod = LoadedModManager.GetMod(rimTalkModType);
                if (mod == null)
                {
                    return false;
                }
                Find.WindowStack.Add(new Dialog_ModSettings(mod));
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"RimTalk compat: mod-settings open failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>ToolbarModifierRegistry entry point for the "rimtalk.toggle" shift variant.</summary>
        private static void ActivateOpenModSettings()
        {
            if (!TryOpenModSettings())
            {
                TolkHelper.SpeakData((string)"RimWorldAccess.Narrative.DialogueLog.ActionFailed".Translate());
            }
        }

        // Concern 2 (P2): overlay-only RimTalk lines, on a PlayLog.Add postfix that runs after
        // Bubbles' own so P1 gets first refusal.

        private static void RegisterP2(Harmony harmony)
        {
            try
            {
                MethodInfo playLogAddMethod = AccessTools.Method(typeof(PlayLog), "Add", new[] { typeof(LogEntry) });
                if (playLogAddMethod == null)
                {
                    ModLogger.Warning("RimTalk compat: Verse.PlayLog.Add not found, overlay-only line producer skipped.");
                    return;
                }

                harmony.Patch(playLogAddMethod,
                    postfix: new HarmonyMethod(typeof(RimTalkNarrativeCompat), nameof(PlayLogAddPostfix)) { priority = Priority.Low });
            }
            catch (Exception ex)
            {
                ModLogger.Error($"RimTalk compat (overlay-only producer) registration failed: {ex.Message}");
            }
        }

        private static void PlayLogAddPostfix(LogEntry entry)
        {
            try
            {
                // Checked by full name so the RimTalk assembly is never referenced at compile
                // time.
                if (entry.GetType().FullName != "RimTalk.PlayLogEntry_RimTalkInteraction")
                {
                    return;
                }

                // PlayLogEntry_RimTalkInteraction extends the vanilla PlayLogEntry_Interaction, so
                // the inherited GetConcerns() override carries initiator/recipient and no
                // reflection into RimTalk's own properties is needed.
                if (!(entry is PlayLogEntry_Interaction interactionEntry))
                {
                    return;
                }

                Pawn initiator = null;
                Pawn recipient = null;
                foreach (Thing concern in interactionEntry.GetConcerns())
                {
                    if (!(concern is Pawn pawn))
                    {
                        continue;
                    }
                    if (initiator == null)
                    {
                        initiator = pawn;
                    }
                    else if (recipient == null)
                    {
                        recipient = pawn;
                    }
                }

                if (initiator == null)
                {
                    return;
                }

                string text = entry.ToGameStringFromPOV(initiator, false);
                if (string.IsNullOrEmpty(text))
                {
                    return;
                }

                var narrativeEvent = new NarrativeEvent(
                    initiator,
                    recipient,
                    text,
                    NarrativeSource.RimTalkLine,
                    entry.GetUniqueLoadID(),
                    vocalizedByTts: TtsWillVocalize())
                {
                    AnnounceEligible = IsOverlayEnabled()
                };

                // A no-op when P1 already published this entry, which is correct whenever a
                // bubble actually appeared for it.
                NarrativeFeed.Publish(narrativeEvent);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"RimTalk compat (overlay-only producer) postfix failed: {ex.Message}");
            }
        }

        // Concern 3 (P3): player-typed lines, read from ApiHistory on Overlay.OnLogUpdated.

        private static MethodInfo apiHistoryGetAllMethod;
        private static PropertyInfo apiLogIdProperty;
        private static PropertyInfo apiLogNameProperty;
        private static PropertyInfo apiLogResponseProperty;
        private static PropertyInfo apiLogSpokenTickProperty;
        private static PropertyInfo apiLogChannelProperty;
        private static PropertyInfo apiLogConversationIdProperty;
        private static PropertyInfo apiLogTalkRequestProperty;
        private static PropertyInfo talkRequestInitiatorProperty;
        private static MethodInfo cacheGetPlayerMethod;
        private static object channelUserValue;
        private static EventInfo overlayOnLogUpdatedEvent;
        private static Action overlayLogUpdatedHandler;

        /// <summary>Guids of ApiLog entries already examined, so a re-fired OnLogUpdated cannot re-announce a line.</summary>
        private static readonly HashSet<Guid> seenPlayerLineIds = new HashSet<Guid>();

        private static void RegisterP3()
        {
            try
            {
                var surface = new ReflectionSurface("RimTalk compat (player-line producer)");

                Type apiHistoryType = surface.Type("RimTalk.Data.ApiHistory");
                Type apiLogType = surface.Type("RimTalk.Data.ApiLog");
                Type channelType = surface.Type("RimTalk.Source.Data.Channel");
                Type talkRequestType = surface.Type("RimTalk.Data.TalkRequest");
                Type cacheType = surface.Type("RimTalk.Data.Cache");
                Type overlayType = surface.Type("RimTalk.UI.Overlay");

                MethodInfo getAllMethod = surface.Method(apiHistoryType, "GetAll", Type.EmptyTypes);
                PropertyInfo idProperty = surface.Property(apiLogType, "Id");
                PropertyInfo nameProperty = surface.Property(apiLogType, "Name");
                PropertyInfo responseProperty = surface.Property(apiLogType, "Response");
                PropertyInfo spokenTickProperty = surface.Property(apiLogType, "SpokenTick");
                PropertyInfo channelProperty = surface.Property(apiLogType, "Channel");
                PropertyInfo conversationIdProperty = surface.Property(apiLogType, "ConversationId");
                PropertyInfo talkRequestProperty = surface.Property(apiLogType, "TalkRequest");
                PropertyInfo initiatorProperty = surface.Property(talkRequestType, "Initiator");
                MethodInfo getPlayerMethod = surface.Method(cacheType, "GetPlayer", Type.EmptyTypes);
                EventInfo logUpdatedEvent = overlayType?.GetEvent("OnLogUpdated", BindingFlags.Public | BindingFlags.Static);

                if (!surface.Ready || logUpdatedEvent == null)
                {
                    return;
                }

                if (!Enum.IsDefined(channelType, "User"))
                {
                    ModLogger.Warning("RimTalk compat: Channel.User not found, player-line producer skipped.");
                    return;
                }

                apiHistoryGetAllMethod = getAllMethod;
                apiLogIdProperty = idProperty;
                apiLogNameProperty = nameProperty;
                apiLogResponseProperty = responseProperty;
                apiLogSpokenTickProperty = spokenTickProperty;
                apiLogChannelProperty = channelProperty;
                apiLogConversationIdProperty = conversationIdProperty;
                apiLogTalkRequestProperty = talkRequestProperty;
                talkRequestInitiatorProperty = initiatorProperty;
                cacheGetPlayerMethod = getPlayerMethod;
                channelUserValue = Enum.Parse(channelType, "User");
                overlayOnLogUpdatedEvent = logUpdatedEvent;

                overlayLogUpdatedHandler = OnOverlayLogUpdated;
                logUpdatedEvent.AddEventHandler(null, overlayLogUpdatedHandler);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"RimTalk compat (player-line producer) registration failed: {ex.Message}");
            }
        }

        private static void OnOverlayLogUpdated()
        {
            try
            {
                if (apiHistoryGetAllMethod == null)
                {
                    return;
                }

                if (!(apiHistoryGetAllMethod.Invoke(null, null) is IEnumerable allLogs))
                {
                    return;
                }

                Pawn playerPawn = cacheGetPlayerMethod.Invoke(null, null) as Pawn;

                foreach (object apiLog in allLogs)
                {
                    object channelValue = apiLogChannelProperty.GetValue(apiLog);
                    if (channelValue == null || !channelValue.Equals(channelUserValue))
                    {
                        continue;
                    }

                    if (!(apiLogSpokenTickProperty.GetValue(apiLog) is int spokenTick) || spokenTick <= 0)
                    {
                        continue;
                    }

                    if (!(apiLogIdProperty.GetValue(apiLog) is Guid id) || !seenPlayerLineIds.Add(id))
                    {
                        continue;
                    }

                    // A Channel.User entry is genuinely player-typed only when its
                    // TalkRequest.Initiator is the synthetic player pawn; see the class header.
                    object talkRequest = apiLogTalkRequestProperty.GetValue(apiLog);
                    Pawn initiator = talkRequest != null ? talkRequestInitiatorProperty.GetValue(talkRequest) as Pawn : null;
                    if (initiator == null || playerPawn == null || !ReferenceEquals(initiator, playerPawn))
                    {
                        continue;
                    }

                    string text = apiLogResponseProperty.GetValue(apiLog) as string;
                    if (string.IsNullOrEmpty(text))
                    {
                        continue;
                    }

                    int conversationId = apiLogConversationIdProperty.GetValue(apiLog) is int cid ? cid : -1;

                    var narrativeEvent = new NarrativeEvent(
                        playerPawn,
                        null,
                        text,
                        NarrativeSource.PlayerLine,
                        id.ToString(),
                        // Player-typed lines never reach TalkService.CreateInteraction, so the TTS
                        // addon never touches them.
                        vocalizedByTts: false)
                    {
                        ConversationId = conversationId,
                        AnnounceEligible = IsOverlayEnabled(),
                    };

                    NarrativeFeed.Publish(narrativeEvent);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error($"RimTalk compat (player-line producer) OnLogUpdated handler failed: {ex.Message}");
            }
        }

        /// <summary>Session-boundary reset: ApiHistory is wiped on new game or load, so the seen-ids set is too.</summary>
        public static void Reset()
        {
            seenPlayerLineIds.Clear();
        }

        // Concern 4: the "Rim Talk Debug" main button opens a MainTabWindow whose PostOpen
        // silently toggles OverlayEnabled, writes settings and closes itself, so the toggle would
        // otherwise be inaudible. Announce-only; the toggle itself is untouched.

        private static void RegisterOverlayToggleAnnouncement(Harmony harmony)
        {
            try
            {
                Type overlayTabLauncherType = AccessTools.TypeByName("RimTalk.UI.OverlayTabLauncher");
                if (overlayTabLauncherType == null)
                {
                    ModLogger.Warning("RimTalk compat: OverlayTabLauncher not found, overlay-toggle announcement skipped.");
                    return;
                }

                MethodInfo postOpenMethod = AccessTools.Method(overlayTabLauncherType, "PostOpen", Type.EmptyTypes);
                if (postOpenMethod == null)
                {
                    ModLogger.Warning("RimTalk compat: OverlayTabLauncher.PostOpen not found, overlay-toggle announcement skipped.");
                    return;
                }

                harmony.Patch(postOpenMethod,
                    postfix: new HarmonyMethod(typeof(RimTalkNarrativeCompat), nameof(OverlayTabLauncherPostOpenPostfix)));
            }
            catch (Exception ex)
            {
                ModLogger.Error($"RimTalk compat (overlay-toggle announcement) registration failed: {ex.Message}");
            }
        }

        private static void OverlayTabLauncherPostOpenPostfix()
        {
            try
            {
                if (!settingsGate.Ensure())
                {
                    return;
                }
                TolkHelper.SpeakData((string)(IsOverlayEnabled()
                    ? "RimWorldAccess.Compat.RimTalk.OverlayOn".Translate()
                    : "RimWorldAccess.Compat.RimTalk.OverlayOff".Translate()));
            }
            catch (Exception ex)
            {
                ModLogger.Error($"RimTalk compat (overlay-toggle announcement) postfix failed: {ex.Message}");
            }
        }
    }
}
