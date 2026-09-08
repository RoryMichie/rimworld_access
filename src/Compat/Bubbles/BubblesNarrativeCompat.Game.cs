using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Narrative Feed producer: a postfix on Bubbles.Core.Bubbler.Add(LogEntry) covering
    /// both vanilla interaction bubbles and RimTalk lines whenever a bubble actually
    /// appears. A missing type or member self-disables the producer and never patches
    /// anything (shipped-DLL drift safety).
    ///
    /// Priority.Low: RimTalk hard-depends on Bubbles and patches Bubbler.Add itself
    /// (prefix suppresses vanilla bubbles it wants to replace with LLM chitchat, postfix
    /// re-queues the replacement) -- this postfix must observe the dictionary AFTER that
    /// pair has run, or it would see stale state mid-suppression.
    ///
    /// "Did a bubble actually appear" decision object: Bubbler's own private static
    /// Dictionary&lt;Pawn, List&lt;Bubble&gt;&gt; -- every survivor of Add's gates
    /// (activation toggle, world render mode, game-speed auto-hide, current map, hearing
    /// check, non-player/animal/drafted filters) is appended there as `new Bubble(pawn,
    /// entry)`; RimTalk's suppression prefix (ProcessNonRimTalkInteractions on) returns
    /// false before Add ever reaches that append. Bubbles.Core.Bubble keeps a public
    /// `LogEntry Entry` property set from its constructor, so this producer matches the
    /// postfixed entry against the LAST Bubble in the initiator's list by reference
    /// equality.
    ///
    /// Initiator/recipient selection mirrors Bubbler.Add's own two-branch type test
    /// exactly: PlayLogEntry_Interaction (initiator + recipient) or
    /// PlayLogEntry_InteractionSinglePawn (initiator only), read through their public
    /// GetConcerns() override. Any other LogEntry subtype (e.g.
    /// PlayLogEntry_InteractionWithMany) is one Bubbler.Add itself never bubbles.
    /// </summary>
    internal static class BubblesNarrativeCompat
    {
        private static FieldInfo dictionaryField;
        private static PropertyInfo bubbleEntryProperty;
        private static MethodInfo showSettingsWindowMethod;

        public static void Register(Harmony harmony)
        {
            if (!ModsConfig.IsActive("jaxe.bubbles"))
            {
                return;
            }

            RegisterSettingsWindowReader();

            // The SHIFT variant of Bubbles' own PlaySettings toggle icon: its
            // DoPlaySettingsGlobalControls postfix opens the window below on a shift click.
            // RimTalk hard-depends on Bubbles, not the reverse, so this must not be gated on
            // RimTalk's own presence.
            ToolbarModifierRegistry.Register("bubbles.toggle",
                "RimWorldAccess.Narrative.DialogueLog.OpenBubblesSettings",
                ActivateOpenSettingsWindow,
                () => showSettingsWindowMethod != null);

            try
            {
                var surface = new ReflectionSurface("Bubbles compat (narrative producer)");
                Type bubblerType = surface.Type("Bubbles.Core.Bubbler");
                Type bubbleType = surface.Type("Bubbles.Core.Bubble");

                MethodInfo bubblerAddMethod = surface.Method(bubblerType, "Add", new[] { typeof(LogEntry) });
                FieldInfo dictionary = surface.Field(bubblerType, "Dictionary");
                PropertyInfo entryProperty = surface.Property(bubbleType, "Entry");

                if (!surface.Ready)
                {
                    return;
                }

                dictionaryField = dictionary;
                bubbleEntryProperty = entryProperty;

                harmony.Patch(bubblerAddMethod,
                    postfix: new HarmonyMethod(typeof(BubblesNarrativeCompat), nameof(Postfix)) { priority = Priority.Low });
            }
            catch (Exception ex)
            {
                ModLogger.Error($"Bubbles compat (narrative producer) registration failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Bubbles offers its settings twice: on its page under Options &gt;
        /// Mod settings, and as a standalone window a mouse user opens by
        /// shift-clicking the bubble toggle in the play-settings row. Both
        /// draw the identical content through <c>SettingsEditor.DrawSettings</c>,
        /// so the first route already reads correctly through the generic
        /// reader; the standalone window does not, because it is a bare
        /// <c>Window</c> subclass that never sets the modal flag the reader
        /// takes as a window's declaration that it is a dialog. Opting the
        /// type in by name is all that is needed — the reader itself has no
        /// Bubbles-specific behaviour, and it matters because a sighted
        /// player at the same keyboard can open that window at any time.
        /// </summary>
        private static void RegisterSettingsWindowReader()
        {
            if (!Shell.ScopeForWindow.TryRegisterGenericReaderForWindow("Bubbles.Configuration.SettingsEditor+Dialog"))
            {
                ModLogger.Warning("Bubbles compat: SettingsEditor+Dialog not found, its standalone settings window will not be read.");
            }

            Type settingsEditorType = AccessTools.TypeByName("Bubbles.Configuration.SettingsEditor");
            showSettingsWindowMethod = settingsEditorType != null
                ? AccessTools.Method(settingsEditorType, "ShowWindow", Type.EmptyTypes)
                : null;
            if (showSettingsWindowMethod == null)
            {
                ModLogger.Warning("Bubbles compat: SettingsEditor.ShowWindow not found, its settings-window opener will not be offered.");
            }
        }

        /// <summary>Vehicle A: the exact static call Bubbles' own shift-click branch makes, `SettingsEditor.ShowWindow()`.</summary>
        internal static bool TryOpenSettingsWindow()
        {
            if (showSettingsWindowMethod == null)
            {
                return false;
            }
            try
            {
                showSettingsWindowMethod.Invoke(null, null);
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"Bubbles compat: settings-window open failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>ToolbarModifierRegistry entry point for the "bubbles.toggle" shift variant.</summary>
        private static void ActivateOpenSettingsWindow()
        {
            if (!TryOpenSettingsWindow())
            {
                TolkHelper.SpeakData((string)"RimWorldAccess.Narrative.DialogueLog.ActionFailed".Translate());
            }
        }

        private static void Postfix(LogEntry entry)
        {
            try
            {
                if (!TryGetInitiatorAndRecipient(entry, out Pawn initiator, out Pawn recipient))
                {
                    return;
                }

                if (!BubbleAppearedFor(initiator, entry))
                {
                    return;
                }

                string text = entry.ToGameStringFromPOV(initiator, false);
                if (string.IsNullOrEmpty(text))
                {
                    return;
                }

                bool isRimTalkLine = entry.GetType().FullName == "RimTalk.PlayLogEntry_RimTalkInteraction";
                var narrativeEvent = new NarrativeEvent(
                    initiator,
                    recipient,
                    text,
                    isRimTalkLine ? NarrativeSource.RimTalkLine : NarrativeSource.Interaction,
                    entry.GetUniqueLoadID(),
                    // The TTS addon only ever vocalizes RimTalk-generated lines (it prefixes
                    // TalkService.CreateInteraction, which vanilla interactions never reach) --
                    // vanilla interaction bubbles always report "not vocalized".
                    vocalizedByTts: isRimTalkLine && RimTalkNarrativeCompat.TtsWillVocalize());

                NarrativeFeed.Publish(narrativeEvent);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"Bubbles compat (narrative producer) postfix failed: {ex.Message}");
            }
        }

        private static bool TryGetInitiatorAndRecipient(LogEntry entry, out Pawn initiator, out Pawn recipient)
        {
            initiator = null;
            recipient = null;

            if (entry is PlayLogEntry_Interaction interactionEntry)
            {
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
                return initiator != null;
            }

            if (entry is PlayLogEntry_InteractionSinglePawn singlePawnEntry)
            {
                foreach (Thing concern in singlePawnEntry.GetConcerns())
                {
                    if (concern is Pawn pawn)
                    {
                        initiator = pawn;
                        break;
                    }
                }
                return initiator != null;
            }

            return false;
        }

        private static bool BubbleAppearedFor(Pawn initiator, LogEntry entry)
        {
            IDictionary dict = dictionaryField.GetValue(null) as IDictionary;
            if (dict == null || !dict.Contains(initiator))
            {
                return false;
            }

            IList bubbleList = dict[initiator] as IList;
            if (bubbleList == null || bubbleList.Count == 0)
            {
                return false;
            }

            object lastBubble = bubbleList[bubbleList.Count - 1];
            object storedEntry = bubbleEntryProperty.GetValue(lastBubble);
            return ReferenceEquals(storedEntry, entry);
        }
    }
}
