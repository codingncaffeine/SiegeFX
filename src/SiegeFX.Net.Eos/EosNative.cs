using System.Reflection;
using System.Runtime.InteropServices;
using Epic.OnlineServices;
using SiegeFX.Core.Net;

namespace SiegeFX.Net.Eos;

/// <summary>Where the EOS SDK's native library comes from. Epic's bindings
/// import one library name per operating system (<see cref="Common.LIBRARY_NAME"/>,
/// fixed when the module is compiled), but Epic ships 64-bit ARM Linux as a
/// library of its own name, so one module serves both Linux machines only if
/// the import is pointed at the file for the processor it runs on. The library
/// is always the one beside this module, never another copy on the system.</summary>
static class EosNative
{
    /// <summary>Epic's library for 64-bit ARM Linux.</summary>
    public const string LinuxArm64Library = "libEOSSDK-LinuxArm64-Shipping.so";

    static int _installed;
    static readonly object _gate = new();
    // The runtime asks once per imported function; the library loads once.
    static IntPtr _handle;

    /// <summary>The library file this process needs: the ARM one on 64-bit ARM
    /// Linux, otherwise the one the bindings name (the Mac library holds both
    /// Mac processors).</summary>
    public static string FileName =>
        OperatingSystem.IsLinux() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64
            ? LinuxArm64Library : Common.LIBRARY_NAME;

    /// <summary>Route the bindings' imports through <see cref="Resolve"/>. Runs
    /// once, before the first call into the SDK.</summary>
    public static void Install()
    {
        if (Interlocked.Exchange(ref _installed, 1) != 0) return;
        NativeLibrary.SetDllImportResolver(typeof(EosNative).Assembly, Resolve);
    }

    static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != Common.LIBRARY_NAME) return IntPtr.Zero;
        lock (_gate)
        {
            if (_handle == IntPtr.Zero) _handle = Load(assembly);
            return _handle;
        }
    }

    static IntPtr Load(Assembly assembly)
    {
        string dir = Path.GetDirectoryName(assembly.Location) is { Length: > 0 } d ? d : AppContext.BaseDirectory;
        string path = Path.Combine(dir, FileName);
        if (NativeLibrary.TryLoad(path, out var handle))
        {
            NetLog.Info($"EOS native library: {FileName} ({RuntimeInformation.RuntimeIdentifier})");
            return handle;
        }
        NetLog.Error($"EOS native library {FileName} did not load from {dir} " +
                     $"({(File.Exists(path) ? "present, but not loadable on this machine" : "missing")})");
        return IntPtr.Zero;   // the runtime's own search then reports DllNotFoundException
    }
}
