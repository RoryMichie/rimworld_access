using System.Collections.Generic;
using RimWorldAccess.Shell;

namespace RimWorldAccess
{
    /// <summary>
    /// Position of one captured widget for row banding: its rect's left edge
    /// in the capture pass's GUI-group space (reading order within a row is
    /// only ever compared inside one row), plus the two data that decide row
    /// membership — the widget's screen-space top edge and the GUIClip context
    /// it was drawn under (see <see cref="CapturedRowBander"/>'s remarks).
    /// </summary>
    public struct BandGeom
    {
        public float X;

        /// <summary>
        /// The top edge in SCREEN space. Two widgets drawn in unrelated GUI
        /// groups can share a group-local top edge and still sit rows apart on
        /// screen; this is the coordinate that tells them apart.
        /// </summary>
        public float ScreenY;

        /// <summary>The GUIClip context this widget was drawn under, for context equality alongside the proximity test.</summary>
        public TipClip Clip;

        /// <summary>
        /// False when the capture recorded no usable clip context (GuiSpace's
        /// reflection bindings degraded, so every widget carries the same empty
        /// key). Banding then falls back to screen-space proximity alone.
        /// </summary>
        public bool HasClip;

        /// <summary>
        /// Local-only geometry: no clip context is known, so the group-local
        /// top edge stands in for the screen one used in the proximity test.
        /// </summary>
        public BandGeom(float x, float y)
            : this(x, y, default(TipClip), false)
        {
        }

        public BandGeom(float x, float screenY, TipClip clip, bool hasClip)
        {
            X = x;
            ScreenY = screenY;
            Clip = clip;
            HasClip = hasClip;
        }
    }

    /// <summary>
    /// Groups a captured widget stream into visual rows. Draw order is almost
    /// reading order, but surfaces draw a
    /// row's fragments in layout order, not left-to-right (the Gear tab draws
    /// each row's mass label before its name label), so fragments sharing a
    /// top edge must be re-read left-to-right.
    ///
    /// Banding is CONSECUTIVE-ONLY: a widget joins the current band when its
    /// top edge sits within tolerance of the band's first member, otherwise it
    /// starts a new band. Non-adjacent widgets never merge — a surface that
    /// redraws the same screen line later in the stream (a second column, a
    /// second panel) must not have both draws collapse into one row.
    /// Within a band, members are ordered by X (stable: draw order breaks
    /// ties).
    ///
    /// CONSECUTIVE IS NOT ENOUGH: two ADJACENT records can also sit in
    /// unrelated GUI groups whose local coordinates coincide — vanilla
    /// Page_ConfigureStartingPawns draws its "Selected" section header in the
    /// outer scroll view and each pawn's name label inside a doubly-nested
    /// per-row group, and both land on the same local Y ("Selected, O'Brien",
    /// one nonsense row). Membership therefore ALSO requires the band's clip
    /// CONTEXT (<see cref="BandGeom.Clip"/>) and screen-space proximity
    /// (<see cref="BandGeom.ScreenY"/>): equal-size per-row groups produce
    /// IDENTICAL clip keys (GUIClip.visibleRect is group-local, so every
    /// same-size group reads (0,0,w,h) at the same depth), so context equality
    /// alone cannot separate two rows — only the screen edge can, and it is
    /// comparable across every clip depth and UI scale (see GuiSpace). A geom
    /// carrying no clip context at all falls back to screen proximity alone.
    /// </summary>
    public static class CapturedRowBander
    {
        /// <summary>
        /// Top-edge tolerance for same-row membership. Fragments of one row
        /// share their top edge exactly or within a pixel or two; distinct
        /// rows sit a full row height (24+) apart.
        /// </summary>
        public const float DefaultYTolerance = 6f;

        /// <summary>
        /// Bands <paramref name="geoms"/> (in draw order) into visual rows.
        /// Returns bands in draw order; each band holds indices into
        /// <paramref name="geoms"/>, ordered left-to-right.
        /// </summary>
        public static List<List<int>> Band(IReadOnlyList<BandGeom> geoms, float yTolerance = DefaultYTolerance)
        {
            var bands = new List<List<int>>();
            if (geoms == null || geoms.Count == 0)
            {
                return bands;
            }

            List<int> current = null;
            BandGeom anchor = default(BandGeom);
            for (int i = 0; i < geoms.Count; i++)
            {
                BandGeom geom = geoms[i];
                if (current == null || !JoinsBand(anchor, geom, yTolerance))
                {
                    current = new List<int>();
                    bands.Add(current);
                    anchor = geom;
                }
                current.Add(i);
            }

            foreach (List<int> band in bands)
            {
                SortByXStable(band, geoms);
            }
            return bands;
        }

        /// <summary>
        /// Whether <paramref name="candidate"/> belongs to the band anchored on
        /// <paramref name="anchor"/>: same clip context, and top edges within
        /// tolerance in screen space, which is what actually proves "same
        /// visual row" (see the class remarks). With no clip context recorded,
        /// screen proximity is the only usable test; a geom built from local
        /// coordinates alone mirrors its local Y into ScreenY, so that degrades
        /// to exactly the original local-Y rule.
        /// </summary>
        private static bool JoinsBand(BandGeom anchor, BandGeom candidate, float yTolerance)
        {
            if (!anchor.HasClip || !candidate.HasClip)
            {
                return Within(anchor.ScreenY, candidate.ScreenY, yTolerance);
            }
            return anchor.Clip.Equals(candidate.Clip)
                && Within(anchor.ScreenY, candidate.ScreenY, yTolerance);
        }

        private static bool Within(float anchor, float value, float tolerance)
        {
            return value >= anchor - tolerance && value <= anchor + tolerance;
        }

        /// <summary>Stable insertion sort by X — bands are tiny (1-5 members) and ties must keep draw order.</summary>
        private static void SortByXStable(List<int> band, IReadOnlyList<BandGeom> geoms)
        {
            for (int i = 1; i < band.Count; i++)
            {
                int index = band[i];
                float x = geoms[index].X;
                int j = i - 1;
                while (j >= 0 && geoms[band[j]].X > x)
                {
                    band[j + 1] = band[j];
                    j--;
                }
                band[j + 1] = index;
            }
        }
    }
}
