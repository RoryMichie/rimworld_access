using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Compat for the two RimWorld of Magic abilities that target the WORLD map
    /// rather than the local one: Gateway (TorannMagic.Verb_FoldReality) and
    /// Global Light Skip (TorannMagic.Verb_LightSkipGlobal).
    ///
    /// Both bypass vanilla's Ability system entirely. Their TryCastShot calls a
    /// private StartChoosingDestination that jumps the camera to the planet and
    /// calls Find.WorldTargeter.BeginTargeting directly, so
    /// AbilityTargetingPatch's Command_Ability hook never runs (there is no
    /// Command_Ability), TransportPodPatch's WorldTargeter hook never runs (each
    /// verb passes its own mouse attachment, not CompLaunchable's), and no
    /// external-targeter provider owns the session either. The result was a
    /// live world-targeting session that announced nothing and that
    /// TargetingScope's confirm/cancel claims did not cover, leaving both
    /// abilities uncastable from the keyboard.
    ///
    /// The fix is the same shape as JecsAbilityCompat.ProcessInputPostfix:
    /// postfix the method that started the session and, once the session is
    /// confirmed live, hand it to the state that already knows how to drive a
    /// WorldTargeter — WorldAbilityTargetingState, whose confirm invokes
    /// WorldTargeter's own callback rather than reimplementing either verb's
    /// ChoseWorldTarget. What happens after the tile is chosen (Gateway opening
    /// a second, local targeter on the destination map; Global Light Skip's
    /// arrival float menu and confirmation box) is left entirely to the mod and
    /// rides the shell's existing targeting, float-menu and message-box paths.
    /// </summary>
    internal static class RwomWorldAbilityCompat
    {
        // AbilityUser.Verb_UseAbility.Ability -> PawnAbility, and PawnAbility.Def,
        // used only to name the session in the opening announcement.
        private static PropertyInfo verbAbilityProperty;
        private static PropertyInfo pawnAbilityDefProperty;

        private static readonly string[] WorldTargetingVerbTypes =
        {
            "TorannMagic.Verb_FoldReality",
            "TorannMagic.Verb_LightSkipGlobal",
        };

        public static void TryRegister(Harmony harmony)
        {
            try
            {
                Type verbUseAbilityType = AccessTools.TypeByName("AbilityUser.Verb_UseAbility");
                if (verbUseAbilityType == null)
                    return; // JecsTools' AbilityUser framework isn't loaded.

                if (AccessTools.TypeByName("TorannMagic.CompAbilityUserMagic") == null)
                    return; // RimWorld of Magic itself isn't loaded.

                var surface = new ReflectionSurface("RwomWorldAbilityCompat");
                surface.Supplied("AbilityUser.Verb_UseAbility", verbUseAbilityType);
                verbAbilityProperty = surface.Property(verbUseAbilityType, "Ability");
                pawnAbilityDefProperty = surface.Property(verbAbilityProperty?.PropertyType, "Def");
                if (!surface.Ready)
                {
                    return; // Without a label the sessions would announce unnamed.
                }

                var postfix = new HarmonyMethod(typeof(RwomWorldAbilityCompat),
                    nameof(StartChoosingDestinationPostfix));

                foreach (string typeName in WorldTargetingVerbTypes)
                {
                    Type verbType = AccessTools.TypeByName(typeName);
                    if (verbType == null)
                    {
                        ModLogger.Error($"RwomWorldAbilityCompat: {typeName} did not resolve; that ability's " +
                            "world targeting will open silently.");
                        continue;
                    }

                    MethodInfo start = AccessTools.Method(verbType, "StartChoosingDestination");
                    if (start == null)
                    {
                        ModLogger.Error($"RwomWorldAbilityCompat: {typeName}.StartChoosingDestination did not " +
                            "resolve; that ability's world targeting will open silently.");
                        continue;
                    }

                    harmony.Patch(start, postfix: postfix);
                }

            }
            catch (Exception ex)
            {
                ModLogger.Error($"RwomWorldAbilityCompat registration failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Runs after the verb has called Find.WorldTargeter.BeginTargeting.
        /// Opens the shared world-targeting state so the session announces itself
        /// and TargetingScope's confirm/cancel claims go live.
        /// </summary>
        public static void StartChoosingDestinationPostfix(object __instance)
        {
            try
            {
                if (Find.WorldTargeter == null || !Find.WorldTargeter.IsTargeting)
                    return; // The verb declined to start a session after all.

                if (WorldAbilityTargetingState.IsActive || TransportPodLaunchState.IsActive)
                    return; // Already owned; never double-activate.

                WorldAbilityTargetingState.OpenExternal(ResolveLabel(__instance), (__instance as Verb)?.CasterPawn);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"RwomWorldAbilityCompat.StartChoosingDestinationPostfix failed: {ex.Message}");
            }
        }

        private static string ResolveLabel(object verb)
        {
            try
            {
                object pawnAbility = verbAbilityProperty.GetValue(verb);
                if (pawnAbility == null)
                    return null;

                return (pawnAbilityDefProperty.GetValue(pawnAbility) as Def)?.LabelCap;
            }
            catch
            {
                return null;
            }
        }
    }
}
