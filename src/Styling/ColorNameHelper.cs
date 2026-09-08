using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Spoken name for a raw <see cref="Color"/>: an exact <see cref="ColorDef"/> keeps the game's
    /// translated label; anything else is described from its actual HSV components ("dark muted
    /// orange") — never nearest-matched, since the sparse def vocabulary misnames freely (a pure
    /// dark orange came back "auburn"). Hue/saturation words depend only on hue and saturation:
    /// the <see cref="RimWorld.Dialog_ColorPickerBase"/> family saves exactly those two components
    /// onto the target's old brightness, so a picked swatch and the color it produces share every
    /// word but the lightness one.
    /// </summary>
    public static class ColorNameHelper
    {
        // Cached snapshot of every named color def, built once. ColorDefs are static game data.
        private static List<ColorDef> namedColors;

        private static List<ColorDef> NamedColors
        {
            get
            {
                if (namedColors == null)
                {
                    namedColors = DefDatabase<ColorDef>.AllDefs
                        .Where(c => !c.label.NullOrEmpty())
                        .ToList();
                }
                return namedColors;
            }
        }

        /// <summary>Spoken name for a color: an exact (indistinguishable) ColorDef's own label, otherwise a description of its HSV components.</summary>
        public static string NameForColor(Color color)
        {
            // Exact match first — this is how the game itself compares swatches.
            foreach (ColorDef def in NamedColors)
            {
                if (color.IndistinguishableFrom(def.color))
                    return def.LabelCap.ToString();
            }
            return DescriptiveName(color);
        }

        // Hue buckets, centered on their word so the picker family's 20-degree hue grid lands one
        // word per step (the two greens are the lone collision; NamesForColors numbers those).
        private static readonly (float upTo, string key)[] HueBuckets =
        {
            (10f, "Red"), (30f, "Orange"), (50f, "Amber"), (70f, "Yellow"), (90f, "Lime"),
            (150f, "Green"), (170f, "Teal"), (190f, "Cyan"), (210f, "Azure"), (230f, "Blue"),
            (250f, "Indigo"), (270f, "Violet"), (290f, "Purple"), (310f, "Magenta"),
            (330f, "Pink"), (350f, "Rose"), (360f, "Red"),
        };

        private static string DescriptiveName(Color color)
        {
            Color.RGBToHSV(color, out float h, out float s, out float v);
            if (s < 0.12f)
            {
                string grayKey;
                if (v < 0.15f) grayKey = "Black";
                else if (v >= 0.9f) grayKey = "White";
                else if (v < 0.4f) grayKey = "GrayDark";
                else if (v >= 0.7f) grayKey = "GrayLight";
                else grayKey = "Gray";
                return ("RimWorldAccess.Color." + grayKey).Translate().ToString().CapitalizeFirst();
            }

            string hueWord = HueWord(h * 360f);
            string band = v < 0.4f ? "Dark" : v >= 0.7f ? "Light" : "Mid";
            string strength = s >= 0.7f ? "Vivid" : s >= 0.35f ? "Muted" : "Pale";
            return ("RimWorldAccess.Color." + band + strength)
                .Translate(hueWord).ToString().CapitalizeFirst();
        }

        private static string HueWord(float degrees)
        {
            foreach ((float upTo, string key) in HueBuckets)
            {
                if (degrees < upTo)
                    return ("RimWorldAccess.Color.Hue." + key).Translate().ToString();
            }
            return "RimWorldAccess.Color.Hue.Red".Translate().ToString();
        }

        /// <summary>
        /// Names a whole swatch list, disambiguating colors that resolve to the same name. The game
        /// ships several distinct shades sharing one label (three "Purple"s, etc.); a screen reader
        /// can't tell them apart by name alone, so collisions get a trailing number ordered darkest
        /// to lightest ("Purple 1" = darkest). Unique names are left untouched.
        /// </summary>
        public static List<string> NamesForColors(List<Color> colors)
        {
            var names = colors.Select(NameForColor).ToList();

            var collisions = names
                .Select((name, index) => new { name, index })
                .GroupBy(x => x.name)
                .Where(g => g.Count() > 1);

            foreach (var group in collisions)
            {
                var orderedByLuminance = group
                    .OrderBy(x => Luminance(colors[x.index]))
                    .ToList();
                for (int i = 0; i < orderedByLuminance.Count; i++)
                    names[orderedByLuminance[i].index] = $"{orderedByLuminance[i].name} {i + 1}";
            }

            return names;
        }

        // Perceived brightness (Rec. 601). Used only to order same-named shades deterministically.
        private static float Luminance(Color c) => 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
    }
}
