using System;
using System.Reflection;
using RimWorldAccess.Shell;

namespace RimWorldAccess
{
    /// <summary>
    /// Escape support for the DLG_Base dialog family. Every Rimworld Together dialog sets
    /// <c>closeOnCancel = false</c> and keeps its only dismissal on a drawn button, so
    /// vanilla's Escape chain is a silent no-op on all of them. Two
    /// <see cref="WindowDismissRegistry"/> entries restore the convention: DLG_YesNo gets a
    /// delegate rule (its No caption is caller-parameterized, so labels cannot identify it),
    /// and the rest of the family rides its hardcoded Cancel/Close/Back captions. Accept-only
    /// dialogs (DLG_Message's pager, the admin config screens, DLG_Wait) match no entry
    /// button and stay inert on Escape -- the same dead end a sighted player has.
    /// </summary>
    internal static class RimworldTogetherDismissCompat
    {
        public static void Register()
        {
            var surface = new ReflectionSurface("RimworldTogetherDismissCompat");
            Type dlgBaseType = surface.Type("RTClient.Dialogs.DLG_Base");
            Type yesNoType = surface.Type("RTClient.Dialogs.Default.DLG_YesNo");
            PropertyInfo onCancelProperty = surface.Property(dlgBaseType, "OnCancel");
            if (!surface.Ready)
            {
                return;
            }

            // Mirrors DLG_YesNo.DoWindowContents' No button body exactly: OnCancel?.Invoke()
            // then Close(true) -- the dialog's own dismissal delegate, label-independent.
            WindowDismissRegistry.RegisterHandler(
                w => yesNoType.IsInstanceOfType(w),
                w =>
                {
                    try
                    {
                        var onCancel = onCancelProperty.GetValue(w) as Action;
                        if (onCancel != null)
                        {
                            onCancel.Invoke();
                        }
                        w.Close(true);
                    }
                    catch (Exception ex)
                    {
                        ModLogger.LimitedError("Rimworld Together YesNo dismiss", ex);
                    }
                });

            // absorbInputAroundWindow excludes the non-modal chat panel, which is not a dialog.
            WindowDismissRegistry.RegisterLabels(
                w => dlgBaseType.IsInstanceOfType(w) && w.absorbInputAroundWindow,
                "Cancel", "Close", "Back"); // l10n-exempt: the target mod's own hardcoded button captions, matched against captures, never spoken
        }
    }
}
