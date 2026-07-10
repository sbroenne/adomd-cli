public class SchemaSettingsTests
{
    [Fact]
    public void Validate_Fails_WhenRowsetValueIsNotAGuid()
    {
        var settings = new SchemaSettings
        {
            Server = "localhost",
            Limit = 200,
            ConnectTimeoutSeconds = 15,
            Rowsets = ["not-a-guid"]
        };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains("not-a-guid", result.Message);
    }

    [Fact]
    public void Validate_Succeeds_WhenRowsetValuesAreValidGuids()
    {
        var settings = new SchemaSettings
        {
            Server = "localhost",
            Limit = 200,
            ConnectTimeoutSeconds = 15,
            Rowsets = [Guid.NewGuid().ToString()]
        };

        Assert.True(settings.Validate().Successful);
    }

    [Fact]
    public void Validate_Succeeds_WhenNoRowsetsProvided()
    {
        var settings = new SchemaSettings { Server = "localhost", Limit = 200, ConnectTimeoutSeconds = 15 };

        Assert.True(settings.Validate().Successful);
    }
}
