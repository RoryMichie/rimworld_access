using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Reflection-only compatibility surface for Vanilla Psycasts Expanded's
    /// psyset editing -- <c>VanillaPsycastsExpanded.PsySet</c>,
    /// <c>Hediff_PsycastAbilities</c>'s psyset list, and the two dialogs that
    /// manage them (<c>UI.Dialog_Psyset</c>, the membership editor;
    /// <c>UI.Dialog_RenamePsyset</c>, a plain <c>Dialog_Rename&lt;PsySet&gt;</c>
    /// already covered for free by the shell's generic rename registration).
    /// Mirrors the <see cref="VfAssignSeatsCompat"/> idiom: every VPE/VEF type
    /// and member is resolved once behind <see cref="Ready"/>, a missing TYPE
    /// is a silent decline (VPE not installed), a resolved type missing a
    /// MEMBER is a logged error and a graceful decline.
    /// <see cref="VpePsysetEditorScope"/> and <see cref="VpePsycastsTabAdapter"/>
    /// are the sole consumers and never touch reflection directly -- every
    /// VPE/VEF-typed value (the psyset, its abilities set, CompAbilities)
    /// stays boxed as <c>object</c>/<see cref="ThingComp"/> here and is read
    /// back only through this facade's own methods.
    /// </summary>
    internal static class VpePsysetCompat
    {
        private static readonly Type psysetType;
        private static readonly Type dialogPsysetType;
        private static readonly Type dialogRenameType;
        private static readonly Type hediffType;
        private static readonly Type compAbilitiesType;
        private static readonly Type abilityType;
        private static readonly Type extType;

        private static readonly FieldInfo abilitiesField;
        private static readonly MethodInfo hashSetAddMethod;
        private static readonly MethodInfo hashSetRemoveMethod;
        private static readonly MethodInfo hashSetContainsMethod;
        private static readonly FieldInfo nameField;
        private static readonly FieldInfo psysetsField;
        private static readonly MethodInfo removePsySetMethod;

        private static readonly ConstructorInfo dialogPsysetCtor;
        private static readonly FieldInfo dialogPsysetPsysetField;
        private static readonly FieldInfo dialogPsysetPawnField;
        private static readonly FieldInfo dialogPsysetCompField;

        private static readonly ConstructorInfo dialogRenameCtor;

        private static readonly PropertyInfo learnedAbilitiesProp;
        private static readonly MethodInfo hasAbilityMethod;
        private static readonly FieldInfo abilityDefField;

        private static readonly bool ready;

        public static bool Ready => ready;
        public static Type DialogPsysetType => dialogPsysetType;

        static VpePsysetCompat()
        {
            var surface = new ReflectionSurface("VpePsysetCompat");

            psysetType = surface.Type("VanillaPsycastsExpanded.PsySet");
            dialogPsysetType = surface.Type("VanillaPsycastsExpanded.UI.Dialog_Psyset");
            dialogRenameType = surface.Type("VanillaPsycastsExpanded.UI.Dialog_RenamePsyset");
            hediffType = surface.Type("VanillaPsycastsExpanded.Hediff_PsycastAbilities");
            compAbilitiesType = surface.Type("VEF.Abilities.CompAbilities");
            abilityType = surface.Type("VEF.Abilities.Ability");
            extType = surface.Type("VanillaPsycastsExpanded.AbilityExtension_Psycast");

            abilitiesField = surface.Field(psysetType, "Abilities");
            if (abilitiesField != null)
            {
                // The set's own Add/Remove/Contains, off its runtime HashSet<T> instantiation.
                hashSetAddMethod = abilitiesField.FieldType.GetMethod("Add");
                hashSetRemoveMethod = abilitiesField.FieldType.GetMethod("Remove");
                hashSetContainsMethod = abilitiesField.FieldType.GetMethod("Contains");
            }
            nameField = surface.Field(psysetType, "Name");
            psysetsField = surface.Field(hediffType, "psysets");
            removePsySetMethod = psysetType != null
                ? surface.Method(hediffType, "RemovePsySet", new[] { psysetType })
                : null;

            dialogPsysetCtor = dialogPsysetType != null && psysetType != null
                ? AccessTools.Constructor(dialogPsysetType, new[] { psysetType, typeof(Pawn) })
                : null;
            dialogPsysetPsysetField = surface.Field(dialogPsysetType, "psyset");
            dialogPsysetPawnField = surface.Field(dialogPsysetType, "pawn");
            dialogPsysetCompField = surface.Field(dialogPsysetType, "compAbilities");

            dialogRenameCtor = dialogRenameType != null && psysetType != null
                ? AccessTools.Constructor(dialogRenameType, new[] { psysetType })
                : null;

            learnedAbilitiesProp = surface.Property(compAbilitiesType, "LearnedAbilities");
            hasAbilityMethod = surface.Method(compAbilitiesType, "HasAbility");
            abilityDefField = surface.Field(abilityType, "def");

            ready = surface.Ready && hashSetAddMethod != null && hashSetRemoveMethod != null
                && hashSetContainsMethod != null && dialogPsysetCtor != null && dialogRenameCtor != null;
        }

        // ------------------------------------------------------------------
        // Reads.
        // ------------------------------------------------------------------

        public static string PsysetName(object psyset)
        {
            if (!ready)
                return "";
            try
            {
                return (string)nameField.GetValue(psyset);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VpePsysetCompat.PsysetName failed: {ex.Message}");
                return "";
            }
        }

        public static Pawn PawnOf(Window dialog)
        {
            if (!ready)
                return null;
            try
            {
                return (Pawn)dialogPsysetPawnField.GetValue(dialog);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VpePsysetCompat.PawnOf failed: {ex.Message}");
                return null;
            }
        }

        public static object PsysetOf(Window dialog)
        {
            if (!ready)
                return null;
            try
            {
                return dialogPsysetPsysetField.GetValue(dialog);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VpePsysetCompat.PsysetOf failed: {ex.Message}");
                return null;
            }
        }

        public static ThingComp CompAbilitiesOf(Window dialog)
        {
            if (!ready)
                return null;
            try
            {
                return (ThingComp)dialogPsysetCompField.GetValue(dialog);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VpePsysetCompat.CompAbilitiesOf failed: {ex.Message}");
                return null;
            }
        }

        public static List<Def> Members(object psyset)
        {
            var result = new List<Def>();
            if (!ready || psyset == null)
                return result;
            try
            {
                if (abilitiesField.GetValue(psyset) is IEnumerable set)
                {
                    foreach (object obj in set)
                    {
                        if (obj is Def def)
                            result.Add(def);
                    }
                }
                result.Sort((a, b) => string.Compare(a.LabelCap, b.LabelCap, StringComparison.CurrentCultureIgnoreCase));
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VpePsysetCompat.Members failed: {ex.Message}");
            }
            return result;
        }

        public static bool IsMember(object psyset, Def ability)
        {
            if (!ready || psyset == null || ability == null)
                return false;
            try
            {
                return (bool)hashSetContainsMethod.Invoke(abilitiesField.GetValue(psyset), new object[] { ability });
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VpePsysetCompat.IsMember failed: {ex.Message}");
                return false;
            }
        }

        public static bool HasAbility(ThingComp compAbilities, Def ability)
        {
            if (!ready || compAbilities == null || ability == null)
                return false;
            try
            {
                return (bool)hasAbilityMethod.Invoke(compAbilities, new object[] { ability });
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VpePsysetCompat.HasAbility failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>The abilities the pawn owns that are psycasts and not already in the set.</summary>
        public static List<Def> Candidates(object psyset, ThingComp compAbilities)
        {
            var result = new List<Def>();
            if (!ready || psyset == null || compAbilities == null)
                return result;
            try
            {
                var seen = new HashSet<Def>();
                IList learned = (IList)learnedAbilitiesProp.GetValue(compAbilities);
                if (learned == null)
                    return result;
                foreach (object ability in learned)
                {
                    if (!(abilityDefField.GetValue(ability) is Def def))
                        continue;
                    if (!IsPsycast(def))
                        continue;
                    if (IsMember(psyset, def))
                        continue;
                    if (seen.Add(def))
                        result.Add(def);
                }
                result.Sort((a, b) => string.Compare(a.LabelCap, b.LabelCap, StringComparison.CurrentCultureIgnoreCase));
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VpePsysetCompat.Candidates failed: {ex.Message}");
            }
            return result;
        }

        private static bool IsPsycast(Def def)
        {
            return def.modExtensions != null && def.modExtensions.Any(e => extType.IsInstanceOfType(e));
        }

        // ------------------------------------------------------------------
        // Mutators.
        // ------------------------------------------------------------------

        public static bool AddAbility(object psyset, ThingComp compAbilities, Def ability)
        {
            if (!ready || psyset == null || compAbilities == null || ability == null)
                return false;
            try
            {
                if (!HasAbility(compAbilities, ability))
                    return false;
                object set = abilitiesField.GetValue(psyset);
                // MUTATION-C: mirrors Dialog_Psyset.DoWindowContents' DropArea callback
                // (this.psyset.Abilities.Add((AbilityDef) obj)); the add is an inline IMGUI drop
                // lambda with no invocable vehicle. Gated by HasAbility exactly as vanilla only makes
                // owned abilities draggable into the set.
                hashSetAddMethod.Invoke(set, new object[] { ability });
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VpePsysetCompat.AddAbility failed: {ex.Message}");
                return false;
            }
        }

        public static bool RemoveAbility(object psyset, Def ability)
        {
            if (!ready || psyset == null || ability == null)
                return false;
            try
            {
                object set = abilitiesField.GetValue(psyset);
                if (!(bool)hashSetContainsMethod.Invoke(set, new object[] { ability }))
                    return false;
                // MUTATION-C: mirrors Dialog_Psyset.DoWindowContents' ability-tile ButtonInvisible
                // (this.psyset.Abilities.Remove(def)); inline IMGUI click lambda, no invocable vehicle.
                hashSetRemoveMethod.Invoke(set, new object[] { ability });
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VpePsysetCompat.RemoveAbility failed: {ex.Message}");
                return false;
            }
        }

        public static object CreatePsyset(Hediff hediff, string name)
        {
            if (!ready || hediff == null)
                return null;
            try
            {
                object psyset = Activator.CreateInstance(psysetType);
                IList list = (IList)psysetsField.GetValue(hediff);
                // MUTATION-C: mirrors ITab_Pawn_Psycasts.DoPsysets' "VPE.CreatePsyset" button
                // (hediff.psysets.Add(new PsySet { Name = ... })); reflection construction splits
                // that single statement into this field SetValue and the list Add below.
                nameField.SetValue(psyset, name);
                list.Add(psyset);
                return psyset;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VpePsysetCompat.CreatePsyset failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>Vehicle A: invokes VPE's own Hediff_PsycastAbilities.RemovePsySet, which does its own bookkeeping.</summary>
        public static void RemovePsyset(Hediff hediff, object psyset)
        {
            if (!ready || hediff == null || psyset == null)
                return;
            try
            {
                removePsySetMethod.Invoke(hediff, new object[] { psyset });
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VpePsysetCompat.RemovePsyset failed: {ex.Message}");
            }
        }

        /// <summary>Vehicle A: constructs and opens VPE's own Dialog_Psyset, the membership editor.</summary>
        public static void OpenEditor(object psyset, Pawn pawn)
        {
            if (!ready || psyset == null || pawn == null)
                return;
            try
            {
                Find.WindowStack.Add((Window)dialogPsysetCtor.Invoke(new object[] { psyset, pawn }));
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VpePsysetCompat.OpenEditor failed: {ex.Message}");
            }
        }

        /// <summary>Vehicle A: constructs and opens VPE's own Dialog_RenamePsyset (a Dialog_Rename&lt;PsySet&gt;, already covered by the shell's generic rename registration).</summary>
        public static void OpenRename(object psyset)
        {
            if (!ready || psyset == null)
                return;
            try
            {
                Find.WindowStack.Add((Window)dialogRenameCtor.Invoke(new object[] { psyset }));
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VpePsysetCompat.OpenRename failed: {ex.Message}");
            }
        }
    }
}
