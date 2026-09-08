using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Slim facade over <c>Dialog_ConfigureIdeo(forArchonexusRestart: true)</c> — reflection
    /// surface, row source, and mutation vehicles only. The two-tab presentation and input
    /// machinery this class used to own
    /// (HandleInput, the list/detail tab switch, <c>IdeoSectionEditorState</c>/the shared
    /// read-only <c>viewer</c> hosting, typeahead, every announce builder) moved to
    /// <see cref="RimWorldAccess.Shell.ArchonexusIdeoScreenScope"/>, a real windowed
    /// <see cref="RimWorldAccess.Shell.ScreenScope"/> — see that class's remarks for the
    /// Ideoligions/Details regions it presents instead.
    ///
    /// What survives here, and why: <see cref="EnsureOpen"/>/<see cref="Close"/> (lifecycle,
    /// called by the PostOpen/PostClose patches below, unchanged in spirit — just shorter, since
    /// there is no more tab/typeahead/viewer state to reset); the reflection surface into the
    /// dialog's private fields/methods; <see cref="RebuildRows"/> (the Ideoligions-region row
    /// source, now returning <see cref="Row"/>/<see cref="RowKind"/> values the scope describes
    /// and presents itself — no baked label strings, since presentation is entirely the scope's
    /// job now); and the vanilla-vehicle action methods the scope's row/toolbar activations
    /// invoke (<see cref="MakeSelectedPrimary"/>, <see cref="RemoveNewIdeoligion"/>,
    /// <see cref="CreateNew"/>, <see cref="LoadSaved"/>, <see cref="AssignColonists"/>,
    /// <see cref="ConfirmAndProceed"/>) plus the gates the scope's row descriptions read
    /// (<see cref="AnyColonistToConvert"/>, <see cref="CustomOrLoadedIdeo"/>,
    /// <see cref="IsEditable"/>, <see cref="ShouldShowRemoveNewIdeoligion"/>,
    /// <see cref="CanMakeSelectedPrimary"/>).
    /// </summary>
    public static class ArchonexusReformIdeoState
    {
        public static bool IsActive { get; private set; }

        /// <summary>The Ideoligions-region row kinds. No label is baked in — the scope resolves each row's presented text at describe time (localized, live-state-dependent for <see cref="MakeOrRemovePrimary"/>).</summary>
        public enum RowKind { CreateNew, CreateFluid, Ideo, AssignColonists, MakeOrRemovePrimary }

        public sealed class Row
        {
            public RowKind Kind;
            public Ideo Ideo;
        }

        private static Dialog_ConfigureIdeo dialog;
        private static readonly List<Row> rows = new List<Row>();

        #region Reflection cache

        private static readonly Type DialogType = typeof(Dialog_ConfigureIdeo);
        private static readonly FieldInfo NextActionField = AccessTools.Field(DialogType, "nextAction");
        private static readonly FieldInfo PawnsField = AccessTools.Field(DialogType, "pawns");
        private static readonly FieldInfo PawnConvertToIdeoField = AccessTools.Field(DialogType, "pawnConvertToIdeo");
        private static readonly FieldInfo InitialPrimaryIdeoField = AccessTools.Field(DialogType, "initialPrimaryIdeo");
        private static readonly FieldInfo CustomOrLoadedIdeoField = AccessTools.Field(DialogType, "customOrLoadedIdeo");
        private static readonly PropertyInfo CurrentPrimaryIdeoProp = AccessTools.Property(DialogType, "CurrentPrimaryIdeo");
        private static readonly MethodInfo CheckRemoveAndMakePrimaryMethod = AccessTools.Method(DialogType, "CheckRemoveNewIdeoAndMakePrimary");
        private static readonly MethodInfo CreateNewIdeoMethod = AccessTools.Method(DialogType, "CreateNewIdeo");

        #endregion

        #region Lifecycle

        public static void EnsureOpen(Dialog_ConfigureIdeo d)
        {
            // Reference-equality (not IsActive) guard: see ArchonexusColonyState.EnsureOpen /
            // IdeoLoadState.EnsureOpen for the same idiom. Idempotent against a second PostOpen
            // postfix call for the same instance.
            if (ReferenceEquals(dialog, d))
            {
                return;
            }
            dialog = d;
            IsActive = true;
            RebuildRows();
        }

        public static void Close()
        {
            IsActive = false;
            rows.Clear();
            // dialog reference intentionally retained — see EnsureOpen.
        }

        /// <summary>Rebuilds the row list from live game state. Called by the scope's own RefreshContent on every model refresh (cheap, matches IdeoBuilderScreenScope.BuildListRows' own always-rebuild convention — cursor position is owned by the scope's ListModel, not by this list's shape).</summary>
        public static void RebuildRows()
        {
            rows.Clear();
            rows.Add(new Row { Kind = RowKind.CreateNew });
            rows.Add(new Row { Kind = RowKind.CreateFluid });
            if (Find.IdeoManager != null)
            {
                foreach (Ideo ideo in Find.IdeoManager.IdeosInViewOrder)
                {
                    rows.Add(new Row { Kind = RowKind.Ideo, Ideo = ideo });
                }
            }
            // "Assign colonists" only when some colonist isn't already on the (pending) primary —
            // mirrors the vanilla button's visibility condition.
            if (AnyColonistToConvert())
            {
                rows.Add(new Row { Kind = RowKind.AssignColonists });
            }
            // Mirrors vanilla's bottom-left "MakeIdeoligionPrimary"/"RemoveNewIdeoligion" button —
            // always present as ONE row (read-only law: stays navigable and says why rather than
            // appearing/disappearing as the Ideoligions-region cursor moves); the scope computes
            // its live label/disabled state per describe via ShouldShowRemoveNewIdeoligion/
            // CanMakeSelectedPrimary below.
            rows.Add(new Row { Kind = RowKind.MakeOrRemovePrimary });
        }

        public static IReadOnlyList<Row> Rows
        {
            get { return rows; }
        }

        private static bool AnyColonistToConvert()
        {
            // Same condition vanilla uses to show its "Assign colonists" button (source.Any(),
            // Dialog_ConfigureIdeo.cs:113): any colonist NOT already on the faction's CURRENT
            // (live, not pending) primary ideoligion.
            return PawnsField.GetValue(dialog) is List<Pawn> pawns
                && pawns.Any(p => p.IsColonist && p.Ideo != Faction.OfPlayer.ideos.PrimaryIdeo);
        }

        /// <summary>The one ideoligion vanilla renders editable (in non-dev-edit-mode) in this screen: the custom ideo the player created or loaded (<c>Dialog_ConfigureIdeo.customOrLoadedIdeo</c>). Null until they do so.</summary>
        public static Ideo CustomOrLoadedIdeo
        {
            get { return dialog == null ? null : (Ideo)CustomOrLoadedIdeoField.GetValue(dialog); }
        }

        public static bool IsEditable(Ideo i)
        {
            return i != null && i == CustomOrLoadedIdeo;
        }

        /// <summary>The dialog's own pending primary (<c>newPrimaryIdeo ?? initialPrimaryIdeo</c>) — not yet committed to <see cref="Faction.OfPlayer"/> until <see cref="ConfirmAndProceed"/>.</summary>
        public static Ideo CurrentPrimaryIdeo
        {
            get { return dialog == null ? null : (Ideo)CurrentPrimaryIdeoProp.GetValue(dialog); }
        }

        /// <summary>Mirrors <c>Dialog_ConfigureIdeo.DoWindowContents</c>' "RemoveNewIdeoligion" gate (decompiled :129): the custom/loaded ideo is currently selected AND is already the pending primary.</summary>
        public static bool ShouldShowRemoveNewIdeoligion()
        {
            Ideo custom = CustomOrLoadedIdeo;
            return custom != null && IdeoUIUtility.selected == custom && CurrentPrimaryIdeo == custom;
        }

        /// <summary>Mirrors the "MakeIdeoligionPrimary" gate (decompiled :136): the currently-selected ideoligion isn't already the pending primary. Only meaningful when <see cref="ShouldShowRemoveNewIdeoligion"/> is false (mutually exclusive, matching vanilla).</summary>
        public static bool CanMakeSelectedPrimary()
        {
            return IdeoUIUtility.selected != null && IdeoUIUtility.selected != CurrentPrimaryIdeo;
        }

        #endregion

        #region Actions — every one rides a vanilla vehicle (see class remarks)

        /// <summary>Vehicle A: the dialog's own "MakeIdeoligionPrimary" button body (<c>CheckRemoveNewIdeoAndMakePrimary</c>), acting on <see cref="IdeoUIUtility.selected"/> exactly like vanilla's button does.</summary>
        public static void MakeSelectedPrimary()
        {
            Ideo ideo = IdeoUIUtility.selected;
            if (ideo == null || ideo == CurrentPrimaryIdeo)
            {
                return;
            }
            CheckRemoveAndMakePrimaryMethod.Invoke(dialog, new object[] { ideo });
        }

        /// <summary>Vehicle A: the dialog's own "RemoveNewIdeoligion" button body — removes the just-created/loaded ideo and restores the pre-dialog primary.</summary>
        public static void RemoveNewIdeoligion()
        {
            var initial = (Ideo)InitialPrimaryIdeoField.GetValue(dialog);
            CheckRemoveAndMakePrimaryMethod.Invoke(dialog, new object[] { initial });
        }

        public static void CreateNew(bool fluid)
        {
            // Opens the game's Dialog_ChooseMemes (Structure) — already accessible via
            // IdeoMemeScreenScope — and, on accept, makes the new ideo primary.
            CreateNewIdeoMethod.Invoke(dialog, new object[] { fluid });
        }

        public static void LoadSaved()
        {
            // Mirrors the dialog's own archonexus load callback (Dialog_ConfigureIdeo.cs:89-92):
            // make the loaded ideo primary and track it as the custom/loaded ideo. The load
            // picker (Dialog_IdeoList_Load) has its own registered scope.
            Find.WindowStack.Add(new Dialog_IdeoList_Load(delegate (Ideo loaded)
            {
                CheckRemoveAndMakePrimaryMethod.Invoke(dialog, new object[] { loaded });
                CustomOrLoadedIdeoField.SetValue(dialog, loaded);
            }));
        }

        public static void AssignColonists()
        {
            // Open the game's per-pawn conversion dialog with the same wiring the vanilla
            // "Assign colonists" button uses (Dialog_ConfigureIdeo.cs:114-127). The dialog has its
            // own registered scope (ArchonexusConvertColonistsScope). The setter writes into the
            // dialog's pawnConvertToIdeo map, which ConfirmAndProceed ("Next") then applies.
            if (!(PawnsField.GetValue(dialog) is List<Pawn> pawns))
            {
                return;
            }
            if (!(PawnConvertToIdeoField.GetValue(dialog) is Dictionary<Pawn, Ideo> convert))
            {
                return;
            }
            Ideo primary = CurrentPrimaryIdeo;
            var initialPrimary = (Ideo)InitialPrimaryIdeoField.GetValue(dialog);

            // Match vanilla's source filter exactly (Dialog_ConfigureIdeo.cs:113): colonists NOT on
            // the faction's CURRENT primary. Colonists already on your existing ideoligion
            // auto-convert to the new one and are intentionally omitted — only those needing a
            // manual choice appear.
            IEnumerable<Pawn> source = pawns.Where(p => p.IsColonist && p.Ideo != Faction.OfPlayer.ideos.PrimaryIdeo);
            if (!source.Any())
            {
                TolkHelper.Speak("RimWorldAccess.Archonexus.Reform.NoColonistsNeedConverting".Loc());
                return;
            }

            Find.WindowStack.Add(new Dialog_ChooseColonistsForIdeo(
                primary,
                source,
                (Pawn p) => p.Ideo != initialPrimary && p.Ideo != primary,
                (Pawn p) => p.Ideo,
                (Pawn p) => convert.TryGetValue(p, out Ideo i) && i != null ? i : p.Ideo,
                delegate (Pawn p, Ideo i)
                {
                    convert[p] = (p.Ideo == i) ? null : i;
                }));
        }

        public static void ConfirmAndProceed()
        {
            // Replicates the dialog's "Next" button: commit the primary, apply any pawn
            // conversions, close, then run the questline's nextAction (InitMoveColony).
            try
            {
                Ideo primary = CurrentPrimaryIdeo;
                if (Faction.OfPlayer.ideos.PrimaryIdeo != primary)
                {
                    Faction.OfPlayer.ideos.SetPrimary(primary);
                }

                if (PawnsField.GetValue(dialog) is List<Pawn> pawns
                    && PawnConvertToIdeoField.GetValue(dialog) is Dictionary<Pawn, Ideo> conversions)
                {
                    foreach (Pawn pawn in pawns)
                    {
                        if (conversions.TryGetValue(pawn, out Ideo target) && target != null)
                        {
                            pawn.ideo.SetIdeo(target);
                        }
                    }
                }

                var next = NextActionField.GetValue(dialog) as Action;
                dialog.Close();
                next?.Invoke();
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorld Access] Error confirming reform ideo: {ex}");
                TolkHelper.Speak("RimWorldAccess.Archonexus.Reform.ErrorConfirming".Loc(), SpeechPriority.High);
            }
        }

        #endregion
    }

    // Lifecycle patches for Dialog_ConfigureIdeo. Keyboard input and Escape/Enter ownership are
    // driven by RimWorldAccess.Shell.ArchonexusIdeoScreenScope (registered on Dialog_ConfigureIdeo
    // in ShellBootstrap.Game.cs) — see that scope's
    // class remarks. Ritual-sound preview needs no per-frame rehoming here (see that scope's
    // remarks on why the retired ArchonexusReformIdeoScopeMirror's duty is now provably dead code
    // in this context).
    [HarmonyPatch(typeof(Window), "PostOpen")]
    public static class ArchonexusReformIdeoPatch_PostOpen
    {
        [HarmonyPostfix]
        static void Postfix(Window __instance)
        {
            if (__instance is Dialog_ConfigureIdeo d)
                ArchonexusReformIdeoState.EnsureOpen(d);
        }
    }

    [HarmonyPatch(typeof(Window), "PostClose")]
    public static class ArchonexusReformIdeoPatch_PostClose
    {
        [HarmonyPostfix]
        static void Postfix(Window __instance)
        {
            if (__instance is Dialog_ConfigureIdeo)
            {
                ArchonexusReformIdeoState.Close();
                // Defense in depth: this screen's Details region can open the same windowless
                // IdeoBuilder overlay editors (precept/typed-precept/deity/appearance) the worldgen
                // hub and in-game reform dialog do — the dialog closing while one is still open
                // must not leave its IsActive stuck true.
                IdeoBuilderOverlays.CloseAllOverlayEditors();
            }
        }
    }
}
