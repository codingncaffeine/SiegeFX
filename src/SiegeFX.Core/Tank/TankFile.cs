using SiegeFX.Core.IO;

namespace SiegeFX.Core.Tank;

public sealed class TankFile : IDisposable
{
    public string Path { get; }
    public long   SizeBytes => _reader.Length;
    public TankHeader Header { get; }

    internal readonly SiegeBinaryReader _reader;

    private TankFile(string path, SiegeBinaryReader reader, TankHeader header)
    {
        Path = path;
        _reader = reader;
        Header = header;
    }

    public static TankFile Open(string path)
    {
        var stream = File.Open(path, new FileStreamOptions
        {
            Access = FileAccess.Read,
            Mode   = FileMode.Open,
            Share  = FileShare.Read,
        });

        var reader = new SiegeBinaryReader(stream);
        try
        {
            var header = TankHeader.Read(reader);
            header.Validate(reader.Length);
            return new TankFile(path, reader, header);
        }
        catch
        {
            reader.Dispose();
            throw;
        }
    }

    public void Dispose() => _reader.Dispose();
}
