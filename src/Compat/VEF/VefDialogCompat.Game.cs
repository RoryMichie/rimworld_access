using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Registers Vanilla Expanded Framework's own dialogs against their scopes (
    /// <c>VEF.Planet.Dialog_Hire</c> via <see cref="VefHireCompat"/>/<see cref="VefHireScope"/>,
    /// <c>VEF.Storyteller.Window_Contracts</c> via <see cref="VefContractsCompat"/>/
    /// <see cref="VefContractsScope"/>, and <c>VEF.Graphics.Dialog_GraphicCustomization</c> via
    /// <see cref="VefGraphicCustomizationCompat"/>/<see cref="VefGraphicCustomizationScope"/>,
    /// and <c>VEF.Memes.Dialog_FloatMenuOptions</c> via <see cref="VefPreceptOptionsCompat"/>/
    /// <see cref="VefPreceptOptionsScope"/>), mirroring <see cref="VfDialogCompat"/>'s hub for
    /// Vehicle Framework.
    /// </summary>
    internal static class VefDialogCompat
    {
        public static void RegisterDialogScopes()
        {
            int registered = 0;

            if (VefHireCompat.Ready)
            {
                ScopeForWindow.RegisterHierarchy(VefHireCompat.DialogHireType, delegate (Window w)
                {
                    return new VefHireScope(w);
                });
                registered++;
            }

            if (VefContractsCompat.Ready)
            {
                ScopeForWindow.RegisterHierarchy(VefContractsCompat.WindowType, delegate (Window w)
                {
                    return new VefContractsScope(w);
                });
                registered++;
            }

            if (VefGraphicCustomizationCompat.Ready)
            {
                ScopeForWindow.RegisterHierarchy(VefGraphicCustomizationCompat.DialogType, delegate (Window w)
                {
                    return new VefGraphicCustomizationScope(w);
                });
                registered++;
            }

            if (VefPreceptOptionsCompat.Ready)
            {
                ScopeForWindow.RegisterHierarchy(VefPreceptOptionsCompat.DialogType, delegate (Window w)
                {
                    return new VefPreceptOptionsScope(w);
                });
                registered++;
            }

            if (registered > 0)
                Log.Message($"[RimWorld Access] VEF compat: registered {registered} dialog scope(s)");
        }
    }
}
