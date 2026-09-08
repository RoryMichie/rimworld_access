using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The Character Editor module's exact-entry idiom: a digit-bounded
    /// <see cref="TextFieldSpec"/>, parse on confirm, then per-site out-of-range policy (clamp or
    /// drop), applied through the caller's own vehicle. One home for the digit regexes and the
    /// invariant-culture float parse.
    ///
    /// The out-of-range policy is SITE behavior, harvested from each mod widget this module
    /// mirrors: a widget whose own exact-entry path clamps gets <see cref="OutOfRange.Clamp"/>, one
    /// whose path silently ignores an out-of-range figure (the age fields' `result &gt; 0` guard)
    /// gets <see cref="OutOfRange.Drop"/>. Both policies leave an unparseable buffer untouched.
    ///
    /// The spec is derived from the range by default (digit count from the wider bound, sign
    /// allowed only when asked). Sites whose own harvested field rule differs -- no length cap at
    /// all, a cap the mod declares independently of the range, a non-empty requirement -- pass
    /// their own <c>spec</c>, keeping that rule visible at the call site.
    /// </summary>
    internal static class CharEdNumericEntry
    {
        public static readonly Regex DigitsOnly = new Regex("^[0-9]*$");
        public static readonly Regex SignedDigits = new Regex("^-?[0-9]*$");
        public static readonly Regex UnsignedFloat = new Regex(@"^[0-9]*\.?[0-9]*$");
        public static readonly Regex SignedFloat = new Regex(@"^-?[0-9]*\.?[0-9]*$");

        public enum OutOfRange
        {
            Clamp,
            Drop,
        }

        public static void OpenInt(TextFieldEditSession session, string label, int current,
            int min, int max, Action<int> apply, Action onExit,
            bool signed = false, OutOfRange policy = OutOfRange.Clamp, TextFieldSpec spec = null)
        {
            session.EnterEdit(current.ToString(), spec ?? RangeSpec(min, max, signed), label,
                value => ApplyInt(value, min, max, policy, apply),
                onExit: onExit);
        }

        /// <summary>
        /// The float twin. Every float site formats its own seed text (the mod's own "0.##"/"0"
        /// renderings) and carries its own harvested spec, so both are caller-supplied rather than
        /// guessed here.
        /// </summary>
        public static void OpenFloat(TextFieldEditSession session, string label, string currentText,
            TextFieldSpec spec, float min, float max, Action<float> apply, Action onExit,
            OutOfRange policy = OutOfRange.Clamp)
        {
            session.EnterEdit(currentText ?? string.Empty, spec, label,
                value => ApplyFloat(value, min, max, policy, apply),
                onExit: onExit);
        }

        private static void ApplyInt(string value, int min, int max, OutOfRange policy, Action<int> apply)
        {
            if (!int.TryParse(value, out int parsed))
            {
                return;
            }
            if (parsed < min || parsed > max)
            {
                if (policy == OutOfRange.Drop)
                {
                    return;
                }
                parsed = parsed < min ? min : max;
            }
            apply(parsed);
        }

        private static void ApplyFloat(string value, float min, float max, OutOfRange policy, Action<float> apply)
        {
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
            {
                return;
            }
            if (parsed < min || parsed > max)
            {
                if (policy == OutOfRange.Drop)
                {
                    return;
                }
                parsed = parsed < min ? min : max;
            }
            apply(parsed);
        }

        private static TextFieldSpec RangeSpec(int min, int max, bool signed)
        {
            int digits = Math.Max(min.ToString().Length, max.ToString().Length);
            return new TextFieldSpec(labelKey: null, maxLength: digits, minLength: 0,
                allowedChars: signed ? SignedDigits : DigitsOnly);
        }
    }
}
