using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Reflection-only compatibility surface for Vanilla Expanded Framework's mercenary-hiring
    /// dialog -- <c>VEF.Planet.Dialog_Hire</c>, <c>VEF.Planet.Hireable</c> (the comms-console
    /// group, itself <c>IEnumerable&lt;HireableFactionDef&gt;</c>), and
    /// <c>VEF.Planet.HireableFactionDef</c>. Mirrors the <see cref="VpePsysetCompat"/> idiom:
    /// every VEF type and member is resolved once behind <see cref="Ready"/>, a missing TYPE is a
    /// silent decline (VEF not installed), a resolved type missing a MEMBER is a logged error and
    /// a graceful decline. <see cref="Shell.VefHireScope"/> is the sole consumer and never
    /// touches reflection directly -- every VEF-typed value (the dialog, a faction, the hire
    /// data) stays boxed as <c>object</c>/<see cref="Window"/> here and is read back only
    /// through this facade's own methods.
    /// </summary>
    internal static class VefHireCompat
    {
        private static readonly Type dialogHireType;
        private static readonly Type hireableType;
        private static readonly Type hireableFactionDefType;

        private static readonly FieldInfo hireableField;
        private static readonly FieldInfo hireDataField;
        private static readonly FieldInfo curFactionField;
        private static readonly FieldInfo daysAmountField;
        private static readonly FieldInfo daysAmountBufferField;
        private static readonly FieldInfo availableSilverField;
        private static readonly FieldInfo riskMultiplierField;
        private static readonly FieldInfo pawnKindsField;

        private static readonly MethodInfo getCallLabel;
        private static readonly MethodInfo costBaseGetter;
        private static readonly MethodInfo costFinalGetter;

        private static readonly bool ready;

        public static bool Ready => ready;
        public static Type DialogHireType => dialogHireType;

        static VefHireCompat()
        {
            var surface = new ReflectionSurface("VefHireCompat");

            dialogHireType = surface.Type("VEF.Planet.Dialog_Hire");
            hireableType = surface.Type("VEF.Planet.Hireable");
            hireableFactionDefType = surface.Type("VEF.Planet.HireableFactionDef");

            hireableField = surface.Field(dialogHireType, "hireable");
            hireDataField = surface.Field(dialogHireType, "hireData");
            curFactionField = surface.Field(dialogHireType, "curFaction");
            daysAmountField = surface.Field(dialogHireType, "daysAmount");
            daysAmountBufferField = surface.Field(dialogHireType, "daysAmountBuffer");
            availableSilverField = surface.Field(dialogHireType, "availableSilver");
            riskMultiplierField = surface.Field(dialogHireType, "riskMultiplier");
            pawnKindsField = surface.Field(hireableFactionDefType, "pawnKinds");

            getCallLabel = surface.Method(hireableType, "GetCallLabel");
            costBaseGetter = surface.Property(dialogHireType, "CostBase")?.GetGetMethod(true);
            costFinalGetter = surface.Property(dialogHireType, "CostFinal")?.GetGetMethod(true);

            ready = surface.Ready;
        }

        // ------------------------------------------------------------------
        // Reads.
        // ------------------------------------------------------------------

        public static string CallLabel(Window dialog)
        {
            if (!ready)
                return "";
            try
            {
                return (string)getCallLabel.Invoke(hireableField.GetValue(dialog), null);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VefHireCompat.CallLabel failed: {ex.Message}");
                return "";
            }
        }

        public static List<object> Factions(Window dialog)
        {
            var result = new List<object>();
            if (!ready)
                return result;
            try
            {
                if (hireableField.GetValue(dialog) is IEnumerable factions)
                {
                    foreach (object faction in factions)
                    {
                        result.Add(faction);
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VefHireCompat.Factions failed: {ex.Message}");
            }
            return result;
        }

        public static Def FactionDefOf(object faction)
        {
            return faction as Def;
        }

        public static List<PawnKindDef> PawnKindsOf(object faction)
        {
            if (!ready || faction == null)
                return null;
            try
            {
                return (List<PawnKindDef>)pawnKindsField.GetValue(faction);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VefHireCompat.PawnKindsOf failed: {ex.Message}");
                return null;
            }
        }

        public static object CurFaction(Window dialog)
        {
            if (!ready)
                return null;
            try
            {
                return curFactionField.GetValue(dialog);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VefHireCompat.CurFaction failed: {ex.Message}");
                return null;
            }
        }

        public static bool FactionLocked(Window dialog, object faction)
        {
            var cur = CurFaction(dialog);
            return cur != null && !ReferenceEquals(cur, faction);
        }

        public static Dictionary<PawnKindDef, Pair<int, string>> HireData(Window dialog)
        {
            if (!ready)
                return null;
            try
            {
                return (Dictionary<PawnKindDef, Pair<int, string>>)hireDataField.GetValue(dialog);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VefHireCompat.HireData failed: {ex.Message}");
                return null;
            }
        }

        public static int CountOf(Window dialog, PawnKindDef pk)
        {
            var data = HireData(dialog);
            return data != null && data.TryGetValue(pk, out var p) ? p.First : 0;
        }

        public static int Days(Window dialog)
        {
            if (!ready)
                return 0;
            try
            {
                return (int)daysAmountField.GetValue(dialog);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VefHireCompat.Days failed: {ex.Message}");
                return 0;
            }
        }

        public static float AvailableSilver(Window dialog)
        {
            if (!ready)
                return 0f;
            try
            {
                return (float)availableSilverField.GetValue(dialog);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VefHireCompat.AvailableSilver failed: {ex.Message}");
                return 0f;
            }
        }

        public static float RiskMultiplier(Window dialog)
        {
            if (!ready)
                return 0f;
            try
            {
                return (float)riskMultiplierField.GetValue(dialog);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VefHireCompat.RiskMultiplier failed: {ex.Message}");
                return 0f;
            }
        }

        public static float CostBase(Window dialog)
        {
            if (!ready)
                return 0f;
            try
            {
                return (float)costBaseGetter.Invoke(dialog, null);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VefHireCompat.CostBase failed: {ex.Message}");
                return 0f;
            }
        }

        public static float CostFinal(Window dialog)
        {
            if (!ready)
                return 0f;
            try
            {
                return (float)costFinalGetter.Invoke(dialog, null);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VefHireCompat.CostFinal failed: {ex.Message}");
                return 0f;
            }
        }

        public static bool CanAfford(Window dialog)
        {
            return CostFinal(dialog) <= AvailableSilver(dialog);
        }

        // ------------------------------------------------------------------
        // Mutators.
        // ------------------------------------------------------------------

        /// <summary>
        /// Sets one pawn kind's hire count for a faction, mirroring
        /// <c>Dialog_Hire.DoHireableFaction</c>'s per-row write-back.
        /// </summary>
        public static void SetPawnCount(Window dialog, object faction, PawnKindDef pk, int value)
        {
            if (!ready)
                return;
            try
            {
                var data = HireData(dialog);
                if (data == null)
                    return;
                value = Mathf.Clamp(value, 0, 99);
                // MUTATION-C: mirrors Dialog_Hire.DoHireableFaction per-row write-back (VEF Dialog_Hire,
                // Source/VEF/Planet/Misc/HireableSystem/Dialog_Hire.cs ~L205-210); the count/curFaction
                // update is inline in the vanilla IMGUI draw loop with no gated setter to call.
                data[pk] = new Pair<int, string>(value, value.ToString());
                var cur = CurFaction(dialog);
                if (value > 0 && cur == null)
                {
                    // MUTATION-C: mirrors the same site -- first non-zero pick locks the chosen faction.
                    curFactionField.SetValue(dialog, faction);
                }
                else if (value == 0 && ReferenceEquals(cur, faction)
                    && PawnKindsOf(faction).All(p => (data.TryGetValue(p, out var q) ? q.First : 0) == 0))
                {
                    // MUTATION-C: mirrors the same site -- clearing the last unit unlocks faction choice.
                    curFactionField.SetValue(dialog, null);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VefHireCompat.SetPawnCount failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Sets the contract length, mirroring the days <c>UIUtility.DrawCountAdjuster</c> call
        /// in <c>Dialog_Hire.DoWindowContents</c>.
        /// </summary>
        public static void SetDays(Window dialog, int value)
        {
            if (!ready)
                return;
            try
            {
                value = Mathf.Clamp(value, 0, 60);
                // MUTATION-C: mirrors Dialog_Hire days DrawCountAdjuster (VEF Dialog_Hire,
                // Source/VEF/Planet/Misc/HireableSystem/Dialog_Hire.cs ~L131); no gated setter exists.
                daysAmountField.SetValue(dialog, value);
                // MUTATION-C: mirrors the adjuster keeping the edit buffer in sync with the value.
                daysAmountBufferField.SetValue(dialog, value.ToString());
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VefHireCompat.SetDays failed: {ex.Message}");
            }
        }
    }
}
