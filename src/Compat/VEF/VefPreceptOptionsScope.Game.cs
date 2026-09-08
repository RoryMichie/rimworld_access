using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The keyboard focus scope for Vanilla Expanded Framework's
    /// <c>VEF.Memes.Dialog_FloatMenuOptions</c>, the searchable window its IdeoFloatMenuPlus
    /// feature substitutes for a plain <c>FloatMenu</c> on long precept lists (venerated
    /// animals). Registered through <see cref="ScopeForWindow.RegisterHierarchy"/> via
    /// <see cref="VefDialogCompat"/>; all reflection lives in
    /// <see cref="VefPreceptOptionsCompat"/>.
    ///
    /// The window is semantically a float menu, so this mirrors
    /// <see cref="FloatMenuScope"/>: a <see cref="ScreenScope"/> of one content region over
    /// the same <see cref="FloatMenuOption"/> objects, the shared float-menu announcement
    /// grammar, activation through the option's own <see cref="FloatMenuOption.Chosen"/>
    /// (vehicle A) followed by the window removal the dialog's own click handler performs,
    /// and the same keyboard-only deviation for a DISABLED option (reject sound plus
    /// "unavailable", the dialog stays open). Navigation, the search grammar and the
    /// announcements all come from the chassis rather than the hand-rolled copies this
    /// scope used to carry.
    ///
    /// Where it diverges: the dialog draws its own search box and filters the drawn list by
    /// it, so the typeahead buffer is published INTO that field and the rows here are the
    /// same filtered set, computed with the dialog's own predicate over that same text
    /// (<see cref="RefreshContent"/> does both, in that order, so the two can never disagree
    /// — a sighted viewer sees exactly the list the keyboard is walking, and the focus ring
    /// is always on a row that is actually drawn). Because those rows are already the product
    /// of a <c>Contains</c> filter, <see cref="TypeaheadSubstringFallback"/> is on: without
    /// it a mid-word query narrowed the drawn list while the typeahead reported no matches
    /// and wiped the buffer.
    /// </summary>
    internal sealed class VefPreceptOptionsScope : ScreenScope
    {
        private readonly Window dialog;
        private readonly List<FloatMenuOption> visible = new List<FloatMenuOption>();

        public VefPreceptOptionsScope(Window w)
        {
            dialog = w;
            // Shift+Enter: this dialog has no proceed button, so the chord keeps its
            // vanilla shift-click meaning — activate the focused row. The base's
            // SearchSettle claim shares the chord and takes it first while a search is live.
            Claim(SharedMenuGrammar.ActivateDefault, e => ActivateCurrent(),
                when: () => !TypeaheadHasActiveSearch);
            Claim(SharedMenuGrammar.Cancel, OnCancel, when: () => !TypeaheadHasActiveSearch);
            Claim(SharedMenuGrammar.Info, OnInfo);
        }

        public override string Name
        {
            get { return "vef-precept-options"; }
        }

        public override bool IsLive
        {
            get { return true; }
        }

        /// <summary>
        /// Escape is this scope's throughout: the claim below closes the dialog and
        /// announces it, so the window must never also close itself out from under that.
        /// While a search is live the base's own claim clears it first.
        /// </summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        /// <summary>A float-menu dialog has no button row of its own.</summary>
        protected override bool IncludeActionsRegion
        {
            get { return false; }
        }

        protected override string ComposeOpenAnnouncement()
        {
            return "RimWorldAccess.Compat.Vef.PreceptOptions.Opened".Loc(visible.Count).ToString();
        }

        // ---------------------------------------------------------------
        // Rows: the dialog's own filtered list, by its own predicate.
        // ---------------------------------------------------------------

        /// <summary>Mid-word matches are needed here — see the class remarks.</summary>
        protected override bool TypeaheadSubstringFallback
        {
            get { return true; }
        }

        protected override void RefreshContent()
        {
            // The dialog's search box IS this screen's search buffer: publish before reading
            // the filter back, so the drawn list and these rows are one set at every instant.
            VefPreceptOptionsCompat.SetSearchText(dialog, TypeaheadSearchText);
            string search = VefPreceptOptionsCompat.SearchText(dialog).ToLower();
            List<FloatMenuOption> options = VefPreceptOptionsCompat.Options(dialog);
            visible.Clear();
            for (int i = 0; i < options.Count; i++)
            {
                string label = options[i].Label ?? "";
                if (search.Length > 0 && !label.ToLower().Contains(search))
                {
                    continue;
                }
                visible.Add(options[i]);
            }
        }

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        /// <summary>Reuses the shared "Menu" phrase; a single-region screen only ever speaks it on a region frame.</summary>
        protected override string ContentRegionName(int region)
        {
            return "RimWorldAccess.MainMenu.MenuRegion".Translate().ToString();
        }

        protected override int ContentItemCount(int region)
        {
            return visible.Count;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            FloatMenuOption option = OptionAt(index);
            if (option == null)
            {
                return new ElementDescription();
            }
            return new ElementDescription
            {
                Label = option.Label,
                Role = ElementRole.MenuItem,
                Disabled = option.Disabled,
            };
        }

        protected override void ActivateContentItem(int region, int index)
        {
            FloatMenuOption option = OptionAt(index);
            if (option == null)
            {
                return;
            }

            if (option.Disabled)
            {
                // Accessibility deviation from vanilla's close-on-click: keep the dialog open
                // so the user can pick something else.
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("RimWorldAccess.UI.FloatMenu.LabelUnavailable".Loc(option.Label));
                return;
            }

            // Same path as a click in the dialog: the option's own Chosen, then the window
            // leaves the stack (which pops this scope). There is no FloatMenu to hand it.
            option.Chosen(colonistOrdering: false, floatMenu: null);
            dialog.Close();
            TolkHelper.Speak("RimWorldAccess.UI.FloatMenu.Selected".Loc(option.Label));
        }

        /// <summary>
        /// A typed character searched the rows the PREVIOUS buffer left drawn (the matcher
        /// has to see the wider set to find the new match). Re-derive them against the buffer
        /// it produced, immediately, so the drawn list narrows in the same keystroke — and
        /// carry the cursor with the option the search landed on, exactly as the retired
        /// ApplySearch did, instead of letting a bare index point at a different row once the
        /// list is shorter. Backspace needs none of this: it only ever widens, and
        /// <see cref="OnCursorSettled"/> republishes for it.
        /// </summary>
        public override bool HandleChar(char c)
        {
            if (!base.HandleChar(c))
            {
                return false;
            }
            FloatMenuOption landed = OptionAt(CursorIndex);
            RefreshModel();
            ListModel region = Model.CurrentRegion;
            if (region != null && !region.IsEmpty)
            {
                int index = landed == null ? -1 : visible.IndexOf(landed);
                region.MoveTo(index >= 0 ? index : 0);
            }
            return true;
        }

        /// <summary>Cheap, silent and idempotent (the seam's contract): keeps the dialog's own box equal to the buffer on the landing paths that re-derive nothing of their own.</summary>
        protected override void OnCursorSettled(int region, int index)
        {
            VefPreceptOptionsCompat.SetSearchText(dialog, TypeaheadSearchText);
        }

        private FloatMenuOption OptionAt(int index)
        {
            return index >= 0 && index < visible.Count ? visible[index] : null;
        }

        private int CursorIndex
        {
            get
            {
                ListModel region = Model.CurrentRegion;
                return region == null ? -1 : region.Index;
            }
        }

        /// <summary>Mirrors FloatMenuScope.IsFocusedOption: bounds-guarded reference check
        /// against the current row, no separate WindowStack test (FocusStack.Top being this
        /// scope already implies the dialog is live), and no refresh — the caller is the
        /// dialog's own draw pass.</summary>
        internal bool IsFocusedOption(FloatMenuOption option)
        {
            int index = CursorIndex;
            return index >= 0 && index < visible.Count && ReferenceEquals(visible[index], option);
        }

        private void OnCancel(KeyEventSnapshot e)
        {
            ShellFrameStamps.MarkCancelConsumed();
            dialog.Close();
            TolkHelper.Speak("RimWorldAccess.Input.Close.MenuClosed".Loc());
        }

        private void OnInfo(KeyEventSnapshot e)
        {
            RefreshModel();
            FloatMenuOption option = OptionAt(CursorIndex);
            if (option == null)
            {
                InfoCardState.SpeakNoInfoCardAvailable();
                return;
            }
            InfoCardState.TryOpenInfoCardForDef(FloatMenuScope.ResolveInfoDef(option));
        }
    }
}
