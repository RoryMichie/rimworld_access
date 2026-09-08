using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Speaks incoming multiplayer chat lines the moment they land.
    /// <c>PM_Chat.AddMessageToChat</c> is the single arrival point for every chat packet
    /// (already on the main thread: <c>ClientNetwork.OnReadPacket</c> marshals all non-handshake
    /// packets through <c>MainThreadManager</c>), and it appends the fully formatted line --
    /// timestamp, colored username, parsed message -- to <c>DLG_Chat.ChatMessages</c>.
    /// A sighted player only sees that line while the chat window is open; when it is closed the
    /// mod plays its own receive cue instead, so this postfix stays silent there (visual parity,
    /// no invented notification channel).
    /// </summary>
    internal static class RimworldTogetherChatCompat
    {
        private static PropertyInfo isDialogOpenProperty;
        private static PropertyInfo chatMessagesProperty;

        public static void Register(Harmony harmony)
        {
            var surface = new ReflectionSurface("RimworldTogetherChatCompat");
            Type pmChatType = surface.Type("RTClient.PacketManagers.PM_Chat");
            Type dlgChatType = surface.Type("RTClient.Dialogs.DLG_Chat");
            MethodInfo addMessageMethod = surface.Method(pmChatType, "AddMessageToChat");
            PropertyInfo isDialogOpen = surface.Property(dlgChatType, "IsDialogOpen");
            PropertyInfo chatMessages = surface.Property(dlgChatType, "ChatMessages");
            if (!surface.Ready)
            {
                return;
            }

            isDialogOpenProperty = isDialogOpen;
            chatMessagesProperty = chatMessages;

            harmony.Patch(addMessageMethod,
                postfix: new HarmonyMethod(typeof(RimworldTogetherChatCompat), nameof(AddMessageToChatPostfix)));
        }

        private static void AddMessageToChatPostfix()
        {
            try
            {
                if (!(bool)isDialogOpenProperty.GetValue(null))
                {
                    return;
                }

                var messages = chatMessagesProperty.GetValue(null) as IList;
                if (messages == null || messages.Count == 0)
                {
                    return;
                }

                string line = messages[messages.Count - 1] as string;
                if (string.IsNullOrEmpty(line))
                {
                    return;
                }

                TolkHelper.SpeakData(line.StripTags(), SpeechPriority.Normal);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Rimworld Together chat announcement", ex);
            }
        }
    }
}
