using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Manual storage linking selection mode: navigation uses the map cursor, Space toggles the
    /// storage at the cursor, Enter confirms.
    /// </summary>
    public static class ShelfLinkingState
    {
        /// <summary>Whether storage linking selection mode is active.</summary>
        public static bool IsActive { get; private set; }

        /// <summary>The map where selection is happening.</summary>
        private static Map currentMap;

        /// <summary>The source storage that initiated linking mode.</summary>
        private static IStorageGroupMember sourceStorage;

        /// <summary>The storage group tag used for compatibility checks.</summary>
        private static string sourceTag;

        /// <summary>Storage members currently selected for linking.</summary>
        private static HashSet<IStorageGroupMember> selectedStorage;

        /// <summary>Opens manual storage linking selection mode from <paramref name="source"/>.</summary>
        public static void Open(IStorageGroupMember source)
        {
            if (!GuardHelper.RequireMap(SpeechPriority.High)) return;

            if (source == null)
            {
                TolkHelper.Speak("RimWorldAccess.Building.Shelf.NoStorageSelected".Loc(), SpeechPriority.High);
                return;
            }

            currentMap = Find.CurrentMap;
            sourceStorage = source;
            sourceTag = source.StorageGroupTag;
            selectedStorage = new HashSet<IStorageGroupMember>();

            // Pre-select the source storage.
            selectedStorage.Add(source);

            // Clear game selection and select the source thing.
            if (source is Thing sourceThing)
            {
                Find.Selector.ClearSelection();
                Find.Selector.Select(sourceThing, playSound: false, forceDesignatorDeselect: false);
            }

            IsActive = true;

            string sourceLabel = ShelfLinkingHelper.GetStorageLabel(source);
            TolkHelper.Speak("RimWorldAccess.Building.Shelf.OpenPrompt".Loc(sourceLabel), SpeechPriority.High);
        }

        /// <summary>
        /// Clears session state with no announcement and no selection mutation; Close() delegates
        /// here for the field-clearing part. Used by StateResetRegistry at a session boundary, where
        /// announcing a cancellation and touching Find.Selector would be wrong.
        /// </summary>
        public static void Reset()
        {
            IsActive = false;
            currentMap = null;
            sourceStorage = null;
            sourceTag = null;
            selectedStorage = null;
        }

        /// <summary>Closes storage linking selection mode without linking.</summary>
        public static void Close()
        {
            Reset();

            Find.Selector.ClearSelection();

            TolkHelper.Speak("RimWorldAccess.Building.Shelf.LinkingCancelled".Loc(), SpeechPriority.Normal);
        }

        /// <summary>
        /// Toggles selection of the storage at the current cursor position. Arrow keys are never
        /// handled here: they fall through to map navigation, which the scope's non-modal posture
        /// reproduces with no claim.
        /// </summary>
        internal static void ToggleStorageAtCursor()
        {
            IntVec3 cursorPos = MapNavigationState.CurrentCursorPosition;

            if (!cursorPos.InBounds(currentMap))
            {
                TolkHelper.Speak("RimWorldAccess.Building.Shelf.InvalidPosition".Loc(), SpeechPriority.Normal);
                return;
            }

            var storage = ShelfLinkingHelper.GetStorageAt(cursorPos, sourceTag, currentMap);

            if (storage == null)
            {
                // Storage exists here but carries the wrong tag.
                var anyStorage = ShelfLinkingHelper.GetAllStorageAt(cursorPos, currentMap);
                if (anyStorage.Count > 0)
                {
                    string label = ShelfLinkingHelper.GetStorageLabel(anyStorage[0]);
                    TolkHelper.Speak("RimWorldAccess.Building.Shelf.IncompatibleStorageType".Loc(label), SpeechPriority.Normal);
                }
                else
                {
                    TolkHelper.Speak("RimWorldAccess.Building.Shelf.NoStorageHere".Loc(), SpeechPriority.Normal);
                }
                return;
            }

            // The source may not be deselected.
            if (storage == sourceStorage)
            {
                TolkHelper.Speak("RimWorldAccess.Building.Shelf.SourceCannotDeselect".Loc(), SpeechPriority.Normal);
                return;
            }

            string storageLabel = ShelfLinkingHelper.GetStorageLabel(storage);

            if (selectedStorage.Contains(storage))
            {
                selectedStorage.Remove(storage);
                if (storage is Thing thing)
                {
                    Find.Selector.Deselect(thing);
                }
                TolkHelper.Speak("RimWorldAccess.Building.Shelf.StorageDeselected".Loc(storageLabel, selectedStorage.Count), SpeechPriority.Normal);
            }
            else
            {
                selectedStorage.Add(storage);
                if (storage is Thing thing)
                {
                    Find.Selector.Select(thing, playSound: false, forceDesignatorDeselect: false);
                }
                TolkHelper.Speak("RimWorldAccess.Building.Shelf.StorageSelected".Loc(storageLabel, selectedStorage.Count), SpeechPriority.Normal);
            }
        }

        /// <summary>
        /// Confirms selection and links all selected storage. Stays IsActive while the
        /// already-linked-items confirmation raises a real Dialog_MessageBox — genuine coexistence,
        /// not a close-before-open handoff. The dialog keeps the keyboard because
        /// ShelfLinkingScopeMirror stands down while a real dialog with an attached scope is up;
        /// stack order alone would NOT protect it, since the mirror's per-frame Push re-floats an
        /// already-stacked scope to the top.
        /// </summary>
        internal static void ConfirmSelection()
        {
            if (selectedStorage.Count <= 1)
            {
                TolkHelper.Speak("RimWorldAccess.Building.Shelf.SelectAtLeastOneOther".Loc(), SpeechPriority.High);
                return;
            }

            // Items already in different groups need confirmation.
            var alreadyLinked = ShelfLinkingHelper.GetAlreadyLinkedItems(
                selectedStorage.ToList(), sourceStorage.Group);

            if (alreadyLinked.Count > 0)
            {
                ShelfLinkingConfirmDialog.Show(
                    alreadyLinked,
                    onYes: () => PerformLinking(),
                    onNo: () =>
                    {
                        TolkHelper.Speak("RimWorldAccess.Building.Shelf.LinkingCancelledStillSelecting".Loc(), SpeechPriority.Normal);
                    });
            }
            else
            {
                PerformLinking();
            }
        }

        /// <summary>Performs the actual linking operation.</summary>
        private static void PerformLinking()
        {
            int count = selectedStorage.Count;
            string countStr = ShelfLinkingHelper.FormatStorageCount(selectedStorage.ToList());

            bool success = ShelfLinkingHelper.LinkStorageItems(
                sourceStorage, selectedStorage.ToList(), currentMap);

            IsActive = false;
            currentMap = null;
            var source = sourceStorage;
            sourceStorage = null;
            sourceTag = null;
            selectedStorage = null;

            Find.Selector.ClearSelection();

            if (success)
            {
                TolkHelper.Speak("RimWorldAccess.Building.Shelf.LinkedCount".Loc(countStr), SpeechPriority.High);
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Building.Shelf.LinkingFailed".Loc(), SpeechPriority.High);
            }
        }

        /// <summary>The currently selected storage items, for visual feedback.</summary>
        public static IEnumerable<IStorageGroupMember> GetSelectedStorage()
        {
            if (!IsActive || selectedStorage == null)
                yield break;

            foreach (var storage in selectedStorage)
            {
                yield return storage;
            }
        }

        /// <summary>The source storage, for visual feedback.</summary>
        public static IStorageGroupMember GetSourceStorage()
        {
            return IsActive ? sourceStorage : null;
        }

        /// <summary>The storage tag used for compatibility, for external checks.</summary>
        public static string GetSourceTag()
        {
            return IsActive ? sourceTag : null;
        }

        /// <summary>Whether any selected storage occupies <paramref name="position"/>.</summary>
        public static bool IsStorageSelectedAt(IntVec3 position)
        {
            if (!IsActive || selectedStorage == null || currentMap == null)
                return false;

            foreach (var storage in selectedStorage)
            {
                if (storage is Thing thing && thing.Spawned)
                {
                    if (thing.OccupiedRect().Contains(position))
                        return true;
                }
            }
            return false;
        }
    }
}
