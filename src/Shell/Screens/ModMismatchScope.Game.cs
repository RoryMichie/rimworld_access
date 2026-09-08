using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Drives the real <see cref="Dialog_ModMismatch"/>, the load-flow dialog vanilla spawns when
    /// a save's recorded mod list doesn't match the active one: four buttons (Go Back / Save Mod
    /// List / Change Loaded Mods / Load Anyway) plus Added/Missing/Shared mod lists.
    ///
    /// One region per mod list the dialog shows, in vanilla's column order, each read from the
    /// dialog's live list under its own localized header — a list vanilla has nothing to put in is
    /// simply not there to navigate, which is also what happens on a mismatch that only reordered
    /// the list. The automatic Buttons region captures the four real buttons, and Enter injects the
    /// click so vanilla's inline handler runs unmodified, including "Change Loaded Mods", whose
    /// handler opens a confirmation box rather than acting directly.
    ///
    /// No OnCancelKeyPressed override exists on this dialog, so Escape's vanilla meaning is exactly
    /// what Go Back does; this scope claims it to reach that handler and name it. Enter keeps the
    /// chassis default <c>OwnsAccept == true</c>, load-bearing here: OnAcceptKeyPressed is
    /// overridden unconditionally with NO base call, so Enter would otherwise always trigger "Load
    /// Anyway" one GUI phase after every keypress. <see cref="ModMismatchAcceptKeyRouterPatch"/>
    /// applies the router's rule directly to the override the base-type patch cannot see.
    /// Load Anyway stays the default via <see cref="CapturedDefaultAcceptAction"/>.
    /// </summary>
    public sealed class ModMismatchScope : ScreenScope
    {
        private static readonly AccessTools.FieldRef<Dialog_ModMismatch, List<string>> addedModsField =
            AccessTools.FieldRefAccess<Dialog_ModMismatch, List<string>>("addedModsList");
        private static readonly AccessTools.FieldRef<Dialog_ModMismatch, List<string>> missingModsField =
            AccessTools.FieldRefAccess<Dialog_ModMismatch, List<string>>("missingModsList");
        private static readonly AccessTools.FieldRef<Dialog_ModMismatch, List<string>> sharedModsField =
            AccessTools.FieldRefAccess<Dialog_ModMismatch, List<string>>("sharedModsList");

        private static readonly MethodInfo handleGoBackMethod =
            AccessTools.Method(typeof(Dialog_ModMismatch), "HandleGoBackClicked");
        private static readonly MethodInfo handleLoadAnywayMethod =
            AccessTools.Method(typeof(Dialog_ModMismatch), "HandleLoadAnywayClicked");

        private static readonly List<string> EmptyList = new List<string>();

        private const int AddedRegion = 0;
        private const int MissingRegion = 1;
        private const int SharedRegion = 2;

        private readonly Dialog_ModMismatch dialog;

        public ModMismatchScope(Dialog_ModMismatch dialog)
        {
            this.dialog = dialog;
            Claim(SharedMenuGrammar.Cancel, OnCancel);
        }

        public override string Name
        {
            get { return "mod-mismatch"; }
        }

        /// <summary>
        /// FOREIGN-WINDOW GUARD, for any scopeless window opened above this dialog. Going
        /// non-live rather than merely non-modal is what such a window needs: the dispatcher skips
        /// a non-live scope entirely, ShellGuards' input ownership stands down with it so the
        /// native modal swallow stops eating the foreign window's keys, and the Escape/Enter
        /// routers consult OwnsCancel/OwnsAccept only for a live top scope. A window WITH a scope
        /// takes the focus-stack top itself, so this scope's claims are never consulted.
        /// </summary>
        public override bool IsLive
        {
            get { return base.IsLive && !ForeignWindowAbove(); }
        }

        /// <summary>
        /// Escape is this scope's so it can reach Go Back's own handler. Unconditionally true,
        /// folding the base's typeahead case, whose Escape claim is registered ahead of this one.
        /// </summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        /// <summary>Vanilla's OnAcceptKeyPressed makes Load Anyway the default; the button is a captured row, so the stand-in rides the same handler under the button's own label.</summary>
        protected override ScreenAction CapturedDefaultAcceptAction
        {
            get
            {
                return new ScreenAction(
                    "LoadAnyway".Translate().ToString(),
                    delegate { handleLoadAnywayMethod.Invoke(dialog, null); });
            }
        }

        internal bool Owns(Window window)
        {
            return ReferenceEquals(window, dialog);
        }

        private bool ForeignWindowAbove()
        {
            // NOT a plain "top window != ours" test; TextDialogShared.ForeignWindowAbove carries
            // the full contract.
            return TextDialogShared.ForeignWindowAbove(dialog);
        }

        // Row model.

        protected override int ContentRegionCount
        {
            get { return 3; }
        }

        protected override string ContentRegionName(int region)
        {
            switch (region)
            {
                case MissingRegion:
                    return (string)"MissingModsList".Translate();
                case SharedRegion:
                    return (string)"SharedModsList".Translate();
                default:
                    return (string)"AddedModsList".Translate();
            }
        }

        protected override int ContentItemCount(int region)
        {
            return ModList(region).Count;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            List<string> mods = ModList(region);
            return new ElementDescription
            {
                Label = index >= 0 && index < mods.Count ? mods[index] : "",
                ReadOnly = true,
            };
        }

        /// <summary>A mod name is all a row is; activation re-reads it.</summary>
        protected override void ActivateContentItem(int region, int index)
        {
            AnnounceCurrentItem();
        }

        private List<string> ModList(int region)
        {
            switch (region)
            {
                case AddedRegion:
                    return addedModsField(dialog) ?? EmptyList;
                case MissingRegion:
                    return missingModsField(dialog) ?? EmptyList;
                case SharedRegion:
                    return sharedModsField(dialog) ?? EmptyList;
                default:
                    return EmptyList;
            }
        }

        // Escape.

        private void OnCancel(KeyEventSnapshot e)
        {
            ShellFrameStamps.MarkCancelConsumed();
            // No OnCancelKeyPressed override exists here, so Escape's vanilla meaning is exactly
            // HandleGoBackClicked. Reflect-invoking it rather than calling dialog.Close() keeps
            // Escape and the Go Back button identical.
            TolkHelper.Speak("RimWorldAccess.UI.Dialog.ButtonExecuted".Loc("GoBack".Translate()));
            handleGoBackMethod.Invoke(dialog, null);
        }

        // Announcements.

        /// <summary>
        /// The dialog's own title and warning text, read live and never cached, plus the "order
        /// changed" line vanilla substitutes for the three columns when nothing was added or
        /// removed. The lists themselves are regions, so they are walked rather than recited.
        /// </summary>
        protected override string ComposeOpenAnnouncement()
        {
            string body = "ModsMismatchWarningText".Translate().ToString();
            if (ModList(AddedRegion).Count == 0 && ModList(MissingRegion).Count == 0)
            {
                body = body + " " + "ModsMismatchOrderChanged".Translate();
            }
            return DialogFrame.Opened("ModsMismatchWarningTitle".Translate().ToString(), body, 4);
        }
    }

    /// <summary>
    /// The declaring-type trap for this dialog: Dialog_ModMismatch.OnAcceptKeyPressed is
    /// overridden with no base call and no branch — unconditionally "Load Anyway" — so
    /// Window.OnAcceptKeyPressed's router never sees it. This twin applies the identical rule
    /// directly to the override. No cancel-side twin is needed: the dialog does not override
    /// OnCancelKeyPressed, so WindowCancelKeyRouterPatch already reaches it.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_ModMismatch), "OnAcceptKeyPressed")]
    public static class ModMismatchAcceptKeyRouterPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Dialog_ModMismatch __instance)
        {
            return WindowAcceptKeyRouterPatch.Prefix(__instance);
        }
    }
}
