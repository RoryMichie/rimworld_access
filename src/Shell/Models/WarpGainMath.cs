namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Closed-loop calibration for pointer warps. A warp converts a requested pixel
    /// delta into OS units by dividing by a points-to-pixels quotient read from the
    /// display mode — a value some configurations misreport (2026-08-17 trace: every
    /// warp moved ~2x its request and the settle loop ping-ponged across the screen).
    /// The settle pass measures the movement each warp actually produced, and this
    /// gain — a persistent multiplier on that quotient — absorbs whatever the mode
    /// misreported: observed/requested becomes the correction factor, exact in one
    /// step when the hand is still.
    /// </summary>
    public static class WarpGainMath
    {
        /// <summary>Requests shorter than this cannot separate scale error from pointer jitter.</summary>
        public const float MinMeasurableRequest = 8f;

        /// <summary>Movements shorter than this are jitter, not a measured response.</summary>
        public const float MinMeasurableObserved = 1f;

        public const float MinGain = 0.25f;
        public const float MaxGain = 4f;

        /// <summary>
        /// The gain after one settle measurement. <paramref name="dot"/> is the dot
        /// product of the requested and observed delta vectors: a non-positive value
        /// means the pointer moved against the request (the hand grabbed it, or a
        /// clamp at the screen edge reversed it), which must not be read as scale.
        /// Unmeasurable steps return <paramref name="current"/> unchanged.
        /// </summary>
        public static float Next(float current, float requestedMagnitude, float observedMagnitude, float dot)
        {
            if (requestedMagnitude < MinMeasurableRequest
                || observedMagnitude < MinMeasurableObserved
                || dot <= 0f)
            {
                return current;
            }
            float step = observedMagnitude / requestedMagnitude;
            if (step < MinGain)
            {
                step = MinGain;
            }
            else if (step > MaxGain)
            {
                step = MaxGain;
            }
            float next = current * step;
            if (next < MinGain)
            {
                return MinGain;
            }
            return next > MaxGain ? MaxGain : next;
        }
    }
}
