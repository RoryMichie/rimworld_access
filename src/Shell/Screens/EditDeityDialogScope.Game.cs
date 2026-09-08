using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The keyboard focus scope for vanilla's real <see cref="Dialog_EditDeity"/>, opened both by
    /// <c>IdeoFoundation_Deity.DoInfo</c>'s "EditDeity" menu option and by
    /// <see cref="IdeoDeityScreenScope"/>.
    ///
    /// <b>One content region</b> holding vanilla's three fields in vanilla's order
    /// (Dialog_EditDeity.cs:42-77): deity name, title, gender. The two text rows run the standard
    /// browse/edit grammar through a <see cref="TextFieldEditSession"/> that writes the dialog's
    /// PRIVATE scratch fields live per keystroke — vanilla's <c>Widgets.TextField</c> re-reads
    /// them every pass, so writing them IS the vanilla editing path and every character appears on
    /// screen as typed. The shell owns Unity keyboard focus throughout
    /// (<see cref="ShellTextFocus.ReleaseNativeFocus"/> from the draw prefix below), so no native
    /// control swallows the arrows. Vanilla enforces no length or character limits here, hence
    /// <see cref="TextFieldSpec.Unrestricted"/>.
    ///
    /// <b>Nothing is committed until vanilla commits it.</b> The three fields are scratch copies;
    /// <c>ApplyChanges</c> — reached through Enter or the Done button — is the only writer of
    /// <c>deity.name</c>/<c>type</c>/<c>gender</c>, so Escape and Back discard exactly what vanilla
    /// discards.
    ///
    /// <b>Enter.</b> Keeps <see cref="ScreenScope"/>'s default <c>OwnsAccept == true</c>: every row
    /// needs Enter of its own, so vanilla's focus-blind <c>OnAcceptKeyPressed</c>, which would
    /// apply and close from under any row, must not also fire. Dialog_EditDeity overrides that
    /// method WITHOUT calling base, so <see cref="WindowAcceptKeyRouterPatch"/> — patching the
    /// declaring type <see cref="Window"/> — never sees it;
    /// <see cref="WindowStackKeyRouter.AcceptPatch"/> gates vanilla's sole entry point into the
    /// override instead. Done is reached the standard way: it is one of the two
    /// <c>Widgets.ButtonText</c> calls the window draws (:78-85), captured into the Buttons
    /// region, and Enter there injects vanilla's own click.
    ///
    /// <b>Escape</b> is left to vanilla — the base's <c>OwnsCancel</c> is true only while a
    /// typeahead search is live — so <c>closeOnCancel</c> closes and discards exactly as Back does.
    /// One Escape, one window.
    ///
    /// <b>Coexistence with the deity list beneath.</b> No gate is needed:
    /// <see cref="ScopeForWindow"/> pushes this scope ABOVE the overlay, and
    /// <see cref="IdeoOverlayScopesMirror"/> only pushes a scope not already on the stack, so the
    /// overlay is never re-floated over this dialog. On close, the overlay's own <c>OnFocus</c>
    /// re-announces the deity row with its updated label; that re-announcement IS the confirmation.
    /// </summary>
    public sealed class EditDeityDialogScope : ScreenScope
    {
        private enum Row { Name, Title, Gender }

        private static readonly AccessTools.FieldRef<Dialog_EditDeity, string> NewName =
            AccessTools.FieldRefAccess<Dialog_EditDeity, string>("newDeityName");
        private static readonly AccessTools.FieldRef<Dialog_EditDeity, string> NewTitle =
            AccessTools.FieldRefAccess<Dialog_EditDeity, string>("newDeityTitle");
        private static readonly AccessTools.FieldRef<Dialog_EditDeity, Gender> NewGender =
            AccessTools.FieldRefAccess<Dialog_EditDeity, Gender>("newDeityGender");

        private readonly Dialog_EditDeity dialog;
        private readonly TextFieldEditSession editSession = new TextFieldEditSession();

        /// <summary>A fresh scope instance per window open (ScopeForWindow), so the opening header is pending exactly once.</summary>
        private bool pendingOpeningHeader = true;

        public EditDeityDialogScope(Dialog_EditDeity dialog)
        {
            this.dialog = dialog;
            RegisterPopTeardown(editSession.CancelIfActive);
        }

        public override string Name
        {
            get { return "edit-deity-dialog"; }
        }

        /// <summary>Three named fields and two window buttons — anything else on this window is a mod's, and belongs in the extras net.</summary>
        protected override bool IncludeCapturedExtrasRegion
        {
            get { return true; }
        }

        internal bool Owns(Window window)
        {
            return ReferenceEquals(window, dialog);
        }

        internal static EditDeityDialogScope OwningScope(Window window)
        {
            return TextDialogShared.ScopeOwning<EditDeityDialogScope>(
                window, delegate(EditDeityDialogScope s, Window w) { return s.Owns(w); });
        }

        // Row model.

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        protected override string ContentRegionName(int region)
        {
            return (string)"EditDeity".Translate();
        }

        protected override int ContentItemCount(int region)
        {
            return 3;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            switch ((Row)index)
            {
                case Row.Name:
                    return DescribeTextRow((string)"DeityName".Translate(), NewName(dialog));
                case Row.Title:
                    return DescribeTextRow((string)"DeityTitle".Translate(), NewTitle(dialog));
                case Row.Gender:
                    return new ElementDescription
                    {
                        Label = (string)"DeityGender".Translate(),
                        Role = ElementRole.ComboBox,
                        Value = NewGender(dialog).GetLabel().CapitalizeFirst(),
                    };
                default:
                    return new ElementDescription();
            }
        }

        private static ElementDescription DescribeTextRow(string label, string value)
        {
            var d = new ElementDescription { Label = label, Role = ElementRole.TextField };
            if (string.IsNullOrEmpty(value))
            {
                d.ValueBlank = true;
            }
            else
            {
                d.Value = value;
            }
            return d;
        }

        // Enter.

        protected override void ActivateContentItem(int region, int index)
        {
            switch ((Row)index)
            {
                case Row.Name:
                    BeginTextEdit("DeityName", value => NewName(dialog) = value ?? "", NewName(dialog));
                    break;
                case Row.Title:
                    BeginTextEdit("DeityTitle", value => NewTitle(dialog) = value ?? "", NewTitle(dialog));
                    break;
                case Row.Gender:
                    OpenGenderPicker();
                    break;
            }
        }

        private void BeginTextEdit(string labelKey, Action<string> apply, string current)
        {
            editSession.EnterEdit(
                current ?? "",
                TextFieldSpec.Unrestricted(labelKey),
                (string)labelKey.Translate(),
                apply,
                AnnounceCurrentItem);
        }

        /// <summary>The options vanilla's invisible gender button builds (:64-76), through the windowless float menu so they are navigable and visible in the twin.</summary>
        private void OpenGenderPicker()
        {
            var options = new List<FloatMenuOption>();
            foreach (Gender g in (Gender[])Enum.GetValues(typeof(Gender)))
            {
                Gender captured = g;
                // The delegate body vanilla's own option carries (:70-73): one write into the
                // dialog's scratch field, which only ApplyChanges commits.
                options.Add(new FloatMenuOption(
                    g.GetLabel().CapitalizeFirst(),
                    () => NewGender(dialog) = captured,
                    g.GetIcon(),
                    Color.white));
            }
            // announceSelection:false — this scope's OnFocus re-announcement on close is the only
            // voice.
            WindowlessFloatMenuState.Open(options, colonistOrders: false, announceSelection: false);
        }

        // Focus ring: each row's backing vanilla widget capture.

        protected internal override Rect FocusedContentRect()
        {
            RefreshModel();
            if (Model.RegionIndex != 0)
            {
                return default(Rect);
            }
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty)
            {
                return default(Rect);
            }
            switch ((Row)region.Index)
            {
                // RecordTextField's contract: Label is always "", ordinal among text fields alone,
                // in draw order (:51, :54).
                case Row.Name:
                    return FindCapturedWidgetRect(WidgetKind.TextField, "", 0);
                case Row.Title:
                    return FindCapturedWidgetRect(WidgetKind.TextField, "", 1);
                default:
                    // The gender hotspot is a bare Widgets.ButtonInvisible (:64) spanning icon and
                    // label; it records unlabeled, and it is the only one here.
                    return FindCapturedWidgetRect(WidgetKind.InvisibleButton, "", 0);
            }
        }

        // Per-GUI-pass work, from the dialog's own draw prefix.

        internal void OnDialogDrawPass()
        {
            // The shell must stay the sole owner of Unity keyboard focus so arrows reach the
            // dispatcher; posting the edit buffer is a no-op in browse mode.
            ShellTextFocus.ReleaseNativeFocus();
            editSession.MirrorLive();
        }

        // Opening announcement: the window's own title, folded into the first landing so the
        // screen opens in one utterance.

        protected override string AnnouncePrefix(int region, int index)
        {
            string prefix = base.AnnouncePrefix(region, index);
            if (!pendingOpeningHeader)
            {
                return prefix;
            }
            pendingOpeningHeader = false;
            string header = (string)"EditDeity".Translate();
            return string.IsNullOrEmpty(prefix) ? header : header + ". " + prefix;
        }
    }

    /// <summary>
    /// Brackets the scope's per-pass work to the dialog's own draw, before vanilla reads its
    /// scratch fields into <c>Widgets.TextField</c>. No accept-poll mask is needed:
    /// <see cref="Dialog_EditDeity"/> polls no raw key in its body — its accept path is the
    /// <c>OnAcceptKeyPressed</c> override, which the shell's window-stack accept router already
    /// gates (see <see cref="EditDeityDialogScope"/>'s remarks).
    /// </summary>
    [HarmonyPatch(typeof(Dialog_EditDeity), "DoWindowContents")]
    public static class EditDeityDrawPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Dialog_EditDeity __instance)
        {
            try
            {
                EditDeityDialogScope scope = EditDeityDialogScope.OwningScope(__instance);
                if (scope != null)
                {
                    scope.OnDialogDrawPass();
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Edit deity dialog draw pass error", ex);
            }
        }
    }
}
