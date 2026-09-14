using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;
using Verse.Sound;
using RimWorld;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Keyboard cursor and row model over any real <see cref="Dialog_FileList"/>, on the
    /// shared <see cref="ScreenScope"/> chassis. Mode (save vs load) is read from the
    /// dialog's own <c>ShouldDoTypeInField</c> at construction.
    /// Save mode is two content regions — the save-name field row, then the existing
    /// files; load mode is the file list alone.
    /// The save-name field is a BROWSE row (see <see cref="TextFieldEditSession"/>):
    /// typing runs the list typeahead, Enter opens a modal edit session whose confirm
    /// performs the save and whose Escape returns to browse keeping the typed name.
    /// Row activation, deletion and reload reflect into the dialog's own DoFileInteraction
    /// and ReloadFiles, so vanilla's version/mod-compat checks and save plumbing run
    /// unmodified; this scope only supplies the file name.
    /// Buttons are declared, not captured — every visible row draws its own interact
    /// button, so scraping would present each file's action twice.
    /// Escape closes through this scope's own cancel claim; Enter stays owned
    /// (<see cref="ScreenScope.OwnsAccept"/>) so one press cannot both activate a row and
    /// accept the dialog.
    /// </summary>
    public sealed class FileListScope : ScreenScope
    {
        private const string DeleteSaveActionId = "saveMenu.deleteSave";
        private const string ContextMenuActionId = "saveMenu.contextMenu";
        // DeclaredActions id only, never a chord: names the Save button as the screen's
        // default accept so Shift+Enter saves from anywhere in save mode.
        private const string SaveActionId = "saveMenu.save";

        private static readonly AccessTools.FieldRef<Dialog_FileList, List<SaveFileInfo>> filesField =
            AccessTools.FieldRefAccess<Dialog_FileList, List<SaveFileInfo>>("files");
        private static readonly AccessTools.FieldRef<Dialog_FileList, QuickSearchWidget> searchField =
            AccessTools.FieldRefAccess<Dialog_FileList, QuickSearchWidget>("search");
        private static readonly AccessTools.FieldRef<Dialog_FileList, Vector2> scrollPositionField =
            AccessTools.FieldRefAccess<Dialog_FileList, Vector2>("scrollPosition");
        private static readonly AccessTools.FieldRef<Dialog_FileList, bool> focusedNameAreaField =
            AccessTools.FieldRefAccess<Dialog_FileList, bool>("focusedNameArea");
        private static readonly AccessTools.FieldRef<Dialog_FileList, bool> focusedSearchField =
            AccessTools.FieldRefAccess<Dialog_FileList, bool>("focusedSearch");
        private static readonly AccessTools.FieldRef<Dialog_FileList, string> typingNameField =
            AccessTools.FieldRefAccess<Dialog_FileList, string>("typingName");
        private static readonly AccessTools.FieldRef<Dialog_FileList, string> interactButLabelField =
            AccessTools.FieldRefAccess<Dialog_FileList, string>("interactButLabel");
        private static readonly AccessTools.FieldRef<Dialog_FileList, string> deleteTipKeyField =
            AccessTools.FieldRefAccess<Dialog_FileList, string>("deleteTipKey");
        private static readonly MethodInfo doFileInteractionMethod =
            AccessTools.Method(typeof(Dialog_FileList), "DoFileInteraction");
        private static readonly MethodInfo reloadFilesMethod =
            AccessTools.Method(typeof(Dialog_FileList), "ReloadFiles");
        // Vanilla's own save/load decision (Dialog_FileList.cs:52, protected virtual).
        // Read reflectively rather than by concrete-type check so any subclass, mod-added
        // ones included, gets the right browse/edit shape.
        private static readonly PropertyInfo shouldDoTypeInFieldProperty =
            AccessTools.Property(typeof(Dialog_FileList), "ShouldDoTypeInField");

        private readonly Dialog_FileList dialog;
        private readonly bool isSaveMode;
        // Only the save/load-game and mod-list dialogs return to a main-menu row that stays
        // silent on refocus; other Dialog_FileList hosts re-announce themselves.
        private readonly bool announcesMainMenuOnPop;
        private readonly List<SaveFileInfo> visibleFiles = new List<SaveFileInfo>();
        private readonly List<ScreenAction> actions = new List<ScreenAction>();

        // Save mode only. currentName is the browse-mode backing value, mirrored into the
        // dialog's own typingName every pass so the sighted view matches.
        private readonly TextFieldEditSession session;
        // MUTATION-C: MustBeFilename already gates commit on GenText.IsValidFilename
        // (Verse/GenText.cs L317-324), which itself enforces a 40-char cap; maxLength here
        // used to hand-pick 64, letting the buffer grow past what MustBeFilename would ever
        // accept. 40 mirrors that same cap so the field's own limit and its validator agree.
        private readonly TextFieldSpec nameSpec = new TextFieldSpec(
            labelKey: "RimWorldAccess.TextInput.LabelFilename",
            maxLength: 40,
            minLength: 1,
            mustBeFilename: true);
        private string currentName = string.Empty;

        public FileListScope(Dialog_FileList dialog)
        {
            this.dialog = dialog;
            isSaveMode = (bool)(shouldDoTypeInFieldProperty?.GetValue(dialog) ?? false);
            announcesMainMenuOnPop = dialog is Dialog_SaveFileList
                || dialog is Dialog_ModList_Save
                || dialog is Dialog_ModList_Load;
            if (isSaveMode)
            {
                session = new TextFieldEditSession();
                RegisterPopTeardown(session.CancelIfActive);
            }

            // Screen-specific chords only: navigation, activation, search and (while the
            // edit session is live) every edit key belong elsewhere.
            Claim(DeleteSaveActionId, OnDelete);
            Claim(ContextMenuActionId, OnContextMenu);

            // Escape once no search is active; the base's earlier typeahead claim clears a search first.
            Claim(SharedMenuGrammar.Cancel, e => PerformClose(), when: () => !TypeaheadHasActiveSearch);
        }

        public override string Name
        {
            get { return "file-list"; }
        }

        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        /// <summary>Vanilla's cancel close never fires here once the dispatcher swallows the unclaimed KeyDown, so the claim above owns Escape; folds the base's typeahead case.</summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        /// <summary>
        /// True only while the dialog's own QuickSearchWidget is filtering: that filter is a
        /// substring test (decompiled Dialog_FileList.cs:93), so the rows left drawn include
        /// names no word-prefix tier can reach. Answered from live widget state, per search.
        /// </summary>
        protected override bool TypeaheadSubstringFallback
        {
            get
            {
                QuickSearchWidget search = searchField(dialog);
                return search != null && search.filter.Active;
            }
        }

        /// <summary>Every visible row draws its own interact button; scraping would double it.</summary>
        protected override bool CaptureWindowButtons
        {
            get { return false; }
        }

        /// <summary>Save mode: Shift+Enter presses Save with the typed name from anywhere on the screen.</summary>
        protected override string DefaultAcceptActionId
        {
            get { return isSaveMode ? SaveActionId : null; }
        }

        /// <summary>
        /// The live scope owning <paramref name="window"/>, from anywhere on the focus stack —
        /// what this dialog's patches must resolve with, never the stack top
        /// (<see cref="TextDialogShared.ScopeOwning{T}"/>).
        /// </summary>
        internal static FileListScope OwningScope(Window window)
        {
            return TextDialogShared.ScopeOwning<FileListScope>(
                window, delegate(FileListScope s, Window w) { return s.Owns(w); });
        }

        internal bool Owns(Window window)
        {
            return ReferenceEquals(window, dialog);
        }

        public override void OnPush()
        {
            base.OnPush();

            // Latch vanilla's two one-shot auto-focus grabs (DoTypeInField's UI.FocusControl
            // in save mode, search.Focus() in load mode) BEFORE the first draw: a vanilla
            // control holding Unity keyboard focus swallows the arrow keys this scope needs.
            // Setting focusedSearch in save mode is an inert no-op.
            focusedSearchField(dialog) = true;
            if (isSaveMode)
            {
                focusedNameAreaField(dialog) = true;
            }

            RefreshVisibleFiles();

            if (isSaveMode)
            {
                // Mirror the dialog's own constructor-seeded typingName — recomputing it via
                // Faction.OfPlayer NREs at the main menu. Browse mode: the default is the value.
                currentName = typingNameField(dialog) ?? "";
                MirrorNameToField();
            }
        }

        public override void OnPop()
        {
            base.OnPop();

            // MainMenuScope does not re-announce on refocus, so any close path would land
            // in silence at the main menu. Playing mode needs nothing: PauseMenuScope
            // reattaches through the same mirror and announces itself.
            if (announcesMainMenuOnPop && Current.ProgramState != ProgramState.Playing)
            {
                ListableOption selected = MenuNavigationState.GetCurrentSelection();
                if (selected != null)
                {
                    // MainMenuScope's own row grammar (label only, roleless) so the landing
                    // row sounds like arrowing onto it.
                    ElementDescription d = new ElementDescription();
                    d.Label = selected.label;
                    d.PositionIndex = MenuNavigationState.SelectedIndex + 1;
                    d.PositionCount = MenuNavigationState.GetCurrentColumnLabels().Count;
                    TolkHelper.SpeakData(AnnouncementComposer.ComposeFocus(
                        d, TranslatedShellVocabulary.Instance, TextDialogShared.StandardComposeOptions()));
                }
            }
        }

        /// <summary>An empty file list says so; the region frame alone would not.</summary>
        protected override string ComposeOpenAnnouncement()
        {
            if (visibleFiles.Count > 0)
            {
                return null;
            }
            return (isSaveMode
                ? "RimWorldAccess.UI.Save.NoExistingSaves"
                : "RimWorldAccess.UI.Save.NoFiles").Translate().ToString();
        }

        // --- Row model ---

        /// <summary>In save mode the name-field row sits directly above the files in the
        /// same region, so Down flows from the field into the list.</summary>
        private int NameRows
        {
            get { return isSaveMode ? 1 : 0; }
        }

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        /// <summary>
        /// The region is named by the dialog's own <c>interactButLabel</c> — vanilla's
        /// already-localized, already-per-subclass row-button verb.
        /// </summary>
        protected override string ContentRegionName(int region)
        {
            return interactButLabelField(dialog);
        }

        protected override int ContentItemCount(int region)
        {
            return NameRows + visibleFiles.Count;
        }

        protected override void RefreshContent()
        {
            RefreshVisibleFiles();
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            if (index < NameRows)
            {
                return DescribeNameField();
            }
            int file = index - NameRows;
            if (file < 0 || file >= visibleFiles.Count)
            {
                return new ElementDescription();
            }
            return DescribeRow(visibleFiles[file]);
        }

        /// <summary>Rows match on the file name alone; nobody types the mode verb.</summary>
        protected override string ContentRowSearchText(int region, int row)
        {
            if (row < NameRows)
            {
                return "SaveGameButton".Translate().ToString();
            }
            int file = row - NameRows;
            return file >= 0 && file < visibleFiles.Count
                ? Path.GetFileNameWithoutExtension(visibleFiles[file].FileName)
                : "";
        }

        protected override void ActivateContentItem(int region, int index)
        {
            if (index < NameRows)
            {
                // EnterEdit stamps MarkAcceptConsumed so this opening Enter cannot also
                // reach vanilla's raw poll (see FileListTypeInFieldAcceptGuardPatch).
                BeginEditName(announcePrompt: true);
                return;
            }
            ActivateRow(index - NameRows);
        }

        /// <summary>The window's own Save and Close buttons, each riding the vehicle a click would.</summary>
        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                if (isSaveMode)
                {
                    actions.Add(new ScreenAction("SaveGameButton".Translate().ToString(), ActivateSave, SaveActionId));
                }
                actions.Add(new ScreenAction(
                    "CloseButton".Translate().ToString(),
                    delegate { dialog.Close(); },
                    SharedMenuGrammar.Cancel));
                return actions;
            }
        }

        private void RefreshVisibleFiles()
        {
            List<SaveFileInfo> raw = filesField(dialog) ?? new List<SaveFileInfo>();
            QuickSearchWidget search = searchField(dialog);
            visibleFiles.Clear();
            for (int i = 0; i < raw.Count; i++)
            {
                if (search == null || search.filter.Matches(raw[i].FileName))
                {
                    visibleFiles.Add(raw[i]);
                }
            }
        }

        /// <summary>The file row under the cursor, or -1 when the cursor is anywhere else.</summary>
        private int FocusedFileIndex()
        {
            if (Model.RegionIndex != 0)
            {
                return -1;
            }
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty)
            {
                return -1;
            }
            int file = region.Index - NameRows;
            return file >= 0 && file < visibleFiles.Count ? file : -1;
        }

        private bool NameFieldFocused()
        {
            if (!isSaveMode || Model.RegionIndex != 0)
            {
                return false;
            }
            ListModel region = Model.CurrentRegion;
            return region != null && !region.IsEmpty && region.Index == 0;
        }

        // --- Per-GUI-pass work, driven by the dialog's own draw ---

        /// <summary>Runs from the Dialog_FileList.DoWindowContents postfix.</summary>
        internal void OnGuiPass(Rect inRect)
        {
            // Re-latch every pass: otherwise the search box or name field reclaims Unity
            // focus and eats the arrow keys again, including after a mouse click into it.
            focusedSearchField(dialog) = true;
            ShellTextFocus.ReleaseNativeFocus();

            if (isSaveMode)
            {
                focusedNameAreaField(dialog) = true;
                // MirrorLive writes the live buffer into currentName while editing and
                // no-ops in browse mode; either way the dialog's own typingName then gets
                // the true value so its TextField renders it.
                session.MirrorLive();
                MirrorNameToField();
            }

            RefreshModel();

            // The ring rides this pass rather than FocusedContentRect: row geometry is a
            // function of the dialog's own inRect, which only its draw has.
            int focused = FocusedFileIndex();
            if (focused < 0)
            {
                return;
            }
            Rect outRect = FileListRowGeometry.OuterRect(inRect, isSaveMode);
            DrawFocusRing(outRect, focused);
            AutoScrollToFocused(outRect, focused);
        }

        private void DrawFocusRing(Rect outRect, int index)
        {
            Rect rowRect;
            if (!FileListRowGeometry.TryGetRowRect(
                    outRect, scrollPositionField(dialog), index, visibleFiles.Count, out rowRect)
                || !FileListRowGeometry.WithinBand(rowRect, outRect))
            {
                return;
            }
            FocusRing.Draw(rowRect);
        }

        private void AutoScrollToFocused(Rect outRect, int index)
        {
            float contentY = index * FileListRowGeometry.EntryHeight;
            ref Vector2 scroll = ref scrollPositionField(dialog);
            if (contentY < scroll.y)
            {
                scroll.y = contentY;
            }
            else if (contentY + FileListRowGeometry.EntryHeight > scroll.y + outRect.height)
            {
                scroll.y = contentY + FileListRowGeometry.EntryHeight - outRect.height;
            }
        }

        // --- Name field: browse/edit session + real-field mirror (save mode only) ---

        /// <summary>
        /// Writes the browse-mode backing value into the dialog's protected typingName so
        /// its own TextField renders it. Gated on GenText.IsValidFilename exactly like
        /// vanilla's per-frame check (decompiled :171-174), so the field snaps back the
        /// same way on a rejected candidate.
        /// </summary>
        private void MirrorNameToField()
        {
            if (GenText.IsValidFilename(currentName ?? string.Empty))
            {
                typingNameField(dialog) = currentName ?? string.Empty;
            }
        }

        /// <summary>Enter on the name field: open the modal edit session (announces the edit prompt).</summary>
        private void BeginEditName(bool announcePrompt)
        {
            session.EnterEdit(
                currentName ?? string.Empty,
                nameSpec,
                FieldLabel(),
                ApplyName,
                AnnounceCurrentItem,
                announcePrompt,
                onConfirm: OnNameConfirmed);
        }

        /// <summary>Keeps currentName synced to the live edit buffer (called live and on confirm).</summary>
        private void ApplyName(string value)
        {
            currentName = value ?? string.Empty;
        }

        /// <summary>
        /// Fires once on Enter-confirm in edit mode, never on Escape. The session has
        /// already torn its state down, so closing the dialog from here is safe.
        /// </summary>
        private void OnNameConfirmed()
        {
            ActivateSave();
        }

        private static string FieldLabel()
        {
            return "RimWorldAccess.TextInput.LabelFilename".Loc().ToString();
        }

        // --- Activation and the screen's own chords ---

        private void ActivateSave()
        {
            string name = currentName;
            if (string.IsNullOrEmpty(name))
            {
                TolkHelper.Speak("RimWorldAccess.UI.Save.NeedName".Loc());
                return;
            }
            doFileInteractionMethod.Invoke(dialog, new object[] { name.Trim() });
            ReturnToGameAfterSave();
        }

        // Vanilla's PostClose re-selects the Menu tab after a save, stranding the player in the
        // paused menu; escape it back to the game. Skipped off the map, where there is none.
        private void ReturnToGameAfterSave()
        {
            if (isSaveMode && Current.ProgramState == ProgramState.Playing)
            {
                PauseMenuScope.CloseRealMenuIfOpen();
            }
        }

        private void PerformClose()
        {
            ShellFrameStamps.MarkCancelConsumed();
            dialog.Close();
        }

        private void ActivateRow(int index)
        {
            if (index < 0 || index >= visibleFiles.Count)
            {
                TolkHelper.Speak("RimWorldAccess.UI.Save.InvalidSelection".Loc());
                return;
            }
            string fileName = Path.GetFileNameWithoutExtension(visibleFiles[index].FileName);
            doFileInteractionMethod.Invoke(dialog, new object[] { fileName });
            ReturnToGameAfterSave();
        }

        private void OnDelete(KeyEventSnapshot e)
        {
            RefreshModel();
            if (NameFieldFocused())
            {
                TolkHelper.Speak("RimWorldAccess.UI.Save.CannotDeleteCreateNew".Loc(), SpeechPriority.High);
                return;
            }
            int index = FocusedFileIndex();
            if (index < 0)
            {
                return;
            }

            SaveFileInfo file = visibleFiles[index];
            FileInfo fileInfo = file.FileInfo;
            // The confirmation text uses the full file name WITH extension, as vanilla's
            // own delete button does (decompiled Dialog_FileList.cs:107-112).
            string fullName = fileInfo.Name;

            // MUTATION-C: mirrors Dialog_FileList.DoWindowContents's delete-button
            // branch (decompiled Dialog_FileList.cs:105-112) -- vanilla's own
            // ButtonImage click handler deletes the FileInfo and calls
            // ReloadFiles() inline inside the confirmation delegate, with no
            // gated Try*/Can* vehicle exposed for either step.
            Action confirmedAct = delegate
            {
                fileInfo.Delete();
                reloadFilesMethod.Invoke(dialog, null);
                OnDeleted();
            };

            Window msgBox = Dialog_MessageBox.CreateConfirmation(
                "ConfirmDelete".Translate(fullName),
                confirmedAct,
                destructive: true);
            Find.WindowStack.Add(msgBox);
        }

        /// <summary>Re-reads the list and follows the cursor to whatever now sits where the deleted file was.</summary>
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

        /// <summary>
        /// The focused row's two vanilla buttons as a float menu, labelled from the
        /// dialog's own per-subclass fields.
        /// </summary>
        private void OnContextMenu(KeyEventSnapshot e)
        {
            RefreshModel();
            int index = FocusedFileIndex();
            if (index < 0)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }
            string fileName = Path.GetFileNameWithoutExtension(visibleFiles[index].FileName);
            List<FloatMenuOption> options = new List<FloatMenuOption>
            {
                new FloatMenuOption(interactButLabelField(dialog), delegate { ActivateRow(index); }),
                new FloatMenuOption(deleteTipKeyField(dialog).Translate(), delegate { OnDelete(default(KeyEventSnapshot)); }),
            };
            WindowlessFloatMenuState.OpenTitled(fileName, options);
        }

        // --- Descriptions ---

        /// <summary>The save-name field as a row: vanilla's own "Save" caption, the typed name as value.</summary>
        private ElementDescription DescribeNameField()
        {
            ElementDescription d = new ElementDescription();
            d.Label = "SaveGameButton".Translate().ToString();
            d.Role = ElementRole.TextField;
            if (string.IsNullOrEmpty(currentName))
            {
                d.ValueBlank = true;
            }
            else
            {
                d.Value = currentName;
            }
            return d;
        }

        /// <summary>
        /// One save-file row: mode verb plus file name as the label (the only thing
        /// telling the two dialogs apart from inside the list), timestamp and
        /// autosave/version tags as extras, compatibility tip as their trailing sentence.
        /// CompatibilityTip must be read LIVE, never cached — it races a background load
        /// and returns a "LoadingVersionInfo" sentinel until that finishes.
        /// </summary>
        private ElementDescription DescribeRow(SaveFileInfo file)
        {
            string fileName = Path.GetFileNameWithoutExtension(file.FileName);
            ElementDescription d = new ElementDescription();
            d.Label = (isSaveMode
                ? "RimWorldAccess.UI.Save.OverwriteLabel".Loc(fileName)
                : "RimWorldAccess.UI.Save.LoadLabel".Loc(fileName)).ToString();
            d.Role = ElementRole.MenuItem;

            List<string> extras = new List<string>();
            extras.Add(FormatDateTime(file.LastWriteTime));
            if (SaveGameFilesUtility.IsAutoSave(fileName))
            {
                extras.Add("RimWorldAccess.UI.Save.AutosaveTag".Loc().ToString());
            }
            string version = file.GameVersion;
            string loadingSentinel = "LoadingVersionInfo".Translate().ToString();
            if (!string.IsNullOrEmpty(version) && version != "???" && version != loadingSentinel)
            {
                extras.Add("RimWorldAccess.UI.Save.VersionTag".Loc(version).ToString());
            }

            string extrasText = string.Join(" - ", extras.ToArray());
            string tip = ResolveTipText(file.CompatibilityTip);
            d.Extras = string.IsNullOrEmpty(tip) ? extrasText : extrasText + ". " + tip;
            return d;
        }

        private static string ResolveTipText(TipSignal tip)
        {
            return tip.textGetter != null ? tip.textGetter() : tip.text;
        }

        private static string FormatDateTime(DateTime dateTime)
        {
            return Prefs.TwelveHourClockMode
                ? dateTime.ToString("yyyy-MM-dd h:mm tt")
                : dateTime.ToString("yyyy-MM-dd HH:mm");
        }
    }

    /// <summary>
    /// Pass-bracketed presentation: focus ring, scroll sync, name-field mirror and the
    /// shell's ownership of Unity keyboard focus, driven from the dialog's own draw. No
    /// capture engine is armed — every piece of row content comes from SaveFileInfo
    /// directly. Patches the DECLARING Dialog_FileList.DoWindowContents; no subclass
    /// overrides it.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_FileList), "DoWindowContents")]
    public static class FileListDrawPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Dialog_FileList __instance, Rect inRect)
        {
            try
            {
                FileListScope scope = FileListScope.OwningScope(__instance);
                if (scope != null)
                {
                    scope.OnGuiPass(inRect);
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("File list draw pass error", ex);
            }
        }
    }

    /// <summary>
    /// Raw-poll accept guard. Dialog_FileList.DoTypeInField polls Event.current for a
    /// Return KeyDown directly (decompiled Dialog_FileList.cs:165) and calls
    /// DoFileInteraction(typingName), bypassing Window.OnAcceptKeyPressed; unguarded,
    /// every Enter this scope claims would also trigger a phantom save.
    /// QA R6: that pass can run BEFORE the dispatcher's main pass and shares
    /// Event.current with it, so an Event.current.Use() here would starve this scope's
    /// own Enter claims. The guard instead masks keyCode to None for exactly this call
    /// (prefix stash, postfix restore), leaving every other pass a pristine KeyDown.
    /// The mask fires unconditionally while this scope owns the dialog: no state exists
    /// in which vanilla's poll is the intended submit path, unlike
    /// GiveNameScope/RenameScope which leave browse-mode Enter unmasked.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_FileList), "DoTypeInField")]
    public static class FileListTypeInFieldAcceptGuardPatch
    {
        private static KeyCode maskedKeyCode = KeyCode.None;

        [HarmonyPrefix]
        public static void Prefix(Dialog_FileList __instance)
        {
            maskedKeyCode = KeyCode.None;
            FileListScope scope = FileListScope.OwningScope(__instance);
            if (scope == null)
            {
                return;
            }
            Event e = Event.current;
            if (e == null || e.type != EventType.KeyDown)
            {
                return;
            }
            // Vanilla's poll (decompiled :165) tests Return only; masking KeypadEnter too
            // is inert, since nothing else reads Event.current between prefix and postfix.
            if (e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter)
            {
                return;
            }
            maskedKeyCode = e.keyCode;
            e.keyCode = KeyCode.None;
        }

        /// <summary>Restores the keyCode masked by the prefix twin above.</summary>
        [HarmonyPostfix]
        public static void Postfix()
        {
            if (maskedKeyCode == KeyCode.None)
            {
                return;
            }
            Event e = Event.current;
            if (e != null)
            {
                e.keyCode = maskedKeyCode;
            }
            maskedKeyCode = KeyCode.None;
        }
    }
}
