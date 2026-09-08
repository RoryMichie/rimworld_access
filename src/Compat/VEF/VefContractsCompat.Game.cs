using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Reflection-only compatibility surface for Vanilla Expanded Framework's contract board --
    /// <c>VEF.Storyteller.Window_Contracts</c> (the master/detail quest browser opened from a
    /// comms console), <c>VEF.Storyteller.QuestGiverManager</c> (the window's quest source), and
    /// <c>VEF.Storyteller.QuestInfo</c>/<c>VEF.Storyteller.QuestCurrencyInfo</c> (VEF's own
    /// per-quest wrapper around a vanilla <see cref="Quest"/>). Mirrors the
    /// <see cref="VefHireCompat"/>/<see cref="VpePsysetCompat"/> idiom: every VEF type and member
    /// is resolved once behind <see cref="Ready"/>, a missing TYPE is a silent decline (VEF not
    /// loaded), a resolved type missing a MEMBER is a logged error and a graceful decline.
    /// <see cref="Shell.VefContractsScope"/> is the sole consumer and never touches reflection
    /// directly -- every VEF-typed value (the window, a QuestInfo, its currency info) stays boxed
    /// as <c>object</c>/<see cref="Window"/> here and is read back only through this facade's own
    /// methods. The vanilla <see cref="Quest"/> a QuestInfo wraps is referenceable directly.
    /// </summary>
    internal static class VefContractsCompat
    {
        private static readonly Type windowType;
        private static readonly Type managerType;
        private static readonly Type questInfoType;
        private static readonly Type currencyInfoType;

        private static readonly FieldInfo selectedField;
        private static readonly FieldInfo managerField;
        private static readonly MethodInfo selectMethod;
        private static readonly MethodInfo acceptMethod;
        private static readonly MethodInfo listUnmetMethod;

        private static readonly PropertyInfo availableQuestsProp;

        private static readonly PropertyInfo questProp;
        private static readonly FieldInfo choiceField;
        private static readonly FieldInfo questPartChoiceField;
        private static readonly FieldInfo askerFactionField;
        private static readonly FieldInfo currencyInfoField;

        private static readonly MethodInfo getCurrencyInfoMethod;

        private static readonly MethodInfo chooseMethod;

        private static readonly bool ready;

        public static bool Ready => ready;
        public static Type WindowType => windowType;

        static VefContractsCompat()
        {
            var surface = new ReflectionSurface("VefContractsCompat");

            windowType = surface.Type("VEF.Storyteller.Window_Contracts");
            managerType = surface.Type("VEF.Storyteller.QuestGiverManager");
            questInfoType = surface.Type("VEF.Storyteller.QuestInfo");
            currencyInfoType = surface.Type("VEF.Storyteller.QuestCurrencyInfo");

            selectedField = surface.Field(windowType, "selected");
            managerField = surface.Field(windowType, "questGiverManager");
            selectMethod = questInfoType != null ? surface.Method(windowType, "Select", new[] { questInfoType }) : null;
            acceptMethod = surface.Method(windowType, "AcceptQuestByInterface", new[] { typeof(Action), typeof(bool) });
            listUnmetMethod = surface.Method(windowType, "ListUnmetAcceptRequirements");

            availableQuestsProp = surface.Property(managerType, "AvailableQuests");

            questProp = surface.Property(questInfoType, "Quest");
            choiceField = surface.Field(questInfoType, "choice");
            questPartChoiceField = surface.Field(questInfoType, "quest_Part_choice");
            askerFactionField = surface.Field(questInfoType, "askerFaction");
            currencyInfoField = surface.Field(questInfoType, "currencyInfo");

            getCurrencyInfoMethod = surface.Method(currencyInfoType, "GetCurrencyInfo");

            chooseMethod = surface.Method(typeof(QuestPart_Choice), "Choose");

            ready = surface.Ready;
        }

        // ------------------------------------------------------------------
        // Reads.
        // ------------------------------------------------------------------

        public static List<object> Quests(Window window)
        {
            var result = new List<object>();
            if (!ready)
                return result;
            try
            {
                object manager = managerField.GetValue(window);
                if (manager == null)
                    return result;
                if (availableQuestsProp.GetValue(manager) is IList quests)
                {
                    foreach (object questInfo in quests)
                    {
                        result.Add(questInfo);
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VefContractsCompat.Quests failed: {ex.Message}");
            }
            return result;
        }

        public static Quest QuestOf(object questInfo)
        {
            if (!ready || questInfo == null)
                return null;
            try
            {
                return (Quest)questProp.GetValue(questInfo);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VefContractsCompat.QuestOf failed: {ex.Message}");
                return null;
            }
        }

        public static object Selected(Window window)
        {
            if (!ready)
                return null;
            try
            {
                return selectedField.GetValue(window);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VefContractsCompat.Selected failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>Vehicle A: the window's own selection method, keeping the sighted detail pane in sync.</summary>
        public static void Select(Window window, object questInfo)
        {
            if (!ready)
                return;
            try
            {
                selectMethod.Invoke(window, new object[] { questInfo });
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VefContractsCompat.Select failed: {ex.Message}");
            }
        }

        public static Faction AskerFaction(object questInfo)
        {
            if (!ready || questInfo == null)
                return null;
            try
            {
                return (Faction)askerFactionField.GetValue(questInfo);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VefContractsCompat.AskerFaction failed: {ex.Message}");
                return null;
            }
        }

        public static string CostText(object questInfo)
        {
            if (!ready || questInfo == null)
                return null;
            try
            {
                object currencyInfo = currencyInfoField.GetValue(questInfo);
                return currencyInfo == null ? null : (string)getCurrencyInfoMethod.Invoke(currencyInfo, null);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VefContractsCompat.CostText failed: {ex.Message}");
                return null;
            }
        }

        public static List<string> UnmetRequirements(Window window)
        {
            var result = new List<string>();
            if (!ready)
                return result;
            try
            {
                if (listUnmetMethod.Invoke(window, null) is IEnumerable requirements)
                {
                    foreach (object req in requirements)
                    {
                        if (req is string text)
                            result.Add(text);
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VefContractsCompat.UnmetRequirements failed: {ex.Message}");
            }
            return result;
        }

        public static bool CanAccept(Quest quest)
        {
            return quest != null && QuestUtility.CanAcceptQuest(quest).Accepted;
        }

        // ------------------------------------------------------------------
        // Mutators.
        // ------------------------------------------------------------------

        /// <summary>
        /// Vehicle A: mirrors the decompiled Accept button's own call site (VEF Window_Contracts,
        /// DoQuestsList, decompiled ~L21387-21420) -- makes the choice-part pick (if any) then
        /// invokes the window's own AcceptQuestByInterface, which gates on QuestUtility.CanAcceptQuest,
        /// pops the colonist-picker FloatMenu when required, and commits via
        /// QuestGiverManager.ActivateQuest. No MUTATION-C marker is needed: the actual state change
        /// happens inside that invoked vanilla method.
        /// </summary>
        public static void AcceptSelected(Window window)
        {
            if (!ready)
                return;
            try
            {
                object sel = Selected(window);
                if (sel == null)
                    return;
                Quest quest = QuestOf(sel);
                if (quest == null)
                    return;

                object choice = choiceField.GetValue(sel);
                Action pre = null;
                bool requiresAccepter;
                if (choice != null)
                {
                    object qpc = questPartChoiceField.GetValue(sel);
                    requiresAccepter = quest.PartsListForReading.Any(p => p.RequiresAccepter);
                    pre = delegate { chooseMethod.Invoke(qpc, new object[] { choice }); };
                }
                else
                {
                    requiresAccepter = quest.RequiresAccepter;
                }

                acceptMethod.Invoke(window, new object[] { pre, requiresAccepter });
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VefContractsCompat.AcceptSelected failed: {ex.Message}");
            }
        }
    }
}
