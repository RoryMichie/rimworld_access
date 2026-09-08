using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Shared keyboard focus ring: the 2px highlight scopes paint over a real
    /// vanilla window's own widgets to show where the keyboard cursor sits.
    /// </summary>
    public static class FocusRing
    {
        public static readonly Color RingColor = new Color(0.45f, 0.78f, 1f);

        public static void Draw(Rect rect)
        {
            Color previous = GUI.color;
            GUI.color = RingColor;
            Widgets.DrawBox(rect, 2);
            GUI.color = previous;
        }
    }
}
