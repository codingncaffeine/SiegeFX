using SiegeFX.Core.Save;

namespace SiegeFX.Core.Tests;

public class SaveStoreTests
{
    [Fact]
    public void SerializeRoundTripsAndStampsTheCurrentSchema()
    {
        var bytes = SaveStore.Serialize(new SaveFile());
        var back = SaveStore.Deserialize(bytes);

        Assert.NotNull(back);
        Assert.Equal(SaveFile.CurrentSchemaVersion, back.SchemaVersion);
        Assert.Equal(bytes, SaveStore.Serialize(back));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"SchemaVersion\": \"thirteen\"}")]
    public void DeserializeReturnsNullOnGarbage(string text)
    {
        // The MP late-join path feeds network bytes straight in; it must never throw.
        Assert.Null(SaveStore.Deserialize(System.Text.Encoding.UTF8.GetBytes(text)));
    }
}
