using HarmonyLib;
using Verse;

namespace RimWorldAccess
{
    internal sealed class CharacterEditorModule : CompatModule
    {
        public override string TargetPackageId => "void.charactereditor";

        public override bool ShouldActivate =>
            ModsConfig.IsActive(TargetPackageId)
            || AccessTools.TypeByName("CharacterEditor.CEditor") != null;

        public override void Activate(Harmony harmony)
        {
            CharEditorDialogCompat.RegisterDialogScopes();
        }
    }
}
