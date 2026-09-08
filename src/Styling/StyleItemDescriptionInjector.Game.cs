using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Fills in <see cref="Def.description"/> for hairs, beards and tattoos from
    /// <see cref="StyleDescriptionHelper"/>. Vanilla authors no description for any style item,
    /// so every UI that reads one — ours, the game's, and mods' own tooltip builders — has
    /// nothing to say about what a style looks like. Writing the description onto the def itself
    /// reaches all of them at once instead of teaching each presentation about the helper.
    ///
    /// Only an EMPTY description is filled, so a modded style that ships its own prose keeps it.
    /// Runs again after every play-data load because a language change rebuilds the defs from
    /// scratch, which discards the injected text along with everything else.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class StyleItemDescriptionInjector
    {
        static StyleItemDescriptionInjector()
        {
            Inject();
        }

        public static void Inject()
        {
            // StyleItemDef is abstract: the game registers hairs, beards and tattoos under their
            // own concrete DefDatabase<T>, never under DefDatabase<StyleItemDef> (empty, verified
            // against Dialog_StylingStation's per-type DefDatabase<T> enumeration).
            InjectFor(DefDatabase<HairDef>.AllDefsListForReading);
            InjectFor(DefDatabase<BeardDef>.AllDefsListForReading);
            InjectFor(DefDatabase<TattooDef>.AllDefsListForReading);
        }

        private static void InjectFor<T>(System.Collections.Generic.List<T> defs) where T : StyleItemDef
        {
            foreach (T def in defs)
            {
                if (!def.description.NullOrEmpty())
                    continue;
                string description = StyleDescriptionHelper.Describe(def);
                if (!description.NullOrEmpty())
                    def.description = description;
            }
        }
    }

    /// <summary>A language change reloads every def; re-run the injection on the fresh set.</summary>
    [HarmonyPatch(typeof(PlayDataLoader), "LoadAllPlayData")]
    public static class StyleItemDescriptionReloadPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            StyleItemDescriptionInjector.Inject();
        }
    }
}
