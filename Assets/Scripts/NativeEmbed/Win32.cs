// Win32 API 包裝：給 EmbeddedAppHost 用來把外部 exe 視窗嵌進 Unity。
// 只在 Windows (Editor / Standalone) 下編譯。
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace MIRDC.NativeEmbed
{
    internal static class Win32
    {
        public const int GWL_STYLE = -16;
        public const int GWL_EXSTYLE = -20;
        public const int GWLP_HWNDPARENT = -8; // 對 top-level 視窗而言是「擁有者 (owner)」

        public const long WS_CHILD        = 0x40000000L;
        public const long WS_POPUP        = 0x80000000L;
        public const long WS_CAPTION      = 0x00C00000L;
        public const long WS_THICKFRAME   = 0x00040000L;
        public const long WS_SYSMENU      = 0x00080000L;
        public const long WS_MINIMIZEBOX  = 0x00020000L;
        public const long WS_MAXIMIZEBOX  = 0x00010000L;
        public const long WS_VISIBLE      = 0x10000000L;
        public const long WS_CLIPCHILDREN = 0x02000000L;
        public const long WS_CLIPSIBLINGS = 0x04000000L;

        public const long WS_EX_DLGMODALFRAME = 0x00000001L;
        public const long WS_EX_TOOLWINDOW    = 0x00000080L;
        public const long WS_EX_WINDOWEDGE    = 0x00000100L;
        public const long WS_EX_CLIENTEDGE    = 0x00000200L;
        public const long WS_EX_STATICEDGE    = 0x00020000L;
        public const long WS_EX_APPWINDOW     = 0x00040000L;

        public const uint SWP_NOSIZE       = 0x0001;
        public const uint SWP_NOMOVE       = 0x0002;
        public const uint SWP_NOZORDER     = 0x0004;
        public const uint SWP_NOACTIVATE   = 0x0010;
        public const uint SWP_FRAMECHANGED = 0x0020;
        public const uint SWP_SHOWWINDOW   = 0x0040;
        public const uint SWP_NOOWNERZORDER = 0x0200;
        public const uint SWP_ASYNCWINDOWPOS = 0x4000;

        public static readonly IntPtr HWND_TOP = IntPtr.Zero;

        public const int SW_HIDE   = 0;
        public const int SW_SHOW   = 5;
        public const int SW_SHOWNA = 8;

        public const uint WM_CLOSE = 0x0010;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left, Top, Right, Bottom;
            public int Width => Right - Left;
            public int Height => Bottom - Top;
            public override string ToString() => $"({Left},{Top}) {Width}x{Height}";
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; }

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int maxCount);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);
        [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetParent(IntPtr child, IntPtr newParent);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
        [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr hWnd, ref POINT pt);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr64(IntPtr h, int index);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]    static extern int    GetWindowLong32(IntPtr h, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] static extern IntPtr SetWindowLongPtr64(IntPtr h, int index, IntPtr value);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]    static extern int    SetWindowLong32(IntPtr h, int index, int value);

        public static long GetWindowLong(IntPtr h, int index)
            => IntPtr.Size == 8 ? GetWindowLongPtr64(h, index).ToInt64() : GetWindowLong32(h, index);

        public static void SetWindowLong(IntPtr h, int index, long value)
        {
            if (IntPtr.Size == 8) SetWindowLongPtr64(h, index, new IntPtr(value));
            else SetWindowLong32(h, index, unchecked((int)value));
        }

        // ---- 系統閒置時間（包含在嵌入的 exe / 網頁裡的觸控與鍵盤操作）----
        [StructLayout(LayoutKind.Sequential)]
        struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
        [DllImport("kernel32.dll")] static extern uint GetTickCount();

        /// <summary>距離最後一次觸控 / 滑鼠 / 鍵盤輸入的秒數（整台電腦）。</summary>
        public static float SystemIdleSeconds()
        {
            var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO)) };
            if (!GetLastInputInfo(ref info)) return 0f;
            return unchecked(GetTickCount() - info.dwTime) / 1000f;
        }

        public static string ClassOf(IntPtr h)
        {
            var sb = new StringBuilder(256);
            GetClassName(h, sb, sb.Capacity);
            return sb.ToString();
        }

        // ---- 依 PID 找 top-level 視窗 (IL2CPP 安全：callback 為 static) ----
        static readonly List<IntPtr> s_found = new List<IntPtr>();
        static uint s_findPid;

        [AOT.MonoPInvokeCallback(typeof(EnumWindowsProc))]
        static bool CollectByPid(IntPtr hWnd, IntPtr lParam)
        {
            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == s_findPid) s_found.Add(hWnd);
            return true;
        }

        /// <summary>
        /// 找某個 process 的主視窗。有指定 className 時優先比對 class；
        /// 否則取「可見、無 owner、面積最大」的 top-level 視窗。
        /// </summary>
        public static IntPtr FindMainWindow(int pid, string className = null, bool requireVisible = true)
        {
            s_found.Clear();
            s_findPid = (uint)pid;
            EnumWindows(CollectByPid, IntPtr.Zero);

            IntPtr best = IntPtr.Zero;
            long bestArea = -1;
            foreach (var h in s_found)
            {
                if (!string.IsNullOrEmpty(className))
                {
                    if (ClassOf(h) == className) return h;
                    continue;
                }
                if (requireVisible && !IsWindowVisible(h)) continue;
                if (GetWindow(h, 4 /*GW_OWNER*/) != IntPtr.Zero) continue;
                GetWindowRect(h, out RECT r);
                long area = (long)r.Width * r.Height;
                if (area > bestArea) { bestArea = area; best = h; }
            }
            s_found.Clear();
            return best;
        }
    }
}
#endif
