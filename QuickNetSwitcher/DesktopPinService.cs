#nullable enable
using System;
using System.Runtime.InteropServices;

namespace QuickNetSwitcher;

public static class DesktopPinService
{
    [DllImport("user32.dll")]
    private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll")]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
        uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private static IntPtr _workerW = IntPtr.Zero;

    private static IntPtr GetDesktopWorkerW()
    {
        var progman = FindWindow("Progman", null);
        if (progman == IntPtr.Zero) return IntPtr.Zero;

        // Send a message to Progman to spawn a WorkerW behind the desktop icons
        SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, 0x0000, 1000, out _);

        IntPtr workerW = IntPtr.Zero;
        EnumWindows((hWnd, _) =>
        {
            var shellView = FindWindowEx(hWnd, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (shellView != IntPtr.Zero)
            {
                workerW = FindWindowEx(IntPtr.Zero, hWnd, "WorkerW", null);
            }
            return true;
        }, IntPtr.Zero);

        return workerW;
    }

    public static void PinToDesktop(IntPtr windowHandle)
    {
        var workerW = GetDesktopWorkerW();
        if (workerW != IntPtr.Zero)
        {
            SetParent(windowHandle, workerW);
            _workerW = workerW;
        }
    }

    public static void UnpinFromDesktop(IntPtr windowHandle)
    {
        SetParent(windowHandle, IntPtr.Zero);
        _workerW = IntPtr.Zero;
    }
}
