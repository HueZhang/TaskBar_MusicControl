using System.Runtime.InteropServices;

namespace TaskbarWidget.Interop;

internal static class NativeMethods
{
    // ---- Window management ----
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern nint FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern nint FindWindowEx(nint hWndParent, nint hWndChildAfter, string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    public static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    public static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll")]
    public static extern nint SetParent(nint hWndChild, nint hWndNewParent);

    [DllImport("user32.dll")]
    public static extern nint GetParent(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool MoveWindow(nint hWnd, int X, int Y, int nWidth, int nHeight, [MarshalAs(UnmanagedType.Bool)] bool bRepaint);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessage(nint hWnd, uint Msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ScreenToClient(nint hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ClientToScreen(nint hWnd, ref POINT lpPoint);

    // ---- Low level mouse hook ----
    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    public static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    public static extern nint GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetProcessDpiAwarenessContext(nint value);

    public static readonly nint DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new(-4);

    // ---- Network: main NIC via default route ----
    [DllImport("iphlpapi.dll")]
    public static extern uint GetIpForwardTable(nint pIpForwardTable, ref uint pdwSize, [MarshalAs(UnmanagedType.Bool)] bool bOrder);

    // ---- Acrylic blur behind (frosted glass) ----
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowCompositionAttribute(nint hwnd, ref WindowCompositionAttributeData data);

    public enum AccentState
    {
        ACCENT_DISABLED = 0,
        ACCENT_ENABLE_GRADIENT = 1,
        ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
        ACCENT_ENABLE_BLURBEHIND = 3,
        ACCENT_ENABLE_ACRYLICBLURBEHIND = 4
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AccentPolicy
    {
        public AccentState AccentState;
        public uint AccentFlags;
        public uint GradientColor;
        public uint AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WindowCompositionAttributeData
    {
        public uint Attribute;
        public nint Value;
        public int SizeOfData;
    }

    // ---- CPU / Memory ----
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetSystemTimes(out FILETIME lpIdleTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    // ---- Constants ----
    public const int ERROR_INSUFFICIENT_BUFFER = 122;
    public const int NO_ERROR = 0;

    public const int WH_MOUSE_LL = 14;
    public const int HC_ACTION = 0;
    public const uint WM_LBUTTONDOWN = 0x0201;
    public const uint WM_RBUTTONDOWN = 0x0204;
    public const uint WM_MBUTTONDOWN = 0x0207;
    public const uint WM_MOUSEWHEEL = 0x020A;

    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;
    public const uint WS_POPUP = 0x80000000;
    public const uint WS_CHILD = 0x40000000;
    public const uint WS_VISIBLE = 0x10000000;
    public const uint WS_CLIPSIBLINGS = 0x04000000;
    public const uint WS_EX_NOACTIVATE = 0x08000000;
    public const uint WS_EX_LAYERED = 0x00080000;

    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_FRAMECHANGED = 0x0020;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const uint SWP_HIDEWINDOW = 0x0080;
    public const uint SWP_ASYNCWINDOWPOS = 0x4000;

    public static readonly nint HWND_TOP = nint.Zero;
    public static readonly nint HWND_TOPMOST = new(-1);
    public static readonly nint HWND_NOTOPMOST = new(-2);

    // ---- Delegates & structures ----
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate nint LowLevelMouseProc(int nCode, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FILETIME
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;

        public readonly ulong Value => ((ulong)dwHighDateTime << 32) | dwLowDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_IPFORWARDROW
    {
        public uint dwForwardDest;
        public uint dwForwardMask;
        public uint dwForwardPolicy;
        public uint dwForwardNextHop;
        public uint dwForwardIfIndex;
        public uint dwForwardType;
        public uint dwForwardProto;
        public uint dwForwardAge;
        public uint dwForwardNextHopAS;
        public uint dwForwardMetric1;
        public uint dwForwardMetric2;
        public uint dwForwardMetric3;
        public uint dwForwardMetric4;
        public uint dwForwardMetric5;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_IPFORWARDTABLE
    {
        public uint NumEntries;
        public MIB_IPFORWARDROW Table;
    }

    /// <summary>
    /// Returns the interface index of the default route (0.0.0.0) with the lowest metric.
    /// This is the adapter Windows itself would use for internet traffic.
    /// </summary>
    public static uint GetPrimaryInterfaceIndex()
    {
        uint size = 0;
        uint ret = GetIpForwardTable(0, ref size, true);
        if (ret != ERROR_INSUFFICIENT_BUFFER || size == 0)
            return 0;

        nint buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            ret = GetIpForwardTable(buffer, ref size, true);
            if (ret != NO_ERROR)
                return 0;

            uint count = (uint)Marshal.ReadInt32(buffer);
            int rowOffset = Marshal.OffsetOf<MIB_IPFORWARDTABLE>(nameof(MIB_IPFORWARDTABLE.Table)).ToInt32();
            int rowSize = Marshal.SizeOf<MIB_IPFORWARDROW>();

            uint bestIndex = 0;
            uint bestMetric = uint.MaxValue;
            for (uint i = 0; i < count; i++)
            {
                nint rowPtr = IntPtr.Add(buffer, rowOffset + (int)i * rowSize);
                var row = Marshal.PtrToStructure<MIB_IPFORWARDROW>(rowPtr);
                if (row.dwForwardDest != 0) // only default routes
                    continue;
                if (row.dwForwardMetric1 < bestMetric)
                {
                    bestMetric = row.dwForwardMetric1;
                    bestIndex = row.dwForwardIfIndex;
                }
            }
            return bestIndex;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
