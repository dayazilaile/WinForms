using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;

namespace WinForms
{
    internal static class MouseCoordinateService
    {
        [StructLayout(LayoutKind.Sequential)]private struct NativePoint { public int X,Y; }
        [DllImport("user32.dll",SetLastError=true)]private static extern bool GetPhysicalCursorPos(out NativePoint point);
        [DllImport("user32.dll",SetLastError=true)]private static extern bool SetPhysicalCursorPos(int x,int y);
        [DllImport("user32.dll",SetLastError=true)]private static extern bool GetCursorPos(out NativePoint point);
        [DllImport("user32.dll")]private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        private sealed class PhysicalContext : IDisposable
        {
            private IntPtr previous;
            public bool Active{get{return previous!=IntPtr.Zero;}}
            public PhysicalContext(){try{previous=SetThreadDpiAwarenessContext(new IntPtr(-4));if(previous==IntPtr.Zero)previous=SetThreadDpiAwarenessContext(new IntPtr(-3));}catch(EntryPointNotFoundException){}}
            public void Dispose(){if(previous!=IntPtr.Zero){SetThreadDpiAwarenessContext(previous);previous=IntPtr.Zero;}}
        }
        public static Point CurrentPhysicalPosition()
        {
            using(var context=new PhysicalContext())
            {
                NativePoint point;if(GetPhysicalCursorPos(out point))return new Point(point.X,point.Y);
                int error=Marshal.GetLastWin32Error();
                // GetCursorPos is physical only while this thread is per-monitor DPI aware.
                if(context.Active&&GetCursorPos(out point))return new Point(point.X,point.Y);
                throw new Win32Exception(error,"无法读取鼠标物理坐标，未使用可能偏移的逻辑坐标。请在正常交互桌面重试。");
            }
        }
        public static void MovePhysicalPosition(Point target)
        {
            using(var context=new PhysicalContext())
            {
                if(!SetPhysicalCursorPos(target.X,target.Y))throw new Win32Exception(Marshal.GetLastWin32Error(),"鼠标测试移动失败。");
                Point actual=CurrentPhysicalPosition();if(Math.Abs((long)actual.X-target.X)>3||Math.Abs((long)actual.Y-target.Y)>3)throw new InvalidOperationException("鼠标未到达目标物理坐标，可能超出显示器或受到光标限制。实际 "+actual+"，目标 "+target);
            }
        }
    }
}
