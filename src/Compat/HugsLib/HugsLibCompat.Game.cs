using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using RimWorldAccess.Shell;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Compat for HugsLib, the shared settings/patch library underneath dozens of
    /// mods. Each of the three capabilities below resolves and degrades on its own,
    /// with its own warn-once message, so one HugsLib API change never silences the
    /// other two.
    ///
    /// HugsLib fully replaces vanilla's mod-settings flow: a postfix on
    /// Dialog_Options.PostOpen injects fake SettingsProxyMod entries into the
    /// dialog's mod list, and a transpiler on Dialog_Options.DoModOptions swaps
    /// vanilla's own <c>new Dialog_ModSettings(mod)</c> construction for
    /// <c>HugsLib.Settings.OptionsDialogExtensions.GetModSettingsWindow(mod)</c>,
    /// which returns HugsLib's OWN Dialog_ModSettings for a proxy entry. Our code
    /// constructs <c>new Dialog_ModSettings(...)</c> directly at two call sites
    /// (OptionsScope, ModListActions), bypassing that transpiler entirely, so every
    /// HugsLib mod's settings opened as an empty vanilla window (SettingsProxyMod
    /// inherits Mod's no-op DoSettingsWindowContents). <see cref="TryGetSettingsWindow"/>
    /// re-derives the SAME routing the transpiler would have applied, so both call
    /// sites open the mod's real settings window instead.
    ///
    /// A HugsLib mod that registers no Verse.Mod subclass (ModBase-only) has no
    /// ModHandle at all, so the mod-list page's usual settings lookup finds nothing
    /// even though Options > Mod options lists it under HugsLib's own proxy entry.
    /// <see cref="TryGetSettingsWindowForPackage"/>/<see cref="HasSettingsForPackage"/>
    /// walk HugsLibController's own child-mod list to construct HugsLib's window
    /// directly for that case (additive: any resolution failure declines silently,
    /// since a ModHandle-based settings button may already cover the same package).
    ///
    /// Separately, HugsLib's own Dialog_ModSettings.DrawHandleEntry never routes a
    /// setting's Description through TooltipHandler.TipRegion (ModSettingsWidgets.
    /// DrawImmediateTooltip draws it directly, hover-only), so a captured row in
    /// that window has no way to surface its description. <see cref="Register"/>
    /// brackets DrawHandleEntry to inject the description into
    /// <see cref="TooltipCapture"/> the same way a real TipRegion call would have.
    /// </summary>
    internal static class HugsLibCompat
    {
        private static bool initialized;
        private static bool available;

        private static MethodInfo getModSettingsWindowMethod;

        private static PropertyInfo controllerInstanceProperty;
        private static FieldInfo childModsField;
        private static PropertyInfo modContentPackProperty;
        private static PropertyInfo settingsPackInternalAccessProperty;
        private static PropertyInfo handlesProperty;
        private static PropertyInfo neverVisibleProperty;
        private static ConstructorInfo hugsDialogCtor;

        private static PropertyInfo titleProperty;
        private static PropertyInfo descriptionProperty;
        private static MethodInfo drawHandleEntryMethod;

        private static bool warnedGetWindowFailure;
        private static bool warnedPackageWalkFailure;
        private static bool warnedTooltipTapFailure;

        private static void EnsureInit()
        {
            if (initialized)
            {
                return;
            }
            initialized = true;

            Type optionsDialogExtensionsType = AccessTools.TypeByName("HugsLib.Settings.OptionsDialogExtensions");
            available = optionsDialogExtensionsType != null;
            if (!available)
            {
                return;
            }

            getModSettingsWindowMethod = AccessTools.Method(optionsDialogExtensionsType, "GetModSettingsWindow");

            Type hugsLibControllerType = AccessTools.TypeByName("HugsLib.HugsLibController");
            controllerInstanceProperty = hugsLibControllerType != null ? AccessTools.Property(hugsLibControllerType, "Instance") : null;
            childModsField = hugsLibControllerType != null ? AccessTools.Field(hugsLibControllerType, "childMods") : null;

            Type modBaseType = AccessTools.TypeByName("HugsLib.ModBase");
            modContentPackProperty = modBaseType != null ? AccessTools.Property(modBaseType, "ModContentPack") : null;
            settingsPackInternalAccessProperty = modBaseType != null ? AccessTools.Property(modBaseType, "SettingsPackInternalAccess") : null;

            Type modSettingsPackType = AccessTools.TypeByName("HugsLib.Settings.ModSettingsPack");
            handlesProperty = modSettingsPackType != null ? AccessTools.Property(modSettingsPackType, "Handles") : null;

            Type settingHandleType = AccessTools.TypeByName("HugsLib.Settings.SettingHandle");
            neverVisibleProperty = settingHandleType != null ? AccessTools.Property(settingHandleType, "NeverVisible") : null;
            titleProperty = settingHandleType != null ? AccessTools.Property(settingHandleType, "Title") : null;
            descriptionProperty = settingHandleType != null ? AccessTools.Property(settingHandleType, "Description") : null;

            Type hugsDialogModSettingsType = AccessTools.TypeByName("HugsLib.Settings.Dialog_ModSettings");
            hugsDialogCtor = hugsDialogModSettingsType != null && modSettingsPackType != null
                ? AccessTools.Constructor(hugsDialogModSettingsType, new[] { modSettingsPackType })
                : null;

            drawHandleEntryMethod = hugsDialogModSettingsType != null && settingHandleType != null
                ? AccessTools.Method(hugsDialogModSettingsType, "DrawHandleEntry",
                    new[] { settingHandleType, typeof(Rect), typeof(float).MakeByRefType(), typeof(float) })
                : null;
        }

        /// <summary>
        /// Registers the DrawHandleEntry tooltip bracket. No-ops when HugsLib
        /// isn't installed or the target method didn't resolve.
        /// </summary>
        public static void Register(Harmony harmony)
        {
            EnsureInit();
            if (!available)
            {
                return;
            }

            if (drawHandleEntryMethod == null || titleProperty == null || descriptionProperty == null)
            {
                WarnTooltipTapUnavailable();
                return;
            }

            try
            {
                harmony.Patch(drawHandleEntryMethod,
                    prefix: new HarmonyMethod(typeof(HugsLibCompat), nameof(DrawHandleEntryPrefix)),
                    postfix: new HarmonyMethod(typeof(HugsLibCompat), nameof(DrawHandleEntryPostfix)));
            }
            catch (Exception ex)
            {
                ModLogger.Error($"HugsLib compat (handle tooltip tap) registration failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Re-derives HugsLib's own OptionsDialogExtensions.GetModSettingsWindow
        /// routing: a SettingsProxyMod (HugsLib's synthetic mod-list entry) opens
        /// HugsLib's Dialog_ModSettings for its pack, any other Mod opens vanilla's
        /// own Dialog_ModSettings. Returns false (window null) when HugsLib isn't
        /// installed, or when installed but resolution/invocation fails -- either
        /// way the caller falls back to constructing vanilla's Dialog_ModSettings
        /// itself, exactly as it did before this compat existed.
        /// </summary>
        public static bool TryGetSettingsWindow(Mod forMod, out Window window)
        {
            window = null;
            EnsureInit();
            if (!available)
            {
                return false;
            }

            if (getModSettingsWindowMethod == null)
            {
                WarnGetWindowFailure(null);
                return false;
            }

            try
            {
                window = getModSettingsWindowMethod.Invoke(null, new object[] { forMod }) as Window;
            }
            catch (Exception ex)
            {
                WarnGetWindowFailure(ex.Message);
                window = null;
                return false;
            }

            if (window == null)
            {
                WarnGetWindowFailure(null);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Cheap pre-check for <see cref="TryGetSettingsWindowForPackage"/>: true
        /// when this package is owned by a HugsLib ModBase (no ModHandle) that has
        /// at least one visible setting, without constructing anything. Callers
        /// gate a button on this, then call TryGetSettingsWindowForPackage fresh
        /// inside the button's own Action so a window is built per click.
        /// </summary>
        public static bool HasSettingsForPackage(ModMetaData mod)
        {
            object pack;
            return TryFindPackForPackage(mod, out pack);
        }

        /// <summary>
        /// ModBase-only fallback: walks
        /// HugsLibController.Instance's own child-mod list to find the
        /// ModSettingsPack owned by this package, then constructs HugsLib's OWN
        /// Dialog_ModSettings for it directly (HugsLib's ctor, HugsLib's window --
        /// vehicle A). Purely additive: any resolution failure declines silently,
        /// since a ModHandle-based settings button may already cover this package.
        /// </summary>
        public static bool TryGetSettingsWindowForPackage(ModMetaData mod, out Window window)
        {
            window = null;
            object pack;
            if (!TryFindPackForPackage(mod, out pack))
            {
                return false;
            }

            if (hugsDialogCtor == null)
            {
                WarnPackageWalkFailure(null);
                return false;
            }

            try
            {
                window = hugsDialogCtor.Invoke(new[] { pack }) as Window;
            }
            catch (Exception ex)
            {
                WarnPackageWalkFailure(ex.Message);
                return false;
            }

            return window != null;
        }

        private static bool TryFindPackForPackage(ModMetaData mod, out object pack)
        {
            pack = null;
            EnsureInit();
            if (!available || mod == null)
            {
                return false;
            }

            if (controllerInstanceProperty == null || childModsField == null || modContentPackProperty == null
                || settingsPackInternalAccessProperty == null || handlesProperty == null || neverVisibleProperty == null)
            {
                // Decline silently rather than warn: TryGetSettingsWindow already
                // speaks up for the primary (ModHandle) path when HugsLib is present
                // but its API drifted, and both failures share that one cause.
                return false;
            }

            try
            {
                object controllerInstance = controllerInstanceProperty.GetValue(null, null);
                IEnumerable childMods = controllerInstance != null ? childModsField.GetValue(controllerInstance) as IEnumerable : null;
                if (childMods == null)
                {
                    return false;
                }

                foreach (object modBase in childMods)
                {
                    ModContentPack contentPack = modContentPackProperty.GetValue(modBase, null) as ModContentPack;
                    if (contentPack == null || !mod.SamePackageId(contentPack.PackageId))
                    {
                        continue;
                    }

                    object settingsPack = settingsPackInternalAccessProperty.GetValue(modBase, null);
                    if (settingsPack == null)
                    {
                        return false;
                    }

                    IEnumerable handles = handlesProperty.GetValue(settingsPack, null) as IEnumerable;
                    if (handles == null)
                    {
                        return false;
                    }

                    bool anyVisible = false;
                    foreach (object handle in handles)
                    {
                        object neverVisible = neverVisibleProperty.GetValue(handle, null);
                        if (neverVisible is bool && !(bool)neverVisible)
                        {
                            anyVisible = true;
                            break;
                        }
                    }

                    if (!anyVisible)
                    {
                        return false;
                    }

                    pack = settingsPack;
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                WarnPackageWalkFailure(ex.Message);
                return false;
            }
        }

        private static void WarnGetWindowFailure(string detail)
        {
            if (warnedGetWindowFailure)
            {
                return;
            }
            warnedGetWindowFailure = true;
            ModLogger.Warning("HugsLib is loaded but OptionsDialogExtensions.GetModSettingsWindow could not be resolved/invoked; falling back to the vanilla settings window."
                + (detail != null ? " (" + detail + ")" : ""));
        }

        private static void WarnPackageWalkFailure(string detail)
        {
            if (warnedPackageWalkFailure)
            {
                return;
            }
            warnedPackageWalkFailure = true;
            ModLogger.Warning("HugsLib is loaded but its ModBase settings pack could not be resolved/invoked; the mod-list settings fallback for library-only mods will not be offered."
                + (detail != null ? " (" + detail + ")" : ""));
        }

        private static void WarnTooltipTapUnavailable()
        {
            if (warnedTooltipTapFailure)
            {
                return;
            }
            warnedTooltipTapFailure = true;
            ModLogger.Warning("HugsLib is loaded but Dialog_ModSettings.DrawHandleEntry could not be resolved; HugsLib setting descriptions will not be read.");
        }

        // DrawHandleEntry tooltip bracket. The thread-unsafe stash is fine: IMGUI
        // is single-threaded and DrawHandleEntry never re-enters itself.
        //
        // curY is the caller's own running Y cursor, passed by ref: its value on
        // entry (stashed here) is where THIS entry starts, and its value on exit
        // (read in the postfix) is where the NEXT entry starts, so the delta is
        // exactly the entry height DrawHandleEntry used internally, without
        // reflecting any of its private layout constants.

        private static object stashedHandle;
        private static Rect stashedParentRect;
        private static float stashedStartY;
        private static bool stashActive;

        private static void DrawHandleEntryPrefix(object handle, Rect parentRect, ref float curY)
        {
            stashedHandle = handle;
            stashedParentRect = parentRect;
            stashedStartY = curY;
            stashActive = true;
        }

        private static void DrawHandleEntryPostfix(ref float curY)
        {
            if (!stashActive)
            {
                return;
            }
            stashActive = false;

            try
            {
                object handle = stashedHandle;
                string description = descriptionProperty.GetValue(handle, null) as string;
                if (string.IsNullOrEmpty(description))
                {
                    return;
                }

                string title = titleProperty.GetValue(handle, null) as string;
                Rect entryRect = new Rect(stashedParentRect.x, stashedParentRect.y + stashedStartY,
                    stashedParentRect.width, curY - stashedStartY);
                string combined = string.IsNullOrEmpty(title) ? description : title + ". " + description;
                // RecordSignal gates on its own armed/pass state, so no extra
                // pass guard belongs here.
                TooltipCapture.RecordSignal(entryRect, combined);
            }
            catch (Exception ex)
            {
                WarnTooltipCaptureFailure(ex.Message);
            }
        }

        private static bool warnedTooltipCaptureFailure;

        private static void WarnTooltipCaptureFailure(string detail)
        {
            if (warnedTooltipCaptureFailure)
            {
                return;
            }
            warnedTooltipCaptureFailure = true;
            ModLogger.Warning("HugsLib setting-row tooltip capture failed; setting descriptions will not be read for this session. (" + detail + ")");
        }
    }
}
