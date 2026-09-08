using System;
using System.Runtime.InteropServices;

namespace RimWorldAccess.Platform
{
    /// <summary>
    /// Linux pointer warping through Xlib. PLATFORM-SPECIFIC: every entry point below exists
    /// only where libX11 is loaded, so nothing in this file may be touched unless
    /// <see cref="SystemPointer.Current"/> selected it, which it does only for a session
    /// <see cref="IsX11Session"/> recognises.
    ///
    /// UNTESTED: no Linux machine was available, so this code has never executed. It is
    /// written to fail safe. Wayland has no client pointer warping at all, and under
    /// XWayland XWarpPointer is accepted and silently does nothing, so a warp that reported
    /// success without moving anything would rob the player of the direction-and-distance
    /// announcement that tells them where to push the mouse. Every warp is therefore
    /// verified by reading the pointer back with XQueryPointer, and anything short of a
    /// confirmed landing latches <see cref="Available"/> off for the session.
    ///
    /// Only the root window of our own connection is ever named, and coordinates stay inside
    /// the screen, so no call here can raise an X protocol error - which matters, because
    /// Xlib's default error handler terminates the process and installing our own would
    /// displace the player's (SDL's).
    ///
    /// Scale is 1: X11 root coordinates and the Unity player's render pixels are both
    /// physical device pixels. X11 fractional scaling is a toolkit convention, not a server
    /// one, so it reaches neither space.
    /// </summary>
    public sealed class X11SystemPointer : ISystemPointer
    {
        private const string LibX11 = "libX11.so.6";

        // A warp lands where the server puts it; a pixel of slack absorbs the caller's
        // rounding, and anything larger means the warp did not really happen.
        private const int LandingTolerance = 1;

        [DllImport(LibX11)]
        private static extern IntPtr XOpenDisplay(string displayName);

        [DllImport(LibX11)]
        private static extern IntPtr XDefaultRootWindow(IntPtr display);

        [DllImport(LibX11)]
        private static extern int XWarpPointer(IntPtr display, IntPtr srcWindow, IntPtr destWindow,
            int srcX, int srcY, uint srcWidth, uint srcHeight, int destX, int destY);

        [DllImport(LibX11)]
        private static extern int XSync(IntPtr display, bool discard);

        [DllImport(LibX11)]
        private static extern bool XQueryPointer(IntPtr display, IntPtr window,
            out IntPtr rootReturn, out IntPtr childReturn, out int rootX, out int rootY,
            out int winX, out int winY, out uint maskReturn);

        private IntPtr display;
        private IntPtr root;
        private bool broken;

        /// <summary>
        /// True only for a session where XWarpPointer can actually move the pointer. A
        /// Wayland compositor forbids it by design, and XWayland accepts it without acting,
        /// so both are refused here rather than discovered by a failed warp.
        /// </summary>
        public static bool IsX11Session()
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
                return false;
            string sessionType = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
            if (sessionType != null && sessionType.Trim().ToLowerInvariant() == "wayland")
                return false;
            return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"));
        }

        public bool Available { get { return !broken; } }

        public bool TryGetPosition(out double x, out double y)
        {
            x = 0.0;
            y = 0.0;
            if (!EnsureDisplay())
                return false;
            try
            {
                int rootX, rootY;
                if (!QueryPointer(out rootX, out rootY))
                    return false;
                x = rootX;
                y = rootY;
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
            if (!EnsureDisplay())
                return false;
            try
            {
                int targetX = (int)Math.Round(x);
                int targetY = (int)Math.Round(y);
                XWarpPointer(display, IntPtr.Zero, root, 0, 0, 0, 0, targetX, targetY);
                XSync(display, false);

                int landedX, landedY;
                if (!QueryPointer(out landedX, out landedY))
                    return false;
                if (Math.Abs(landedX - targetX) > LandingTolerance || Math.Abs(landedY - targetY) > LandingTolerance)
                {
                    Disable(new InvalidOperationException(
                        "XWarpPointer did not move the pointer (XWayland or a grabbing compositor)"));
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                Disable(ex);
                return false;
            }
        }

        /// <summary>Always 1: see the scale note in the class header.</summary>
        public double PointsToPixelsAt(double x, double y) { return 1.0; }

        // The connection stays open for the process's life; it is a handful of bytes and the
        // server reclaims it at exit, so there is nothing to close on a shutdown hook.
        private bool EnsureDisplay()
        {
            if (broken)
                return false;
            if (display != IntPtr.Zero)
                return true;
            try
            {
                display = XOpenDisplay(null);
                if (display == IntPtr.Zero)
                {
                    Disable(new InvalidOperationException("XOpenDisplay found no X server"));
                    return false;
                }
                root = XDefaultRootWindow(display);
                return true;
            }
            catch (Exception ex)
            {
                Disable(ex);
                return false;
            }
        }

        private bool QueryPointer(out int rootX, out int rootY)
        {
            IntPtr rootReturn, childReturn;
            int winX, winY;
            uint mask;
            // False means the pointer is on another screen of this display, which no warp of
            // ours can have caused; treat it as a miss, not as a broken implementation.
            return XQueryPointer(display, root, out rootReturn, out childReturn,
                out rootX, out rootY, out winX, out winY, out mask);
        }

        private void Disable(Exception ex)
        {
            broken = true;
            ModLogger.LimitedError("X11 pointer warp unavailable", ex);
        }
    }
}
