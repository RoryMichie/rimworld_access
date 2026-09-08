using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace RimWorldAccess.Platform
{
    /// <summary>
    /// Windows pointer warping through user32. PLATFORM-SPECIFIC: every entry point below
    /// exists only on Windows, so nothing in this file may be touched unless
    /// <see cref="SystemPointer.Current"/> selected it.
    ///
    /// UNTESTED: no Windows machine was available, so this code has never executed. It is
    /// written to fail safe — any exception or any native call returning false latches
    /// <see cref="Available"/> off for the rest of the session, and the caller then falls
    /// back to announcing the pointer's direction and distance from the keyboard cursor.
    /// A wrong assumption here costs the warp, never a mislanded pointer.
    ///
    /// DPI: the caller warps by a pure pixel DELTA (see PointerWarpState), so any scale
    /// factor that applies equally to Unity's client render pixels and to user32's cursor
    /// coordinates cancels out of the arithmetic and the ratio is 1. The two spaces cannot
    /// come apart on Windows, because DPI virtualization is a per-process property: a
    /// DPI-aware process gets physical pixels from GetCursorPos/SetCursorPos and renders
    /// into a client area measured in the same physical pixels, while a DPI-unaware one has
    /// BOTH its cursor coordinates and its client area virtualized to 96 dpi by the same
    /// factor. Either way the ratio is 1, which is why <see cref="PointsToPixelsAt"/>
    /// returns 1 and neither Screen.dpi nor GetDpiForWindow is consulted.
    ///
    /// What would falsify that: at a display scale other than 100%, a warp that moves the
    /// pointer by the requested distance times the scale factor (or divided by it) instead
    /// of by the requested distance. Symptom is the caller needing all of its corrective
    /// passes, or the pointer overshooting back and forth at 200%. The fix would be for
    /// PointsToPixelsAt to return the real ratio rather than 1.
    /// </summary>
    public sealed class WindowsSystemPointer : ISystemPointer
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetCursorPos(int X, int Y);

        private bool broken;

        public bool Available { get { return !broken; } }

        public bool TryGetPosition(out double x, out double y)
        {
            x = 0.0;
            y = 0.0;
            if (broken)
                return false;
            try
            {
                POINT p;
                if (!GetCursorPos(out p))
                {
                    Disable(new Win32Exception());
                    return false;
                }
                x = p.X;
                y = p.Y;
                return true;
            }
            catch (Exception ex)
            {
                Disable(ex);
                return false;
            }
        }

        public bool TryWarp(double x, double y)
        {
            if (broken)
                return false;
            try
            {
                if (SetCursorPos((int)Math.Round(x), (int)Math.Round(y)))
                    return true;
                // The documented failures are all permanent for this process (no desktop
                // access, blocked by UIPI), so a refusal retires the implementation.
                Disable(new Win32Exception());
                return false;
            }
            catch (Exception ex)
            {
                Disable(ex);
                return false;
            }
        }

        /// <summary>Always 1: see the DPI note in the class header.</summary>
        public double PointsToPixelsAt(double x, double y) { return 1.0; }

        private void Disable(Exception ex)
        {
            broken = true;
            ModLogger.LimitedError("user32 pointer warp unavailable", ex);
        }
    }
}
