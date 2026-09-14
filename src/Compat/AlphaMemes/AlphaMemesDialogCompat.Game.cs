using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Registers Alpha Memes' own windows against their scopes: the four <c>Dialog_ChangeStyles*</c>
    /// grids via <see cref="AlphaMemesStyleCompat"/>/<see cref="AlphaMemesStylePickerScope"/>, and
    /// <c>Dialog_AnimalDatabase</c> -- a read-only search box, checkbox and label list, squarely
    /// inside the generic reader's competence -- straight to that reader. Mirrors
    /// <see cref="VefDialogCompat"/>'s hub.
    ///
    /// None of these windows absorb input, so without a registration no scope attaches at all
    /// while <c>closeOnClickedOutside</c> still suppresses the map keys underneath: a silent
    /// window over a deaf map.
    /// </summary>
    internal static class AlphaMemesDialogCompat
    {
        public static void RegisterDialogScopes()
        {
            if (AlphaMemesStyleCompat.Ready)
            {
                RegisterStyleDialog(AlphaMemesStyleCompat.SingleType, AlphaMemesStyleVariant.Single);
                RegisterStyleDialog(AlphaMemesStyleCompat.AreaType, AlphaMemesStyleVariant.Area);
                RegisterStyleDialog(AlphaMemesStyleCompat.SwapType, AlphaMemesStyleVariant.SwapSource);
                RegisterStyleDialog(AlphaMemesStyleCompat.SwapSecondType, AlphaMemesStyleVariant.SwapTarget);
            }

            ScopeForWindow.TryRegisterGenericReaderForWindow("AlphaMemes.Dialog_AnimalDatabase");
        }

        /// <summary>Per-type guard so a mod version that renames one grid degrades to the other three.</summary>
        private static void RegisterStyleDialog(System.Type dialogType, AlphaMemesStyleVariant variant)
        {
            if (dialogType == null)
                return;
            ScopeForWindow.RegisterHierarchy(dialogType, delegate (Window w)
            {
                return new AlphaMemesStylePickerScope(w, variant);
            });
        }
    }
}
