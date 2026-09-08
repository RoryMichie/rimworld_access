using System.Reflection;
using HarmonyLib;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Registers the Character Editor mod's own windows against their scopes: the editor window
    /// itself (<c>CharacterEditor.CEditor+EditorUI</c>) against <see cref="CharacterEditorScope"/>,
    /// its browser-style dialogs against the shared <see cref="CharEditorBrowserScope"/> with one
    /// factory per dialog constructing the matching <see cref="ICharEditorBrowserAdapter"/>, and the
    /// rest against their own bespoke scopes. Each binding block gates on its OWN Ready flag so a
    /// mod update breaking one dialog's members does not take down any other registration.
    ///
    /// EXACT-TYPE REGISTRATION BEATS THE DELIBERATE-ATTACH WATCH, with no change needed to the
    /// watch itself. <c>ScopeForWindow.Attach</c> consults the exact-type factory table FIRST and
    /// only falls through to <c>TryCreateGenericReader</c> (whose eligibility test is where
    /// <c>ArmDeliberateGenericAttach</c> lives) when no factory matched, so once a type has a
    /// factory the generic reader is never even asked about it. The F12 opener's replay path
    /// behaves the same way: <c>AttachExistingScopelessForeignWindows</c> skips windows that
    /// already carry a scope and otherwise calls <c>AttachOnDemand</c>, which routes through the
    /// same factory-first <c>Attach</c>. The watch stays fully intact for every OTHER modded
    /// window, which is the shipped behavior it exists for.
    ///
    /// <c>DialogChangeBackstory</c> has ONE type but TWO call sites (childhood, adulthood) with no
    /// field on the open instance recording which -- its own constructor parameter
    /// (<c>_isChildhood</c>) is not retrievable after the fact except via its PRIVATE
    /// <c>isChildhood</c> field, which the factory below reads once at attach time to decide which
    /// adapter (and title) to build.
    /// </summary>
    internal static class CharEditorDialogCompat
    {
        private static FieldInfo isChildhoodField;

        public static void RegisterDialogScopes()
        {
            if (CharEditorCompat.ModPresent && CharEditorCompat.Ready)
            {
                ScopeForWindow.Register(CharEditorCompat.EditorUIType, delegate (Window w)
                {
                    return new CharacterEditorScope(w);
                });

                ModLogger.Msg("Character Editor compat: registered the editor screen");
            }

            if (CharEditorBrowserCompat.ModPresent && CharEditorBrowserCompat.Ready)
            {
                isChildhoodField = AccessTools.Field(CharEditorBrowserCompat.ChangeBackstoryDialogType, "isChildhood");

                ScopeForWindow.Register(CharEditorBrowserCompat.AddTraitDialogType, delegate (Window w)
                {
                    return new CharEditorBrowserScope(w, new AddTraitAdapter(w));
                });
                ScopeForWindow.Register(CharEditorBrowserCompat.ChangeBackstoryDialogType, delegate (Window w)
                {
                    bool isChildhood = isChildhoodField != null && (bool)isChildhoodField.GetValue(w);
                    return new CharEditorBrowserScope(w, new ChangeBackstoryAdapter(w, isChildhood));
                });

                // Each dialog type below was resolved by the SAME CharEditorBrowserCompat.Ready
                // check above, so no additional per-type null guard is needed before registering.
                ScopeForWindow.Register(CharEditorBrowserCompat.AddAbilityDialogType, delegate (Window w)
                {
                    return new CharEditorBrowserScope(w, new AddAbilityAdapter(w));
                });
                ScopeForWindow.Register(CharEditorBrowserCompat.ChangeRaceDialogType, delegate (Window w)
                {
                    return new CharEditorBrowserScope(w, new ChangeRaceAdapter(w));
                });
                ScopeForWindow.Register(CharEditorBrowserCompat.ChangeFactionDialogType, delegate (Window w)
                {
                    return new CharEditorBrowserScope(w, new ChangeFactionAdapter(w));
                });
                ScopeForWindow.Register(CharEditorBrowserCompat.FindPawnDialogType, delegate (Window w)
                {
                    return new CharEditorBrowserScope(w, new FindPawnAdapter(w));
                });

                ModLogger.Msg("Character Editor compat: registered the Add Trait / Change Backstory / Add Ability / Change Race / Change Faction / Find Pawn browser screens");
            }

            // The Birthday dialog gets its own small dedicated scope rather than the shared
            // browser: there is no list, only numeric fields.
            if (CharEditorBirthdayCompat.ModPresent && CharEditorBirthdayCompat.Ready
                && CharEditorCompat.ModPresent && CharEditorCompat.Ready)
            {
                ScopeForWindow.Register(CharEditorBirthdayCompat.DialogType, delegate (Window w)
                {
                    // The dialog carries no public/reflectable back-reference to the pawn it was
                    // opened for beyond its own private fields already bound elsewhere for the
                    // numeric rows; the currently edited pawn is the same pawn the opener passed
                    // in (BlockBio.AAddBirthdayTick always opens it with API.Pawn), so reading the
                    // editor's own CurrentPawn here is equivalent and needs no extra binding.
                    return new CharEditorBirthdayScope(w, CharEditorCompat.CurrentPawn);
                });

                ModLogger.Msg("Character Editor compat: registered the Birthday dialog");
            }

            // ONE registration serves all nine modes DialogColorPicker opens in; the scope reads
            // its mode live off the open instance.
            if (CharEditorColorPickerCompat.ModPresent && CharEditorColorPickerCompat.Ready)
            {
                ScopeForWindow.Register(CharEditorColorPickerCompat.DialogType, delegate (Window w)
                {
                    return new CharEditorColorScope(w);
                });

                ModLogger.Msg("Character Editor compat: registered the color picker dialog");
            }

            // DialogObjects joins the shared browser scope, one ObjectsAdapter reading the live
            // mDialogType field to pick its shape.
            if (CharEditorObjectsCompat.ModPresent && CharEditorObjectsCompat.Ready)
            {
                ScopeForWindow.Register(CharEditorObjectsCompat.DialogType, delegate (Window w)
                {
                    return new CharEditorBrowserScope(w, new ObjectsAdapter(w));
                });

                ModLogger.Msg("Character Editor compat: registered the Objects browser dialog");
            }

            // DialogChangeHeadAddons gets its own small dedicated scope. Alien-race-only in
            // practice, so this registration simply never attaches without a HAR-based race.
            if (CharEditorHeadAddonsCompat.ModPresent && CharEditorHeadAddonsCompat.Ready)
            {
                ScopeForWindow.Register(CharEditorHeadAddonsCompat.DialogType, delegate (Window w)
                {
                    return new CharEditorHeadAddonsScope(w);
                });

                ModLogger.Msg("Character Editor compat: registered the Head addons dialog");
            }

            // DialogAddHediff, DialogFullheal, DialogChoosePart and DialogChoosePawn all join the
            // shared browser scope, one adapter per dialog.
            if (CharEditorHealthBrowserCompat.ModPresent && CharEditorHealthBrowserCompat.Ready)
            {
                ScopeForWindow.Register(CharEditorHealthBrowserCompat.AddHediffDialogType, delegate (Window w)
                {
                    return new CharEditorBrowserScope(w, new AddHediffAdapter(w));
                });
                ScopeForWindow.Register(CharEditorHealthBrowserCompat.FullhealDialogType, delegate (Window w)
                {
                    return new CharEditorBrowserScope(w, new FullhealAdapter(w));
                });
                ScopeForWindow.Register(CharEditorHealthBrowserCompat.ChoosePartDialogType, delegate (Window w)
                {
                    return new CharEditorBrowserScope(w, new ChoosePartAdapter(w));
                });
                ScopeForWindow.Register(CharEditorHealthBrowserCompat.ChoosePawnDialogType, delegate (Window w)
                {
                    return new CharEditorBrowserScope(w, new ChoosePawnAdapter(w));
                });

                ModLogger.Msg("Character Editor compat: registered the Add Hediff / Full Heal / Choose Part / Choose Pawn browser screens");
            }

            // One registration covers both entry points: Needs' unfiltered "Add thought..." row and
            // Social's Social-preset "Add thought" icon open the SAME dialog type.
            if (CharEditorThoughtBrowserCompat.ModPresent && CharEditorThoughtBrowserCompat.Ready)
            {
                ScopeForWindow.Register(CharEditorThoughtBrowserCompat.DialogType, delegate (Window w)
                {
                    return new CharEditorBrowserScope(w, new AddThoughtAdapter(w));
                });

                ModLogger.Msg("Character Editor compat: registered the Add Thought browser screen");
            }

            // DialogViewXenoGenes gets its own compact TreeRegionScope (the gene grid is reused
            // from GeneTreeBuilder, not re-transcribed); DialogGenery joins the shared browser
            // scope via GeneryAdapter in browse mode; DialogXenoType gets its own
            // GeneDialogScopeBase subclass, the same base XenotypeEditorScope rides for vanilla
            // Dialog_CreateXenotype. All three self-close without Biotech at the mod's own
            // construction/PostOpen, so registration never warns on a Biotech-less install.
            if (CharEditorXenoGenesCompat.ModPresent && CharEditorXenoGenesCompat.Ready)
            {
                ScopeForWindow.Register(CharEditorXenoGenesCompat.DialogType, delegate (Window w)
                {
                    return new CharEditorXenoGenesScope(w);
                });

                ModLogger.Msg("Character Editor compat: registered the View Xenogenes dialog");
            }

            if (CharEditorGeneryCompat.ModPresent && CharEditorGeneryCompat.Ready)
            {
                ScopeForWindow.Register(CharEditorGeneryCompat.DialogType, delegate (Window w)
                {
                    return new CharEditorBrowserScope(w, new GeneryAdapter(w));
                });

                ModLogger.Msg("Character Editor compat: registered the Add Gene browser screen");
            }

            if (CharEditorXenoTypeCompat.ModPresent && CharEditorXenoTypeCompat.Ready)
            {
                ScopeForWindow.Register(CharEditorXenoTypeCompat.DialogType, delegate (Window w)
                {
                    return new CharEditorXenoTypeScope(w);
                });

                ModLogger.Msg("Character Editor compat: registered the custom Xenotype creator dialog");
            }

            // DialogCapsuleUI gets its own compact TreeRegionScope, capsule view only -- see
            // CharEditorCapsuleScope's class remarks.
            if (CharEditorCapsuleCompat.ModPresent && CharEditorCapsuleCompat.Ready)
            {
                ScopeForWindow.Register(CharEditorCapsuleCompat.DialogType, delegate (Window w)
                {
                    return new CharEditorCapsuleScope(w);
                });

                ModLogger.Msg("Character Editor compat: registered the Capsule dialog");
            }

            // DialogConfigurate (the mod options dialog) gets its own bespoke scope.
            if (CharEditorConfigCompat.ModPresent && CharEditorConfigCompat.Ready)
            {
                ScopeForWindow.Register(CharEditorConfigCompat.DialogType, delegate (Window w)
                {
                    return new CharEditorConfigScope(w);
                });
                CharEditorConfigRowRingPatch.Register();

                ModLogger.Msg("Character Editor compat: registered the options dialog");
            }
        }
    }
}
