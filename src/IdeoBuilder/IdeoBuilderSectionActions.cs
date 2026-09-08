using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Maps a hub section to the editor it opens. Shared by the Custom-creation hub
    /// (IdeoBuilderHubState) and the in-game reform dialog's free-edit stage
    /// (IdeoReformState) so the "Enter on a section opens its editor" dispatch lives in
    /// one place and operates on whichever Ideo the caller passes.
    /// </summary>
    public static class IdeoBuilderSectionActions
    {
        /// <summary>
        /// Opens the editor a hub row leads to. A section discovered from a mod's own precept class
        /// carries that class instead of a <see cref="IdeoBuilderHelper.SectionKind"/>
        /// (<see cref="IdeoBuilderHelper.HubSection.PreceptClass"/>), so it is dispatched first.
        /// </summary>
        public static void Activate(Ideo ideo, IdeoBuilderHelper.HubSection section)
        {
            if (ideo == null || section == null) return;
            if (section.PreceptClass != null)
            {
                IdeoTypedPreceptState.Open(ideo, section.Kind, section.PreceptClass, section.Label);
                return;
            }
            Activate(ideo, section.Kind);
        }

        public static void Activate(Ideo ideo, IdeoBuilderHelper.SectionKind kind)
        {
            if (ideo == null) return;

            switch (kind)
            {
                case IdeoBuilderHelper.SectionKind.StructureMeme:
                    Find.WindowStack.Add(new Dialog_ChooseMemes(ideo, MemeCategory.Structure, initialSelection: false));
                    break;
                case IdeoBuilderHelper.SectionKind.NormalMemes:
                    Find.WindowStack.Add(new Dialog_ChooseMemes(ideo, MemeCategory.Normal, initialSelection: false));
                    break;
                case IdeoBuilderHelper.SectionKind.Precepts:
                    IdeoPreceptSelectionState.Open(ideo);
                    break;
                case IdeoBuilderHelper.SectionKind.Roles:
                case IdeoBuilderHelper.SectionKind.Rituals:
                case IdeoBuilderHelper.SectionKind.Buildings:
                case IdeoBuilderHelper.SectionKind.Relics:
                case IdeoBuilderHelper.SectionKind.Weapons:
                case IdeoBuilderHelper.SectionKind.VeneratedAnimals:
                case IdeoBuilderHelper.SectionKind.PreferredXenotypes:
                case IdeoBuilderHelper.SectionKind.Apparel:
                    IdeoTypedPreceptState.Open(ideo, kind);
                    break;
                case IdeoBuilderHelper.SectionKind.Name:
                case IdeoBuilderHelper.SectionKind.Adjective:
                case IdeoBuilderHelper.SectionKind.MemberName:
                case IdeoBuilderHelper.SectionKind.WorshipRoom:
                case IdeoBuilderHelper.SectionKind.Icon:
                case IdeoBuilderHelper.SectionKind.Color:
                    // Vanilla groups all six on one dialog; the scope lands on the opening row.
                    Shell.ChooseIdeoSymbolsScope.ArmOpeningFocus(SymbolsFocusFor(kind));
                    Find.WindowStack.Add(new Dialog_ChooseIdeoSymbols(ideo));
                    break;
                case IdeoBuilderHelper.SectionKind.Description:
                    IdeoSymbolEditState.OpenDescriptionMenu(ideo);
                    break;
                case IdeoBuilderHelper.SectionKind.Culture:
                    IdeoSymbolEditState.OpenCulturePicker(ideo);
                    break;
                case IdeoBuilderHelper.SectionKind.Styles:
                    IdeoSymbolEditState.OpenStylePicker(ideo);
                    break;
                case IdeoBuilderHelper.SectionKind.Deities:
                    IdeoDeityListState.Open(ideo);
                    break;
                case IdeoBuilderHelper.SectionKind.Appearance:
                    // Vanilla's own expression, from the box that opens this editor (decompiled
                    // IdeoUIUtility.cs:2034). Every host that reaches this dispatcher is an
                    // edit-mode host (see IdeoEditorRegionCore.EditAllowed's remarks), and the
                    // dialog itself only ever asks whether the mode is None (:180, :455, :703),
                    // so GameStart and Reform are the same answer to it.
                    Find.WindowStack.Add(new Dialog_EditIdeoStyleItems(
                        ideo,
                        StyleItemTab.HairAndBeard,
                        IdeoUIUtility.DevEditMode ? IdeoEditMode.Dev : IdeoEditMode.GameStart));
                    break;
                default:
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    break;
            }
        }

        private static Shell.ChooseIdeoSymbolsScope.OpeningFocus SymbolsFocusFor(IdeoBuilderHelper.SectionKind kind)
        {
            switch (kind)
            {
                case IdeoBuilderHelper.SectionKind.Adjective: return Shell.ChooseIdeoSymbolsScope.OpeningFocus.Adjective;
                case IdeoBuilderHelper.SectionKind.MemberName: return Shell.ChooseIdeoSymbolsScope.OpeningFocus.MemberName;
                case IdeoBuilderHelper.SectionKind.WorshipRoom: return Shell.ChooseIdeoSymbolsScope.OpeningFocus.WorshipRoom;
                case IdeoBuilderHelper.SectionKind.Icon: return Shell.ChooseIdeoSymbolsScope.OpeningFocus.Icon;
                case IdeoBuilderHelper.SectionKind.Color: return Shell.ChooseIdeoSymbolsScope.OpeningFocus.Color;
                default: return Shell.ChooseIdeoSymbolsScope.OpeningFocus.Name;
            }
        }
    }
}
