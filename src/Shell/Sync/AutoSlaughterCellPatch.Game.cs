using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Records the on-screen rect of every EDITABLE cell of the
    /// <see cref="Dialog_AutoSlaughter"/> grid — the five numeric max fields and the two
    /// allow-slaughter checkboxes — so <see cref="AutoSlaughterScope"/> can ring the exact cell the
    /// keyboard sits on instead of the whole animal row (the listing ring, which stays the fallback for
    /// the header and the read-only count cells).
    ///
    /// Vanilla hands out no rect for these cells, but it does walk the row with a
    /// <c>WidgetRow</c> whose <c>FinalX</c> is public: bracketing <c>DoMaxColumn</c>
    /// (decompiled RimWorld/Dialog_AutoSlaughter.cs:212) turns the cursor's advance across
    /// that one cell into the cell's own x-extent, and the checkbox cells are recorded from
    /// the very <c>row.FinalX</c>/size values vanilla passes to <c>Widgets.Checkbox</c>
    /// (:372, :383). No layout math is mirrored and no vanilla constant is copied.
    ///
    /// The row's group IS the row (<c>Widgets.BeginGroup(rect)</c> at :350), so cell rects are
    /// group-local: x from the WidgetRow, y/height from the group's own live clip.
    /// </summary>
    internal static class AutoSlaughterCellPatch
    {
        private struct CellKey : IEquatable<CellKey>
        {
            public readonly AutoSlaughterConfig Config;
            public readonly int Column;

            public CellKey(AutoSlaughterConfig config, int column)
            {
                Config = config;
                Column = column;
            }

            public bool Equals(CellKey other)
            {
                return ReferenceEquals(Config, other.Config) && Column == other.Column;
            }

            public override bool Equals(object obj)
            {
                return obj is CellKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return (Config == null ? 0 : Config.GetHashCode()) ^ Column;
            }
        }

        private static readonly RowDrawCapture Cells = new RowDrawCapture();

        /// <summary>The config whose row is drawing, or null outside a row (also the recorders' live-state gate).</summary>
        private static AutoSlaughterConfig currentConfig;

        /// <summary>How many <c>DoMaxColumn</c> calls this row has made — their fixed order IS the column index (:358-366).</summary>
        private static int maxColumnOrdinal;

        private static float pendingStartX;
        private static bool pendingArmed;

        /// <summary>The cell's last-drawn rect in absolute UI points, or empty when it is not drawn (scrolled out, read-only column, no pass).</summary>
        internal static Rect CellRect(AutoSlaughterConfig config, int column)
        {
            if (config == null)
            {
                return default(Rect);
            }
            return Cells.FindLast(new CellKey(config, column));
        }

        internal static void BeginRow(AutoSlaughterConfig config)
        {
            currentConfig = AutoSlaughterState.IsActive ? config : null;
            maxColumnOrdinal = 0;
            pendingArmed = false;
        }

        internal static void EndRow()
        {
            currentConfig = null;
            maxColumnOrdinal = 0;
            pendingArmed = false;
        }

        internal static void ArmMaxColumn(WidgetRow row)
        {
            if (currentConfig == null || row == null)
            {
                return;
            }
            pendingStartX = row.FinalX;
            pendingArmed = true;
        }

        internal static void RecordMaxColumn(WidgetRow row)
        {
            if (!pendingArmed)
            {
                return;
            }
            pendingArmed = false;
            int column = maxColumnOrdinal++;
            float width = row.FinalX - pendingStartX;
            if (width <= 0f)
            {
                return;
            }
            Cells.Record(new CellKey(currentConfig, column), new Rect(pendingStartX, 0f, width, RowHeight()));
        }

        /// <summary>Keeps the remaining cells of a row correctly numbered when vanilla's own body throws mid-cell.</summary>
        internal static void DisarmMaxColumn()
        {
            if (!pendingArmed)
            {
                return;
            }
            pendingArmed = false;
            maxColumnOrdinal++;
        }

        /// <summary>Called from inside <c>DoAnimalRow</c> by the transpiler below, with the arguments vanilla is passing to <c>Widgets.Checkbox</c>.</summary>
        public static void RecordCheckbox(float x, int ordinal, float size)
        {
            try
            {
                if (currentConfig == null)
                {
                    return;
                }
                int column = ordinal == 0
                    ? (int)AutoSlaughterState.Column.AllowPregnant
                    : (int)AutoSlaughterState.Column.AllowBonded;
                Cells.Record(new CellKey(currentConfig, column), new Rect(x, 0f, size, size));
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Auto-slaughter checkbox cell capture error", ex);
            }
        }

        /// <summary>The row group's own clip, which vanilla opened on the row rect itself — never a constant of ours.</summary>
        private static float RowHeight()
        {
            return GuiSpace.CurrentClip().VisibleRect.yMax;
        }
    }

    /// <summary>
    /// Row identity for the cell recorder: every cell drawn between this prefix and its
    /// finalizer belongs to <c>config</c>. The body recalculates animal counts when a
    /// checkbox flips (:375), so it can throw — hence the finalizer.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_AutoSlaughter), "DoAnimalRow")]
    internal static class AutoSlaughterRowCellPatch
    {
        [HarmonyPrefix]
        public static void Prefix(AutoSlaughterConfig config)
        {
            try
            {
                AutoSlaughterCellPatch.BeginRow(config);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Auto-slaughter row capture error", ex);
            }
        }

        [HarmonyFinalizer]
        public static void Finalizer()
        {
            AutoSlaughterCellPatch.EndRow();
        }
    }

    /// <summary>
    /// The five numeric max cells: the WidgetRow's advance across one <c>DoMaxColumn</c> call
    /// is that cell's width. Vanilla parses the player's typed buffer inside the body
    /// (<c>TextFieldNumeric</c>, :231), so the armed start-x is disarmed by a finalizer.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_AutoSlaughter), "DoMaxColumn")]
    internal static class AutoSlaughterMaxColumnPatch
    {
        [HarmonyPrefix]
        public static void Prefix(WidgetRow row)
        {
            try
            {
                AutoSlaughterCellPatch.ArmMaxColumn(row);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Auto-slaughter cell capture error", ex);
            }
        }

        [HarmonyPostfix]
        public static void Postfix(WidgetRow row)
        {
            try
            {
                AutoSlaughterCellPatch.RecordMaxColumn(row);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Auto-slaughter cell capture error", ex);
            }
        }

        [HarmonyFinalizer]
        public static void Finalizer()
        {
            AutoSlaughterCellPatch.DisarmMaxColumn();
        }
    }

    /// <summary>
    /// The two allow-slaughter checkbox cells. <c>Widgets.Checkbox</c> is a global hot path,
    /// so nothing patches it; instead each of the two callsites in <c>DoAnimalRow</c> gets a
    /// recorder call spliced in behind the <c>row.FinalX</c> it just pushed, carrying the box
    /// size vanilla itself passes as <c>szBox</c>. The callsites are paired POSITIONALLY
    /// (first = pregnant, second = bonded), so the count is asserted and a game update that
    /// changes it leaves vanilla's IL untouched and says so in the log.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_AutoSlaughter), "DoAnimalRow")]
    internal static class AutoSlaughterCheckboxCellPatch
    {
        private const int ExpectedCheckboxCallsites = 2;

        private static readonly MethodInfo Checkbox =
            AccessTools.Method(typeof(Widgets), "Checkbox", new[]
            {
                typeof(float), typeof(float), typeof(bool).MakeByRefType(), typeof(float),
                typeof(bool), typeof(bool), typeof(Texture2D), typeof(Texture2D),
            });

        private static readonly MethodInfo FinalX =
            AccessTools.PropertyGetter(typeof(WidgetRow), nameof(WidgetRow.FinalX));

        private static readonly MethodInfo Recorder =
            AccessTools.Method(typeof(AutoSlaughterCellPatch), nameof(AutoSlaughterCellPatch.RecordCheckbox));

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            if (Checkbox == null || FinalX == null || Recorder == null)
            {
                ModLogger.Error("Auto-slaughter checkbox cell ring: could not resolve the vanilla members it splices between; the row ring stays.");
                return codes;
            }

            var callsites = new List<int>();
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].Calls(Checkbox))
                {
                    callsites.Add(i);
                }
            }
            if (callsites.Count != ExpectedCheckboxCallsites)
            {
                ModLogger.Error("Auto-slaughter checkbox cell ring: expected " + ExpectedCheckboxCallsites
                    + " Widgets.Checkbox callsites in Dialog_AutoSlaughter.DoAnimalRow, found " + callsites.Count
                    + "; the row ring stays.");
                return codes;
            }

            // Plan first, splice after: a half-applied splice would corrupt vanilla's IL.
            var splices = new List<KeyValuePair<int, CodeInstruction[]>>();
            int searchFloor = 0;
            for (int site = 0; site < callsites.Count; site++)
            {
                int callsite = callsites[site];
                int finalX = LastIndexOf(codes, searchFloor, callsite, ci => ci.Calls(FinalX));
                int size = LastIndexOf(codes, searchFloor, callsite, ci => ci.opcode == OpCodes.Ldc_R4);
                if (finalX < 0 || size < 0 || size < finalX)
                {
                    ModLogger.Error("Auto-slaughter checkbox cell ring: callsite " + site
                        + " in Dialog_AutoSlaughter.DoAnimalRow does not have the expected WidgetRow.FinalX/size argument shape; the row ring stays.");
                    return codes;
                }
                splices.Add(new KeyValuePair<int, CodeInstruction[]>(finalX + 1, new[]
                {
                    new CodeInstruction(OpCodes.Dup),
                    new CodeInstruction(OpCodes.Ldc_I4, site),
                    new CodeInstruction(OpCodes.Ldc_R4, (float)codes[size].operand),
                    new CodeInstruction(OpCodes.Call, Recorder),
                }));
                searchFloor = callsite + 1;
            }

            for (int i = splices.Count - 1; i >= 0; i--)
            {
                codes.InsertRange(splices[i].Key, splices[i].Value);
            }
            return codes;
        }

        private static int LastIndexOf(List<CodeInstruction> codes, int floor, int ceiling, Func<CodeInstruction, bool> match)
        {
            for (int i = ceiling - 1; i >= floor; i--)
            {
                if (match(codes[i]))
                {
                    return i;
                }
            }
            return -1;
        }
    }
}
