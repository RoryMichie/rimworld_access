using System;
using System.Runtime.InteropServices;

namespace RimWorldAccess.Platform
{
    /// <summary>
    /// macOS pointer warping through CoreGraphics. PLATFORM-SPECIFIC: every entry point
    /// below exists only on macOS, so nothing in this file may be touched unless
    /// <see cref="SystemPointer.Current"/> selected it.
    ///
    /// CoreGraphics' global space is top-left origin, y down, measured in POINTS, while
    /// Unity renders in backing pixels — hence <see cref="PointsToPixelsAt"/>, which reads
    /// the ratio out of the display mode rather than assuming 1 or 2.
    /// </summary>
    public sealed class MacSystemPointer : ISystemPointer
    {
        // The framework has no on-disk file since macOS 11 (it lives in the dyld shared
        // cache), but dyld still resolves it by this path, and so does DllImport.
        private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
        private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

        [StructLayout(LayoutKind.Sequential)]
        private struct CGPoint
        {
            public double X;
            public double Y;
        }

        [DllImport(CoreGraphics)]
        private static extern int CGWarpMouseCursorPosition(CGPoint newCursorPosition);

        // A warp makes the window server drop mouse deltas for about a quarter second;
        // re-associating cancels that, so the first physical move after a warp lands.
        [DllImport(CoreGraphics)]
        private static extern int CGAssociateMouseAndMouseCursorPosition(int connected);

        [DllImport(CoreGraphics)]
        private static extern IntPtr CGEventCreate(IntPtr source);

        [DllImport(CoreGraphics)]
        private static extern CGPoint CGEventGetLocation(IntPtr theEvent);

        [DllImport(CoreGraphics)]
        private static extern int CGGetDisplaysWithPoint(CGPoint point, uint maxDisplays, [Out] uint[] displays, out uint matchingDisplayCount);

        [DllImport(CoreGraphics)]
        private static extern uint CGMainDisplayID();

        [DllImport(CoreGraphics)]
        private static extern IntPtr CGDisplayCopyDisplayMode(uint display);

        [DllImport(CoreGraphics)]
        private static extern IntPtr CGDisplayModeGetPixelWidth(IntPtr mode);

        [DllImport(CoreGraphics)]
        private static extern IntPtr CGDisplayModeGetWidth(IntPtr mode);

        [DllImport(CoreGraphics)]
        private static extern void CGDisplayModeRelease(IntPtr mode);

        [DllImport(CoreFoundation)]
        private static extern void CFRelease(IntPtr cf);

        private bool broken;

        public bool Available { get { return !broken; } }

        public bool TryGetPosition(out double x, out double y)
        {
            x = 0.0;
            y = 0.0;
            if (broken)
                return false;
            IntPtr ev = IntPtr.Zero;
            try
            {
                ev = CGEventCreate(IntPtr.Zero);
                if (ev == IntPtr.Zero)
                    return false;
                CGPoint p = CGEventGetLocation(ev);
                x = p.X;
                y = p.Y;
                return true;
            }
            catch (Exception ex)
            {
                Disable(ex);
                return false;
            }
            finally
            {
                if (ev != IntPtr.Zero)
                    CFRelease(ev);
            }
        }

        public bool TryWarp(double x, double y)
        {
            if (broken)
                return false;
            try
            {
                CGPoint target;
                target.X = x;
                target.Y = y;
                if (CGWarpMouseCursorPosition(target) != 0)
                    return false;
                CGAssociateMouseAndMouseCursorPosition(1);
                return true;
            }
            catch (Exception ex)
            {
                Disable(ex);
                return false;
            }
        }

        public double PointsToPixelsAt(double x, double y)
        {
            if (broken)
                return 1.0;
            IntPtr mode = IntPtr.Zero;
            try
            {
                CGPoint p;
                p.X = x;
                p.Y = y;
                uint[] displays = new uint[1];
                uint count;
                uint display = CGGetDisplaysWithPoint(p, 1, displays, out count) == 0 && count > 0
                    ? displays[0]
                    : CGMainDisplayID();

                mode = CGDisplayCopyDisplayMode(display);
                if (mode == IntPtr.Zero)
                    return 1.0;
                long pixels = CGDisplayModeGetPixelWidth(mode).ToInt64();
                long points = CGDisplayModeGetWidth(mode).ToInt64();
                return points > 0 && pixels > 0 ? (double)pixels / points : 1.0;
            }
            catch (Exception ex)
            {
                Disable(ex);
                return 1.0;
            }
            finally
            {
                if (mode != IntPtr.Zero)
                    CGDisplayModeRelease(mode);
            }
        }

        private void Disable(Exception ex)
        {
            broken = true;
            ModLogger.LimitedError("CoreGraphics pointer warp unavailable", ex);
        }
    }
}
