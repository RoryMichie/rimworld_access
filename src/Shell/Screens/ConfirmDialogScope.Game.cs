using System;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Dialog-family scope for vanilla <see cref="Dialog_Confirm"/> — a plain
    /// <see cref="Window"/> (NOT a <see cref="Dialog_MessageBox"/> subclass),
    /// used for confirmation gates like deleting a policy or releasing an
    /// animal. Same shape as <see cref="MessageBoxScope"/>: one content region
    /// holding the question (re-readable with Enter, and flowing into the
    /// buttons so Down off it lands on the first one), and the automatic Buttons
    /// region capturing the dialog's own two <c>Widgets.ButtonText</c> calls, so
    /// Enter injects the click and vanilla's own inline handler runs unmodified.
    /// The buttons read in vanilla's own draw order, Cancel first.
    ///
    /// THE BUG this scope's draw patch fixes: Dialog_Confirm.DoWindowContents
    /// confirms on any Enter KeyDown itself, before our shell dispatcher runs
    /// (the window's own GUI pass shares Event state and executes first) — so
    /// a raw Enter always confirmed regardless of which button was focused.
    /// See ConfirmDialogDrawPatch.
    /// </summary>
    public sealed class ConfirmDialogScope : ScreenScope
    {
        private static readonly AccessTools.FieldRef<Dialog_Confirm, string> titleField =
            AccessTools.FieldRefAccess<Dialog_Confirm, string>("title");

        private readonly Dialog_Confirm box;

        public ConfirmDialogScope(Dialog_Confirm box)
        {
            this.box = box;
            Claim(SharedMenuGrammar.Cancel, OnCancel);
        }

        public override string Name
        {
            get { return "confirm-dialog"; }
        }

        /// <summary>
        /// Escape is this scope's so the dismissal is spoken; vanilla's meaning
        /// (a plain close, no confirm) is what runs. Unconditionally true, which
        /// folds the base's typeahead case — the base's Escape claim is
        /// registered ahead of this one and clears a live search first.
        /// </summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        internal bool Owns(Window window)
        {
            return ReferenceEquals(window, box);
        }

        // ---------------------------------------------------------------
        // Row model.
        // ---------------------------------------------------------------

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        /// <summary>Unnamed: the body rows ARE the dialog, so a "Description" frame word is noise.</summary>
        protected override string ContentRegionName(int region)
        {
            return "";
        }

        protected override int ContentItemCount(int region)
        {
            return Question.Length > 0 ? 1 : 0;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            return new ElementDescription
            {
                Label = BuildDescriptionAnnouncement(),
                ReadOnly = true,
            };
        }

        protected override void ActivateContentItem(int region, int index)
        {
            AnnounceCurrentItem();
        }

        /// <summary>Prose, not an item name — see MessageBoxScope's twin override.</summary>
        protected override bool ContentRowSearchable(int region, int row)
        {
            return false;
        }

        protected override bool ContentFlowsToActions(int region)
        {
            return true;
        }

        private string Question
        {
            get { return StripOrEmpty(titleField(box)); }
        }

        // ---------------------------------------------------------------
        // Escape.
        // ---------------------------------------------------------------

        private void OnCancel(KeyEventSnapshot e)
        {
            // Stamp before acting: vanilla re-tests the Cancel binding in this
            // window's deferred GUI pass, invisible to this frame's Event.Use().
            ShellFrameStamps.MarkCancelConsumed();
            // Vanilla always closes on Escape here (closeOnClickedOutside is
            // the only dismissal guard it defines, and OnCancelKeyPressed
            // falls back to the base Window behavior: plain close).
            TolkHelper.Speak("RimWorldAccess.UI.Cancelled".Loc());
            Find.WindowStack.TryRemove(box);
        }

        // ---------------------------------------------------------------
        // Announcements (legacy RimWorldAccess.UI.Dialog.* wording).
        // ---------------------------------------------------------------

        /// <summary>The frame and the button count only; the question is the body row's own text — see MessageBoxScope's twin.</summary>
        protected override string ComposeOpenAnnouncement()
        {
            return DialogFrame.Opened(null, null, 2);
        }

        /// <summary>
        /// The question is this dialog's only text — passed as body (not
        /// title) so DialogFrame's own trailing-punctuation normalization
        /// applies to it instead of the title branch's unconditional ". ".
        /// </summary>
        private string BuildDescriptionAnnouncement()
        {
            return DialogFrame.Description("", Question);
        }

        private static string StripOrEmpty(string value)
        {
            return string.IsNullOrEmpty(value) ? "" : value.StripTags();
        }
    }

    /// <summary>
    /// The window-pass guard this dialog needs, and nothing else — the button
    /// capture, the focus ring and the model refresh all ride the shared
    /// <see cref="ScreenScopeDrawPatch"/> bracket.
    ///
    /// Dialog_Confirm's own DoWindowContents runs BEFORE our shell dispatcher
    /// and shares Event state, and it confirms on ANY Enter KeyDown itself
    /// (decompiled Verse/Dialog_Confirm.cs:38-42), regardless of which button
    /// the scope has focused. Per shell doctrine (window-pass guards must be
    /// state-based or mask keyCode+restore around the vanilla body, never
    /// same-frame ShellFrameStamps, never Use() on a key a scope claims), the
    /// prefix masks Event.current.keyCode to None for the duration of the
    /// vanilla body whenever this window has an attached scope, so Enter is
    /// invisible to Dialog_Confirm's own check and routes only through the
    /// scope's own Enter claim; the postfix restores the real keyCode so later
    /// passes (and the dispatcher) still see it.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_Confirm), "DoWindowContents")]
    public static class ConfirmDialogDrawPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Dialog_Confirm __instance, out KeyCode? __state)
        {
            __state = null;
            try
            {
                if (ScopeForWindow.HasAttachedScope(__instance)
                    && Event.current.type == EventType.KeyDown
                    && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter))
                {
                    __state = Event.current.keyCode;
                    Event.current.keyCode = KeyCode.None;
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Confirm dialog draw pass error", ex);
            }
        }

        [HarmonyPostfix]
        public static void Postfix(KeyCode? __state)
        {
            try
            {
                if (__state.HasValue)
                {
                    Event.current.keyCode = __state.Value;
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Confirm dialog draw pass error", ex);
            }
        }
    }
}
