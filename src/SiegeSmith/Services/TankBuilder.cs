using System;
using System.IO;
using SiegeFX.Core.Tank;

namespace SiegeSmith.Services;

/// <summary>Packages a folder of loose files into a DS1 tank (.dsres/.dsmap/.dsmod) via the
/// engine's <see cref="TankWriter"/>. Every file under the source folder becomes a '/'-rooted
/// resource path mirroring its relative location (TankWriter lowercases as DS1 requires).</summary>
public static class TankBuilder
{
    public static (int Files, long Bytes) BuildFromFolder(
        string sourceDir, string outPath,
        string title, string author, string description, TankPriority priority,
        DateTime utcBuildTime)
    {
        var writer = new TankWriter
        {
            Title = title,
            Author = author,
            Description = description,
            Priority = priority,
        };

        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(sourceDir, file).Replace('\\', '/');
            writer.Add("/" + rel, File.ReadAllBytes(file));
        }

        if (writer.FileCount == 0)
            throw new InvalidOperationException("The source folder contains no files.");

        var dir = Path.GetDirectoryName(outPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        writer.Write(outPath, utcBuildTime);
        return (writer.FileCount, new FileInfo(outPath).Length);
    }
}
