using System.Runtime.InteropServices;

namespace SiegeFX.Runtime.Render;

/// <summary>Windows tells a program that the session is ending (logging out,
/// shutting down, restarting) with WM_QUERYENDSESSION and WM_ENDSESSION sent
/// to its windows, and may end the process as soon as WM_ENDSESSION returns.
/// GLFW leaves both to DefWindowProc, so the saves that run when the window
/// closes never ran. This hooks the game window's procedure and runs the save
/// step inside WM_ENDSESSION, on the thread that pumps the window: the game's
/// own, so it touches the game's state as a frame would.</summary>
internal sealed class WindowsSessionEnd
{
    const int GWLP_WNDPROC = -4;
    const uint WM_ENDSESSION = 0x0016;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    delegate nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);

    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
    static extern nint CallWindowProc(nint previous, nint hwnd, uint msg, nint wParam, nint lParam);

    readonly WndProc _proc;   // held: the window calls it for as long as it lives
    readonly Action _onEnd;
    nint _previous;
    bool _ran;

    WindowsSessionEnd(Action onEnd)
    {
        _onEnd = onEnd;
        _proc = Proc;
    }

    /// <summary>Null off Windows or without a window handle.</summary>
    public static WindowsSessionEnd? Hook(nint hwnd, Action onEnd)
    {
        if (!OperatingSystem.IsWindows() || hwnd == 0) return null;
        var hook = new WindowsSessionEnd(onEnd);
        hook._previous = SetWindowLongPtr(hwnd, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(hook._proc));
        if (hook._previous == 0)
        {
            Console.WriteLine($"[app] the end-of-session hook was not set (error {Marshal.GetLastPInvokeError()})");
            return null;
        }
        return hook;
    }

    nint Proc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        // wParam false: the session is not ending after all (another program
        // refused); nothing to do.
        if (msg == WM_ENDSESSION && wParam != 0 && !_ran)
        {
            _ran = true;
            try { _onEnd(); }
            catch (Exception ex) { Console.WriteLine($"[app] saving as the session ends: {ex.Message}"); }
            return 0;
        }
        return CallWindowProc(_previous, hwnd, msg, wParam, lParam);
    }
}
