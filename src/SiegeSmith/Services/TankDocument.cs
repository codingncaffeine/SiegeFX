using System;
using System.Collections.Generic;
using SysPath = System.IO.Path;
using SiegeFX.Core.Tank;

namespace SiegeSmith.Services;

/// <summary>An opened resource tank — owns the <see cref="TankFile"/>/<see cref="TankReader"/>
/// pair and exposes browsing + extraction. The file is opened shared-read, so it never
/// contends with a running copy of the game.</summary>
public sealed class TankDocument : IDisposable
{
    private readonly TankFile _file;
    public TankReader Reader { get; }

    public string Path => _file.Path;
    public string Name => SysPath.GetFileName(_file.Path);
    public long SizeBytes => _file.SizeBytes;
    public TankHeader Header => _file.Header;

    public int FileCount => Reader.FileCount;
    public int DirCount => Reader.DirCount;
    public int InvalidFileCount => Reader.InvalidFileCount;

    private TankDocument(TankFile file, TankReader reader)
    {
        _file = file;
        Reader = reader;
    }

    public static TankDocument Open(string path)
    {
        var file = TankFile.Open(path);
        try { return new TankDocument(file, new TankReader(file)); }
        catch { file.Dispose(); throw; }
    }

    /// <summary>All file paths in the tank ('/'-rooted), sorted case-insensitively.</summary>
    public IReadOnlyList<string> ListFiles()
    {
        var list = new List<string>(Reader.ListFiles());
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
    }

    /// <summary>Extracts a single file to <paramref name="destPath"/>.</summary>
    public void Extract(string tankPath, string destPath) => Reader.ExtractToFile(tankPath, destPath);

    /// <summary>Extracts every file under <paramref name="prefix"/> (a '/'-rooted directory
    /// path, or "" for the whole tank) into <paramref name="destRoot"/>, recreating the tank's
    /// folder structure below the prefix. Returns the number of files written.</summary>
    public int ExtractTree(string prefix, string destRoot)
    {
        prefix = prefix.TrimEnd('/');
        int written = 0;
        foreach (var path in Reader.ListFiles())
        {
            if (prefix.Length > 0 &&
                !path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase)) continue;

            var rel = prefix.Length > 0 ? path[prefix.Length..].TrimStart('/') : path.TrimStart('/');
            var dest = SysPath.Combine(destRoot, rel.Replace('/', SysPath.DirectorySeparatorChar));
            Reader.ExtractToFile(path, dest);
            written++;
        }
        return written;
    }

    public void Dispose() => _file.Dispose();
}
