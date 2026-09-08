using System;
using System.Reflection;
using RimWorld.Planet;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Declares Rimworld Together's HUD icon strip to <see cref="ModHudToolbarRegistry"/>. The
    /// mod draws seven tooltip-less <c>Widgets.ButtonImageWithBG</c> buttons plus a ping label
    /// straight onto the map UI (<c>Patch_GlobalControls_GlobalControlsOnGUI</c>) and world UI
    /// (<c>Patch_WorldInterface_WorldInterfaceOnGUI</c>, where Search replaces Market), gated on
    /// <c>SessionManager.IsReadyToPlay</c> — no window, no labels, nothing to capture. Each
    /// entry here mirrors the matching <c>InterfaceDrawer.Draw*Button</c> click body exactly,
    /// in the drawer's own call order; entries are offered per-view the same way the two
    /// patches split (Market on the map, settlement Search on the world).
    /// </summary>
    internal static class RimworldTogetherToolbarCompat
    {
        private const string ToolbarId = "rimworldtogether";
        private const string PackageId = "nova.rimworldtogether";

        /// <summary>
        /// One RT singleton dialog: the shared IsDialogOpen/Instance shape every DLG_* class
        /// carries, plus its parameterless constructor for the drawer-mirrored open branch.
        /// DLG_Leaderboard alone has no such constructor (the server's reply builds it via
        /// <c>PM_Leaderboard</c>), so its handle skips ctor resolution.
        /// </summary>
        private sealed class DialogHandle
        {
            private readonly PropertyInfo isOpen;
            private readonly PropertyInfo instance;
            private readonly ConstructorInfo ctor;

            public DialogHandle(ReflectionSurface surface, string typeName, bool constructible = true)
            {
                Type type = surface.Type(typeName);
                isOpen = surface.Property(type, "IsDialogOpen");
                instance = surface.Property(type, "Instance");
                ctor = constructible ? surface.Constructor(type, Type.EmptyTypes) : null;
            }

            public bool IsOpen()
            {
                return (bool)isOpen.GetValue(null);
            }

            public Window Create()
            {
                return ctor.Invoke(null) as Window;
            }

            public void CloseInstance()
            {
                var window = instance.GetValue(null) as Window;
                if (window != null)
                {
                    window.Close(true);
                }
            }
        }

        private static readonly PropertyInfo isReadyToPlayProperty;
        private static readonly PropertyInfo isAdminProperty;
        private static readonly PropertyInfo hasGuildProperty;
        private static readonly PropertyInfo currentActionValuesProperty;
        private static readonly PropertyInfo guildActionProperty;
        private static readonly PropertyInfo leaderboardActionProperty;
        private static readonly PropertyInfo marketActionProperty;
        private static readonly PropertyInfo actionIsEnabledProperty;
        private static readonly PropertyInfo currentPingProperty;
        private static readonly MethodInfo pushNewDialogMethod;
        private static readonly ConstructorInfo messageCtor;
        private static readonly MethodInfo onNoGuildOpenMethod;
        private static readonly MethodInfo leaderboardAskMethod;
        private static readonly DialogHandle optionsDialog;
        private static readonly DialogHandle chatDialog;
        private static readonly DialogHandle guildDialog;
        private static readonly DialogHandle leaderboardDialog;
        private static readonly DialogHandle marketDialog;
        private static readonly DialogHandle searchDialog;
        private static readonly DialogHandle adminDialog;
        private static readonly bool ready;

        private static string cachedTitle;

        static RimworldTogetherToolbarCompat()
        {
            var surface = new ReflectionSurface("RimworldTogetherToolbarCompat");

            Type sessionManagerType = surface.Type("RTClient.Managers.SessionManager");
            isReadyToPlayProperty = surface.Property(sessionManagerType, "IsReadyToPlay");
            isAdminProperty = surface.Property(sessionManagerType, "IsAdmin");
            hasGuildProperty = surface.Property(sessionManagerType, "HasGuild");
            currentActionValuesProperty = surface.Property(sessionManagerType, "CurrentActionValues");

            Type actionsConfigType = surface.Type("RTShared.Files.FL_ActionsConfig");
            guildActionProperty = surface.Property(actionsConfigType, "GuildAction");
            leaderboardActionProperty = surface.Property(actionsConfigType, "LeaderboardAction");
            marketActionProperty = surface.Property(actionsConfigType, "MarketAction");
            actionIsEnabledProperty = surface.Property(surface.Type("RTShared.Files.Actions.ACT_Base"), "IsEnabled");

            currentPingProperty = surface.Property(surface.Type("RTNetwork.PacketManagers.PM_KeepAlive"), "CurrentPing");

            Type dlgBaseType = surface.Type("RTClient.Dialogs.DLG_Base");
            pushNewDialogMethod = surface.Method(dlgBaseType, "PushNewDialog", new[] { typeof(Window) });
            messageCtor = surface.Constructor(surface.Type("RTClient.Dialogs.Default.DLG_Message"),
                new[] { typeof(string), typeof(string[]), typeof(Action) });

            onNoGuildOpenMethod = surface.Method(surface.Type("RTClient.PacketManagers.PM_Guilds"), "OnNoGuildOpen", Type.EmptyTypes);
            leaderboardAskMethod = surface.Method(surface.Type("RTClient.PacketManagers.PM_Leaderboard"), "Ask", Type.EmptyTypes);

            optionsDialog = new DialogHandle(surface, "RTClient.Dialogs.DLG_Options");
            chatDialog = new DialogHandle(surface, "RTClient.Dialogs.DLG_Chat");
            guildDialog = new DialogHandle(surface, "RTClient.Dialogs.Guild.DLG_Guild");
            leaderboardDialog = new DialogHandle(surface, "RTClient.Dialogs.DLG_Leaderboard", constructible: false);
            marketDialog = new DialogHandle(surface, "RTClient.Dialogs.Marketplace.DLG_Market");
            searchDialog = new DialogHandle(surface, "RTClient.Dialogs.DLG_SettlementFinder");
            adminDialog = new DialogHandle(surface, "RTClient.Dialogs.DLG_Admin");

            ready = surface.Ready;
        }

        public static void Register()
        {
            if (!ready)
            {
                return;
            }

            ModHudToolbarRegistry.Register(ToolbarId, Title, IsReadyToPlay);

            // Entries in InterfaceDrawer's own call order (Options first, Admin last), each
            // mirroring that Draw*Button's click body. Every button is an open/close toggle in
            // the mod; only chat can actually be open while this menu is (the other dialogs
            // absorb input), so only its label carries the state.
            ModHudToolbarRegistry.AddEntry(ToolbarId,
                () => "RimWorldAccess.Compat.RimworldTogether.Toolbar.Options".Translate(),
                () => Toggle(optionsDialog));
            ModHudToolbarRegistry.AddEntry(ToolbarId,
                () => (chatDialog.IsOpen()
                    ? "RimWorldAccess.Compat.RimworldTogether.Toolbar.CloseChat"
                    : "RimWorldAccess.Compat.RimworldTogether.Toolbar.OpenChat").Translate(),
                () => Toggle(chatDialog));
            ModHudToolbarRegistry.AddEntry(ToolbarId,
                () => "RimWorldAccess.Compat.RimworldTogether.Toolbar.Guild".Translate(),
                ActivateGuild);
            ModHudToolbarRegistry.AddEntry(ToolbarId,
                () => "RimWorldAccess.Compat.RimworldTogether.Toolbar.Leaderboard".Translate(),
                ActivateLeaderboard);
            ModHudToolbarRegistry.AddEntry(ToolbarId,
                () => "RimWorldAccess.Compat.RimworldTogether.Toolbar.Market".Translate(),
                ActivateMarket,
                () => !WorldRendererUtility.WorldSelected);
            ModHudToolbarRegistry.AddEntry(ToolbarId,
                () => "RimWorldAccess.Compat.RimworldTogether.Toolbar.SettlementSearch".Translate(),
                () => Toggle(searchDialog),
                () => WorldRendererUtility.WorldSelected);
            ModHudToolbarRegistry.AddEntry(ToolbarId,
                () => "RimWorldAccess.Compat.RimworldTogether.Toolbar.Admin".Translate(),
                () => Toggle(adminDialog),
                IsAdmin);
            ModHudToolbarRegistry.AddEntry(ToolbarId,
                () => "RimWorldAccess.Compat.RimworldTogether.Toolbar.Ping".Translate(
                    (int)currentPingProperty.GetValue(null)),
                null);
        }

        /// <summary>The mod's own listed name, so the pause-menu item and the menu's region heading match whatever the player's mod list actually calls it.</summary>
        private static string Title()
        {
            if (cachedTitle == null)
            {
                ModMetaData mod = ModLister.GetActiveModWithIdentifier(PackageId, ignorePostfix: true);
                cachedTitle = mod != null ? mod.Name : "RimWorld Together";
            }
            return cachedTitle;
        }

        private static bool IsReadyToPlay()
        {
            return (bool)isReadyToPlayProperty.GetValue(null);
        }

        private static bool IsAdmin()
        {
            return (bool)isAdminProperty.GetValue(null);
        }

        /// <summary>Mirrors the shared Draw*Button toggle shape: open branch is vanilla's own <c>Find.WindowStack.Add(new DLG_*())</c> line, close branch its <c>Instance.Close(true)</c> line.</summary>
        private static void Toggle(DialogHandle dialog)
        {
            if (dialog.IsOpen())
            {
                dialog.CloseInstance();
                return;
            }
            Window window = dialog.Create();
            if (window != null)
            {
                Find.WindowStack.Add(window);
            }
        }

        /// <summary>Mirrors <c>InterfaceDrawer.DrawGuildButton</c>'s click body branch for branch, including its server-side feature gate and its no-guild hand-off.</summary>
        private static void ActivateGuild()
        {
            if (guildDialog.IsOpen())
            {
                guildDialog.CloseInstance();
                return;
            }
            if (!ActionEnabled(guildActionProperty))
            {
                PushDisabledMessage();
                return;
            }
            if ((bool)hasGuildProperty.GetValue(null))
            {
                pushNewDialogMethod.Invoke(null, new object[] { guildDialog.Create() });
            }
            else
            {
                onNoGuildOpenMethod.Invoke(null, null);
            }
        }

        /// <summary>Mirrors <c>InterfaceDrawer.DrawLeaderboardButton</c>: the open branch asks the server (<c>PM_Leaderboard.Ask</c>) rather than constructing the dialog locally.</summary>
        private static void ActivateLeaderboard()
        {
            if (leaderboardDialog.IsOpen())
            {
                leaderboardDialog.CloseInstance();
                return;
            }
            if (!ActionEnabled(leaderboardActionProperty))
            {
                PushDisabledMessage();
                return;
            }
            leaderboardAskMethod.Invoke(null, null);
        }

        /// <summary>Mirrors <c>InterfaceDrawer.DrawMarketButton</c>, feature gate included.</summary>
        private static void ActivateMarket()
        {
            if (marketDialog.IsOpen())
            {
                marketDialog.CloseInstance();
                return;
            }
            if (!ActionEnabled(marketActionProperty))
            {
                PushDisabledMessage();
                return;
            }
            Find.WindowStack.Add(marketDialog.Create());
        }

        /// <summary>
        /// The drawer's <c>SessionManager.CurrentActionValues.XAction.IsEnabled</c> read. A null
        /// config folds to enabled — the mod's own defaults are all true, and the drawer only
        /// runs after a connect has populated the values anyway.
        /// </summary>
        private static bool ActionEnabled(PropertyInfo actionProperty)
        {
            object values = currentActionValuesProperty.GetValue(null);
            if (values == null)
            {
                return true;
            }
            object action = actionProperty.GetValue(values);
            return action == null || (bool)actionIsEnabledProperty.GetValue(action);
        }

        /// <summary>
        /// The disabled-feature branch shared by the guild/leaderboard/market buttons, verbatim:
        /// the mod's own <c>DLG_Message("ERROR", ...)</c> pushed through its own dialog stack.
        /// </summary>
        private static void PushDisabledMessage()
        {
            // l10n-exempt: both literals are the target mod's own hardcoded dialog strings
            // (InterfaceDrawer.cs), mirrored byte-for-byte so the keyboard path shows the exact
            // dialog a mouse click shows.
            object message = messageCtor.Invoke(new object[]
            {
                "ERROR",
                new[] { "This feature has been disabled in this server!" },
                null
            });
            pushNewDialogMethod.Invoke(null, new object[] { message });
        }
    }
}
