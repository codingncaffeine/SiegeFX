using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace SiegeSmith.Services;

/// <summary>Remembers each tool window's size, position and maximized state across
/// sessions (%APPDATA%\SiegeSmith\windows.json, keyed by window name). Restored
/// bounds are clamped to the current virtual screen so a window never reopens
/// off-monitor after a display change.</summary>
public static class WindowPlacement
{
    private sealed class Placement
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool Maximized { get; set; }
    }

    private static string StorePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SiegeSmith", "windows.json");

    /// <summary>Call once from the window's constructor (after InitializeComponent).
    /// Applies any saved placement and hooks Closing to persist the latest.</summary>
    public static void Track(Window window, string key)
    {
        try
        {
            if (Load().TryGetValue(key, out var p) && p.Width >= 200 && p.Height >= 150)
            {
                // clamp: at least a 60px grab handle must stay on the virtual screen
                double vx = SystemParameters.VirtualScreenLeft, vy = SystemParameters.VirtualScreenTop;
                double vw = SystemParameters.VirtualScreenWidth, vh = SystemParameters.VirtualScreenHeight;
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Width = Math.Min(p.Width, vw);
                window.Height = Math.Min(p.Height, vh);
                window.Left = Math.Clamp(p.Left, vx - p.Width + 60, vx + vw - 60);
                window.Top = Math.Clamp(p.Top, vy, vy + vh - 60);
                if (p.Maximized) window.WindowState = WindowState.Maximized;
            }
        }
        catch { /* placement is a convenience — never block startup */ }

        window.Closing += (_, _) =>
        {
            try
            {
                var all = Load();
                var b = window.WindowState == WindowState.Normal
                    ? new Rect(window.Left, window.Top, window.Width, window.Height)
                    : window.RestoreBounds; // remember the un-maximized bounds
                all[key] = new Placement
                {
                    Left = b.Left, Top = b.Top, Width = b.Width, Height = b.Height,
                    Maximized = window.WindowState == WindowState.Maximized,
                };
                Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
                File.WriteAllText(StorePath, JsonSerializer.Serialize(all));
            }
            catch { /* best effort */ }
        };
    }

    private static Dictionary<string, Placement> Load()
    {
        try
        {
            if (File.Exists(StorePath))
                return JsonSerializer.Deserialize<Dictionary<string, Placement>>(File.ReadAllText(StorePath)) ?? new();
        }
        catch { }
        return new Dictionary<string, Placement>();
    }
}
