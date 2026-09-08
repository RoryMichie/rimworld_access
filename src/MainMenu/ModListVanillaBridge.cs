using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Steamworks;
using Verse;
using Verse.Steam;

namespace RimWorldAccess
{
    /// <summary>
    /// Named accessors for Page_ModsConfig's (and one Workshop) private/internal
    /// members, backing the ModList family (ModListState/Navigation/DetailView/
    /// Actions/Announcements). Every lookup routes through VanillaAccess's shared
    /// (Type, name) cache instead of resolving MemberInfo locally, so no ModList
    /// caller touches raw MemberInfo. Consolidates 16 of the 19 members the
    /// original static-ctor cache held (the other 3 -- lastSelectedIndex,
    /// inactiveModListOrderCached, RecacheSelectedModInfo -- were cached but
    /// never read/called even before the split and were dropped once the
    /// re-audit confirmed zero callers): 8 instance/static fields on
    /// Page_ModsConfig used across the split (primarySelectedMod, modListsDirty, primaryModHandle,
    /// activeModListOrderCached, filteredActiveModListOrderCached,
    /// filteredInactiveModListOrderCached, modWarningsCached, selectedMods),
    /// 7 methods used across the split (TrySetModActive, TrySetModInactive,
    /// SelectMod, ModListsInOrder, SaveModList, LoadModList, UnsubscribeMods),
    /// plus the ad-hoc uncached "saveChanges" field lookup that used to live
    /// inline in SaveChanges() — folded in here for the same reason, zero
    /// behavior change since it's the same type/field, just now cached like
    /// everything else.
    ///
    /// One exception: UnsubscribeFromWorkshop below keeps its raw,
    /// parameter-typed GetMethod call in place (VanillaAccess's cache is keyed
    /// only by (Type, name) and can't disambiguate overloads) — it's a named
    /// accessor so no caller sees the MemberInfo, but the resolution itself is
    /// unchanged from the original inline call.
    /// </summary>
    internal static class ModListVanillaBridge
    {
        private static readonly System.Type PageType = typeof(Page_ModsConfig);

        public static ModMetaData GetPrimarySelectedMod(Page_ModsConfig page) =>
            VanillaAccess.GetField(PageType, "primarySelectedMod")?.GetValue(page) as ModMetaData;

        public static void SetModListsDirty(Page_ModsConfig page, bool value) =>
            VanillaAccess.GetField(PageType, "modListsDirty")?.SetValue(page, value);

        public static Mod GetPrimaryModHandle(Page_ModsConfig page) =>
            VanillaAccess.GetField(PageType, "primaryModHandle")?.GetValue(page) as Mod;

        public static List<ModMetaData> GetActiveModListCached() =>
            VanillaAccess.GetField(PageType, "activeModListOrderCached")?.GetValue(null) as List<ModMetaData>;

        public static List<ModMetaData> GetFilteredActiveModList() =>
            VanillaAccess.GetField(PageType, "filteredActiveModListOrderCached")?.GetValue(null) as List<ModMetaData>;

        public static List<ModMetaData> GetFilteredInactiveModList() =>
            VanillaAccess.GetField(PageType, "filteredInactiveModListOrderCached")?.GetValue(null) as List<ModMetaData>;

        public static Dictionary<string, string> GetModWarningsCached() =>
            VanillaAccess.GetField(PageType, "modWarningsCached")?.GetValue(null) as Dictionary<string, string>;

        public static object TrySetModActive(Page_ModsConfig page, ModMetaData mod) =>
            VanillaAccess.GetMethod(PageType, "TrySetModActive")?.Invoke(page, new object[] { mod });

        public static void TrySetModInactive(Page_ModsConfig page, ModMetaData mod) =>
            VanillaAccess.GetMethod(PageType, "TrySetModInactive")?.Invoke(page, new object[] { mod });

        public static void SelectMod(Page_ModsConfig page, ModMetaData mod) =>
            VanillaAccess.GetMethod(PageType, "SelectMod")?.Invoke(page, new object[] { mod });

        public static void RefreshModListsInOrder(Page_ModsConfig page) =>
            VanillaAccess.GetMethod(PageType, "ModListsInOrder")?.Invoke(page, null);

        public static List<ModMetaData> GetSelectedMods(Page_ModsConfig page) =>
            VanillaAccess.GetField(PageType, "selectedMods")?.GetValue(page) as List<ModMetaData> ?? new List<ModMetaData>();

        public static bool HasSaveModListMethod =>
            VanillaAccess.GetMethod(PageType, "SaveModList") != null;

        public static bool HasLoadModListMethod =>
            VanillaAccess.GetMethod(PageType, "LoadModList") != null;

        public static void SaveModList(Page_ModsConfig page, List<ModMetaData> mods) =>
            VanillaAccess.GetMethod(PageType, "SaveModList")?.Invoke(page, new object[] { mods });

        public static void LoadModList(Page_ModsConfig page, ModList modList) =>
            VanillaAccess.GetMethod(PageType, "LoadModList")?.Invoke(page, new object[] { modList });

        public static bool HasUnsubscribeModsMethod =>
            VanillaAccess.GetMethod(PageType, "UnsubscribeMods") != null;

        public static void UnsubscribeMods(Page_ModsConfig page, IEnumerable<ModMetaData> mods) =>
            VanillaAccess.GetMethod(PageType, "UnsubscribeMods")?.Invoke(page, new object[] { mods });

        /// <summary>
        /// Bonus consolidation of the ad-hoc uncached lookup that used to live
        /// inline in ModListState.SaveChanges() — same type, same field, now
        /// routed through the shared cache like everything else.
        /// </summary>
        public static void SetSaveChanges(Page_ModsConfig page, bool value) =>
            VanillaAccess.GetField(PageType, "saveChanges")?.SetValue(page, value);

        /// <summary>
        /// Workshop.Unsubscribe is overloaded, so it needs the parameter-typed
        /// GetMethod lookup rather than VanillaAccess's (Type, name)-only cache.
        /// Preserved verbatim from the original inline call inside a named
        /// accessor, so callers still never see a MemberInfo directly.
        /// </summary>
        public static void UnsubscribeFromWorkshop(WorkshopUploadable uploadable)
        {
            var unsubMethod = typeof(Workshop).GetMethod("Unsubscribe",
                BindingFlags.NonPublic | BindingFlags.Static,
                null,
                new[] { typeof(WorkshopUploadable) },
                null);
            unsubMethod?.Invoke(null, new object[] { uploadable });
        }

        /// <summary>
        /// The other Workshop.Unsubscribe overload (Verse.Steam/Workshop.cs:82),
        /// used by the downloading-item placeholder rows whose only handle is a
        /// PublishedFileId_t rather than a WorkshopUploadable. Same
        /// parameter-typed lookup as the overload above, for the same reason.
        /// </summary>
        public static void UnsubscribeFromWorkshop(PublishedFileId_t pfid)
        {
            var unsubMethod = typeof(Workshop).GetMethod("Unsubscribe",
                BindingFlags.NonPublic | BindingFlags.Static,
                null,
                new[] { typeof(PublishedFileId_t) },
                null);
            unsubMethod?.Invoke(null, new object[] { pfid });
        }

        /// <summary>
        /// Workshop.Upload is internal and, like Unsubscribe, overloaded
        /// (Verse.Steam/Workshop.cs:47) -- same parameter-typed GetMethod
        /// pattern as UnsubscribeFromWorkshop above.
        /// </summary>
        public static void UploadToWorkshop(WorkshopUploadable uploadable)
        {
            var uploadMethod = typeof(Workshop).GetMethod("Upload",
                BindingFlags.NonPublic | BindingFlags.Static,
                null,
                new[] { typeof(WorkshopUploadable) },
                null);
            uploadMethod?.Invoke(null, new object[] { uploadable });
        }

        /// <summary>
        /// WorkshopItems.AllDownloadingItems (Verse.Steam/WorkshopItems.cs:16) is
        /// public, so this just snapshots it into a stable list -- Steam
        /// callbacks (Notify_Subscribed/Installed/Unsubscribed) rebuild the
        /// backing list asynchronously, so callers must not hold this snapshot
        /// across frames.
        /// </summary>
        public static List<WorkshopItem_Downloading> GetDownloadingItems()
        {
            var items = new List<WorkshopItem_Downloading>();
            var downloading = WorkshopItems.AllDownloadingItems;
            if (downloading != null)
            {
                items.AddRange(downloading);
            }
            return items;
        }
    }
}
