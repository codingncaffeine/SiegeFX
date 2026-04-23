namespace SiegeFX.Core.Tank;

public enum TankPriority : uint
{
    Factory   = 0x0000,
    Language  = 0x1000,
    Expansion = 0x2000,
    Patch     = 0x3000,
    User      = 0x4000,
}

public enum TankDataFormat : ushort
{
    Raw  = 0,
    Zlib = 1,
    Lzo  = 2,
}

[Flags]
public enum TankFlags : uint
{
    None                 = 0,
    NonRetail            = 1 << 0,
    AllowMultiplayerXfer = 1 << 1,
    ProtectedContent     = 1 << 2,
}

[Flags]
public enum TankFileFlags : ushort
{
    None    = 0,
    Invalid = 1 << 15,
}

public static class TankDataFormatExtensions
{
    public static bool IsCompressed(this TankDataFormat f) => f != TankDataFormat.Raw;
}
