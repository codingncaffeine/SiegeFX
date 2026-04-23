using SiegeFX.Core.IO;

namespace SiegeFX.Core.Tank;

public sealed class TankHeader
{
    public const uint InvalidOffset   = 0xFFFFFFFF;
    public const uint InvalidChecksum = 0x00000000;

    public FourCC         ProductId;
    public FourCC         TankId;
    public uint           HeaderVersion;
    public uint           DirSetOffset;
    public uint           FileSetOffset;
    public uint           IndexSize;
    public uint           DataOffset;
    public ProductVersion ProductVersion;
    public ProductVersion MinimumVersion;
    public TankPriority   Priority;
    public TankFlags      Flags;
    public FourCC         CreatorId;
    public TankGuid       Guid;
    public uint           IndexCrc32;
    public uint           DataCrc32;
    public TankSystemTime UtcBuildTime;
    public string         CopyrightText = string.Empty;
    public string         BuildText     = string.Empty;
    public string         TitleText     = string.Empty;
    public string         AuthorText    = string.Empty;
    public string         DescriptionText = string.Empty;

    public bool IsDs1 => ProductId == TankFourCCs.ProductId_DS1;
    public bool IsDs2 => ProductId == TankFourCCs.ProductId_DS2;

    public static TankHeader Read(SiegeBinaryReader r)
    {
        var h = new TankHeader
        {
            ProductId      = r.ReadFourCC(),
            TankId         = r.ReadFourCC(),
            HeaderVersion  = r.ReadU32(),
            DirSetOffset   = r.ReadU32(),
            FileSetOffset  = r.ReadU32(),
            IndexSize      = r.ReadU32(),
            DataOffset     = r.ReadU32(),
            ProductVersion = r.ReadProductVersion(),
            MinimumVersion = r.ReadProductVersion(),
            Priority       = (TankPriority)r.ReadU32(),
            Flags          = (TankFlags)r.ReadU32(),
            CreatorId      = r.ReadFourCC(),
            Guid           = r.ReadTankGuid(),
            IndexCrc32     = r.ReadU32(),
            DataCrc32      = r.ReadU32(),
            UtcBuildTime   = r.ReadSystemTime(),
            CopyrightText  = r.ReadFixedWideString(100),
            BuildText      = r.ReadFixedWideString(100),
            TitleText      = r.ReadFixedWideString(100),
            AuthorText     = r.ReadFixedWideString(40),
        };
        h.DescriptionText = r.ReadWideNString();
        return h;
    }

    public void Validate()
    {
        if (ProductId != TankFourCCs.ProductId_DS1 && ProductId != TankFourCCs.ProductId_DS2)
            throw new TankException($"Unknown Tank product id: '{ProductId}' (expected DSig or DSg2)");

        if (TankId != TankFourCCs.TankId)
            throw new TankException($"Tank id mismatch: '{TankId}' (expected 'Tank')");
    }
}

public sealed class TankException : Exception
{
    public TankException(string message) : base(message) { }
    public TankException(string message, Exception inner) : base(message, inner) { }
}
