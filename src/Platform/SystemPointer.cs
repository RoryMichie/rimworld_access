using UnityEngine;

namespace RimWorldAccess.Platform
{
    /// <summary>
    /// Moving the real OS mouse pointer. Unity cannot do this — Input.mousePosition is
    /// read-only — so every implementation is a platform-native call behind this
    /// interface, and a platform with no implementation degrades to <see cref="Unavailable"/>
    /// rather than misbehaving.
    ///
    /// Coordinates are the host OS's global pointer space: origin at the top-left of the
    /// primary display, y increasing downward, measured in the OS's own logical units
    /// (points on macOS, physical pixels on Windows and X11). <see cref="PointsToPixelsAt"/>
    /// converts those units to the render pixels Unity's Input.mousePosition speaks.
    /// </summary>
    public interface ISystemPointer
    {
        bool Available { get; }

        bool TryGetPosition(out double x, out double y);

        bool TryWarp(double x, double y);

        /// <summary>Render pixels per OS unit at the given global point (retina backing scale).</summary>
        double PointsToPixelsAt(double x, double y);
    }

    /// <summary>Selects the implementation for the running platform, once.</summary>
    public static class SystemPointer
    {
        private static ISystemPointer current;

        public static ISystemPointer Current
        {
            get
            {
                if (current == null)
                    current = Select();
                return current;
            }
        }

        /// <summary>
        /// Whether the game window holds OS focus — the one gate every pointer read and every
        /// warp asks. RimWorld runs with "run in background", so frames keep ticking while the
        /// player works in another application, and the pointer then belongs to that
        /// application: reading it announces where they are working, and warping it drags their
        /// pointer away (on macOS a warp during a Command+Tab cancels the switch outright).
        /// Unity leaves this false for a frame or two around a switch, which is the
        /// conservative answer here.
        /// </summary>
        public static bool HostFocused
        {
            get { return Application.isFocused; }
        }

        private static ISystemPointer Select()
        {
            switch (Application.platform)
            {
                case RuntimePlatform.OSXPlayer:
                case RuntimePlatform.OSXEditor:
                    return new MacSystemPointer();
                case RuntimePlatform.WindowsPlayer:
                case RuntimePlatform.WindowsEditor:
                    return new WindowsSystemPointer();
                case RuntimePlatform.LinuxPlayer:
                case RuntimePlatform.LinuxEditor:
                    // Wayland has no client pointer warping, and XWayland takes the X11 call
                    // without acting on it, so only a genuine X11 session gets an
                    // implementation; everything else keeps the offset announcement.
                    return X11SystemPointer.IsX11Session()
                        ? (ISystemPointer)new X11SystemPointer()
                        : new UnavailableSystemPointer();
                default:
                    return new UnavailableSystemPointer();
            }
        }
    }

    public sealed class UnavailableSystemPointer : ISystemPointer
    {
        public bool Available { get { return false; } }

        public bool TryGetPosition(out double x, out double y)
        {
            x = 0.0;
            y = 0.0;
            return false;
        }

        public bool TryWarp(double x, double y) { return false; }

        public double PointsToPixelsAt(double x, double y) { return 1.0; }
    }
}
