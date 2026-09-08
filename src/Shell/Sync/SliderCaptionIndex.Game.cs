using System.Collections.Generic;
using UnityEngine;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Names for sliders whose caller drew the name as a separate widget, so
    /// <c>Widgets.HorizontalSlider</c> never sees it (Listing_TreeDefs.cs:188,
    /// EditWindow_DefEditor, Dialog_Slider, Colony Manager Redux's threshold
    /// row). The presentation layers already fuse those names onto the slider
    /// row; this republishes the finished fusion under vanilla's own drag
    /// identity — the <c>sliderDraggingID</c> hash
    /// <see cref="SliderDragSpeech.ControlId"/> recomputes — so the mouse-drag
    /// reader can name the control it is steering. Never a rect match: the
    /// hash is what vanilla itself steers the gesture by, and it costs nothing
    /// in coordinate space (PointerRouting's rule).
    ///
    /// Double-buffered rather than cleared per pass: fusion only finishes
    /// AFTER the window's draw, so every read during frame N wants the set
    /// frame N-1 finished, and several windows may each publish within one
    /// frame. The first publish of a new frame retires the previous frame's
    /// build buffer into the readable one; a readable set older than one frame
    /// is treated as absent, which is also what heals a window that stopped
    /// drawing.
    /// </summary>
    internal static class SliderCaptionIndex
    {
        internal struct Entry
        {
            public string Name;
            public bool CarriesValue;
        }

        private static Dictionary<int, Entry> readable = new Dictionary<int, Entry>();
        private static Dictionary<int, Entry> building = new Dictionary<int, Entry>();
        private static int readableFrame = -1;
        private static int buildingFrame = -1;

        /// <summary>Publishes one fused slider name. Call only from a live (non-detached, non-Layout) presentation rebuild.</summary>
        internal static void Publish(int controlId, string name, bool carriesValue)
        {
            if (controlId == 0 || string.IsNullOrEmpty(name))
            {
                return;
            }
            RollFrame();
            building[controlId] = new Entry { Name = name, CarriesValue = carriesValue };
        }

        internal static Entry Lookup(int controlId)
        {
            RollFrame();
            if (controlId == 0 || Time.frameCount - readableFrame > 1)
            {
                return default(Entry);
            }
            Entry entry;
            return readable.TryGetValue(controlId, out entry) ? entry : default(Entry);
        }

        // Readers roll the frame too, not just writers: within a frame the
        // reads all happen during the window's draw and the write only at the
        // end of it, so a reader-driven roll is what makes the very first
        // window drawn each frame see the previous frame's set.
        private static void RollFrame()
        {
            int frame = Time.frameCount;
            if (frame == buildingFrame)
            {
                return;
            }
            Dictionary<int, Entry> retired = readable;
            readable = building;
            readableFrame = buildingFrame;
            retired.Clear();
            building = retired;
            buildingFrame = frame;
        }
    }
}
