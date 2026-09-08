using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.Sound;
using Verse.Steam;
using UnityEngine;

namespace RimWorldAccess
{
    /// <summary>
    /// Per-mod business logic for the mod list: enable/disable, reordering, save/load/auto-sort,
    /// OS-specific folder-open, and the three vanilla bottom-bar float menus. Pure mutation logic,
    /// independent of keyboard and navigation state.
    ///
    /// The deferred-execution delegates below — FloatMenuOption actions, Dialog_MessageBox
    /// confirmations — deliberately read ModListState.CurrentPage live at invocation rather than
    /// capturing a snapshot when the menu is built.
    /// </summary>
    internal static class ModListActions
    {
        /// <summary>
        /// <paramref name="onReturnToList"/> is invoked once the toggle commits: the caller passes
        /// its own callback so its cursor returns to the Mods region afterward.
        /// </summary>
        private static void ToggleMod(ModMetaData mod, Action onReturnToList)
        {
            var page = ModListState.CurrentPage;
            bool wasActive = mod.Active;

            if (wasActive)
            {
                ModListVanillaBridge.TrySetModInactive(page, mod);
            }
            else
            {
                var result = ModListVanillaBridge.TrySetModActive(page, mod);
                if (result is bool success && !success)
                {
                    TolkHelper.Speak("RimWorldAccess.ModList.CouldNotEnableMod".Loc(), SpeechPriority.High);
                    return;
                }
            }

            ModListVanillaBridge.SetModListsDirty(page, true);
            ForceRefreshModLists();

            ModListNavigation.SetColumn(wasActive ? ModListColumn.Inactive : ModListColumn.Active);

            var newList = ModListNavigation.GetCurrentList();
            if (newList != null)
            {
                int newIndex = newList.IndexOf(mod);
                ModListNavigation.SetSelectedIndex(newIndex >= 0 ? newIndex : 0);
            }

            ModListNavigation.SyncSelection();
            // Details rebuilds from the caller's cursor on the next RefreshContent cycle.

            string action = wasActive
                ? (string)"RimWorldAccess.ModList.ActionDisabled".Translate()
                : (string)"RimWorldAccess.ModList.ActionEnabled".Translate();
            TolkHelper.Speak("RimWorldAccess.ModList.ModWithAction".Loc(mod.Name, action));
            onReturnToList?.Invoke();
        }

        /// <summary>Quick enable/disable toggle from list level; stays in list view.</summary>
        internal static void QuickToggle()
        {
            var page = ModListState.CurrentPage;
            if (page == null) return;

            var list = ModListNavigation.GetCurrentList();
            if (list == null || list.Count == 0 || ModListNavigation.SelectedIndex >= list.Count) return;

            var mod = list[ModListNavigation.SelectedIndex];
            bool wasActive = mod.Active;

            if (wasActive)
            {
                ModListVanillaBridge.TrySetModInactive(page, mod);
            }
            else
            {
                var result = ModListVanillaBridge.TrySetModActive(page, mod);
                if (result is bool success && !success)
                {
                    TolkHelper.Speak("RimWorldAccess.ModList.CouldNotEnableMod".Loc(), SpeechPriority.High);
                    return;
                }
            }

            ModListVanillaBridge.SetModListsDirty(page, true);
            ForceRefreshModLists();

            // Stay in the current column, adjusting for the mod leaving this list.
            var currentList = ModListNavigation.GetCurrentList();
            if (currentList != null)
            {
                if (ModListNavigation.SelectedIndex >= currentList.Count)
                {
                    ModListNavigation.SetSelectedIndex(Math.Max(0, currentList.Count - 1));
                }
                if (currentList.Count > 0)
                {
                    ModListNavigation.SyncSelection();
                }
            }

            string action = wasActive
                ? (string)"RimWorldAccess.ModList.ActionDisabled".Translate()
                : (string)"RimWorldAccess.ModList.ActionEnabled".Translate();
            string nextModInfo = "";
            var nextMod = ModListNavigation.GetSelectedMod();
            if (nextMod != null && currentList != null && currentList.Count > 0)
            {
                nextModInfo = "RimWorldAccess.ModList.NowOnNext".Translate(nextMod.Name, MenuHelper.FormatPosition(ModListNavigation.SelectedIndex, currentList.Count));
            }
            else if (currentList == null || currentList.Count == 0)
            {
                nextModInfo = "RimWorldAccess.ModList.NowEmpty".Translate();
            }
            TolkHelper.Speak("RimWorldAccess.ModList.ModWithActionAndNext".Loc(mod.Name, action, nextModInfo));
        }

        internal static void MoveUp()
        {
            if (ModListNavigation.CurrentColumn != ModListColumn.Active)
            {
                TolkHelper.Speak("RimWorldAccess.ModList.CanOnlyReorderActiveMods".Loc());
                return;
            }

            var list = ModListNavigation.GetCurrentList();
            if (list == null || list.Count == 0 || ModListNavigation.SelectedIndex <= 0) return;

            var mod = list[ModListNavigation.SelectedIndex];
            int currentPos = GetModLoadOrderIndex(mod);

            if (currentPos <= 0)
            {
                MenuHelper.SpeakAlreadyAtEdge(MenuHelper.EdgeDirection.Top);
                return;
            }

            if (ModsConfig.TryReorder(currentPos, currentPos - 1, out string errorMessage))
            {
                ModListVanillaBridge.SetModListsDirty(ModListState.CurrentPage, true);
                ModListNavigation.SetSelectedIndex(ModListNavigation.SelectedIndex - 1);
                ModListNavigation.SyncSelection();
                TolkHelper.Speak("RimWorldAccess.ModList.MovedToPosition".Loc(mod.Name, ModListNavigation.SelectedIndex + 1));
            }
            else if (!string.IsNullOrEmpty(errorMessage))
            {
                TolkHelper.Speak("RimWorldAccess.ModList.CannotReorder".Loc(errorMessage), SpeechPriority.High);
            }
        }

        internal static void MoveDown()
        {
            if (ModListNavigation.CurrentColumn != ModListColumn.Active)
            {
                TolkHelper.Speak("RimWorldAccess.ModList.CanOnlyReorderActiveMods".Loc());
                return;
            }

            var list = ModListNavigation.GetCurrentList();
            if (list == null || list.Count == 0 || ModListNavigation.SelectedIndex >= list.Count - 1) return;

            var mod = list[ModListNavigation.SelectedIndex];
            int currentPos = GetModLoadOrderIndex(mod);
            int maxPos = ModsConfig.ActiveModsInLoadOrder.Count() - 1;

            if (currentPos >= maxPos)
            {
                MenuHelper.SpeakAlreadyAtEdge(MenuHelper.EdgeDirection.Bottom);
                return;
            }

            if (ModsConfig.TryReorder(currentPos, currentPos + 2, out string errorMessage))
            {
                ModListVanillaBridge.SetModListsDirty(ModListState.CurrentPage, true);
                ModListNavigation.SetSelectedIndex(ModListNavigation.SelectedIndex + 1);
                ModListNavigation.SyncSelection();
                TolkHelper.Speak("RimWorldAccess.ModList.MovedToPosition".Loc(mod.Name, ModListNavigation.SelectedIndex + 1));
            }
            else if (!string.IsNullOrEmpty(errorMessage))
            {
                TolkHelper.Speak("RimWorldAccess.ModList.CannotReorder".Loc(errorMessage), SpeechPriority.High);
            }
        }

        internal static void SaveChanges()
        {
            var page = ModListState.CurrentPage;
            if (page == null) return;

            ModListVanillaBridge.SetSaveChanges(page, true);
            ModsConfig.Save();
            TolkHelper.Speak("RimWorldAccess.ModList.ChangesSaved".Loc());
        }

        internal static void AutoSortMods()
        {
            var page = ModListState.CurrentPage;
            if (page == null) return;

            ModsConfig.TrySortMods();
            ModListVanillaBridge.SetModListsDirty(page, true);

            ModListNavigation.SetSelectedIndex(0);
            ModListNavigation.SyncSelection();
            TolkHelper.Speak("RimWorldAccess.ModList.AutoSorted".Loc());
        }

        // Bottom-bar affordances: Page_ModsConfig.DoBottomButtons (:830-889) draws three WidgetRow
        // buttons. Each opens a REAL vanilla FloatMenu with vanilla's own options and delegate
        // bodies, so the generic FloatMenuScope drives it with no windowless wiring.

        /// <summary>
        /// Alt+G. Neither delegate has a gate to honor — both just open an external page — so this
        /// replicates vanilla's FloatMenuOption bodies (:838-848) with the same public calls.
        /// Announces what it is about to open before handing off to the OS or Steam overlay.
        /// </summary>
        internal static void OpenGetModsMenu()
        {
            if (ModListState.CurrentPage == null) return;

            SoundDefOf.Click.PlayOneShotOnCamera();
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("FromWorkshop".Translate(), delegate
                {
                    TolkHelper.Speak("RimWorldAccess.ModList.OpeningWorkshop".Loc());
                    SteamUtility.OpenSteamWorkshopPage();
                }),
                new FloatMenuOption("FromForum".Translate(), delegate
                {
                    TolkHelper.Speak("RimWorldAccess.ModList.OpeningForum".Loc());
                    Application.OpenURL("http://rimworldgame.com/getmods");
                })
            };
            Find.WindowStack.Add(new FloatMenu(options));
        }

        /// <summary>
        /// Alt+U. The private UnsubscribeMods each option calls (:856-867) opens vanilla's own
        /// confirmation before touching anything, so this reflects into that real method rather
        /// than skipping to the result.
        /// </summary>
        internal static void OpenUnsubscribeMultipleMenu()
        {
            var page = ModListState.CurrentPage;
            if (page == null || !ModListVanillaBridge.HasUnsubscribeModsMethod) return;

            ForceRefreshModLists();
            var filteredInactiveMods = ModListVanillaBridge.GetFilteredInactiveModList() ?? new List<ModMetaData>();
            var selected = ModListVanillaBridge.GetSelectedMods(page);

            SoundDefOf.Click.PlayOneShotOnCamera();
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("AllSelectedMods".Translate(), delegate
                {
                    ModListVanillaBridge.UnsubscribeMods(ModListState.CurrentPage, (IEnumerable<ModMetaData>)selected);
                }),
                new FloatMenuOption("AllIncompatibleMods".Translate(), delegate
                {
                    ModListVanillaBridge.UnsubscribeMods(ModListState.CurrentPage,
                        filteredInactiveMods.Where(mod => !mod.Official && !mod.VersionCompatible));
                }),
                new FloatMenuOption("AllDisabledMods".Translate(), delegate
                {
                    ModListVanillaBridge.UnsubscribeMods(ModListState.CurrentPage,
                        filteredInactiveMods.Where(mod => !mod.Official));
                })
            };
            Find.WindowStack.Add(new FloatMenu(options));
        }

        /// <summary>
        /// Alt+L: mod-list presets (:871-889). SaveModList and LoadModList are private, so both
        /// options reflect into the real method. SaveModList opens the real Dialog_ModList_Save,
        /// which FileListScope drives once it recognizes the type; LoadModList's callback is the
        /// private method itself, invoked from inside the Dialog_ModList_Load callback.
        /// </summary>
        internal static void OpenSaveLoadListMenu()
        {
            if (ModListState.CurrentPage == null || !ModListVanillaBridge.HasSaveModListMethod || !ModListVanillaBridge.HasLoadModListMethod) return;

            SoundDefOf.Click.PlayOneShotOnCamera();
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("SaveModList".Translate(), delegate
                {
                    ForceRefreshModLists();
                    var activeMods = ModListVanillaBridge.GetActiveModListCached();
                    ModListVanillaBridge.SaveModList(ModListState.CurrentPage, activeMods);
                }),
                new FloatMenuOption("LoadModList".Translate(), delegate
                {
                    Find.WindowStack.Add(new Dialog_ModList_Load(delegate(ModList modList)
                    {
                        ModListVanillaBridge.LoadModList(ModListState.CurrentPage, modList);
                    }));
                })
            };
            Find.WindowStack.Add(new FloatMenu(options));
        }

        // Detail-view button list (per-mod actions).

        /// <summary>
        /// <paramref name="mod"/> and <paramref name="downloadingItem"/> are the row the caller's
        /// region-0 cursor rests on, passed in rather than read from a bridge field so the result
        /// never depends on when the caller last synced. <paramref name="onReturnToList"/> reaches
        /// only the buttons that return to list view (Enable/Disable, both Unsubscribe variants);
        /// every other button leaves the cursor where it is.
        /// </summary>
        internal static void PopulateButtons(ModMetaData mod, WorkshopItem_Downloading downloadingItem, List<ButtonInfo> buttons, Action onReturnToList)
        {
            if (mod == null)
            {
                // Downloading placeholder row. Vanilla's checkbox in DoModRowDownloading
                // (:596-601) calls Workshop.Unsubscribe directly with no confirmation, mirrored
                // here unconfirmed.
                if (downloadingItem != null)
                {
                    buttons.Add(new ButtonInfo
                    {
                        Label = "Unsubscribe".Translate().ToString(),
                        Action = () =>
                        {
                            ModListVanillaBridge.UnsubscribeFromWorkshop(downloadingItem.PublishedFileId);
                            ModListVanillaBridge.SetModListsDirty(ModListState.CurrentPage, true);
                            TolkHelper.Speak("RimWorldAccess.ModList.UnsubscribedFromDownloading".Loc());
                            onReturnToList?.Invoke();
                        }
                    });
                }
                return;
            }

            if (mod.Active)
            {
                buttons.Add(new ButtonInfo
                {
                    Label = "Disable".Translate().ToString(),
                    Action = () => ToggleMod(mod, onReturnToList)
                });
            }
            else
            {
                buttons.Add(new ButtonInfo
                {
                    Label = "Enable".Translate().ToString(),
                    Action = () => ToggleMod(mod, onReturnToList)
                });
            }

            var modHandle = ModListVanillaBridge.GetPrimaryModHandle(ModListState.CurrentPage);
            // Vanilla's primaryModHandle is the FIRST ModHandle matching this package id
            // (:971), which for a package whose first-registered class carries no settings leaves
            // it settings-less even though Options > Mod options lists the same package under a
            // later handle. Fall back to any other handle that does carry settings.
            if (modHandle == null || modHandle.SettingsCategory().NullOrEmpty())
            {
                modHandle = LoadedModManager.ModHandles.FirstOrDefault(p =>
                    mod.SamePackageId(p.Content.PackageId) && !p.SettingsCategory().NullOrEmpty());
            }
            if (modHandle != null && !modHandle.SettingsCategory().NullOrEmpty())
            {
                buttons.Add(new ButtonInfo
                {
                    Label = "ModOptions".Translate().ToString(),
                    Action = () =>
                    {
                        Window settingsWindow;
                        if (!HugsLibCompat.TryGetSettingsWindow(modHandle, out settingsWindow))
                        {
                            settingsWindow = new Dialog_ModSettings(modHandle);
                        }
                        Find.WindowStack.Add(settingsWindow);
                    }
                });
            }
            else if (HugsLibCompat.HasSettingsForPackage(mod))
            {
                // A ModBase-only HugsLib mod registers no Verse.Mod subclass and so has no
                // ModHandle, though Options > Mod options lists it under HugsLib's proxy entry.
                buttons.Add(new ButtonInfo
                {
                    Label = "ModOptions".Translate().ToString(),
                    Action = () =>
                    {
                        Window settingsWindow;
                        if (HugsLibCompat.TryGetSettingsWindowForPackage(mod, out settingsWindow))
                        {
                            Find.WindowStack.Add(settingsWindow);
                        }
                    }
                });
            }

            if (!mod.Url.NullOrEmpty())
            {
                buttons.Add(new ButtonInfo
                {
                    Label = "ModWebsite".Translate().ToString(),
                    Action = () =>
                    {
                        Application.OpenURL(mod.Url);
                        TolkHelper.Speak("RimWorldAccess.ModList.OpeningWebsiteFor".Loc(mod.Name));
                    }
                });
            }

            if (mod.OnSteamWorkshop)
            {
                buttons.Add(new ButtonInfo
                {
                    Label = "WorkshopPage".Translate().ToString(),
                    Action = () =>
                    {
                        SteamUtility.OpenWorkshopPage(mod.GetPublishedFileId());
                        TolkHelper.Speak("RimWorldAccess.ModList.OpeningWorkshopPageFor".Loc(mod.Name));
                    }
                });
            }

            buttons.Add(new ButtonInfo
            {
                Label = "ModFolder".Translate().ToString(),
                Action = () =>
                {
                    string path = mod.RootDir.FullName;
                    if (Application.platform == RuntimePlatform.OSXPlayer ||
                        Application.platform == RuntimePlatform.OSXEditor)
                    {
                        System.Diagnostics.Process.Start("open", $"\"{path}\"");
                    }
                    else if (Application.platform == RuntimePlatform.LinuxPlayer ||
                             Application.platform == RuntimePlatform.LinuxEditor)
                    {
                        System.Diagnostics.Process.Start("xdg-open", $"\"{path}\"");
                    }
                    else
                    {
                        Application.OpenURL(path);
                    }
                    TolkHelper.Speak("RimWorldAccess.ModList.OpeningFolderFor".Loc(mod.Name));
                }
            });

            if (mod.Source == ContentSource.SteamWorkshop)
            {
                buttons.Add(new ButtonInfo
                {
                    Label = "Unsubscribe".Translate().ToString(),
                    Action = () =>
                    {
                        Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                            "ConfirmUnsubscribeFrom".Translate(mod.Name),
                            () =>
                            {
                                ModListVanillaBridge.UnsubscribeFromWorkshop(mod);
                                ModListVanillaBridge.SetModListsDirty(ModListState.CurrentPage, true);
                                TolkHelper.Speak("RimWorldAccess.ModList.UnsubscribedFrom".Loc(mod.Name));
                                onReturnToList?.Invoke();
                            },
                            destructive: true
                        ));
                    }
                });
            }

            // Dev mode only, the same gate as vanilla's "More actions" entry (:718).
            if (Prefs.DevMode && SteamManager.Initialized && mod.CanToUploadToWorkshop())
            {
                buttons.Add(new ButtonInfo
                {
                    Label = Workshop.UploadButtonLabel(mod.GetPublishedFileId()),
                    Action = () => StartModUpload(mod)
                });
            }
        }

        /// <summary>
        /// Body-copies vanilla's inline upload delegate (:720-751), which lives inside DoModInfo's
        /// "More actions" builder with no isolated method to invoke. Dialog_ConfirmModUpload and
        /// its two chained confirmations are the vanilla vehicles themselves, and MessageBoxScope
        /// already drives that dialog's checkbox and interaction delay.
        /// </summary>
        private static void StartModUpload(ModMetaData mod)
        {
            List<string> issues = mod.loadFolders?.GetIssueList(mod);
            if (mod.HadIncorrectlyFormattedVersionInMetadata)
            {
                Messages.Message("MessageModNeedsWellFormattedTargetVersion".Translate(VersionControl.CurrentMajor + "." + VersionControl.CurrentMinor), MessageTypeDefOf.RejectInput, historical: false);
            }
            else if (mod.HadIncorrectlyFormattedPackageId)
            {
                Find.WindowStack.Add(new Dialog_MessageBox("MessageModNeedsWellFormattedPackageId".Translate()));
            }
            else if (!issues.NullOrEmpty())
            {
                Find.WindowStack.Add(new Dialog_MessageBox("ModHadLoadFolderIssues".Translate() + "\n" + issues.ToLineList("  - ")));
            }
            else
            {
                Find.WindowStack.Add(new Dialog_ConfirmModUpload(mod, delegate
                {
                    SoundDefOf.Tick_High.PlayOneShotOnCamera();
                    Dialog_MessageBox box = Dialog_MessageBox.CreateConfirmation("ConfirmContentAuthor".Translate(), delegate
                    {
                        SoundDefOf.Tick_High.PlayOneShotOnCamera();
                        ModListVanillaBridge.UploadToWorkshop(mod);
                    }, destructive: true);
                    box.buttonAText = "Yes".Translate();
                    box.buttonBText = "No".Translate();
                    box.interactionDelay = 6f;
                    Find.WindowStack.Add(box);
                }));
            }
        }

        // Shared helpers.

        private static int GetModLoadOrderIndex(ModMetaData mod)
        {
            var activeMods = ModsConfig.ActiveModsInLoadOrder.ToList();
            return activeMods.IndexOf(mod);
        }

        /// <summary>Forces the game's filtered mod lists to rebuild now rather than at the next render pass.</summary>
        private static void ForceRefreshModLists()
        {
            var page = ModListState.CurrentPage;
            if (page != null)
            {
                ModListVanillaBridge.RefreshModListsInOrder(page);
            }
        }
    }
}
