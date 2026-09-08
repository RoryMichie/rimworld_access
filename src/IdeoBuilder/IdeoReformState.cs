using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Slim facade over <c>Dialog_ReformIdeo</c> (the in-game two-stage fluid-ideoligion reform
    /// dialog) — reflection surface, row source, and mutation vehicles only. The stage/panel
    /// presentation and input machinery this class
    /// used to own (HandleInput, the flat action-menu selectedIndex/typeahead, the section list
    /// built by RebuildForStage, every announce builder) moved to
    /// <see cref="RimWorldAccess.Shell.IdeoReformScreenScope"/>, a real windowed
    /// <see cref="RimWorldAccess.Shell.ScreenScope"/> — see that class's remarks for the two
    /// stage-aware regions (Changes / Edit) it presents instead.
    ///
    /// What survives here, and why: <see cref="EnsureOpen"/>/<see cref="Close"/> (lifecycle,
    /// called by <c>IdeoReformPatch</c>'s PostOpen/PostClose, unchanged); the reflection surface
    /// into the dialog's private <c>newIdeo</c>/<c>ideo</c>/<c>stage</c> fields and its public
    /// <c>StructureMemeChanged</c>/<c>NormalMemesChanged</c>/<c>StylesChanged</c>/
    /// <c>AnyChooseOneChanges</c> properties; <see cref="BuildStage1Actions"/>, the stage-1
    /// "choose one change" row source (now returning only the three real content rows — Reset
    /// changes/Next moved to the new scope's Buttons region, matching vanilla's own separation of
    /// the choose-one-change body from its bottom button row, decompiled Dialog_ReformIdeo.cs
    /// :240-261); and the three vanilla-vehicle methods the scope's Buttons-region actions invoke
    /// (<see cref="ResetChanges"/>, <see cref="RandomizeNewIdeo"/>, <see cref="Confirm"/>) plus
    /// <see cref="BuildImpactLine"/>, the impact-readout builder the scope's Edit-region status
    /// row reuses verbatim.
    /// </summary>
    public static class IdeoReformState
    {
        public static bool IsActive { get; private set; }

        /// <summary>
        /// Marker stored on the viewer's "Reform" tree node (see IdeologyHelper.BuildFluidSection).
        /// Activating that node closes the viewer and opens Dialog_ReformIdeo for this ideoligion.
        /// </summary>
        public sealed class ReformActionMarker
        {
            public Ideo Ideo;
        }

        private static Dialog_ReformIdeo dialog;
        private static Ideo newIdeo;
        private static Ideo originalIdeo;

        #region Reflection

        private static readonly System.Reflection.FieldInfo NewIdeoField = AccessTools.Field(typeof(Dialog_ReformIdeo), "newIdeo");
        private static readonly System.Reflection.FieldInfo IdeoField = AccessTools.Field(typeof(Dialog_ReformIdeo), "ideo");
        private static readonly System.Reflection.FieldInfo StageField = AccessTools.Field(typeof(Dialog_ReformIdeo), "stage");
        private static readonly System.Reflection.MethodInfo RandomizeNewIdeoMethod = AccessTools.Method(typeof(Dialog_ReformIdeo), "RandomizeNewIdeo");
        private static readonly System.Reflection.MethodInfo ResetChangesMethod = AccessTools.Method(typeof(Dialog_ReformIdeo), "ResetAllChooseOneChanges");

        /// <summary>The reform's scratch working copy — every stage-1/stage-2 edit mutates this, never <see cref="OriginalIdeo"/>.</summary>
        public static Ideo NewIdeo
        {
            get { return newIdeo; }
        }

        /// <summary>The live ideoligion the reform will overwrite on Apply. Read-only outside this class.</summary>
        public static Ideo OriginalIdeo
        {
            get { return originalIdeo; }
        }

        /// <summary>Reflected read/write of the dialog's own private <c>stage</c> field — writing it keeps vanilla's own draw in sync, exactly as its Back/Next buttons do.</summary>
        public static IdeoReformStage Stage
        {
            get { return (IdeoReformStage)StageField.GetValue(dialog); }
            set { StageField.SetValue(dialog, value); }
        }

        public static bool StructureMemeChanged
        {
            get { return dialog.StructureMemeChanged; }
        }

        public static bool NormalMemesChanged
        {
            get { return dialog.NormalMemesChanged; }
        }

        public static bool StylesChanged
        {
            get { return dialog.StylesChanged; }
        }

        public static bool AnyChooseOneChanges
        {
            get { return dialog.AnyChooseOneChanges; }
        }

        #endregion

        #region Lifecycle

        /// <summary>
        /// Idempotent (ReferenceEquals-guarded, called from <c>IdeoReformPatch_PostOpen</c>).
        /// Presentation is owned entirely by <see cref="RimWorldAccess.Shell.IdeoReformScreenScope"/>
        /// now — its own constructor/OnFocus builds the first region and speaks the opening
        /// announcement, and its own OnPush registers this scope with
        /// <see cref="IdeoEditNotifyHub"/> — so this method only resolves the reflection surface.
        /// </summary>
        public static void EnsureOpen(Dialog_ReformIdeo d)
        {
            if (IsActive && System.Object.ReferenceEquals(dialog, d))
                return;
            dialog = d;
            newIdeo = (Ideo)NewIdeoField.GetValue(d);
            originalIdeo = (Ideo)IdeoField.GetValue(d);
            IsActive = true;
        }

        public static void Close()
        {
            IsActive = false;
            dialog = null;
            newIdeo = null;
            originalIdeo = null;
        }

        #endregion

        #region Stage 1 actions

        public sealed class Stage1Action
        {
            public string Label;
            public bool Enabled;
            public string DisabledReason;
            public System.Action Activate;
        }

        /// <summary>
        /// The three "choose one change" content rows (decompiled Dialog_ReformIdeo.cs :142-209):
        /// change structure meme / add-or-remove normal memes / change styles. Reset changes and
        /// Next are vanilla's own separate bottom BUTTONS (:250,257) — presented by
        /// <see cref="RimWorldAccess.Shell.IdeoReformScreenScope"/>'s Buttons region instead of as
        /// flat rows here, correcting the pre-ScreenScope FocusScope-era flattening that put all
        /// five items in one menu.
        /// </summary>
        public static List<Stage1Action> BuildStage1Actions()
        {
            var list = new List<Stage1Action>();
            string oneChangeReason = "MessageFluidIdeoOneChangeAllowed".Translate();

            list.Add(new Stage1Action
            {
                Label = "ReformIdeoChangeStructure".Translate(),
                Enabled = !AnyChooseOneChanges || StructureMemeChanged,
                DisabledReason = oneChangeReason,
                Activate = () => Find.WindowStack.Add(new Dialog_ChooseMemes(newIdeo, MemeCategory.Structure, initialSelection: false, null, null, reformingIdeo: true)),
            });

            string normalLabel = originalIdeo.memes.Count(m => m.category == MemeCategory.Normal) <= 1
                ? "ReformIdeoAddMeme".Translate()
                : "ReformIdeoAddOrRemoveMeme".Translate();
            list.Add(new Stage1Action
            {
                Label = normalLabel,
                Enabled = !AnyChooseOneChanges || NormalMemesChanged,
                DisabledReason = oneChangeReason,
                Activate = () =>
                {
                    var preSelected = newIdeo.memes.Where(m => !originalIdeo.memes.Contains(m)).ToList();
                    originalIdeo.CopyTo(newIdeo);
                    Find.WindowStack.Add(new Dialog_ChooseMemes(newIdeo, MemeCategory.Normal, initialSelection: false, null, preSelected, reformingIdeo: true));
                },
            });

            list.Add(new Stage1Action
            {
                Label = "ReformIdeoChangeStyles".Translate(),
                Enabled = !AnyChooseOneChanges || StylesChanged,
                DisabledReason = oneChangeReason,
                Activate = () => IdeoSymbolEditState.OpenStylePicker(newIdeo),
            });

            return list;
        }

        #endregion

        #region Vehicles

        /// <summary>Vehicle A: mirrors the "ReformIdeoResetChanges" button body verbatim (decompiled :250-254).</summary>
        public static void ResetChanges()
        {
            ResetChangesMethod.Invoke(dialog, null);
            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
        }

        /// <summary>Vehicle A: mirrors the "Randomize" button body verbatim (decompiled :293-296).</summary>
        public static void RandomizeNewIdeo()
        {
            RandomizeNewIdeoMethod.Invoke(dialog, null);
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
        }

        /// <summary>"Impact: N, label" for the working copy's normal memes, or "" if it has none. Reused verbatim by the Edit region's status row.</summary>
        public static string BuildImpactLine()
        {
            if (newIdeo == null) return "";
            var normals = newIdeo.memes.Where(m => m.category == MemeCategory.Normal).ToList();
            if (normals.Count == 0) return "";
            int impact = IdeoBuilderHelper.ImpactOf(normals);
            return $"{"IdeoImpact".Translate()}: {impact}, {IdeoImpactUtility.OverallImpactLabel(impact)}";
        }

        /// <summary>
        /// Vehicle A: the "DoneButton" body (decompiled :297-304) — <c>IdeoDevelopmentUtility
        /// .ConfirmChangesToIdeo</c> opens vanilla's own lost-precept confirmation
        /// <c>Dialog_MessageBox</c> when needed (already keyboard-accessible via its own
        /// ScopeForWindow registration) and otherwise applies immediately. The
        /// <c>FirstIncompatiblePreceptPair</c> gate ahead of it is NOT vanilla parity (vanilla's
        /// own Done button applies unconditionally; the pair only ever drives a cosmetic red
        /// label, decompiled :269-286) — a pre-existing, lead-approved accessibility hardening
        /// carried forward unchanged from the retired IdeoReformState.Confirm.
        /// </summary>
        public static void Confirm()
        {
            var pair = newIdeo.FirstIncompatiblePreceptPair();
            if (pair != default(Pair<Precept, Precept>))
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.SpeakData("MessageIdeoIncompatiblePrecepts".Translate(
                    pair.First.Label.Named("PRECEPT1"), pair.Second.Label.Named("PRECEPT2")).CapitalizeFirst(),
                    SpeechPriority.High);
                return;
            }

            var ideoLocal = originalIdeo;
            var newLocal = newIdeo;
            var dlg = dialog;
            string reformedName = ideoLocal.name;
            IdeoDevelopmentUtility.ConfirmChangesToIdeo(ideoLocal, newLocal, delegate
            {
                IdeoDevelopmentUtility.ApplyChangesToIdeo(ideoLocal, newLocal);
                dlg.Close();
                // The game shows no message on a successful reform, so confirm it ourselves —
                // otherwise Apply just silently closes the dialog.
                TolkHelper.SpeakData(reformedName + ", " + (string)"RimWorldAccess.Ideology.Builder.Status.Reformed".Translate(), SpeechPriority.High);
            });
        }

        #endregion
    }
}
