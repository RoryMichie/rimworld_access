using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Mutation/action layer for Dialog_ChooseMemes (structure + normal meme picker). Kept as a
    /// stateless data/action layer after the ScreenScope retrofit: all
    /// navigation, row/label building, and speech now belong to
    /// <see cref="RimWorldAccess.Shell.IdeoMemeScreenScope"/>, which delegates every mutation here
    /// and composes every announcement itself (the "scope owns all speech" rule).
    /// <see cref="ToggleMeme"/>/<see cref="Randomize"/>/<see cref="Accept"/>/<see cref="Back"/> each
    /// mirror one piece of <c>Dialog_ChooseMemes</c>'s own click/button handling — see each method's
    /// remarks for the exact vanilla source it rides.
    /// </summary>
    public static class IdeoMemeSelectionState
    {
        public static bool IsActive { get; private set; }

        private static Dialog_ChooseMemes currentDialog;

        public static Dialog_ChooseMemes CurrentDialog => currentDialog;

        #region Lifecycle

        public static void EnsureOpen(Dialog_ChooseMemes dialog)
        {
            if (IsActive && System.Object.ReferenceEquals(currentDialog, dialog))
                return;

            currentDialog = dialog;
            IsActive = true;
        }

        public static void Close()
        {
            IsActive = false;
            currentDialog = null;
        }

        #endregion

        #region Selection toggle

        /// <summary>
        /// The outcome of a <see cref="ToggleMeme"/> call: either it was rejected (no mutation
        /// happened; <see cref="RejectReason"/> is the already-localized sentence to speak, at
        /// <see cref="RejectPriority"/>), or it succeeded, in which case <see cref="NowSelected"/> is
        /// the meme's new state and <see cref="Displaced"/> lists any OTHER memes a single-select
        /// swap silently removed (never null, may be empty).
        /// </summary>
        public struct ToggleOutcome
        {
            public bool Accepted;
            public bool NowSelected;
            public List<MemeDef> Displaced;
            public string RejectReason;
            public SpeechPriority? RejectPriority;
        }

        /// <summary>
        /// Mirrors the click-handling block in Dialog_ChooseMemes.DrawMeme (decompiled :564-625).
        /// We can't call it directly (it's tied to ButtonInvisible) so we re-implement the same
        /// rules: structure memes are single-select; configuring-new-fluid restricts to one Normal;
        /// reforming fluid limits removals; otherwise toggles freely subject to CanRemoveMeme.
        /// MUTATION-C: mirrors Dialog_ChooseMemes.DrawMeme's click branch; the branch is inline in a
        /// private ButtonInvisible handler, so there is no gated vanilla method to call instead.
        /// Speech is NOT built here — the caller composes and speaks the announcement from the
        /// returned outcome (the scope owns all speech); sound effects stay here since they mirror
        /// vanilla's own per-branch SFX exactly, EXCEPT when <paramref name="playSounds"/> is
        /// false: the structure list's radio-group auto-select (arrowing onto a meme selects it,
        /// <see cref="Shell.IdeoMemeScreenScope.OnCursorSettled"/>) is not a click, and a checkbox
        /// tick per arrow key is exactly the noise the one-announcement doctrine forbids. The
        /// mutation rules themselves are identical either way.
        /// </summary>
        public static ToggleOutcome ToggleMeme(MemeDef meme, bool playSounds = true)
        {
            var outcome = new ToggleOutcome { Displaced = new List<MemeDef>() };
            if (currentDialog == null || meme == null)
                return outcome;

            var newMemes = IdeoMemeSelectionHelper.GetNewMemes(currentDialog);
            bool configuringNewFluid = IdeoMemeSelectionHelper.GetConfiguringNewFluidIdeo(currentDialog);
            bool reformingFluid = IdeoMemeSelectionHelper.GetReformingFluidIdeo(currentDialog);
            var ideo = IdeoMemeSelectionHelper.GetIdeo(currentDialog);

            bool isSelected = newMemes.Contains(meme);

            if (isSelected)
            {
                if (meme.category == MemeCategory.Structure)
                {
                    // Vanilla's own click handler returns silently here (a structure meme can't be
                    // deselected, only swapped); a silent no-op is keyboard-hostile, so this speaks
                    // the reject instead — a deliberate deviation, not a parity gap.
                    if (playSounds)
                    {
                        SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    }
                    outcome.RejectReason = "ChooseStructureMeme".Loc().ToString();
                    return outcome;
                }

                var report = IdeoMemeSelectionHelper.CanRemoveMeme(currentDialog, meme);
                if (!report.Accepted)
                {
                    // Vanilla shows a message only for required memes; the one-change-per-reform rule
                    // returns a bare false with no message, so we stay silent there too (the reject
                    // sound conveys "can't") — faithful parity, don't invent a message.
                    if (playSounds)
                    {
                        SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    }
                    outcome.RejectReason = report.Reason;
                    outcome.RejectPriority = SpeechPriority.High;
                    return outcome;
                }
                newMemes.Remove(meme);
                if (playSounds)
                {
                    SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();
                }
                outcome.Accepted = true;
                outcome.NowSelected = false;
                return outcome;
            }

            // Single-select modes silently displace other memes; collect them so the caller can
            // name what was deselected (e.g. picking a new Normal meme during the fluid initial pick
            // swaps out the previous one).
            if (meme.category == MemeCategory.Structure)
            {
                outcome.Displaced.AddRange(newMemes.Where(m => m.category == MemeCategory.Structure));
                newMemes.RemoveAll(m => m.category == MemeCategory.Structure);
            }
            else if (configuringNewFluid)
            {
                outcome.Displaced.AddRange(newMemes.Where(m => m.category == MemeCategory.Normal));
                newMemes.RemoveAll(m => m.category == MemeCategory.Normal);
            }
            else if (reformingFluid)
            {
                int removeCount = IdeoMemeSelectionHelper.GetNormalMemesRemoveCount(currentDialog);
                if (removeCount >= 1 && !ideo.memes.Contains(meme))
                {
                    // Reforming a fluid ideoligion allows only one normal-meme change.
                    if (playSounds)
                    {
                        SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    }
                    outcome.RejectReason = "ReformIdeoAddOrRemoveMeme".Loc().ToString();
                    outcome.RejectPriority = SpeechPriority.High;
                    return outcome;
                }
                outcome.Displaced.AddRange(newMemes.Where(m => !ideo.memes.Contains(m)));
                newMemes.RemoveAll(m => !ideo.memes.Contains(m));
            }

            newMemes.Add(meme);
            if (playSounds)
            {
                SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
            }
            outcome.Accepted = true;
            outcome.NowSelected = true;
            return outcome;
        }

        #endregion

        #region Randomize / Accept / Back

        /// <summary>
        /// Mirrors the Randomize button (Dialog_ChooseMemes.DoWindowContents decompiled :179-198)
        /// and speaks the result itself — self-contained, unlike ToggleMeme, since there is no
        /// tree-cursor entanglement to extract here. Returns the structure meme the roll landed on
        /// so the caller can move its cursor there (structure mode only; null otherwise, since a
        /// Normal-mode roll has no single "the" meme to land on).
        /// </summary>
        public static MemeDef Randomize()
        {
            if (currentDialog == null) return null;
            try
            {
                var ideo = IdeoMemeSelectionHelper.GetIdeo(currentDialog);
                var category = IdeoMemeSelectionHelper.GetMemeCategory(currentDialog);
                var newMemes = IdeoMemeSelectionHelper.GetNewMemes(currentDialog);
                var range = IdeoMemeSelectionHelper.GetMemeCountRangeAbsolute(currentDialog);
                bool configuringNewFluid = IdeoMemeSelectionHelper.GetConfiguringNewFluidIdeo(currentDialog);
                bool reformingFluid = IdeoMemeSelectionHelper.GetReformingFluidIdeo(currentDialog);

                FactionDef forFaction = IdeoUIUtility.FactionForRandomization(ideo);
                List<MemeDef> randomized;
                if (category == MemeCategory.Normal)
                {
                    if (reformingFluid)
                        randomized = IdeoUtility.RandomizeNormalMemesForReforming(range.max, ideo.memes, forFaction);
                    else
                        randomized = IdeoUtility.RandomizeNormalMemes(
                            GenMath.RoundRandom(range.Average), newMemes, forFaction, configuringNewFluid);
                }
                else
                {
                    randomized = IdeoUtility.RandomizeStructureMeme(newMemes, forFaction);
                }

                // Replace newMemes contents (preserves the same list reference the dialog uses).
                newMemes.Clear();
                newMemes.AddRange(randomized);
                SoundDefOf.Tick_High.PlayOneShotOnCamera();

                // Structure is single-select: name the structure meme that was rolled so the caller
                // can move the cursor onto it, letting the player read/keep or re-roll.
                if (category == MemeCategory.Structure)
                {
                    var chosen = newMemes.FirstOrDefault(m => m.category == MemeCategory.Structure);
                    if (chosen != null)
                    {
                        TolkHelper.SpeakData((string)"Randomize".Translate() + ". " + chosen.LabelCap + ", " + (string)"RimWorldAccess.Ideology.Builder.Status.Selected".Translate());
                        return chosen;
                    }
                }
                // Normal memes: name the memes that were rolled (not just the impact), then the
                // impact/validation status.
                string names = string.Join(", ", newMemes
                    .Where(m => m.category == MemeCategory.Normal)
                    .Select(m => (string)m.LabelCap));
                if (string.IsNullOrEmpty(names)) names = "None".Translate();
                TolkHelper.SpeakData((string)"Randomize".Translate() + ". " + names + ", " + (string)"RimWorldAccess.Ideology.Builder.Status.Selected".Translate() + ". "
                    + IdeoMemeSelectionHelper.BuildStatusLine(currentDialog));
                return null;
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimWorld Access] Error randomizing memes: {ex}");
                return null;
            }
        }

        /// <summary>Mirrors the Done button / OnAcceptKeyPressed: runs TryAccept unchanged (its own gates + Messages).</summary>
        public static void Accept()
        {
            if (currentDialog == null) return;
            IdeoMemeSelectionHelper.InvokeTryAccept(currentDialog);
        }

        /// <summary>
        /// Mirrors the Back button (Dialog_ChooseMemes.DoWindowContents decompiled :160-178): for
        /// the Normal dialog's initial-selection step, chains back to a fresh Structure picker;
        /// otherwise closes and notifies the builder page.
        /// </summary>
        public static void Back()
        {
            if (currentDialog == null) return;
            var category = IdeoMemeSelectionHelper.GetMemeCategory(currentDialog);
            bool initialSelection = IdeoMemeSelectionHelper.GetInitialSelection(currentDialog);
            var ideo = IdeoMemeSelectionHelper.GetIdeo(currentDialog);

            currentDialog.Close();

            if (category == MemeCategory.Normal && initialSelection)
            {
                // Chain back to structure picker (vanilla behavior).
                Find.WindowStack.Add(new Dialog_ChooseMemes(ideo, MemeCategory.Structure, initialSelection: true));
                return;
            }

            var page = Find.WindowStack.WindowOfType<Page_ConfigureIdeo>();
            page?.Notify_ClosedChooseMemesDialog();

            // Notify_ClosedChooseMemesDialog removes the freshly-made empty ideo when no normal
            // meme was chosen (i.e. the player abandoned the initial structure pick) but leaves
            // page.ideo dangling at that removed, unconfigured ideo — culture-less, name-less, with
            // uninitialized style counts. There is nothing to configure, so leave the builder and
            // return to the previous page (preset selection) rather than stranding the player on a
            // ghost ideo. A configured ideo (still in the manager) keeps the player on the hub.
            if (page != null && (page.ideo == null || !Find.IdeoManager.IdeosListForReading.Contains(page.ideo)))
                IdeoBuilderHubPatch.LeaveBuilderAbandoned(page);
        }

        #endregion
    }
}
