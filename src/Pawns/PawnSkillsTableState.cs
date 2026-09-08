using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Data/lifecycle owner for the read-only pawn-skills table (Alt+P):
    /// the colonist row order (bar order, or the player's active sort) and
    /// the snapshot used to restore that order when a sort clears.
    ///
    /// Navigation, typeahead, and every announcement now live on
    /// <see cref="RimWorldAccess.Shell.PawnSkillsTableScope"/> (the table-model migration;
    /// <c>TabularMenuHelper</c> is retired for this screen). This class keeps only what the
    /// state-mirrored lifecycle needs: <see cref="IsActive"/>,
    /// <see cref="Open"/>, <see cref="Close"/>, row access for the scope's
    /// table contract, and the sort-data mutators
    /// <c>PawnSkillsTableScope.ApplyContentSort</c> calls into (comparisons
    /// themselves stay in <see cref="PawnSkillsTableHelper"/>, wired to the
    /// game's own skill/passion values — never display strings).
    ///
    /// <see cref="IsActive"/> is read by <c>MapNavigationPatch</c> and
    /// <c>ShellGuards</c>; <see cref="Open"/> is called by the ambient
    /// Alt+P claim in <c>MapScope.QuickInfo.Game.cs</c>. Both must stay
    /// public static regardless of how the rest of this class is shaped.
    /// </summary>
    public static class PawnSkillsTableState
    {
        public static bool IsActive { get; private set; }

        private static List<Pawn> pawns = new List<Pawn>();
        private static List<Pawn> defaultOrder = new List<Pawn>();
        private static Func<IReadOnlyList<Pawn>> rowOrderSource;

        /// <summary>
        /// Hands the row order to the live vanilla <c>PawnTable</c> the skills window draws
        /// (set on mount, cleared on unmount). That table sorts on its own header clicks as
        /// well as on the keyboard's, so reading the order back out of it is what keeps the
        /// spoken rows and the drawn rows identical.
        /// </summary>
        public static void SetRowOrderSource(Func<IReadOnlyList<Pawn>> source)
        {
            rowOrderSource = source;
        }

        /// <summary>Current row order: the window's table while it exists, else the list below.</summary>
        public static IReadOnlyList<Pawn> Pawns
        {
            get
            {
                IReadOnlyList<Pawn> live = rowOrderSource != null ? rowOrderSource() : null;
                return live ?? pawns;
            }
        }

        /// <summary>The colonist-bar order captured at <see cref="Open"/> — the table's unsorted order.</summary>
        public static IReadOnlyList<Pawn> DefaultOrder
        {
            get { return defaultOrder; }
        }

        public static Pawn PawnAt(int index)
        {
            IReadOnlyList<Pawn> rows = Pawns;
            return index >= 0 && index < rows.Count ? rows[index] : null;
        }

        public static int IndexOf(Pawn pawn)
        {
            IReadOnlyList<Pawn> rows = Pawns;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i] == pawn) return i;
            }
            return -1;
        }

        #region Lifecycle

        public static void Open()
        {
            if (IsActive) return;
            if (Current.ProgramState != ProgramState.Playing)
            {
                TolkHelper.Speak("RimWorldAccess.Guard.NotInGame".Loc());
                return;
            }
            if (Find.CurrentMap == null)
            {
                TolkHelper.Speak("RimWorldAccess.Guard.NoMapLoaded".Loc());
                return;
            }

            PawnSkillsTableHelper.RefreshSkills();

            pawns = Find.ColonistBar.GetColonistsInOrder()
                .Where(p => p != null && p.Spawned && p.Map == Find.CurrentMap && p.skills != null)
                .ToList();

            if (pawns.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Input.WorkMenu.NoColonistsAvailable".Loc());
                return;
            }

            defaultOrder = new List<Pawn>(pawns);
            IsActive = true;
            // The opening announcement (colonist/skill counts + the first
            // cell) is PawnSkillsTableScope.OnFocus's job, not this method's:
            // the mirror that pushes the scope hasn't run yet at this point
            // (mirror pushes land on the next dispatcher pass — see
            // the FocusScope class header), so there is no live scope here
            // to announce through the composer.
        }

        public static void Close()
        {
            if (!IsActive) return;
            IsActive = false;
            pawns.Clear();
            defaultOrder.Clear();
            TolkHelper.Speak("RimWorldAccess.Pawns.SkillsTable.Closed".Loc());
        }

        /// <summary>
        /// Silent hard reset for session-boundary hygiene — everything <see cref="Close"/>
        /// drops, minus the announcement (nothing closed from the player's side), plus the
        /// row-order hook, so a delegate over the previous session's table can never
        /// outlive it. Both lists hold pawns from a game that no longer exists.
        /// </summary>
        internal static void ResetHard()
        {
            IsActive = false;
            pawns.Clear();
            defaultOrder.Clear();
            rowOrderSource = null;
        }

        #endregion

        #region Sort data (PawnSkillsTableScope.ApplyContentSort calls these)

        /// <summary>
        /// Re-order by a column using the game-wired comparer in <see cref="PawnSkillsTableHelper"/>,
        /// always from the captured order so repeated sorts compound no further than
        /// vanilla's do (PawnTable re-sorts its pawns getter's output every recache).
        /// </summary>
        public static void SortByColumn(int columnIndex, bool descending)
        {
            pawns = PawnSkillsTableHelper.SortPawnsByColumn(defaultOrder, columnIndex, descending);
        }

        /// <summary>Restore the colonist-bar order captured at <see cref="Open"/> (the sort-cleared state).</summary>
        public static void RestoreDefaultOrder()
        {
            pawns = new List<Pawn>(defaultOrder);
        }

        #endregion
    }
}
