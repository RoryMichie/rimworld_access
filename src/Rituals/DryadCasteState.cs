using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.Sound;
using RimWorldAccess.Shell;

namespace RimWorldAccess
{
    /// <summary>
    /// Data and mutation state for Dialog_ChangeDryadCaste: the dialog's own caste list, its
    /// opening announcement, and the commit that opens vanilla's confirmation. Navigation,
    /// typeahead and per-row announcements belong to <see cref="DryadCasteScope"/>'s
    /// <see cref="ScreenScope"/> chassis, which reads <see cref="AllModes"/> and
    /// <see cref="DescribeMode"/> from here.
    ///
    /// Row descriptions are composed through <see cref="AnnouncementComposer.ComposeFocus"/>
    /// rather than hand-built strings. Each
    /// caste maps onto <see cref="ElementRole.RadioButton"/> (a mutually exclusive
    /// current-caste picker): <see cref="ElementDescription.Selected"/> carries the
    /// already-selected state instead of the old ad hoc "AlreadySelected" status word, and an
    /// unmet-requirements caste is <see cref="ElementDescription.Disabled"/> with the specific
    /// reason (missing memes / missing prior caste) in <see cref="ElementDescription.Extras"/>
    /// — the same "disabled rows stay navigable and speak their reason" channel every other
    /// screen in the mod uses, per <see cref="ScreenScope.DescribeActionRow"/>. The old
    /// meets-all-requirements status word ("available", a literal that was never actually
    /// localized) is now simply the absence of the Disabled state word, matching how every
    /// other enabled row in the mod stays silent about being enabled.
    /// </summary>
    public static class DryadCasteState
    {
        private static bool isActive;
        private static Dialog_ChangeDryadCaste currentDialog;
        private static List<GauranlenTreeModeDef> allModes = new List<GauranlenTreeModeDef>();

        // Cached reflection handles
        private static System.Reflection.FieldInfo selectedModeField;
        private static System.Reflection.FieldInfo currentModeField;
        private static System.Reflection.FieldInfo allModesField;
        private static System.Reflection.FieldInfo treeConnectionField;
        private static System.Reflection.FieldInfo connectedPawnField;
        private static System.Reflection.MethodInfo meetsRequirementsMethod;
        private static System.Reflection.MethodInfo meetsMemeRequirementsMethod;
        private static System.Reflection.MethodInfo startChangeMethod;

        public static bool IsActive => isActive;

        /// <summary>The dialog's own caste list, in vanilla's own order — the scope's content region.</summary>
        internal static IReadOnlyList<GauranlenTreeModeDef> AllModes => allModes;

        /// <summary>
        /// Where the tree's CURRENT caste sits in <see cref="AllModes"/>, or 0 when the dialog
        /// reports none — the row the scope lands its cursor on when the screen opens.
        /// </summary>
        internal static int CurrentModeIndex
        {
            get
            {
                GauranlenTreeModeDef currentMode = currentModeField?.GetValue(currentDialog) as GauranlenTreeModeDef;
                int index = currentMode == null ? -1 : allModes.IndexOf(currentMode);
                return index < 0 ? 0 : index;
            }
        }

        public static void Open(Dialog_ChangeDryadCaste dialog)
        {
            if (dialog == null)
                return;

            try
            {
                CacheReflection();

                currentDialog = dialog;
                allModes = (allModesField?.GetValue(dialog) as List<GauranlenTreeModeDef>) ?? new List<GauranlenTreeModeDef>();
                isActive = true;

                Pawn connectedPawn = connectedPawnField?.GetValue(dialog) as Pawn;
                CompTreeConnection treeConnection = treeConnectionField?.GetValue(dialog) as CompTreeConnection;
                string connectedPawnLabel = connectedPawn?.LabelShortCap ?? "connected pawn";

                TolkHelper.SpeakData((string)"RimWorldAccess.Rituals.DryadCaste.OpenAnnouncement".Translate(
                    (string)"ChangeMode".Translate(), allModes.Count.ToString(), connectedPawnLabel));

                if (connectedPawn != null && treeConnection?.parent != null)
                {
                    try
                    {
                        var cocoonProps = ThingDefOf.DryadCocoon?.GetCompProperties<CompProperties_DryadCocoon>();
                        int daysToComplete = (int)(cocoonProps?.daysToComplete ?? 0f);
                        string intro = "ChooseProductionModeInitialDesc".Translate(
                            connectedPawn.Named("PAWN"),
                            treeConnection.parent.Named("TREE"),
                            daysToComplete.Named("UPGRADEDURATION"));
                        TolkHelper.SpeakData(SanitizeText(intro));
                    }
                    catch (Exception introEx)
                    {
                        Log.Warning($"[DryadCasteState] Could not read initial intro text: {introEx.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[DryadCasteState] Error opening: {ex.Message}");
                Close();
            }
        }

        public static void Close()
        {
            isActive = false;
            currentDialog = null;
            allModes.Clear();
        }

        /// <summary>
        /// One caste row's datum, read live off
        /// the dialog's own requirement methods. See this class's header for the
        /// Role/Selected/Disabled mapping; the scope composes and speaks it.
        /// </summary>
        internal static ElementDescription DescribeMode(GauranlenTreeModeDef mode)
        {
            ElementDescription d = new ElementDescription();
            d.Role = ElementRole.RadioButton;

            if (mode == null)
            {
                d.Label = "RimWorldAccess.Rituals.DryadCaste.UnknownCaste".Translate();
                return d;
            }

            GauranlenTreeModeDef currentMode = currentModeField?.GetValue(currentDialog) as GauranlenTreeModeDef;
            Pawn connectedPawn = connectedPawnField?.GetValue(currentDialog) as Pawn;

            bool meetsMemes = meetsMemeRequirementsMethod != null
                && (bool)(meetsMemeRequirementsMethod.Invoke(currentDialog, new object[] { mode }) ?? false);
            bool meetsAll = meetsRequirementsMethod != null
                && (bool)(meetsRequirementsMethod.Invoke(currentDialog, new object[] { mode }) ?? false);
            bool isCurrent = mode == currentMode;

            d.Label = mode.LabelCap.ToString();
            d.Selected = isCurrent;

            var parts = new List<string>();

            if (!isCurrent && !meetsAll)
            {
                d.Disabled = true;
                if (!meetsMemes)
                {
                    parts.Add("MissingRequiredMemes".Translate());
                }
                else if (mode.previousStage != null && currentMode != mode.previousStage)
                {
                    parts.Add("MissingRequiredCaste".Translate());
                }
                // else: locked for some other reason MeetsRequirements checks internally that
                // this dialog has no more specific vanilla string for — Disabled alone still
                // speaks, matching the retired bare "Locked" status with no colon detail.
            }

            string description = SanitizeText(mode.Description);
            if (!string.IsNullOrEmpty(description))
                parts.Add(description);

            // Announce all required memes, marking missing ones so the user has the same info as a sighted player.
            if (connectedPawn?.Ideo != null
                && Find.IdeoManager != null
                && !Find.IdeoManager.classicMode
                && !mode.requiredMemes.NullOrEmpty())
            {
                var memeParts = new List<string>();
                foreach (var memeDef in mode.requiredMemes)
                {
                    bool has = connectedPawn.Ideo.HasMeme(memeDef);
                    // Short "(missing)" marker — the Disabled reason already said which overall
                    // requirement is blocking; this just tags which specific memes are the problem.
                    memeParts.Add(has
                        ? memeDef.LabelCap.ToString()
                        : $"{memeDef.LabelCap} (missing)");
                }
                parts.Add($"{"RequiredMemes".Translate()}: {string.Join(", ", memeParts)}");
            }

            if (mode.previousStage != null)
            {
                string stageLabel = mode.previousStage.pawnKindDef?.LabelCap.ToString()
                    ?? mode.previousStage.LabelCap.ToString();
                parts.Add($"{"RequiredStage".Translate()}: {stageLabel}");
            }

            if (mode.displayedStats != null && mode.displayedStats.Count > 0 && mode.pawnKindDef?.race != null)
            {
                var statsLines = new List<string>();
                foreach (var statDef in mode.displayedStats)
                {
                    try
                    {
                        string statValue = statDef.ValueToString(
                            mode.pawnKindDef.race.GetStatValueAbstract(statDef),
                            statDef.toStringNumberSense);
                        statsLines.Add($"{statDef.LabelCap}: {statValue}");
                    }
                    catch { }
                }
                if (statsLines.Count > 0)
                    parts.Add("Stats: " + string.Join(", ", statsLines));
            }

            if (parts.Count > 0)
            {
                d.Extras = string.Join(". ", parts);
            }

            return d;
        }

        /// <summary>
        /// Enter on a caste row: honors the dialog's own requirement gates and, when they pass,
        /// opens vanilla's confirmation box exactly as its own Accept button does (mutation
        /// vehicle A — <c>StartChange</c> runs only from that confirmation's callback).
        /// </summary>
        internal static void SelectCaste(GauranlenTreeModeDef mode)
        {
            if (mode == null)
            {
                TolkHelper.Speak("RimWorldAccess.Rituals.DryadCaste.NoCasteSelected".Loc());
                return;
            }

            GauranlenTreeModeDef currentMode = currentModeField?.GetValue(currentDialog) as GauranlenTreeModeDef;

            if (mode == currentMode)
            {
                TolkHelper.Speak("AlreadySelected".Loc());
                return;
            }

            bool meetsRequirements = meetsRequirementsMethod != null
                && (bool)(meetsRequirementsMethod.Invoke(currentDialog, new object[] { mode }) ?? false);
            if (!meetsRequirements)
            {
                bool meetsMemeRequirements = meetsMemeRequirementsMethod != null
                    && (bool)(meetsMemeRequirementsMethod.Invoke(currentDialog, new object[] { mode }) ?? false);
                if (!meetsMemeRequirements)
                {
                    TolkHelper.Speak("MissingRequiredMemes".Loc());
                    return;
                }

                if (mode.previousStage != null && currentMode != mode.previousStage)
                {
                    TolkHelper.Speak("MissingRequiredCaste".Loc());
                    return;
                }

                TolkHelper.Speak("Locked".Loc());
                return;
            }

            CompTreeConnection treeConnection = treeConnectionField?.GetValue(currentDialog) as CompTreeConnection;
            Pawn connectedPawn = connectedPawnField?.GetValue(currentDialog) as Pawn;
            if (treeConnection?.parent == null || connectedPawn == null)
            {
                TolkHelper.Speak("RimWorldAccess.Rituals.DryadCaste.MissingTreeData".Loc());
                return;
            }

            // Update the dialog's selectedMode field so StartChange applies the right caste.
            selectedModeField?.SetValue(currentDialog, mode);
            SoundDefOf.Click.PlayOneShotOnCamera();

            // Capture locally so the confirmation callback is safe even if our state closes first.
            Dialog_ChangeDryadCaste capturedDialog = currentDialog;
            System.Reflection.MethodInfo capturedStartChange = startChangeMethod;

            float duration = ThingDefOf.DryadCocoon.GetCompProperties<CompProperties_DryadCocoon>().daysToComplete;
            string confirmRaw = "GauranlenModeChangeDescFull".Translate(
                treeConnection.parent.Named("TREE"),
                connectedPawn.Named("CONNECTEDPAWN"),
                duration.Named("DURATION"));

            Dialog_MessageBox confirm = Dialog_MessageBox.CreateConfirmation(confirmRaw, () =>
            {
                capturedStartChange?.Invoke(capturedDialog, null);
            });

            Find.WindowStack.Add(confirm);
            // MessageBoxScope announces the Dialog_MessageBox automatically — no manual Speak needed.
        }

        // Screen readers read "\n" as dead air or literally "newline"; flatten to sentence punctuation.
        private static string SanitizeText(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string result = text.Replace("\r", "").Replace("\n\n", ". ").Replace("\n", ". ");
            while (result.Contains(". . ")) result = result.Replace(". . ", ". ");
            return result.Trim();
        }

        private static void CacheReflection()
        {
            if (selectedModeField == null)
                selectedModeField = AccessTools.Field(typeof(Dialog_ChangeDryadCaste), "selectedMode");
            if (currentModeField == null)
                currentModeField = AccessTools.Field(typeof(Dialog_ChangeDryadCaste), "currentMode");
            if (allModesField == null)
                allModesField = AccessTools.Field(typeof(Dialog_ChangeDryadCaste), "allDryadModes");
            if (treeConnectionField == null)
                treeConnectionField = AccessTools.Field(typeof(Dialog_ChangeDryadCaste), "treeConnection");
            if (connectedPawnField == null)
                connectedPawnField = AccessTools.Field(typeof(Dialog_ChangeDryadCaste), "connectedPawn");

            if (meetsRequirementsMethod == null)
                meetsRequirementsMethod = AccessTools.Method(typeof(Dialog_ChangeDryadCaste), "MeetsRequirements");
            if (meetsMemeRequirementsMethod == null)
                meetsMemeRequirementsMethod = AccessTools.Method(typeof(Dialog_ChangeDryadCaste), "MeetsMemeRequirements");
            if (startChangeMethod == null)
                startChangeMethod = AccessTools.Method(typeof(Dialog_ChangeDryadCaste), "StartChange");
        }
    }
}
