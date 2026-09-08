using System;
using System.Collections.Generic;

namespace RimWorldAccess
{
    /// <summary>
    /// Generic registry for a mod's PlaySettings toolbar icon whose click branches on a held
    /// modifier key at the moment vanilla's own <c>WidgetRow.ToggleableIcon</c> flips its bool.
    /// RimTalk's <c>TogglePatch</c> and Bubbles' own postfix on
    /// <c>PlaySettings.DoPlaySettingsGlobalControls</c> both read <c>Event.current.shift</c>/
    /// <c>.control</c> immediately after that call returns (decompiled-verified: RimTalk
    /// shift=open mod settings, ctrl=open debug window; Bubbles shift=open its settings window).
    /// A mouse user reaches every variant by holding the modifier during the icon's plain click;
    /// a keyboard user has no held-modifier click at all, so each variant needs its own declared
    /// action wherever the icon's plain toggle already surfaces in this mod's keyboard reader
    /// (today: <c>DialogueLogScope</c>'s button row).
    ///
    /// The MACHINERY here is generic — any compat class may call <see cref="Register"/> for any
    /// icon id, and any reader may enumerate <see cref="AvailableVariants"/> for that id. The
    /// per-mod <c>Register(...)</c> calls (in <c>RimTalkNarrativeCompat</c>,
    /// <c>RimTalkDebugWindowCompat</c>, <c>BubblesNarrativeCompat</c>) are the only mod-specific
    /// knowledge; a future mod with a modifier-branching toolbar icon needs only its own
    /// Register call, never a change here or in the reader that consumes it.
    /// </summary>
    internal static class ToolbarModifierRegistry
    {
        /// <summary>One modifier variant of a toolbar icon: a translation key (resolved by the caller, which already has a Verse reference), an optional availability gate, and the vehicle to run.</summary>
        internal readonly struct Variant
        {
            public readonly string LabelKey;
            public readonly Func<bool> Available;
            public readonly Action Activate;

            public Variant(string labelKey, Func<bool> available, Action activate)
            {
                LabelKey = labelKey;
                Available = available;
                Activate = activate;
            }
        }

        private static readonly Dictionary<string, List<Variant>> variantsByIcon = new Dictionary<string, List<Variant>>();

        /// <summary>
        /// Declares one modifier variant of the icon named <paramref name="iconId"/> (an
        /// arbitrary, stable per-mod key chosen by the registering compat — not a texture name,
        /// since two icons could share art). <paramref name="available"/> may be null for a
        /// variant that is always offered once its icon's own presence gate (checked by the
        /// reader) has passed.
        /// </summary>
        public static void Register(string iconId, string labelKey, Action activate, Func<bool> available = null)
        {
            if (string.IsNullOrEmpty(iconId) || string.IsNullOrEmpty(labelKey) || activate == null)
            {
                return;
            }
            if (!variantsByIcon.TryGetValue(iconId, out List<Variant> list))
            {
                list = new List<Variant>();
                variantsByIcon[iconId] = list;
            }
            list.Add(new Variant(labelKey, available, activate));
        }

        /// <summary>Every registered variant of <paramref name="iconId"/> whose own Available() (if any) currently passes, in registration order.</summary>
        public static IEnumerable<Variant> AvailableVariants(string iconId)
        {
            if (!variantsByIcon.TryGetValue(iconId, out List<Variant> list))
            {
                yield break;
            }
            foreach (Variant variant in list)
            {
                if (variant.Available == null || variant.Available())
                {
                    yield return variant;
                }
            }
        }
    }
}
