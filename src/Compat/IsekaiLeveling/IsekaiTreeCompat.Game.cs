using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace RimWorldAccess
{
    /// <summary>Reflection surface over the passive (constellation) tree: tracker, tree defs, nodes, readouts.</summary>
    internal static class IsekaiTreeCompat
    {
        internal static Type TrackerType;
        internal static Type TreeDefType;
        internal static Type NodeType;
        internal static Type NodeTypeEnum;
        internal static Type BonusTypeEnum;
        internal static Type GimmickTypeEnum;

        private static FieldInfo assignedTreeField;
        private static FieldInfo availablePointsField;
        private static FieldInfo starFragmentsField;
        private static MethodInfo isUnlockedMethod;
        private static MethodInfo canUnlockMethod;
        private static PropertyInfo hasNonStartNodesProperty;
        private static MethodInfo unlockedCountInTreeMethod;
        private static PropertyInfo totalAllocatedProperty;
        private static MethodInfo getEnteredTreesMethod;
        private static MethodInfo hasEnteredTreeMethod;
        private static MethodInfo findUnlockChainMethod;
        private static MethodInfo chainCostMethod;
        private static MethodInfo respecMethod;
        private static PropertyInfo canRespecProperty;
        private static PropertyInfo respecsRemainingProperty;
        private static MethodInfo getTotalBonusMethod;
        private static MethodInfo getGimmickTierForMethod;
        private static MethodInfo getActiveGimmicksMethod;
        private static MethodInfo pawnHasStarFragmentMethod;

        private static FieldInfo treeClassField;
        private static FieldInfo treeDescriptionField;
        private static FieldInfo classGimmickField;
        private static FieldInfo classGimmickNameField;
        private static FieldInfo classGimmickDescriptionField;
        private static FieldInfo nodesField;
        private static MethodInfo getNodeMethod;
        private static MethodInfo getNeighborsMethod;
        private static MethodInfo getStartNodeMethod;

        private static FieldInfo nodeIdField;
        private static FieldInfo nodeLabelField;
        private static FieldInfo nodeDescriptionField;
        private static FieldInfo nodeTypeField;
        private static FieldInfo nodeCostField;
        private static FieldInfo nodeBonusesField;
        private static FieldInfo nodeRequireAllField;
        private static FieldInfo nodeXField;
        private static FieldInfo nodeYField;

        private static MethodInfo gimmickStatusMethod;
        private static MethodInfo gimmickNameMethod;
        private static MethodInfo describeTreeMethod;
        private static PropertyInfo allTreeDefsProperty;

        internal static readonly LazyReflectionGate Gate =
            new LazyReflectionGate("Isekai constellation tree", surface =>
            {
                Type tracker = surface.Type("IsekaiLeveling.SkillTree.PassiveTreeTracker");
                Type treeDef = surface.Type("IsekaiLeveling.SkillTree.PassiveTreeDef");
                Type node = surface.Type("IsekaiLeveling.SkillTree.PassiveNodeRecord");
                Type nodeTypeEnum = surface.Type("IsekaiLeveling.SkillTree.PassiveNodeType");
                Type bonusTypeEnum = surface.Type("IsekaiLeveling.SkillTree.PassiveBonusType");
                Type gimmickEnum = surface.Type("IsekaiLeveling.SkillTree.ClassGimmickType");
                Type readout = surface.Type("IsekaiLeveling.SkillTree.ConstellationReadout");
                Type component = surface.Type("IsekaiLeveling.IsekaiComponent");

                FieldInfo assignedTree = surface.Field(tracker, "assignedTree");
                FieldInfo availablePoints = surface.Field(tracker, "availablePoints");
                FieldInfo starFragments = surface.Field(tracker, "starFragmentsAbsorbed");
                MethodInfo isUnlocked = surface.Method(tracker, "IsUnlocked", new[] { typeof(string) });
                MethodInfo canUnlock = surface.Method(tracker, "CanUnlock", new[] { typeof(string), typeof(Pawn) });
                PropertyInfo hasNonStart = surface.Property(tracker, "HasNonStartNodes");
                MethodInfo unlockedInTree = treeDef == null ? null : surface.Method(tracker, "UnlockedCountInTree", new[] { treeDef });
                PropertyInfo totalAllocated = surface.Property(tracker, "TotalAllocatedPoints");
                MethodInfo enteredTrees = surface.Method(tracker, "GetEnteredTrees", Type.EmptyTypes);
                MethodInfo hasEnteredTree = treeDef == null ? null : surface.Method(tracker, "HasEnteredTree", new[] { treeDef });
                MethodInfo findChain = surface.Method(tracker, "FindUnlockChain", new[] { typeof(string) });
                MethodInfo chainCost = surface.Method(tracker, "ChainCost", new[] { typeof(List<string>) });
                MethodInfo respec = surface.Method(tracker, "Respec", Type.EmptyTypes);
                PropertyInfo canRespec = surface.Property(tracker, "CanRespec");
                PropertyInfo respecsRemaining = surface.Property(tracker, "RespecsRemaining");
                MethodInfo totalBonus = bonusTypeEnum == null ? null : surface.Method(tracker, "GetTotalBonus", new[] { bonusTypeEnum });
                MethodInfo gimmickTierFor = gimmickEnum == null ? null : surface.Method(tracker, "GetGimmickTierFor", new[] { gimmickEnum });
                MethodInfo activeGimmicks = surface.Method(tracker, "GetActiveGimmicks", Type.EmptyTypes);
                MethodInfo hasFragment = surface.Method(tracker, "PawnHasStarFragment", new[] { typeof(Pawn) });

                FieldInfo treeClass = surface.Field(treeDef, "treeClass");
                FieldInfo treeDescription = surface.Field(treeDef, "treeDescription");
                FieldInfo classGimmick = surface.Field(treeDef, "classGimmick");
                FieldInfo classGimmickName = surface.Field(treeDef, "classGimmickName");
                FieldInfo classGimmickDescription = surface.Field(treeDef, "classGimmickDescription");
                FieldInfo nodes = surface.Field(treeDef, "nodes");
                MethodInfo getNode = surface.Method(treeDef, "GetNode", new[] { typeof(string) });
                MethodInfo getNeighbors = surface.Method(treeDef, "GetNeighbors", new[] { typeof(string) });
                MethodInfo getStartNode = surface.Method(treeDef, "GetStartNode", Type.EmptyTypes);

                FieldInfo nodeId = surface.Field(node, "nodeId");
                FieldInfo nodeLabel = surface.Field(node, "label");
                FieldInfo nodeDescription = surface.Field(node, "description");
                FieldInfo nodeType = surface.Field(node, "nodeType");
                FieldInfo nodeCost = surface.Field(node, "cost");
                FieldInfo nodeBonuses = surface.Field(node, "bonuses");
                FieldInfo nodeRequireAll = surface.Field(node, "requireAllConnected");
                FieldInfo nodeX = surface.Field(node, "x");
                FieldInfo nodeY = surface.Field(node, "y");

                MethodInfo gimmickStatus = component == null || gimmickEnum == null ? null
                    : surface.Method(readout, "GimmickStatus", new[] { typeof(Pawn), component, gimmickEnum, typeof(int), typeof(bool).MakeByRefType() });
                MethodInfo gimmickName = gimmickEnum == null ? null : surface.Method(readout, "GimmickName", new[] { gimmickEnum });
                MethodInfo describeTree = component == null || treeDef == null ? null
                    : surface.Method(readout, "DescribeTree", new[] { typeof(Pawn), component, treeDef });
                PropertyInfo allDefs = treeDef == null ? null
                    : surface.Required("DefDatabase<PassiveTreeDef>.AllDefsListForReading",
                        typeof(DefDatabase<>).MakeGenericType(treeDef).GetProperty("AllDefsListForReading"));

                if (!surface.Ready)
                    return false;

                TrackerType = tracker;
                TreeDefType = treeDef;
                NodeType = node;
                NodeTypeEnum = nodeTypeEnum;
                BonusTypeEnum = bonusTypeEnum;
                GimmickTypeEnum = gimmickEnum;
                assignedTreeField = assignedTree;
                availablePointsField = availablePoints;
                starFragmentsField = starFragments;
                isUnlockedMethod = isUnlocked;
                canUnlockMethod = canUnlock;
                hasNonStartNodesProperty = hasNonStart;
                unlockedCountInTreeMethod = unlockedInTree;
                totalAllocatedProperty = totalAllocated;
                getEnteredTreesMethod = enteredTrees;
                hasEnteredTreeMethod = hasEnteredTree;
                findUnlockChainMethod = findChain;
                chainCostMethod = chainCost;
                respecMethod = respec;
                canRespecProperty = canRespec;
                respecsRemainingProperty = respecsRemaining;
                getTotalBonusMethod = totalBonus;
                getGimmickTierForMethod = gimmickTierFor;
                getActiveGimmicksMethod = activeGimmicks;
                pawnHasStarFragmentMethod = hasFragment;
                treeClassField = treeClass;
                treeDescriptionField = treeDescription;
                classGimmickField = classGimmick;
                classGimmickNameField = classGimmickName;
                classGimmickDescriptionField = classGimmickDescription;
                nodesField = nodes;
                getNodeMethod = getNode;
                getNeighborsMethod = getNeighbors;
                getStartNodeMethod = getStartNode;
                nodeIdField = nodeId;
                nodeLabelField = nodeLabel;
                nodeDescriptionField = nodeDescription;
                nodeTypeField = nodeType;
                nodeCostField = nodeCost;
                nodeBonusesField = nodeBonuses;
                nodeRequireAllField = nodeRequireAll;
                nodeXField = nodeX;
                nodeYField = nodeY;
                gimmickStatusMethod = gimmickStatus;
                gimmickNameMethod = gimmickName;
                describeTreeMethod = describeTree;
                allTreeDefsProperty = allDefs;
                return true;
            });

        // ---- Tracker ----

        internal static string AssignedTree(object tracker) => assignedTreeField.GetValue(tracker) as string;
        internal static int AvailablePoints(object tracker) => (int)availablePointsField.GetValue(tracker);
        internal static int StarFragmentsAbsorbed(object tracker) => (int)starFragmentsField.GetValue(tracker);
        internal static bool IsUnlocked(object tracker, string nodeId) => (bool)isUnlockedMethod.Invoke(tracker, new object[] { nodeId });
        internal static bool CanUnlock(object tracker, string nodeId, Pawn pawn) => (bool)canUnlockMethod.Invoke(tracker, new object[] { nodeId, pawn });
        internal static bool HasNonStartNodes(object tracker) => (bool)hasNonStartNodesProperty.GetValue(tracker, null);
        internal static int UnlockedCountInTree(object tracker, object treeDef) => (int)unlockedCountInTreeMethod.Invoke(tracker, new[] { treeDef });
        internal static int TotalAllocatedPoints(object tracker) => (int)totalAllocatedProperty.GetValue(tracker, null);
        internal static IList EnteredTrees(object tracker) => getEnteredTreesMethod.Invoke(tracker, null) as IList;
        internal static bool HasEnteredTree(object tracker, object treeDef) => (bool)hasEnteredTreeMethod.Invoke(tracker, new[] { treeDef });
        internal static List<string> FindUnlockChain(object tracker, string nodeId) => findUnlockChainMethod.Invoke(tracker, new object[] { nodeId }) as List<string>;
        internal static int ChainCost(object tracker, List<string> chain) => (int)chainCostMethod.Invoke(tracker, new object[] { chain });
        internal static bool CanRespec(object tracker) => (bool)canRespecProperty.GetValue(tracker, null);
        internal static int RespecsRemaining(object tracker) => (int)respecsRemainingProperty.GetValue(tracker, null);
        internal static float TotalBonus(object tracker, object bonusType) => (float)getTotalBonusMethod.Invoke(tracker, new[] { bonusType });
        internal static int GimmickTierFor(object tracker, object gimmick) => (int)getGimmickTierForMethod.Invoke(tracker, new[] { gimmick });
        internal static IList ActiveGimmicks(object tracker) => getActiveGimmicksMethod.Invoke(tracker, null) as IList;
        internal static bool PawnHasStarFragment(Pawn pawn) => (bool)pawnHasStarFragmentMethod.Invoke(null, new object[] { pawn });

        /// <summary>Vehicle A: the tracker's own Respec, exactly what the window's Respec button calls.</summary>
        internal static void Respec(object tracker) => respecMethod.Invoke(tracker, null);

        // ---- Tree defs ----

        internal static IList AllTreeDefs() => allTreeDefsProperty.GetValue(null, null) as IList;
        internal static string TreeClass(object treeDef) => treeClassField.GetValue(treeDef) as string;
        internal static string TreeDescription(object treeDef) => treeDescriptionField.GetValue(treeDef) as string;
        internal static object ClassGimmick(object treeDef) => classGimmickField.GetValue(treeDef);
        internal static string ClassGimmickName(object treeDef) => classGimmickNameField.GetValue(treeDef) as string;
        internal static string ClassGimmickDescription(object treeDef) => classGimmickDescriptionField.GetValue(treeDef) as string;
        internal static IList Nodes(object treeDef) => nodesField.GetValue(treeDef) as IList;
        internal static object GetNode(object treeDef, string nodeId) => getNodeMethod.Invoke(treeDef, new object[] { nodeId });
        internal static List<string> Neighbors(object treeDef, string nodeId) => getNeighborsMethod.Invoke(treeDef, new object[] { nodeId }) as List<string>;
        internal static object StartNode(object treeDef) => getStartNodeMethod.Invoke(treeDef, null);

        internal static object TreeDefForClass(string treeClass)
        {
            IList defs = AllTreeDefs();
            if (defs == null)
                return null;
            foreach (object def in defs)
            {
                if (TreeClass(def) == treeClass)
                    return def;
            }
            return null;
        }

        internal static bool GimmickIsNone(object gimmick) => gimmick == null || Convert.ToInt32(gimmick) == 0;

        // ---- Nodes ----

        internal static string NodeId(object node) => nodeIdField.GetValue(node) as string;
        internal static string NodeLabel(object node) => nodeLabelField.GetValue(node) as string;
        internal static string NodeDescription(object node) => nodeDescriptionField.GetValue(node) as string;
        internal static object NodeKind(object node) => nodeTypeField.GetValue(node);
        internal static int NodeKindOrdinal(object node) => Convert.ToInt32(NodeKind(node));
        internal static int NodeCost(object node) => (int)nodeCostField.GetValue(node);
        internal static IList NodeBonuses(object node) => nodeBonusesField.GetValue(node) as IList;
        internal static bool NodeRequiresAllConnected(object node) => (bool)nodeRequireAllField.GetValue(node);
        internal static float NodeX(object node) => (float)nodeXField.GetValue(node);
        internal static float NodeY(object node) => (float)nodeYField.GetValue(node);

        /// <summary>PassiveNodeType.Start is ordinal 0 in the mod's enum.</summary>
        internal static bool IsStartNode(object node) => NodeKindOrdinal(node) == 0;

        // ---- Readouts ----

        internal static string GimmickStatus(Pawn pawn, object comp, object gimmick, int tier, out bool active)
        {
            object[] args = { pawn, comp, gimmick, tier, false };
            string text = gimmickStatusMethod.Invoke(null, args) as string;
            active = (bool)args[4];
            return text;
        }

        internal static string GimmickName(object gimmick) => gimmickNameMethod.Invoke(null, new[] { gimmick }) as string;
        internal static string DescribeTree(Pawn pawn, object comp, object treeDef) => describeTreeMethod.Invoke(null, new[] { pawn, comp, treeDef }) as string;
    }
}
