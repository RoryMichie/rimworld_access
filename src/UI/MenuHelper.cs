using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>Shared menu navigation, announcement, and treeview helpers.</summary>
    public static class MenuHelper
    {
        private static Dictionary<string, int> lastAnnouncedLevels = new Dictionary<string, int>();

        /// <summary>Formats position as "X of Y", 1-indexed; empty when the Position part is off.</summary>
        public static string FormatPosition(int index, int total)
        {
            if (!TextDialogShared.PositionPartEnabled)
                return "";
            return "RimWorldAccess.Menu.Position".Translate(index + 1, total).ToString();
        }

        /// <summary>
        /// Level suffix for a 0-indexed indent level, empty when the level has not changed since
        /// the last call under <paramref name="menuKey"/> or when it is the baseline level 1.
        /// Belongs at the END of an announcement.
        /// </summary>
        public static string GetLevelSuffix(string menuKey, int currentLevel, bool skipLevelOne = true)
        {
            if (!TextDialogShared.LevelsPartEnabled)
                return "";

            int displayLevel = currentLevel + 1; // 1-indexed for users

            if (!lastAnnouncedLevels.TryGetValue(menuKey, out int lastLevel))
                lastLevel = -1;

            if (currentLevel == lastLevel)
                return "";

            lastAnnouncedLevels[menuKey] = currentLevel;

            if (skipLevelOne && displayLevel == 1)
                return "";

            return "RimWorldAccess.Menu.LevelSuffix".Translate(displayLevel).ToString();
        }

        /// <summary>Resets level tracking for a menu; call on Open and Close.</summary>
        public static void ResetLevel(string menuKey)
        {
            lastAnnouncedLevels.Remove(menuKey);
        }

        /// <summary>
        /// Directions for <see cref="SpeakAlreadyAtEdge"/>, one canonical phrasing each.
        /// </summary>
        public enum EdgeDirection
        {
            Top,
            Bottom,
            First,
            Last,
            Minimum,
            Maximum,
            TopLevel,
            Zero,
        }

        /// <summary>Announces that navigation reached an edge and can go no further.</summary>
        public static void SpeakAlreadyAtEdge(EdgeDirection direction, SpeechPriority priority = SpeechPriority.Normal)
        {
            string key;
            switch (direction)
            {
                case EdgeDirection.Top: key = "RimWorldAccess.Edge.Top"; break;
                case EdgeDirection.Bottom: key = "RimWorldAccess.Edge.Bottom"; break;
                case EdgeDirection.First: key = "RimWorldAccess.Edge.First"; break;
                case EdgeDirection.Last: key = "RimWorldAccess.Edge.Last"; break;
                case EdgeDirection.Minimum: key = "RimWorldAccess.Edge.Minimum"; break;
                case EdgeDirection.Maximum: key = "RimWorldAccess.Edge.Maximum"; break;
                case EdgeDirection.TopLevel: key = "RimWorldAccess.Edge.TopLevel"; break;
                case EdgeDirection.Zero: key = "RimWorldAccess.Edge.Zero"; break;
                default: key = "RimWorldAccess.Edge.Generic"; break;
            }
            TolkHelper.Speak(key.Loc(), priority);
        }

        /// <summary>Non-wrapping edge tone, in place of re-announcing the row the cursor never left.</summary>
        public static void PlayEdgeTone()
        {
            SoundDefOf.ClickReject.PlayOneShotOnCamera();
        }

        /// <summary>Wrap tone: the cursor came round from one end to the other.</summary>
        public static void PlayWrapTone()
        {
            EmbeddedAudioHelper.PlayEmbeddedSound("wrap.wav", 0.3f * Prefs.VolumeUI);
        }

        /// <summary>Tones for a cursor move: edge when it went nowhere, wrap when it came round. True when the cursor moved.</summary>
        public static bool SoundMove(MoveResult result)
        {
            if (!result.Changed)
            {
                PlayEdgeTone();
                return false;
            }
            if (result.Kind == MoveKind.Wrapped)
                PlayWrapTone();
            return true;
        }

        /// <summary>Wrap tone when a match step came round the far end of the match list.</summary>
        public static void SoundMatchMove(int from, int to, int delta)
        {
            if (to != from && (delta > 0 ? to < from : to > from))
                PlayWrapTone();
        }

        /// <summary>
        /// Announces that the current column cannot be painted, with the reject sound.
        /// </summary>
        public static void SpeakCannotPaintColumn(SpeechPriority priority = SpeechPriority.Normal)
        {
            SoundDefOf.ClickReject.PlayOneShotOnCamera();
            TolkHelper.Speak("RimWorldAccess.Menu.CannotPaintColumn".Loc(), priority);
        }

        /// <summary>Next index, wrapping to the beginning when the WrapNavigation setting is on.</summary>
        public static int SelectNext(int currentIndex, int count)
        {
            return SelectNext(currentIndex, count, out _);
        }

        /// <summary>SelectNext, reporting a wrap so the caller can tone it.</summary>
        public static int SelectNext(int currentIndex, int count, out bool wrapped)
        {
            wrapped = false;
            if (count == 0) return 0;
            if (currentIndex < count - 1)
                return currentIndex + 1;

            if (RimWorldAccessMod_Settings.Settings?.WrapNavigation == true)
            {
                wrapped = count > 1;
                return 0;
            }
            return currentIndex;
        }

        /// <summary>Previous index, wrapping to the end when the WrapNavigation setting is on.</summary>
        public static int SelectPrevious(int currentIndex, int count)
        {
            return SelectPrevious(currentIndex, count, out _);
        }

        /// <summary>SelectPrevious, reporting a wrap so the caller can tone it.</summary>
        public static int SelectPrevious(int currentIndex, int count, out bool wrapped)
        {
            wrapped = false;
            if (count == 0) return 0;
            if (currentIndex > 0)
                return currentIndex - 1;

            if (RimWorldAccessMod_Settings.Settings?.WrapNavigation == true)
            {
                wrapped = count > 1;
                return count - 1;
            }
            return currentIndex;
        }

        public static int JumpToFirst()
        {
            return 0;
        }

        public static int JumpToLast(int count)
        {
            if (count == 0) return 0;
            return count - 1;
        }

        /// <summary>
        /// Parent index of a node in a flattened tree — the nearest preceding node with a lower
        /// indent level. -1 at the root.
        /// </summary>
        public static int FindParentIndex<T>(IList<T> nodes, int currentIndex, Func<T, int> getIndentLevel)
        {
            if (currentIndex <= 0 || currentIndex >= nodes.Count)
                return -1;

            int currentLevel = getIndentLevel(nodes[currentIndex]);
            if (currentLevel <= 0)
                return -1;

            for (int i = currentIndex - 1; i >= 0; i--)
            {
                if (getIndentLevel(nodes[i]) < currentLevel)
                    return i;
            }
            return -1;
        }

        /// <summary>Sibling position (1-indexed) and total count at the same level.</summary>
        public static (int position, int total) GetSiblingPosition<T>(
            IList<T> nodes, int currentIndex, Func<T, int> getIndentLevel)
        {
            if (nodes.Count == 0 || currentIndex < 0 || currentIndex >= nodes.Count)
                return (1, 1);

            int indentLevel = getIndentLevel(nodes[currentIndex]);

            int startIndex = 0;
            int endIndex = nodes.Count - 1;

            for (int i = currentIndex - 1; i >= 0; i--)
            {
                if (getIndentLevel(nodes[i]) < indentLevel)
                {
                    startIndex = i + 1;
                    break;
                }
            }

            for (int i = currentIndex + 1; i < nodes.Count; i++)
            {
                if (getIndentLevel(nodes[i]) < indentLevel)
                {
                    endIndex = i - 1;
                    break;
                }
            }

            int position = 0;
            int total = 0;
            for (int i = startIndex; i <= endIndex; i++)
            {
                if (getIndentLevel(nodes[i]) == indentLevel)
                {
                    total++;
                    if (i <= currentIndex)
                        position = total;
                }
            }

            return (position, total);
        }

        /// <summary>Index of the first sibling at the current node's indent level.</summary>
        public static int JumpToFirstSibling<T>(IList<T> nodes, int currentIndex, Func<T, int> getIndentLevel)
        {
            if (nodes.Count == 0 || currentIndex < 0 || currentIndex >= nodes.Count)
                return 0;

            int indentLevel = getIndentLevel(nodes[currentIndex]);

            for (int i = currentIndex - 1; i >= 0; i--)
            {
                if (getIndentLevel(nodes[i]) < indentLevel)
                {
                    return i + 1;
                }
            }

            // No parent: at root level, so find the first item at this indent.
            for (int i = 0; i < nodes.Count; i++)
            {
                if (getIndentLevel(nodes[i]) == indentLevel)
                    return i;
            }

            return 0;
        }

        /// <summary>Index of the last sibling at the current node's indent level.</summary>
        public static int JumpToLastSibling<T>(IList<T> nodes, int currentIndex, Func<T, int> getIndentLevel)
        {
            if (nodes.Count == 0 || currentIndex < 0 || currentIndex >= nodes.Count)
                return nodes.Count - 1;

            int indentLevel = getIndentLevel(nodes[currentIndex]);

            int lastSibling = currentIndex;
            for (int i = currentIndex + 1; i < nodes.Count; i++)
            {
                int level = getIndentLevel(nodes[i]);
                if (level < indentLevel)
                {
                    break;
                }
                if (level == indentLevel)
                {
                    lastSibling = i;
                }
            }

            return lastSibling;
        }

        /// <summary>
        /// Home key for treeview states: first sibling at the current level, or absolute first
        /// with Ctrl.
        /// </summary>
        public static void HandleTreeHomeKey<T>(
            IList<T> items,
            ref int selectedIndex,
            Func<T, int> getIndentLevel,
            bool ctrlPressed,
            Action onNavigate)
            where T : class
        {
            if (items == null || items.Count == 0)
                return;

            selectedIndex = ctrlPressed ? 0 : JumpToFirstSibling(items, selectedIndex, getIndentLevel);
            onNavigate?.Invoke();
        }

        /// <summary>
        /// End key for treeview states: last visible descendant of an expanded node with
        /// children, last sibling otherwise, absolute last with Ctrl.
        /// </summary>
        public static void HandleTreeEndKey<T>(
            IList<T> items,
            ref int selectedIndex,
            Func<T, int> getIndentLevel,
            Func<T, bool> isExpanded,
            Func<T, bool> hasChildren,
            bool ctrlPressed,
            Action onNavigate)
            where T : class
        {
            if (items == null || items.Count == 0)
                return;

            if (ctrlPressed)
            {
                selectedIndex = items.Count - 1;
            }
            else
            {
                T currentItem = items[selectedIndex];
                if (isExpanded(currentItem) && hasChildren(currentItem))
                {
                    int currentLevel = getIndentLevel(currentItem);
                    int lastDescendantIndex = selectedIndex;

                    for (int i = selectedIndex + 1; i < items.Count; i++)
                    {
                        if (getIndentLevel(items[i]) <= currentLevel)
                            break;
                        lastDescendantIndex = i;
                    }

                    selectedIndex = lastDescendantIndex;
                }
                else
                {
                    selectedIndex = JumpToLastSibling(items, selectedIndex, getIndentLevel);
                }
            }

            onNavigate?.Invoke();
        }

        /// <summary>
        /// Joins a list with the active language's own joiners (RimWorldAccess.Work.NameList.*),
        /// so a language can drop the Oxford comma or change the conjunction.
        /// </summary>
        public static string FormatNameList(List<string> items)
        {
            if (items.Count == 0) return "";
            if (items.Count == 1) return items[0];
            if (items.Count == 2) return "RimWorldAccess.Work.NameList.Two".Translate(items[0], items[1]);
            string separator = "RimWorldAccess.Work.NameList.Separator".Translate();
            string lastJoiner = "RimWorldAccess.Work.NameList.ThreePlusLastJoiner".Translate();
            return string.Join(separator, items.Take(items.Count - 1)) + lastJoiner + items.Last();
        }

        /// <summary>
        /// Temperature with the degree symbol vanilla's ToStringTemperature omits ("21.5°C").
        /// </summary>
        public static string FormatTemperature(float celsiusTemp, string format = "F1")
        {
            string temp = celsiusTemp.ToStringTemperature(format);
            // The unit letter is the last character, whether C, F, or K.
            if (temp.Length > 1)
            {
                return temp.Insert(temp.Length - 1, "°");
            }
            return temp;
        }
    }
}
