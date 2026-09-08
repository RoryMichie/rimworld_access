using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The saved-ideoligion load picker (<c>Dialog_IdeoList_Load</c>), on the shared
    /// <see cref="ScreenScope"/> chassis. Window-attached via ShellBootstrap. One
    /// content region over the state's file list, plus the automatic Buttons region holding
    /// Close; the state itself kept only its lifecycle, its file enumeration and its two
    /// mutation vehicles.
    ///
    /// <b>Buttons are DECLARED, not captured</b> — <c>Dialog_FileList</c> draws a
    /// <c>Widgets.ButtonText</c> on every visible row (decompiled Dialog_FileList.cs:113-116),
    /// so scraping the window's buttons would present each file's Load action a second time.
    ///
    /// <b>IsModal — the generic ForeignWindowAbove fold.</b> The only real child this dialog
    /// can open is the delete-confirmation <c>Dialog_MessageBox</c>, already ScopeForWindow-
    /// registered (MessageBoxScope), so the fold is belt-and-suspenders rather than
    /// load-bearing here — kept generic, through the shared
    /// <see cref="TextDialogShared.ForeignWindowAbove"/> rather than a local copy.
    ///
    /// <b>Escape posture (state-based, QA R6).</b> <c>Dialog_FileList</c> (the declaring base)
    /// sets <c>closeOnCancel = true</c> and neither it nor <c>Dialog_IdeoList</c>/
    /// <c>Dialog_IdeoList_Load</c> override <c>OnCancelKeyPressed</c>
    /// (decompiled-verified) — its real vanilla Escape body (a plain <c>Close()</c>) must run for an
    /// idle Escape, so the dialog stays escapable even if this scope's own routing ever breaks
    /// (the legacy comment's fail-open rationale). An earlier revision left
    /// <c>OwnsCancel</c> a constant FALSE on the strength of the same-frame
    /// <see cref="ShellFrameStamps.MarkCancelConsumed"/> stamp its search-clear claim wrote —
    /// but <see cref="WindowCancelKeyRouterPatch"/> consults the property from the WINDOW pass,
    /// which can run BEFORE the dispatcher's main pass and shares Event state with it
    /// (root-probe evidence 2026-07-16), so that stamp could arrive too late to block the
    /// dialog's real closeOnCancel body: a search-active Escape closed the whole dialog instead
    /// of clearing the search. It therefore had to own cancel exactly when its search-clear
    /// claim WOULD fire — which is precisely <see cref="ScreenScope"/>'s default
    /// (<c>OwnsCancel</c> true only while a typeahead search is live, with the chassis's own
    /// Cancel claim clearing the buffer and stamping the frame), so no override is needed here
    /// and none is written: an idle list stays unowned and Escape still reaches vanilla's plain
    /// <c>Close()</c>.
    ///
    /// <b>OwnsAccept</b> is the chassis default (true), moot but explicit as before:
    /// <c>Dialog_FileList</c> sets <c>closeOnAccept = false</c>, so vanilla Enter is already
    /// inert and owning Accept blocks nothing real.
    ///
    /// <b>Empty-list parity.</b> The retired <c>HandleInput</c>'s
    /// "if (files.Count == 0) return true;" swallowed every key but Escape. The chassis gives
    /// the same outcome without a per-key branch: an empty list announces itself as empty, the
    /// delete chord finds no row, and this scope's own modality swallows the rest.
    /// </summary>
    public sealed class IdeoLoadScope : ScreenScope
    {
        private readonly Dialog_IdeoList_Load dialog;
        private readonly List<ScreenAction> actions = new List<ScreenAction>();

        public IdeoLoadScope(Dialog_IdeoList_Load dialog)
        {
            this.dialog = dialog;
            Claim("ideoLoad.delete", OnDelete);
        }

        public override string Name
        {
            get { return "ideo-load"; }
        }

        public override bool IsModal
        {
            get { return !TextDialogShared.ForeignWindowAbove(dialog); }
        }

        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        /// <summary>See the class remarks: every visible row draws its own Load button.</summary>
        protected override bool CaptureWindowButtons
        {
            get { return false; }
        }

        internal bool Owns(Window window)
        {
            return ReferenceEquals(window, dialog);
        }

        // ---------------------------------------------------------------
        // Row model
        // ---------------------------------------------------------------

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        /// <summary>The dialog's own verb for these rows ("Load"), the same label vanilla prints on each row's button.</summary>
        protected override string ContentRegionName(int region)
        {
            return (string)"LoadGameButton".Translate();
        }

        protected override int ContentItemCount(int region)
        {
            return IdeoLoadState.Files.Count;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            IReadOnlyList<SaveFileInfo> files = IdeoLoadState.Files;
            if (index < 0 || index >= files.Count)
            {
                return new ElementDescription();
            }
            SaveFileInfo file = files[index];
            ElementDescription d = new ElementDescription();
            d.Role = ElementRole.MenuItem;
            d.Label = Path.GetFileNameWithoutExtension(file.FileName);
            if (!file.GameVersion.NullOrEmpty())
            {
                d.Extras = file.GameVersion;
            }
            return d;
        }

        protected override void ActivateContentItem(int region, int index)
        {
            IdeoLoadState.LoadSelected(index);
        }

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                actions.Add(new ScreenAction(
                    "CloseButton".Translate().ToString(),
                    delegate { dialog.Close(); },
                    SharedMenuGrammar.Cancel));
                return actions;
            }
        }

        /// <summary>The banner the retired state spoke on open, now on the chassis's own one-shot channel; the landing row follows it.</summary>
        protected override string ComposeOpenAnnouncement()
        {
            int count = IdeoLoadState.Files.Count;
            return count > 0
                ? $"{(string)"LoadGameButton".Translate()}. {count}"
                : $"{(string)"LoadGameButton".Translate()}. {(string)"NoneLower".Translate()}";
        }

        private void OnDelete(KeyEventSnapshot e)
        {
            RefreshModel();
            int index = FocusedFileIndex();
            if (index < 0)
            {
                return;
            }
            IdeoLoadState.RequestDelete(index, OnDeleted);
        }

        /// <summary>Re-reads the list and follows the cursor to whatever now sits where the deleted file was (the ScenarioLoadScreenScope shape).</summary>
        private void OnDeleted()
        {
            RefreshModel();
            ListModel region = Model.CurrentRegion;
            if (region != null && !region.IsEmpty)
            {
                region.MoveTo(Mathf.Clamp(region.Index, 0, region.Count - 1));
            }
            AnnounceCurrentItem();
        }

        /// <summary>The file row under the cursor, or -1 when the cursor is in the Buttons region or the list is empty.</summary>
        private int FocusedFileIndex()
        {
            if (Model.RegionIndex != 0)
            {
                return -1;
            }
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty || region.Index >= IdeoLoadState.Files.Count)
            {
                return -1;
            }
            return region.Index;
        }

        // ---------------------------------------------------------------
        // Focus ring. The row geometry is a function of the dialog's own inRect,
        // which only its draw has, so the ring rides that pass rather than
        // FocusedContentRect — over the SAME Dialog_FileList row layout
        // FileListScope paints on (Dialog_IdeoList derives from it).
        // ---------------------------------------------------------------

        private static readonly AccessTools.FieldRef<Dialog_FileList, QuickSearchWidget> searchField =
            AccessTools.FieldRefAccess<Dialog_FileList, QuickSearchWidget>("search");
        private static readonly AccessTools.FieldRef<Dialog_FileList, Vector2> scrollPositionField =
            AccessTools.FieldRefAccess<Dialog_FileList, Vector2>("scrollPosition");

        internal void OnGuiPass(Rect inRect)
        {
            RefreshModel();
            int focused = FocusedFileIndex();
            if (focused < 0)
            {
                return;
            }
            IReadOnlyList<SaveFileInfo> files = IdeoLoadState.Files;
            QuickSearchWidget search = searchField(dialog);
            // Vanilla skips the rows its own search filter rejects, so the drawn
            // position is the count of kept files ahead of the focused one.
            int drawnIndex = -1;
            int drawnCount = 0;
            for (int i = 0; i < files.Count; i++)
            {
                if (search != null && !search.filter.Matches(files[i].FileName))
                {
                    continue;
                }
                if (i == focused)
                {
                    drawnIndex = drawnCount;
                }
                drawnCount++;
            }

            Rect outRect = FileListRowGeometry.OuterRect(inRect, reservesTypeInField: false);
            Rect rowRect;
            if (!FileListRowGeometry.TryGetRowRect(outRect, scrollPositionField(dialog), drawnIndex, drawnCount, out rowRect)
                || !FileListRowGeometry.WithinBand(rowRect, outRect))
            {
                return;
            }
            FocusRing.Draw(rowRect);
            UiPointerFollow.NotifyFocusedRect(GuiSpace.ToScreen(rowRect));
        }
    }

    /// <summary>Paints <see cref="IdeoLoadScope"/>'s focus ring from the dialog's own draw, the FileListScope bracket's twin for the saved-ideoligion picker.</summary>
    [HarmonyPatch(typeof(Dialog_FileList), "DoWindowContents")]
    public static class IdeoLoadDrawPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Dialog_FileList __instance, Rect inRect)
        {
            try
            {
                if (!(__instance is Dialog_IdeoList_Load))
                {
                    return;
                }
                IdeoLoadScope scope = TextDialogShared.ScopeOwning<IdeoLoadScope>(
                    __instance, delegate(IdeoLoadScope s, Window w) { return s.Owns(w); });
                if (scope != null)
                {
                    scope.OnGuiPass(inRect);
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Ideo load draw pass error", ex);
            }
        }
    }
}
