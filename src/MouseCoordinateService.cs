using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ITDesktopToolkit
{
    internal static class MouseCoordinateService
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint { public int X, Y; }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetPhysicalCursorPos(out NativePoint point);

        public static Point CurrentPhysicalPosition()
        {
            NativePoint point;
            try
            {
                if (GetPhysicalCursorPos(out point)) return new Point(point.X, point.Y);
            }
            catch (EntryPointNotFoundException) { }
            // WinForms cursor position remains a fallback for older environments.
            return Cursor.Position;
        }
    }
}
