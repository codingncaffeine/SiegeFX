namespace SiegeFX.Runtime.Render;

/// <summary>The game window's desktop identity on Linux, which taskbars and
/// docks use to match the window to siegefx.desktop (its name and icon);
/// without it the window gets a generic icon. On Wayland that is the app id
/// "siegefx", which GLFW 3.4 takes as a window hint before the window is
/// created (Silk.NET 2.23 has no enum member for it, so it goes by GLFW's
/// number). On X11 it is WM_CLASS ("siegefx", "SiegeFX"): the instance hint
/// is ours, and Silk sets the class from the window title itself, which the
/// desktop entry's StartupWMClass names. Hints are global state and Silk
/// does not reset them, so setting them before Window.Create is enough.</summary>
internal static class WindowIdentity
{
    /// <summary>Matches the desktop entry's file name (siegefx.desktop).</summary>
    public const string AppId = "siegefx";

    const int GlfwWaylandAppId = 0x00026001; // GLFW_WAYLAND_APP_ID, glfw3.h (3.4)

    public static void ApplyHints()
    {
        if (!OperatingSystem.IsLinux()) return;
        try
        {
            var glfw = Silk.NET.GLFW.GlfwProvider.GLFW.Value;
            glfw.WindowHintString(GlfwWaylandAppId, AppId);
            glfw.WindowHintString((int)Silk.NET.GLFW.WindowHintString.X11InstanceName, AppId);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[window] desktop identity hints not applied: {ex.Message}");
        }
    }
}
