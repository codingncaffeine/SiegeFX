using SiegeFX.Core.Tank;

namespace SiegeFX.Core.Tests;

public class TankRoundTripTests
{
    [Fact]
    public void WrittenTankReadsBackEveryFile()
    {
        var files = new Dictionary<string, byte[]>
        {
            ["/world/global/test.gas"] = "[a] { x = 1; }"u8.ToArray(),
            ["/world/maps/demo/main.gas"] = new byte[] { 0, 1, 2, 3, 255 },
            ["/art/bitmaps/empty.raw"] = [],
            // Name lengths around the dword-padding boundary.
            ["/abc/abcd/abcde/abcdefg"] = new byte[1000],
        };

        var writer = new TankWriter { Title = "unit test" };
        foreach (var (path, bytes) in files) writer.Add(path, bytes);

        var tmp = Path.Combine(Path.GetTempPath(), $"siegefx-test-{Guid.NewGuid():N}.dsres");
        try
        {
            writer.Write(tmp, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            using var tank = TankFile.Open(tmp);
            var reader = new TankReader(tank);
            Assert.Equal(files.Count, reader.FileCount);
            Assert.Equal(0, reader.InvalidFileCount);
            foreach (var (path, bytes) in files)
            {
                Assert.True(reader.TryGetFile(path, out _), $"missing {path}");
                Assert.Equal(bytes, reader.ExtractToMemory(path));
            }
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void AddLowercasesPathsAndRejectsRelativeOnes()
    {
        var writer = new TankWriter();
        writer.Add("/Art/Foo.RAW", [1]);
        writer.Add("/art/foo.raw", [2]);   // same resource, replaced
        Assert.Equal(1, writer.FileCount);

        Assert.Throws<ArgumentException>(() => writer.Add("art/foo.raw", [1]));
    }
}
