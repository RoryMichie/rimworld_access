using HarmonyLib;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Character Development (WantsAndQuirks). Its recipient-picker dialog is modal and rides the
    /// generic reader; the bespoke work is the Characters main tab, whose reward bubbles are a
    /// raw-event physics canvas (<see cref="WqCharactersTabScope"/>), and the pawn Wants inspect
    /// tab, whose two scrolling panels capture as loose labels (<see cref="WqWantsTabAdapter"/>).
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

            CompatRegistration.TabAdapter("WantsAndQuirks.ITab_Pawn_WantsAndQuirks",
                t => new WqWantsTabAdapter(), "Character Development wants tab compat");

            Log.Message("[RimWorld Access] Character Development compat: characters tab scope registered");
        }
    }
}
