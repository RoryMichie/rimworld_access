using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Data facade for <see cref="Dialog_EntityCodex"/> (Anomaly's discovered-entity
    /// browser). The entry list, the cursor, the typeahead and every announcement
    /// now live on <see cref="Shell.EntityCodexScope"/>, which is a
    /// <see cref="Shell.ScreenScope"/> (the EntityTabState/EntityTabScope split applied
    /// to this screen). This class keeps only what code OUTSIDE the scope reads or writes:
    /// <list type="bullet">
    /// <item><see cref="IsActive"/> / <see cref="Open"/> / <see cref="Close"/> — the
    /// window Harmony hooks in <see cref="EntityCodexPatch"/>.</item>
    /// <item>the resolved entry list, the dev "show all" flag, and the discovery
    /// mutation — the data and the write vehicles the scope drives.</item>
    /// </list>
    ///
    /// The vanilla sort is reproduced exactly: categories with visible entries in
    /// <c>listOrder</c>, then each category's entries by <c>orderInCategory</c> then
    /// <c>label</c> (Dialog_EntityCodex sorts on <c>.label</c>, not LabelCap).
    /// </summary>
    public static class EntityCodexState
    {
        private static readonly List<EntityCodexEntryDef> entries = new List<EntityCodexEntryDef>();

        private static System.Reflection.FieldInfo selectedEntryField;

        public static bool IsActive { get; private set; }

        /// <summary>Every visible entry in vanilla's own draw order.</summary>
        public static IReadOnlyList<EntityCodexEntryDef> Entries
        {
            get { return entries; }
        }

        /// <summary>
        /// The row the scope should land on when it first takes focus, seeded from
        /// the dialog's own <c>selectedEntry</c> so a "View entity codex" letter
        /// action opens on the entry it named. 0 when nothing was pre-selected.
        /// </summary>
        public static int InitialIndex { get; private set; }

        /// <summary>
        /// The dev "Show all" toggle, mirroring Dialog_EntityCodex's own
        /// <c>devShowAll</c> field: reveals every entry's real content regardless of
        /// discovery. Reset per open, matching vanilla's per-window-instance default.
        /// </summary>
        public static bool DevShowAll { get; private set; }

        public static void Open(Dialog_EntityCodex dialog)
        {
            if (dialog == null)
                return;

            try
            {
                if (selectedEntryField == null)
                    selectedEntryField = HarmonyLib.AccessTools.Field(typeof(Dialog_EntityCodex), "selectedEntry");

                DevShowAll = false;
                InitialIndex = 0;

                entries.Clear();
                entries.AddRange(DefDatabase<EntityCategoryDef>.AllDefsListForReading
                    .Where(HasVisibleEntries)
                    .OrderBy(c => c.listOrder)
                    .SelectMany(c => DefDatabase<EntityCodexEntryDef>.AllDefsListForReading
                        .Where(e => e.Visible && e.category == c)
                        .OrderBy(e => e.orderInCategory)
                        .ThenBy(e => e.label)));

                var dialogSelected = selectedEntryField?.GetValue(dialog) as EntityCodexEntryDef;
                if (dialogSelected != null)
                {
                    int index = entries.IndexOf(dialogSelected);
                    if (index >= 0)
                        InitialIndex = index;
                }

                IsActive = true;
            }
            catch (Exception ex)
            {
                Log.Error($"[EntityCodexState] Error opening: {ex.Message}");
                Close();
            }
        }

        public static void Close()
        {
            IsActive = false;
            entries.Clear();
            InitialIndex = 0;
            DevShowAll = false;
        }

        /// <summary>Flips the dev reveal-everything flag (dev + god mode gated by the caller).</summary>
        public static void ToggleDevShowAll()
        {
            DevShowAll = !DevShowAll;
        }

        /// <summary>Whether an entry's real content is readable: genuinely discovered, or revealed by the dev flag.</summary>
        public static bool IsRevealed(EntityCodexEntryDef entry)
        {
            return entry != null && (entry.Discovered || DevShowAll);
        }

        /// <summary>Whether one of an entry's linked things is readable, by the same rule.</summary>
        public static bool IsThingRevealed(ThingDef thing)
        {
            if (thing == null)
                return false;
            if (DevShowAll)
                return true;
            var codex = Find.EntityCodex;
            return codex != null && codex.Discovered(thing);
        }

        /// <summary>
        /// Dev discover for one entry. Vehicle B: <c>EntityCodex.SetDiscovered</c> is
        /// vanilla's own gated method, called per linked thing when the entry has
        /// them and bare otherwise — mirroring Dialog_EntityCodex's own dev button
        /// (decompiled RimWorld/Dialog_EntityCodex.cs:158-171).
        /// </summary>
        public static void SetDiscovered(EntityCodexEntryDef entry)
        {
            if (entry == null || Find.EntityCodex == null)
                return;
            if (!entry.linkedThings.NullOrEmpty())
            {
                for (int i = 0; i < entry.linkedThings.Count; i++)
                {
                    Find.EntityCodex.SetDiscovered(entry, entry.linkedThings[i]);
                }
            }
            else
            {
                Find.EntityCodex.SetDiscovered(entry);
            }
        }

        private static bool HasVisibleEntries(EntityCategoryDef cat)
        {
            return DefDatabase<EntityCodexEntryDef>.AllDefsListForReading
                .Any(e => e.Visible && e.category == cat);
        }
    }
}
