using SiegeFX.Core.Assets;

namespace SiegeFX.Tests;

public class PsdImageTests
{
    // A 2x2 8-bit RGB PSD: header, three empty sections, then the composite.
    static byte[] Psd(ushort channels, ushort compression, byte[] imageData)
    {
        var ms = new MemoryStream();
        void U16(int v) { ms.WriteByte((byte)(v >> 8)); ms.WriteByte((byte)v); }
        void U32(int v) { U16(v >> 16); U16(v); }
        ms.Write("8BPS"u8);
        U16(1); ms.Write(new byte[6]);
        U16(channels); U32(2); U32(2); U16(8); U16(3);
        U32(0); U32(0); U32(0);
        U16(compression);
        ms.Write(imageData);
        return ms.ToArray();
    }

    [Fact]
    public void RawCompositeLoadsBgraBottomUp()
    {
        // Planar R, G, B; rows top-down: top row red, bottom row blue.
        byte[] planes = [255, 255, 0, 0,   0, 0, 0, 0,   0, 0, 255, 255];
        var img = RawImage.Load(Psd(3, 0, planes));
        Assert.Equal((2, 2, 1), (img.Width, img.Height, img.SurfaceCount));
        // Stored bottom-up like a .raw: first row is the blue one.
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, img.Pixels[0..4]);
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, img.Pixels[8..12]);
    }

    [Fact]
    public void PackBitsCompositeKeepsAlpha()
    {
        // Row byte counts (4 channels x 2 rows), then each row as one repeat run.
        var data = new List<byte>();
        for (int i = 0; i < 8; i++) { data.Add(0); data.Add(2); }
        foreach (byte v in new byte[] { 10, 10, 20, 20, 30, 30, 40, 50 })
        { data.Add(unchecked((byte)-1)); data.Add(v); }
        var img = RawImage.Load(Psd(4, 1, data.ToArray()));
        Assert.Equal(new byte[] { 30, 20, 10, 50 }, img.Pixels[0..4]);   // bottom row
        Assert.Equal(new byte[] { 30, 20, 10, 40 }, img.Pixels[8..12]);  // top row
    }
}
