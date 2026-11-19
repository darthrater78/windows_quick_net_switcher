#nullable enable
using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace QuickNetSwitcher;

// "Pin to desktop": the window stays a normal top-level window and is held at the
// bottom of the z-order, so every other window covers it.
//
// It used to be reparented (SetParent) into the desktop shell's WorkerW window. That
// put this elevated window inside a window owned by the unelevated Explorer process,
// joining the two threads' input. On some machines that froze both: the app took no
// input, and Explorer had to be restarted. Nothing here touches another process's
// windows.
public static class DesktopPinService
{
    // user32 is one of the KnownDLLs, which Windows always loads from System32, so this
    // is not open to a planted copy today. Pinned anyway, like the dwmapi import in
    // ThemeService, so none of this elevated process's imports rely on the search order.
    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPOS
    {
        public IntPtr hwnd;
        public IntPtr hwndInsertAfter;
        public int x;
        public int y;
        public int cx;
        public int cy;
        public uint flags;
    }

    private const int WM_WINDOWPOSCHANGING = 0x0046;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private static readonly IntPtr HWND_BOTTOM = new(1);

    private static HwndSource? _pinned;

    public static void PinToDesktop(IntPtr windowHandle)
    {
        var source = HwndSource.FromHwnd(windowHandle);
        if (source == null) return;

        if (!ReferenceEquals(_pinned, source))
        {
            _pinned?.RemoveHook(KeepAtBottom);
            source.AddHook(KeepAtBottom);
            _pinned = source;
        }

        SetWindowPos(windowHandle, HWND_BOTTOM, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    // Unpinning only stops holding the window down; the next activation raises it.
    public static void UnpinFromDesktop(IntPtr windowHandle)
    {
        _pinned?.RemoveHook(KeepAtBottom);
        _pinned = null;
    }

    // Any z-order change (activation, a click, Show) is turned into "go to the bottom".
    // Moves and resizes that leave the z-order alone pass through untouched.
    private static IntPtr KeepAtBottom(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_WINDOWPOSCHANGING || lParam == IntPtr.Zero) return IntPtr.Zero;

        var pos = Marshal.PtrToStructure<WINDOWPOS>(lParam);
        if ((pos.flags & SWP_NOZORDER) == 0 && pos.hwndInsertAfter != HWND_BOTTOM)
        {
            pos.hwndInsertAfter = HWND_BOTTOM;
            Marshal.StructureToPtr(pos, lParam, false);
        }

        return IntPtr.Zero;
    }
}
