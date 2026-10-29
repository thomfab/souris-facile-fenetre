using System.Diagnostics;
using static SourisFacile.Native;

namespace SourisFacile;

// Another app's top-level window.
internal sealed class ManagedWindow(IntPtr handle)
{
    private static readonly uint OwnProcess = (uint)Environment.ProcessId;

    // The desktop, the taskbars and shell surfaces such as the Start menu.
    private static readonly HashSet<string> ShellClasses =
    [
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
        "Windows.UI.Core.CoreWindow", "XamlExplorerHostIslandWindow",
        "TopLevelWindowForOverflowXamlIsland", "NotifyIconOverflowWindow",
        "ForegroundStaging", "MultitaskingViewFrame",
    ];

    public IntPtr Handle { get; } = handle;

    // The window whose content is under `point`, if it can be managed.
    public static ManagedWindow? At(Point point)
    {
        var hit = WindowFromPoint(new POINT { X = (int)point.X, Y = (int)point.Y });
        if (hit == IntPtr.Zero) return null;
        var root = GetAncestor(hit, GA_ROOT);
        if (root == IntPtr.Zero || !IsManageable(root)) return null;
        var window = new ManagedWindow(root);
        // Fullscreen windows (games, videos, presentations) are left alone.
        return window.IsFullscreen ? null : window;
    }

    // A regular, visible window of another app, with a title bar or borders.
    private static bool IsManageable(IntPtr hwnd)
    {
        if (!IsWindowVisible(hwnd) || IsIconic(hwnd) || IsCloaked(hwnd) || IsHungAppWindow(hwnd)) return false;
        if (ProcessId(hwnd) == OwnProcess) return false;
        var style = Style(hwnd);
        if ((style & WS_CHILD) != 0) return false;
        if ((style & WS_CAPTION) != WS_CAPTION && (style & WS_THICKFRAME) == 0) return false;
        return !ShellClasses.Contains(ClassName(hwnd));
    }

    private bool IsFullscreen
    {
        get
        {
            if ((Style(Handle) & WS_CAPTION) == WS_CAPTION || Frame is not { } frame) return false;
            var display = Displays.At(new Point(frame.MidX, frame.MidY));
            return display != null && frame.MinX <= display.Bounds.MinX && frame.MinY <= display.Bounds.MinY
                && frame.MaxX >= display.Bounds.MaxX && frame.MaxY >= display.Bounds.MaxY;
        }
    }

    public bool Exists => IsWindow(Handle);
    public bool IsMaximized => IsZoomed(Handle);
    public bool CanMove => Exists;
    public bool CanResize => Exists && (Style(Handle) & WS_THICKFRAME) != 0;

    // The visible frame, without the invisible borders Windows adds around
    // most windows for resizing.
    public Rect? Frame
    {
        get
        {
            if (DwmGetWindowAttribute(Handle, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT bounds,
                    System.Runtime.InteropServices.Marshal.SizeOf<RECT>()) == 0)
            {
                return bounds.ToRect();
            }
            return GetWindowRect(Handle, out var rect) ? rect.ToRect() : null;
        }
    }

    // The invisible borders, as (left, top, right, bottom).
    private (double, double, double, double) Borders()
    {
        if (!GetWindowRect(Handle, out var outer) || Frame is not { } frame) return (0, 0, 0, 0);
        var o = outer.ToRect();
        return (frame.MinX - o.MinX, frame.MinY - o.MinY, o.MaxX - frame.MaxX, o.MaxY - frame.MaxY);
    }

    // Each returns false once the window is gone, or can't be moved (e.g.
    // it belongs to an app running as administrator).
    public bool SetOrigin(Point origin)
    {
        var (left, top, _, _) = Borders();
        return SetWindowPos(Handle, IntPtr.Zero, (int)(origin.X - left), (int)(origin.Y - top), 0, 0,
            SWP_NOSIZE | SWP_NOZORDER | SWP_NOOWNERZORDER | SWP_NOACTIVATE);
    }

    public bool SetFrame(Rect frame)
    {
        var (left, top, right, bottom) = Borders();
        return SetWindowPos(Handle, IntPtr.Zero, (int)(frame.X - left), (int)(frame.Y - top),
            (int)(frame.Width + left + right), (int)(frame.Height + top + bottom),
            SWP_NOZORDER | SWP_NOOWNERZORDER | SWP_NOACTIVATE);
    }

    // Leaves the maximized state, back to the size it had before.
    public void Restore() => ShowWindow(Handle, SW_SHOWNOACTIVATE);

    public void Raise()
    {
        // Windows only lets the app that got the last input bring a window
        // to the front: give it some.
        InputHooks.PressMaskKey();
        if (!SetForegroundWindow(Handle))
        {
            SetWindowPos(Handle, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
    }

    // The frames of the other visible, regular windows, frontmost first.
    public static List<(IntPtr Handle, Rect Frame)> OnScreen()
    {
        var windows = new List<(IntPtr, Rect)>();
        EnumWindows((hwnd, _) =>
        {
            if (IsManageable(hwnd) && (ExStyle(hwnd) & WS_EX_TOOLWINDOW) == 0
                && new ManagedWindow(hwnd).Frame is { Width: > 0, Height: > 0 } frame)
            {
                windows.Add((hwnd, frame));
            }
            return true;
        }, IntPtr.Zero);
        return windows;
    }

    public override bool Equals(object? obj) => obj is ManagedWindow other && other.Handle == Handle;
    public override int GetHashCode() => Handle.GetHashCode();
}

internal static class Shell
{
    public static void Open(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception) { }
    }
}
