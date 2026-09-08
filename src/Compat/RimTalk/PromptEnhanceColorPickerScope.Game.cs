using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Keyboard support for PromptEnhance's <c>RimTalkHealthEnhance.UI.Dialog_ColorPicker</c> —
    /// ten preset swatches drawn as raw <c>Widgets.DrawBoxSolid</c> under <c>ButtonInvisible</c>
    /// (no check texture), so the generic reader's adjacency-fusion
    /// inference cannot name them (a documented gap for exactly this shape — see
    /// <see cref="PromptEnhanceCompat"/>'s class remarks). Presented here as one list region of
    /// ten selectable rows, named via <see cref="ColorNameHelper.NamesForColors"/> (disambiguating
    /// same-named shades), each Activate invoking the mod's own <c>Action&lt;Color&gt;</c>
    /// callback then closing — mirroring a swatch click exactly (vehicle A). The RGB values are
    /// read from the dialog's own private static <c>PresetColors</c> array at attach time, so the
    /// announced swatch and the one actually applied can never diverge even across a mod update;
    /// the transcribed copy below only serves the field-missing case.
    /// </summary>
    public sealed class PromptEnhanceColorPickerScope : ScreenScope
    {
        // Transcribed from RimTalkHealthEnhance.UI.Dialog_ColorPicker's PresetColors as shipped
        // 2026-08; fallback only, used when the live field fails to reflect.
        private static readonly Color[] FallbackPresetColors =
        {
            new Color(0.2f, 0.8f, 0.2f),
            new Color(0.2f, 0.5f, 0.9f),
            new Color(0.9f, 0.7f, 0.2f),
            new Color(0.9f, 0.3f, 0.3f),
            new Color(0.7f, 0.3f, 0.9f),
            new Color(0.3f, 0.9f, 0.9f),
            new Color(0.9f, 0.5f, 0.2f),
            new Color(0.9f, 0.3f, 0.7f),
            new Color(0.5f, 0.5f, 0.5f),
            new Color(0.9f, 0.9f, 0.9f),
        };

        private readonly Window dialog;
        private readonly FieldInfo callbackField;
        private readonly Color[] presets;
        private readonly List<string> names;

        public PromptEnhanceColorPickerScope(Window dialog, FieldInfo callbackField)
        {
            this.dialog = dialog;
            this.callbackField = callbackField;
            FieldInfo presetsField = dialog.GetType().GetField(
                "PresetColors", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            presets = presetsField?.GetValue(null) as Color[] ?? FallbackPresetColors;
            names = ColorNameHelper.NamesForColors(new List<Color>(presets));
        }

        public override string Name => "prompt-enhance-color-picker";

        protected override int ContentRegionCount => 1;

        protected override string ContentRegionName(int region)
        {
            return "RimWorldAccess.Compat.PromptEnhance.ColorPickerRegionName".Translate();
        }

        protected override int ContentItemCount(int region)
        {
            return presets.Length;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription
            {
                Role = ElementRole.Button,
                Label = names[index],
            };
            return d;
        }

        protected override void ActivateContentItem(int region, int index)
        {
            if (index < 0 || index >= presets.Length)
            {
                return;
            }
            var callback = callbackField.GetValue(dialog) as Action<Color>;
            callback?.Invoke(presets[index]);
            dialog.Close();
        }

        /// <summary>Every swatch is the preset loop's own <c>ButtonInvisible</c>, drawn once per preset in preset order, and the dialog draws no other invisible button.</summary>
        protected internal override Rect FocusedContentRect()
        {
            ListModel region = Model.CurrentRegion;
            if (Model.RegionIndex != 0 || region == null || region.Index < 0 || region.Index >= presets.Length)
            {
                return default(Rect);
            }
            int capture = WidgetCapture.IndexOfKind(WidgetKind.InvisibleButton, region.Index);
            return capture < 0 ? default(Rect) : WidgetCapture.Items[capture].VisibleScreenRect;
        }

        protected override bool CaptureWindowButtons => false;

        protected override bool IncludeCapturedExtrasRegion => true;

        public override void OnFocus()
        {
            base.OnFocus();
            TolkHelper.SpeakData("RimWorldAccess.Compat.PromptEnhance.ColorPickerOpened".Translate());
        }
    }
}
