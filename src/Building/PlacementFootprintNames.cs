namespace RimWorldAccess
{
    /// <summary>
    /// Which part of a multi-part placement footprint an outline is. Anything not named here —
    /// every modded place worker included — is <see cref="Unnamed"/> and reads as the generic
    /// required area.
    /// </summary>
    internal enum FootprintPart
    {
        Unnamed,

        /// <summary>The strip of moving water a water mill's wheel has to turn in.</summary>
        WatermillWheel,

        /// <summary>The stretch of river a water mill draws its flow from.</summary>
        WatermillWaterFlow,
    }

    /// <summary>Vanilla's own verdict on an outline, read off the colour it painted.</summary>
    internal enum OutlineVerdict
    {
        Suitable,
        Unsuitable,

        /// <summary>Another water mill already draws from this stretch of river.</summary>
        SharedWithOtherWatermill,
    }

    /// <summary>
    /// The whole-phrase key that names a footprint part and carries the same measurements the
    /// generic form carries. Each part gets its own key per verdict and per position rather than
    /// a name glued onto a generic sentence, so a translation can put the name where its own
    /// grammar wants it.
    /// </summary>
    internal static class PlacementFootprintNames
    {
        /// <param name="atCursor">True when the outline sits on the cursor, so no distance or
        /// direction is measured and the key takes two arguments instead of four.</param>
        /// <returns>The key, or null when vanilla never paints that part in that verdict.</returns>
        internal static string OutlineKey(FootprintPart part, OutlineVerdict verdict, bool atCursor)
        {
            switch (part)
            {
                case FootprintPart.WatermillWheel:
                    if (verdict == OutlineVerdict.Suitable)
                    {
                        return atCursor
                            ? "RimWorldAccess.Building.Place.WatermillWheelOkHere"
                            : "RimWorldAccess.Building.Place.WatermillWheelOkAt";
                    }
                    if (verdict == OutlineVerdict.Unsuitable)
                    {
                        return atCursor
                            ? "RimWorldAccess.Building.Place.WatermillWheelBadHere"
                            : "RimWorldAccess.Building.Place.WatermillWheelBadAt";
                    }
                    return null;

                case FootprintPart.WatermillWaterFlow:
                    if (verdict == OutlineVerdict.Suitable)
                    {
                        return atCursor
                            ? "RimWorldAccess.Building.Place.WatermillFlowOkHere"
                            : "RimWorldAccess.Building.Place.WatermillFlowOkAt";
                    }
                    if (verdict == OutlineVerdict.SharedWithOtherWatermill)
                    {
                        return atCursor
                            ? "RimWorldAccess.Building.Place.WatermillFlowSharedHere"
                            : "RimWorldAccess.Building.Place.WatermillFlowSharedAt";
                    }
                    return null;

                default:
                    if (verdict == OutlineVerdict.Suitable)
                    {
                        return atCursor
                            ? "RimWorldAccess.Building.Place.OutlineOkHere"
                            : "RimWorldAccess.Building.Place.OutlineOkAt";
                    }
                    if (verdict == OutlineVerdict.Unsuitable)
                    {
                        return atCursor
                            ? "RimWorldAccess.Building.Place.OutlineBadHere"
                            : "RimWorldAccess.Building.Place.OutlineBadAt";
                    }
                    return null;
            }
        }
    }
}
