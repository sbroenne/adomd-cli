public class QuerySettingsTests
{
    [Fact]
    public void Validate_Fails_WhenNeitherQueryNorQueryFileProvided()
    {
        var settings = new QuerySettings { Server = "localhost", Limit = 200, ConnectTimeoutSeconds = 15, QueryTimeoutSeconds = 120 };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains("--query", result.Message);
    }

    [Fact]
    public void Validate_Fails_WhenBothQueryAndQueryFileProvided()
    {
        var settings = new QuerySettings
        {
            Server = "localhost",
            Limit = 200,
            ConnectTimeoutSeconds = 15,
            QueryTimeoutSeconds = 120,
            Query = "SELECT 1",
            QueryFile = "query.mdx"
        };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains("only one", result.Message);
    }

    [Fact]
    public void Validate_Fails_WhenQueryTimeoutIsNotPositive()
    {
        var settings = new QuerySettings
        {
            Server = "localhost",
            Limit = 200,
            ConnectTimeoutSeconds = 15,
            QueryTimeoutSeconds = 0,
            Query = "SELECT 1"
        };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains("--query-timeout", result.Message);
    }

    [Fact]
    public void Validate_Succeeds_WhenQueryProvided()
    {
        var settings = new QuerySettings
        {
            Server = "localhost",
            Limit = 200,
            ConnectTimeoutSeconds = 15,
            QueryTimeoutSeconds = 120,
            Query = "SELECT 1"
        };

        Assert.True(settings.Validate().Successful);
    }

    [Fact]
    public void ResolveQuery_ReturnsQueryText_WhenProvided()
    {
        var settings = new QuerySettings { Query = "EVALUATE {1}" };

        Assert.Equal("EVALUATE {1}", settings.ResolveQuery());
    }

    [Fact]
    public void ResolveQuery_ReadsFromFile_WhenQueryFileProvided()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "EVALUATE {2}");
            var settings = new QuerySettings { QueryFile = path };

            Assert.Equal("EVALUATE {2}", settings.ResolveQuery());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ResolveQuery_Throws_WhenNeitherQueryNorQueryFileSet()
    {
        var settings = new QuerySettings();

        Assert.Throws<InvalidOperationException>(() => settings.ResolveQuery());
    }
}
