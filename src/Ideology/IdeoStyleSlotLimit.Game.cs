using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// How many style-category slots the ideoligion editor may offer. Vanilla's own cap is
    /// <c>IdeoFoundation.MaxStyleCategories</c>, read rather than copied so a game update
    /// carries; VE Memes and Alpha Memes each raise it by transpiling the loop bound in
    /// <c>IdeoUIUtility.DoStyles</c> to a call returning their own settings slider, which
    /// leaves no field, property, or method on the vanilla side to read the live value from
    /// — the raised cap exists only inside patched IL. So the loaded mods' own settings are
    /// the decision object here, read by reflection through one <see cref="ReflectionSurface"/>
    /// per mod, each silent when its own mod is absent.
    ///
    /// The MINIMUM of the loaded providers wins, not the maximum. Both mods patch the same
    /// instruction, so when both are loaded only whichever Harmony transpiler runs first
    /// actually rewrites it (the second no longer finds the constant) and the winner depends
    /// on mod load order. Over-reporting would offer a slot vanilla then refuses to draw,
    /// leaving a style category the player set and cannot see; under-reporting only costs a
    /// slot. Both mods cap their slider at 5 and default to 5, so the two agree in practice.
    /// </summary>
    internal static class IdeoStyleSlotLimit
    {
        private const int VanillaFallback = 3;

        private static readonly int vanillaLimit;

        private static readonly bool veMemesReady;
        private static readonly FieldInfo veMemesStylesAmount;

        private static readonly bool alphaMemesReady;
        private static readonly FieldInfo alphaMemesStylesAmount;

        static IdeoStyleSlotLimit()
        {
            vanillaLimit = ReadVanillaLimit();

            var veMemesSurface = new ReflectionSurface("IdeoStyleSlotLimit.VEMemes");
            Type veMemesType = veMemesSurface.Type("VanillaMemesExpanded.VanillaMemesExpanded_Settings");
            veMemesStylesAmount = veMemesSurface.Field(veMemesType, "stylesAmount");
            veMemesReady = veMemesSurface.Ready;

            var alphaMemesSurface = new ReflectionSurface("IdeoStyleSlotLimit.AlphaMemes");
            Type alphaMemesType = alphaMemesSurface.Type("AlphaMemes.AlphaMemes_Settings");
            alphaMemesStylesAmount = alphaMemesSurface.Field(alphaMemesType, "stylesAmount");
            alphaMemesReady = alphaMemesSurface.Ready;
        }

        /// <summary>The live cap, never below vanilla's own.</summary>
        public static int Current
        {
            get
            {
                int limit = int.MaxValue;
                if (veMemesReady)
                {
                    int value = ReadProvider(veMemesStylesAmount);
                    if (value > 0 && value < limit)
                        limit = value;
                }
                if (alphaMemesReady)
                {
                    int value = ReadProvider(alphaMemesStylesAmount);
                    if (value > 0 && value < limit)
                        limit = value;
                }
                return limit == int.MaxValue || limit < vanillaLimit ? vanillaLimit : limit;
            }
        }

        private static int ReadVanillaLimit()
        {
            try
            {
                FieldInfo field = AccessTools.Field(typeof(IdeoFoundation), "MaxStyleCategories");
                if (field != null && field.IsLiteral)
                    return (int)field.GetRawConstantValue();
            }
            catch (Exception ex)
            {
                ModLogger.Error($"IdeoStyleSlotLimit could not read MaxStyleCategories: {ex.Message}");
            }
            return VanillaFallback;
        }

        private static int ReadProvider(FieldInfo field)
        {
            try
            {
                return (int)(float)field.GetValue(null);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"IdeoStyleSlotLimit provider read failed: {ex.Message}");
                return 0;
            }
        }
    }
}
