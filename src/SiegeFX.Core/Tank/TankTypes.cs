namespace SiegeFX.Core.Tank;

public readonly record struct ProductVersion(uint V1, uint V2, uint V3)
{
    public override string ToString() => $"{V1}.{V2}.{V3}";
}

public readonly record struct TankSystemTime(
    ushort Year, ushort Month, ushort DayOfWeek, ushort Day,
    ushort Hour, ushort Minute, ushort Second, ushort Milliseconds)
{
    public DateTime ToDateTime() =>
        Year == 0 ? DateTime.MinValue
                  : new DateTime(Year, Math.Max((int)Month, 1), Math.Max((int)Day, 1),
                                 Hour, Minute, Second, Milliseconds, DateTimeKind.Utc);

    public override string ToString() => ToDateTime().ToString("u");
}

public readonly record struct TankFileTime(uint LowDateTime, uint HighDateTime)
{
    public long ToFileTime() => ((long)HighDateTime << 32) | LowDateTime;

    public DateTime ToDateTime()
    {
        var ft = ToFileTime();
        return ft <= 0 ? DateTime.MinValue : DateTime.FromFileTimeUtc(ft);
    }
}

public readonly record struct TankGuid(uint Data1, ushort Data2, ushort Data3, ulong Data4Packed)
{
    public Guid ToGuid()
    {
        Span<byte> d4 = stackalloc byte[8];
        BitConverter.TryWriteBytes(d4, Data4Packed);
        return new Guid((int)Data1, (short)Data2, (short)Data3,
            d4[0], d4[1], d4[2], d4[3], d4[4], d4[5], d4[6], d4[7]);
    }

    public override string ToString() => ToGuid().ToString();
}

public static class TankVersion
{
    public const uint Ds1 = (1 << 16) | (0 << 8) | 2; // 1.0.2
    public const uint Ds2 = (1 << 16) | (1 << 8) | 0; // 1.1.0

    public static string ToString(uint versionWord)
    {
        var major = (versionWord >> 16) & 0xFF;
        var minor = (versionWord >> 8) & 0xFF;
        var build = versionWord & 0xFF;
        return $"{major}.{minor}.{build}";
    }
}

public static class TankFourCCs
{
    public static readonly FourCC ProductId_DS1 = new('D', 'S', 'i', 'g');
    public static readonly FourCC ProductId_DS2 = new('D', 'S', 'g', '2');
    public static readonly FourCC TankId        = new('T', 'a', 'n', 'k');
    public static readonly FourCC CreatorIdGPG  = new('!', 'G', 'P', 'G');
    public static readonly FourCC CreatorIdUser = new('U', 'S', 'E', 'R');
}

