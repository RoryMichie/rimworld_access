using HarmonyLib;
using RimWorldAccess.Shell;

namespace RimWorldAccess
{
    internal sealed class RimworldTogetherModule : CompatModule
    {
        public override string TargetPackageId => "nova.rimworldtogether";

        public override void Activate(Harmony harmony)
        {
            RimworldTogetherChatCompat.Register(harmony);
            RimworldTogetherDismissCompat.Register();
            RimworldTogetherChatScopeCompat.Register(harmony);
            RimworldTogetherToolbarCompat.Register();
            RimworldTogetherBlankButtonCompat.Register(harmony);
        }
    }
}
