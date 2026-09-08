using System;
using System.Collections.Generic;
using RimWorldAccess.Shell;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Reusable flat-list cursor: the SelectNext/SelectPrevious/JumpToFirst/JumpToLast idiom plus the
    /// five typeahead operations (character, backspace, clear, next match, previous match).
    /// <see cref="TreeNavigationHelper"/> is the treeview twin.
    /// Wraps <see cref="ListModel"/> for cursor math and <see cref="TypeaheadModel"/> for matching.
    /// The "-1 = nothing selected yet" idiom and priority-grouped typeahead are deliberately NOT
    /// covered.
    ///
    /// Usage:
    ///   private static readonly FlatListCursor cursor = new FlatListCursor();
    ///   cursor.Announce = AnnounceCurrentOption;
    ///   cursor.SetCount(currentOptions.Count);
    ///   cursor.SelectNext();
    ///
    /// For a list whose size changes between navigations with no explicit rebuild step, set
    /// <see cref="CountProvider"/> once instead of calling <see cref="SetCount"/> before every move.
    /// </summary>
    public class FlatListCursor
    {
        private readonly ListModel model = new ListModel();
        private readonly TypeaheadModel typeahead = new TypeaheadModel();

        #region Configuration

        /// <summary>
        /// Invoked after every cursor movement that landed somewhere new, plain or typeahead; a
        /// move blocked by an unwrapped edge plays the edge tone instead.
        /// Write ONE Announce that checks <see cref="HasActiveSearch"/> and branches internally,
        /// rather than separate plain and search-context announcers.
        /// </summary>
        public Action Announce { get; set; }

        /// <summary>
        /// Labels to match against for typeahead, parallel to list indices. Required by
        /// <see cref="HandleChar"/>/<see cref="HandleBackspace"/>, which no-op while it is null.
        /// </summary>
        public Func<List<string>> SearchLabelsProvider { get; set; }

        /// <summary>
        /// When set, consulted at the top of every cursor method in place of a manual
        /// <see cref="SetCount"/> call — for lists re-derived live rather than rebuilt at discrete
        /// points.
        /// </summary>
        public Func<int> CountProvider { get; set; }

        /// <summary>
        /// Overrides wrap behavior. Null, the default, wraps iff
        /// <c>RimWorldAccessMod_Settings.Settings.WrapNavigation</c> is on, re-read live on every
        /// move. Set a fixed value only for a site that must NOT follow that setting.
        /// </summary>
        public bool? WrapOverride { get; set; }

        /// <summary>
        /// When true (the default), <see cref="SelectNext"/>/<see cref="SelectPrevious"/> clear the
        /// active typeahead search before moving. Set false only for a site that composes its own
        /// search-aware move: match-navigation while the search has live matches, else a plain move
        /// that leaves an exhausted search buffer untouched.
        /// </summary>
        public bool ClearSearchOnMove { get; set; } = true;

        /// <summary>
        /// When true, <see cref="HandleBackspace"/> invokes <see cref="Announce"/> on every
        /// successful erase, even one that leaves no match. Default false is silent on a no-match
        /// backspace.
        /// </summary>
        public bool AnnounceOnBackspaceAlways { get; set; }

        /// <summary>
        /// Invoked after the canonical "No matches" announcement whenever a typed character matches
        /// nothing — for a site that also wants a reject sound, rather than duplicating
        /// <see cref="HandleChar"/>'s plumbing at the call site.
        /// </summary>
        public Action OnNoMatch { get; set; }

        #endregion

        #region Read-Only State

        public int Count => model.Count;
        public bool IsEmpty => model.IsEmpty;

        /// <summary>Current 0-based index, or -1 when the list is empty.</summary>
        public int Index => model.Index;

        /// <summary>1-based position for "X of Y" announcements, 0 when empty.</summary>
        public int Position => model.Position;

        public bool HasActiveSearch => typeahead.HasActiveSearch;
        public bool HasNoMatches => typeahead.HasNoMatches;
        public string SearchBuffer => typeahead.Buffer;

        /// <summary>
        /// The canonical search-context suffix (", 2 of 5 matches for 'w'") for the current match, or
        /// an empty string when no search is active.
        /// </summary>
        public string BuildSearchContextSuffix()
        {
            if (!typeahead.HasActiveSearch || typeahead.MatchCount == 0) return "";
            return "RimWorldAccess.Search.ContextSuffix".Translate(
                typeahead.CurrentMatchPosition, typeahead.MatchCount, typeahead.Buffer).ToString();
        }

        #endregion

        #region Setup

        /// <summary>
        /// Sets the list size directly, for lists that do not use <see cref="CountProvider"/>. Keeps
        /// the cursor on the same index where possible, clamping to the new end.
        /// </summary>
        public void SetCount(int count) => model.SetCount(count);

        /// <summary>
        /// Writes the index directly with no search-clear, announce or wrap, for state restoration.
        /// Does nothing on an empty list.
        /// </summary>
        public void SetIndexSilent(int index)
        {
            if (model.Count == 0) return;
            if (index < 0 || index >= model.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            model.MoveTo(index);
        }

        /// <summary>
        /// Resets the search buffer without speaking or invoking <see cref="Announce"/>, for a
        /// teardown path that wants a clean slate on next open. The bool-returning
        /// <see cref="ClearSearch"/> is the speaking counterpart.
        /// </summary>
        public void ClearSearchSilent() => typeahead.Clear();

        #endregion

        #region Plain Navigation

        public void SelectNext()
        {
            Step(1);
        }

        public void SelectPrevious()
        {
            Step(-1);
        }

        private void Step(int delta)
        {
            SyncCount();
            if (model.Count == 0) return;
            if (ClearSearchOnMove) typeahead.Clear();
            SyncWrap();
            AnnounceOrBump(model.MoveBy(delta));
        }

        /// <summary>
        /// First item; never wraps. During an active search with matches this is the first MATCH and
        /// the search is kept, the shared typeahead grammar for Home/End.
        /// </summary>
        public void JumpToFirst()
        {
            SyncCount();
            if (model.Count == 0) return;
            if (typeahead.HasActiveSearch && typeahead.MatchCount > 0)
            {
                FirstMatch();
                return;
            }
            typeahead.Clear();
            AnnounceOrBump(model.MoveFirst());
        }

        /// <summary>Last item; the <see cref="JumpToFirst"/> twin, last match during an active search.</summary>
        public void JumpToLast()
        {
            SyncCount();
            if (model.Count == 0) return;
            if (typeahead.HasActiveSearch && typeahead.MatchCount > 0)
            {
                LastMatch();
                return;
            }
            typeahead.Clear();
            AnnounceOrBump(model.MoveLast());
        }

        #endregion

        #region Typeahead (the standard 5 operations)

        /// <summary>
        /// Typed character: on a match, moves the cursor and invokes <see cref="Announce"/>; on no
        /// match, speaks "No matches for 'x'" directly, the one place this class speaks without going
        /// through Announce. A no-op while <see cref="SearchLabelsProvider"/> is unset or the list is
        /// empty.
        /// </summary>
        public void HandleChar(char c)
        {
            SyncCount();
            if (model.Count == 0) return;
            List<string> labels = SearchLabelsProvider?.Invoke();
            if (labels == null) return;

            int newIndex;
            if (typeahead.Append(c, labels, Time.realtimeSinceStartup, out newIndex))
            {
                model.MoveTo(newIndex);
                Announce?.Invoke();
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Search.NoMatches".Loc(typeahead.LastFailedSearch));
                OnNoMatch?.Invoke();
            }
        }

        /// <summary>
        /// Erases the last search character. DELIBERATELY silent when the erase yields no new match
        /// index: backspacing down to an empty buffer does not re-announce, only
        /// <see cref="ClearSearch"/> does — unlike <see cref="TreeNavigationHelper"/>. Do not "fix"
        /// this; set <see cref="AnnounceOnBackspaceAlways"/> per site instead. A no-op with no active
        /// search.
        /// </summary>
        public void HandleBackspace()
        {
            if (!typeahead.HasActiveSearch) return;
            List<string> labels = SearchLabelsProvider?.Invoke();
            if (labels == null) return;

            int newIndex;
            if (typeahead.Backspace(labels, Time.realtimeSinceStartup, out newIndex))
            {
                if (newIndex >= 0)
                {
                    model.MoveTo(newIndex);
                    Announce?.Invoke();
                }
                else if (AnnounceOnBackspaceAlways)
                {
                    Announce?.Invoke();
                }
            }
        }

        /// <summary>
        /// Clears an active search, speaking "Search cleared" and then <see cref="Announce"/> — two
        /// utterances. False, and no action, when there was no active search, so a caller can tell
        /// whether its Escape still has to close the menu.
        /// </summary>
        public bool ClearSearch()
        {
            if (!typeahead.HasActiveSearch) return false;
            typeahead.Clear();
            TolkHelper.Speak("RimWorldAccess.Search.Cleared".Loc());
            Announce?.Invoke();
            return true;
        }

        /// <summary>Moves to the next match, wrapping. A no-op with no active search.</summary>
        public void NextMatch()
        {
            if (!typeahead.HasActiveSearch) return;
            int next = typeahead.NextMatch(model.Index);
            if (next >= 0)
            {
                MenuHelper.SoundMatchMove(model.Index, next, 1);
                model.MoveTo(next);
                Announce?.Invoke();
            }
        }

        /// <summary>The NextMatch twin.</summary>
        public void PreviousMatch()
        {
            if (!typeahead.HasActiveSearch) return;
            int prev = typeahead.PreviousMatch(model.Index);
            if (prev >= 0)
            {
                MenuHelper.SoundMatchMove(model.Index, prev, -1);
                model.MoveTo(prev);
                Announce?.Invoke();
            }
        }

        /// <summary>
        /// Moves to the first match without clearing the search. A no-op with no active search or no
        /// matches.
        /// </summary>
        public void FirstMatch()
        {
            if (!typeahead.HasActiveSearch) return;
            int first = typeahead.FirstMatch();
            if (first >= 0)
            {
                model.MoveTo(first);
                Announce?.Invoke();
            }
        }

        /// <summary>The FirstMatch twin, for End.</summary>
        public void LastMatch()
        {
            if (!typeahead.HasActiveSearch) return;
            int last = typeahead.LastMatch();
            if (last >= 0)
            {
                model.MoveTo(last);
                Announce?.Invoke();
            }
        }

        #endregion

        #region Internal Helpers

        private void AnnounceOrBump(MoveResult result)
        {
            if (!MenuHelper.SoundMove(result))
                return;
            Announce?.Invoke();
        }

        private void SyncCount()
        {
            if (CountProvider != null)
                model.SetCount(CountProvider());
        }

        private void SyncWrap()
        {
            model.Wrap = WrapOverride ?? (RimWorldAccessMod_Settings.Settings?.WrapNavigation == true);
        }

        #endregion
    }
}
