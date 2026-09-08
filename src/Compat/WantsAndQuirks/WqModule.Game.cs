using HarmonyLib;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Character Development (WantsAndQuirks). Its recipient-picker dialog is modal and rides
    /// the generic reader, and its pawn inspect tab rides the unknown-tab capture branch; the
    /// bespoke work is the Characters main tab, whose reward bubbles are a raw-event physics
    /// canvas (<see cref="WqCharactersTabScope"/>).
    /// </summary>
    internal sealed class WqModule : CompatModule
    {
        public override string TargetPackageId => "ferny.characterdevelopment";

        public override void Activate(Harmony harmony)
        {
            if (!WqCompat.Ready)
            {
                return;
            }

            ScopeForWindow.Register(WqCompat.MainTabType, delegate (Window w)
            {
                return new WqCharactersTabScope(w);
            });

            Log.Message("[RimWorld Access] Character Development compat: characters tab scope registered");
        }
    }
}
