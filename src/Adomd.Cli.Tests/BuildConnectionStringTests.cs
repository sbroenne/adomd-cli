[Collection("EnvironmentSensitive")]
public class BuildConnectionStringTests
{
    [Fact]
    public void ExplicitConnectionString_TakesPrecedence()
    {
        var settings = new CommonSettings
        {
            ConnectionString = "Data Source=explicit;Integrated Security=SSPI",
            Server = "ignored"
        };

        var result = AnalysisServices.BuildConnectionString(settings);

        Assert.Equal("Data Source=explicit;Integrated Security=SSPI", result);
    }

    [Fact]
    public void ComposesSspiString_FromServerAndTimeout()
    {
        var settings = new CommonSettings
        {
            Server = "FinHubAS",
            ConnectTimeoutSeconds = 30
        };

        var result = AnalysisServices.BuildConnectionString(settings);

        Assert.Equal("Data Source=FinHubAS;Integrated Security=SSPI;Connect Timeout=30", result);
    }

    [Fact]
    public void IncludesInitialCatalog_WhenCatalogProvided()
    {
        var settings = new CommonSettings
        {
            Server = "FinHubAS",
            Catalog = "KPILakeRevenue",
            ConnectTimeoutSeconds = 15
        };

        var result = AnalysisServices.BuildConnectionString(settings);

        Assert.Contains("Data Source=FinHubAS", result);
        Assert.Contains("Integrated Security=SSPI", result);
        Assert.Contains("Connect Timeout=15", result);
        Assert.Contains("Initial Catalog=KPILakeRevenue", result);
    }

    [Fact]
    public void OmitsInitialCatalog_WhenCatalogMissing()
    {
        var settings = new CommonSettings
        {
            Server = "FinHubAS",
            ConnectTimeoutSeconds = 15
        };

        var result = AnalysisServices.BuildConnectionString(settings);

        Assert.DoesNotContain("Initial Catalog", result);
    }
}
