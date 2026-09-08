using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Reflection-only surface for Character Development (WantsAndQuirks): the static reward
    /// state, the reward-bubble nodes, the Characters main tab's own gated claim path, and the
    /// per-pawn wants data. <see cref="Shell.WqCharactersTabScope"/> is the sole consumer.
    /// </summary>
    internal static class WqCompat
    {
        private static readonly Type stateType;
        private static readonly Type rewardNodeType;
        private static readonly Type rewardDefType;
        private static readonly Type mainTabType;
        private static readonly Type modType;
        private static readonly Type settingsType;
        private static readonly Type utilityType;
        private static readonly Type wantsDataType;
        private static readonly Type activeWantType;
        private static readonly Type itabType;

        private static readonly FieldInfo rewardNodesField;
        private static readonly FieldInfo rewardPointsField;
        private static readonly FieldInfo characterPointsField;

        private static readonly FieldInfo nodeDefField;
        private static readonly PropertyInfo nodeLabelCapProp;
        private static readonly PropertyInfo nodeDescriptionProp;
        private static readonly FieldInfo defRarityField;

        private static readonly MethodInfo claimRewardMethod;
        private static readonly FieldInfo modSettingsField;
        private static readonly FieldInfo pointsNeededField;

        private static readonly MethodInfo canHaveWantsMethod;
        private static readonly MethodInfo getWantsDataMethod;
        private static readonly FieldInfo activeWantsField;

        private static readonly bool ready;

        public static bool Ready => ready;
        public static Type MainTabType => mainTabType;
        public static Type InspectTabType => itabType;

        static WqCompat()
        {
            var surface = new ReflectionSurface("WqCompat");

            stateType = surface.Type("WantsAndQuirks.State");
            rewardNodeType = surface.Type("WantsAndQuirks.RewardNode");
            rewardDefType = surface.Type("WantsAndQuirks.RewardDef");
            mainTabType = surface.Type("WantsAndQuirks.MainTabWindow_Characters");
            modType = surface.Type("WantsAndQuirks.WantsAndQuirksMod");
            settingsType = surface.Type("WantsAndQuirks.WantsAndQuirksSettings");
            utilityType = surface.Type("WantsAndQuirks.WantsAndQuirksUtility");
            wantsDataType = surface.Type("WantsAndQuirks.PawnWantsData");
            activeWantType = surface.Type("WantsAndQuirks.ActiveWant");
            itabType = surface.Type("WantsAndQuirks.ITab_Pawn_WantsAndQuirks");

            rewardNodesField = surface.Field(stateType, "rewardNodes");
            rewardPointsField = surface.Field(stateType, "rewardPoints");
            characterPointsField = surface.Field(stateType, "characterPoints");

            nodeDefField = surface.Field(rewardNodeType, "def");
            nodeLabelCapProp = surface.Property(rewardNodeType, "LabelCap");
            nodeDescriptionProp = surface.Property(rewardNodeType, "Description");
            defRarityField = surface.Field(rewardDefType, "rarity");

            claimRewardMethod = surface.Method(mainTabType, "ClaimReward");
            modSettingsField = surface.Field(modType, "settings");
            pointsNeededField = surface.Field(settingsType, "pointsNeededForReward");

            canHaveWantsMethod = surface.Method(utilityType, "CanHaveWants");
            getWantsDataMethod = surface.Method(utilityType, "GetWantsData");
            activeWantsField = surface.Field(wantsDataType, "activeWants");

            ready = surface.Ready;
        }

        public static int RewardPoints() => ready ? (int)rewardPointsField.GetValue(null) : 0;
        public static int CharacterPoints() => ready ? (int)characterPointsField.GetValue(null) : 0;

        public static int PointsNeededForReward()
        {
            object settings = modSettingsField.GetValue(null);
            return settings == null ? 0 : (int)pointsNeededField.GetValue(settings);
        }

        public static List<object> RewardNodes()
        {
            var result = new List<object>();
            if (ready && rewardNodesField.GetValue(null) is IEnumerable nodes)
            {
                foreach (object node in nodes)
                {
                    result.Add(node);
                }
            }
            return result;
        }

        public static string NodeLabel(object node) => nodeLabelCapProp.GetValue(node) as string ?? "";
        public static string NodeDescription(object node) => nodeDescriptionProp.GetValue(node) as string ?? "";

        /// <summary>RewardRarity ordinal: 0 common, 1 uncommon, 2 rare, 3 legendary.</summary>
        public static int NodeRarity(object node)
        {
            object def = nodeDefField.GetValue(node);
            return def == null ? 0 : (int)defRarityField.GetValue(def);
        }

        /// <summary>
        /// The tab's own claim path: checks reward points and valid recipients (with its own
        /// reject messages) before opening the recipient-picker dialog.
        /// </summary>
        public static void ClaimReward(Window mainTab, object node)
        {
            claimRewardMethod.Invoke(mainTab, new[] { node });
        }

        public static bool CanHaveWants(Pawn pawn)
        {
            return ready && (bool)canHaveWantsMethod.Invoke(null, new object[] { pawn });
        }

        public static int ActiveWantCount(Pawn pawn)
        {
            object data = getWantsDataMethod.Invoke(null, new object[] { pawn });
            return data == null ? 0 : (activeWantsField.GetValue(data) as IList)?.Count ?? 0;
        }
    }
}
