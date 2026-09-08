using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Reflection-only surface for Vanilla Factions Expanded - Tribals' cornerstone system:
    /// <c>VFETribals.GameComponent_Tribals</c> (the singleton holding ethos, points, and owned
    /// cornerstones), <c>VFETribals.CornerstoneDef</c>, and the two windows
    /// (<c>Window_CustomizeCornerstones</c>, <c>Dialog_EditEthos</c>).
    /// <see cref="Shell.VfetCornerstonesScope"/> is the sole consumer; every VFET-typed value
    /// stays boxed here and is read back only through these methods.
    /// </summary>
    internal static class VfetCompat
    {
        private static readonly Type gameCompType;
        private static readonly Type cornerstoneDefType;
        private static readonly Type cornerstonesWindowType;
        private static readonly Type editEthosDialogType;

        private static readonly FieldInfo instanceField;
        private static readonly FieldInfo cornerstonesField;
        private static readonly FieldInfo pointsField;
        private static readonly FieldInfo ethosField;
        private static readonly FieldInfo ethosLockedField;
        private static readonly MethodInfo addCornerstoneMethod;
        private static readonly ConstructorInfo cornerstonesWindowCtor;
        private static readonly ConstructorInfo editEthosDialogCtor;

        private static readonly bool ready;

        public static bool Ready => ready;
        public static Type CornerstonesWindowType => cornerstonesWindowType;

        static VfetCompat()
        {
            var surface = new ReflectionSurface("VfetCompat");

            gameCompType = surface.Type("VFETribals.GameComponent_Tribals");
            cornerstoneDefType = surface.Type("VFETribals.CornerstoneDef");
            cornerstonesWindowType = surface.Type("VFETribals.Window_CustomizeCornerstones");
            editEthosDialogType = surface.Type("VFETribals.Dialog_EditEthos");

            instanceField = surface.Field(gameCompType, "Instance");
            cornerstonesField = surface.Field(gameCompType, "cornerstones");
            pointsField = surface.Field(gameCompType, "availableCornerstonePoints");
            ethosField = surface.Field(gameCompType, "ethos");
            ethosLockedField = surface.Field(gameCompType, "ethosLocked");
            addCornerstoneMethod = surface.Method(gameCompType, "AddCornerstone");
            cornerstonesWindowCtor = surface.Constructor(cornerstonesWindowType, Type.EmptyTypes);
            editEthosDialogCtor = surface.Constructor(editEthosDialogType, Type.EmptyTypes);

            ready = surface.Ready;
        }

        private static object Instance()
        {
            return ready ? instanceField.GetValue(null) : null;
        }

        public static int Points()
        {
            object comp = Instance();
            return comp == null ? 0 : (int)pointsField.GetValue(comp);
        }

        public static string Ethos()
        {
            object comp = Instance();
            return comp == null ? "" : (string)ethosField.GetValue(comp) ?? "";
        }

        public static bool EthosLocked()
        {
            object comp = Instance();
            return comp != null && (bool)ethosLockedField.GetValue(comp);
        }

        public static void SetEthosLocked(bool value)
        {
            object comp = Instance();
            if (comp != null)
            {
                // MUTATION-C: mirrors VFETribals.Window_CustomizeCornerstones.DoEthos; the lock
                // is a bare field flip in that window's ButtonImage handler, no callable method.
                ethosLockedField.SetValue(comp, value);
            }
        }

        public static bool IsOwned(Def cornerstone)
        {
            return OwnedList(Instance()).Contains(cornerstone);
        }

        private static List<Def> OwnedList(object comp)
        {
            var result = new List<Def>();
            if (comp == null)
            {
                return result;
            }
            if (cornerstonesField.GetValue(comp) is System.Collections.IEnumerable list)
            {
                foreach (object item in list)
                {
                    if (item is Def def)
                    {
                        result.Add(def);
                    }
                }
            }
            return result;
        }

        /// <summary>All cornerstone defs, owned ones first — the window's own display order.</summary>
        public static List<Def> AllCornerstones()
        {
            var result = new List<Def>();
            if (!ready)
            {
                return result;
            }
            result.AddRange(OwnedList(Instance()));
            foreach (Def def in GenDefDatabase.GetAllDefsInDatabaseForDef(cornerstoneDefType))
            {
                if (!result.Contains(def))
                {
                    result.Add(def);
                }
            }
            return result;
        }

        /// <summary>The mod's own unlock path; spends one point and applies the cornerstone.</summary>
        public static void Unlock(Def cornerstone)
        {
            object comp = Instance();
            if (comp != null)
            {
                addCornerstoneMethod.Invoke(comp, new object[] { cornerstone });
            }
        }

        public static void OpenCornerstonesWindow()
        {
            if (ready)
            {
                Find.WindowStack.Add((Window)cornerstonesWindowCtor.Invoke(null));
            }
        }

        public static void OpenEditEthosDialog()
        {
            if (ready)
            {
                Find.WindowStack.Add((Window)editEthosDialogCtor.Invoke(null));
            }
        }

        /// <summary>Cornerstone customization exists only while a tribal-start game runs the component.</summary>
        public static bool HasLiveGame()
        {
            return ready && Current.ProgramState == ProgramState.Playing && Instance() != null;
        }
    }
}
