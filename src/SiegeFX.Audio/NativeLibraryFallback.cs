using System.Runtime.InteropServices;
using Silk.NET.Core.Loader;

namespace SiegeFX.Audio;

/// <summary>Lets Silk.NET find the native libraries the build bundles (GLFW,
/// OpenAL Soft) on any Linux .NET. Silk looks under runtimes/&lt;RID&gt;/native
/// with the runtime's own identifier, and a distribution-built .NET (Arch,
/// Fedora, Ubuntu's own packages) reports a distribution one such as
/// "arch-x64", so a framework-dependent build never looked in
/// runtimes/linux-x64/native and only ran where the distribution happened to
/// ship the library. The portable folder is added as the last candidate: a
/// system library still wins (the distribution's OpenAL with its PipeWire
/// backend, say) and the bundled copy is the fallback, as in a published
/// build where the bundled files sit beside the binary. Call before the
/// first window or audio device is created; idempotent.</summary>
public static class NativeLibraryFallback
{
    static bool _installed;

    public static void Install()
    {
        if (_installed || !OperatingSystem.IsLinux()) return;
        _installed = true;
        string arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "arm",
            _ => "",
        };
        if (arch.Length == 0 || PathResolver.Default is not DefaultPathResolver resolver) return;
        string dir = Path.Combine(AppContext.BaseDirectory, "runtimes", "linux-" + arch, "native");
        if (!Directory.Exists(dir)) return;
        resolver.Resolvers.Add(name =>
        {
            string path = Path.Combine(dir, name);
            return File.Exists(path) ? new[] { path } : Array.Empty<string>();
        });
    }
}
