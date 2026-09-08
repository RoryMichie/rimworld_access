using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Reflection facade for <c>RTClient.Dialogs.DLG_Chat</c>, <c>RTClient.PacketManagers.PM_Chat</c>,
    /// and <c>RTClient.Managers.SessionManager</c> (missing type or member = the mod is absent, or
    /// its shipped DLL drifted — silent decline), plus the two Harmony guards
    /// <see cref="RimworldTogetherChatScope"/>'s class remarks require: the draw-pass mask for
    /// <c>DLG_Chat.CheckForEnterKey</c>'s raw Enter poll, and the native-focus/live-mirror upkeep
    /// the field's <c>TextFieldEditSession</c> needs every pass.
    /// </summary>
    internal static class RimworldTogetherChatScopeCompat
    {
        private static readonly Type dlgChatType;
        private static readonly PropertyInfo chatMessagesProperty;
        private static readonly PropertyInfo currentChatInputProperty;
        private static readonly PropertyInfo shouldScrollChatProperty;
        private static readonly PropertyInfo shouldPlaySoundsProperty;
        private static readonly MethodInfo sendMessageMethod;
        private static readonly PropertyInfo currentServerPlayersProperty;
        private static readonly PropertyInfo currentNetworkStateProperty;
        private static readonly bool ready;

        // DLG_Chat is a singleton (its own constructor sets the static Instance), so one static
        // reference to whichever scope instance is currently attached is enough to tell the guard
        // patch below which window this scope owns, without a per-window dictionary.
        private static RimworldTogetherChatScope attachedScope;

        static RimworldTogetherChatScopeCompat()
        {
            var surface = new ReflectionSurface("RimworldTogetherChatScopeCompat");

            dlgChatType = surface.Type("RTClient.Dialogs.DLG_Chat");
            chatMessagesProperty = surface.Property(dlgChatType, "ChatMessages");
            currentChatInputProperty = surface.Property(dlgChatType, "CurrentChatInput");
            shouldScrollChatProperty = surface.Property(dlgChatType, "ShouldScrollChat");
            shouldPlaySoundsProperty = surface.Property(dlgChatType, "ShouldPlaySounds");

            Type pmChatType = surface.Type("RTClient.PacketManagers.PM_Chat");
            sendMessageMethod = surface.Method(pmChatType, "SendMessage", new[] { typeof(string) });

            Type sessionManagerType = surface.Type("RTClient.Managers.SessionManager");
            currentServerPlayersProperty = surface.Property(sessionManagerType, "CurrentServerPlayers");
            currentNetworkStateProperty = surface.Property(sessionManagerType, "CurrentNetworkState");

            ready = surface.Ready;
        }

        internal static IList GetChatMessages()
        {
            return ready ? chatMessagesProperty.GetValue(null) as IList : null;
        }

        internal static string GetCurrentChatInput()
        {
            return ready ? (currentChatInputProperty.GetValue(null) as string ?? "") : "";
        }

        internal static void SetCurrentChatInput(string value)
        {
            if (ready)
            {
                // MUTATION-C: mirrors DLG_Chat.DrawInput's own return-assign
                // (`CurrentChatInput = text` after its length guard); no invocable vanilla
                // setter method exists for a bare auto-property with a public setter.
                currentChatInputProperty.SetValue(null, value ?? "");
            }
        }

        internal static bool GetShouldScrollChat()
        {
            return ready && (bool)shouldScrollChatProperty.GetValue(null);
        }

        internal static void SetShouldScrollChat(bool value)
        {
            if (ready)
            {
                // MUTATION-C: mirrors DLG_Chat.DrawPinCheckbox's own inline toggle delegate
                // (a bare public-setter property with no gated Toggle method).
                shouldScrollChatProperty.SetValue(null, value);
            }
        }

        internal static bool GetShouldPlaySounds()
        {
            return ready && (bool)shouldPlaySoundsProperty.GetValue(null);
        }

        internal static void SetShouldPlaySounds(bool value)
        {
            if (ready)
            {
                // MUTATION-C: mirrors DLG_Chat.DrawMuteCheckbox's own inline toggle delegate,
                // the same shape as SetShouldScrollChat above.
                shouldPlaySoundsProperty.SetValue(null, value);
            }
        }

        /// <summary>
        /// The mod's own connection gate, read from <c>SessionManager.CurrentNetworkState</c>
        /// (its <c>ClientNetworkState</c> enum, where 0 is Disconnected). Sending with no live
        /// endpoint throws inside the mod: its own raw Enter poll is unguarded, but a player
        /// dropped mid-session keeps the chat panel open, so the keyboard path checks first.
        /// </summary>
        internal static bool IsConnected()
        {
            if (!ready)
            {
                return false;
            }
            object state = currentNetworkStateProperty.GetValue(null);
            return state != null && (int)state != 0;
        }

        internal static void SendMessage(string message)
        {
            if (!IsConnected())
            {
                return;
            }
            try
            {
                sendMessageMethod.Invoke(null, new object[] { message });
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Rimworld Together chat send", ex);
            }
        }

        /// <summary>-1 when not ready or not yet known (the mod's own default before a session connects), matching <c>int.MinValue</c> folded to "unknown" by the caller.</summary>
        internal static int GetCurrentServerPlayers()
        {
            return ready ? (int)currentServerPlayersProperty.GetValue(null) : -1;
        }

        /// <summary>Registers the scope and installs its draw-pass guard. No-op unless both the mod is active and every required member above resolved.</summary>
        public static void Register(Harmony harmony)
        {
            if (!ready)
            {
                return;
            }

            ScopeForWindow.Register(dlgChatType, delegate (Window w)
            {
                attachedScope = new RimworldTogetherChatScope(w);
                return attachedScope;
            });
            PatchGuards(harmony);
        }

        /// <summary>
        /// One patch on DLG_Chat's own DoWindowContents (the declaring type — never the inherited
        /// Window base): masks the raw Enter poll unconditionally while this scope owns the window
        /// (see the scope's class remarks), and drives the field's native-focus/live-mirror upkeep
        /// every pass.
        /// </summary>
        private static void PatchGuards(Harmony harmony)
        {
            try
            {
                MethodInfo doWindowContents = AccessTools.Method(dlgChatType, "DoWindowContents");
                if (doWindowContents == null)
                {
                    ModLogger.Error("RimworldTogetherChatScopeCompat: could not resolve DLG_Chat.DoWindowContents; declining draw-pass guard.");
                    return;
                }
                harmony.Patch(doWindowContents,
                    prefix: new HarmonyMethod(typeof(RimworldTogetherChatScopeCompat), nameof(DrawPrefix)),
                    postfix: new HarmonyMethod(typeof(RimworldTogetherChatScopeCompat), nameof(DrawPostfix)));
            }
            catch (Exception ex)
            {
                ModLogger.Error("RimworldTogetherChatScopeCompat: PatchGuards failed: " + ex.Message);
            }
        }

        public static void DrawPrefix(object __instance)
        {
            try
            {
                RimworldTogetherChatScope scope = attachedScope;
                Window window = __instance as Window;
                if (scope != null && window != null && scope.Owns(window))
                {
                    // Unconditional: this scope drives Enter entirely itself (see the scope's class
                    // remarks), so the window's own raw in-draw Return poll must never independently fire.
                    TextFieldRawPollGuard.MaskAcceptPoll(true);
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("RimworldTogetherChatScopeCompat draw guard", ex);
            }
        }

        public static void DrawPostfix(object __instance)
        {
            try
            {
                TextFieldRawPollGuard.RestoreAcceptPoll();
                RimworldTogetherChatScope scope = attachedScope;
                Window window = __instance as Window;
                if (scope != null && window != null && scope.Owns(window))
                {
                    scope.OnGuiPass();
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("RimworldTogetherChatScopeCompat draw guard", ex);
            }
        }
    }
}
