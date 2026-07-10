public class CommonSettingsTests
{
    private const string EnvVarName = "ADOMD_CONNECTION_STRING";

    [Fact]
    public void Validate_Fails_WhenNeitherServerNorConnectionStringNorEnvVarProvided()
    {
        using var _ = new EnvironmentVariableScope(EnvVarName, null);
        var settings = new CommonSettings { Limit = 200, ConnectTimeoutSeconds = 15 };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains("--server", result.Message);
    }

    [Fact]
    public void Validate_Succeeds_WhenServerProvided()
    {
        using var _ = new EnvironmentVariableScope(EnvVarName, null);
        var settings = new CommonSettings { Server = "localhost", Limit = 200, ConnectTimeoutSeconds = 15 };

        Assert.True(settings.Validate().Successful);
    }

    [Fact]
    public void Validate_Succeeds_WhenConnectionStringProvided()
    {
        using var _ = new EnvironmentVariableScope(EnvVarName, null);
        var settings = new CommonSettings { ConnectionString = "Data Source=localhost", Limit = 200, ConnectTimeoutSeconds = 15 };

        Assert.True(settings.Validate().Successful);
    }

    [Fact]
    public void Validate_Succeeds_WhenOnlyEnvironmentVariableProvided()
    {
        using var _ = new EnvironmentVariableScope(EnvVarName, "Data Source=localhost");
        var settings = new CommonSettings { Limit = 200, ConnectTimeoutSeconds = 15 };

        Assert.True(settings.Validate().Successful);
    }

    [Fact]
    public void Validate_Fails_WhenLimitIsNotPositive()
    {
        using var _ = new EnvironmentVariableScope(EnvVarName, null);
        var settings = new CommonSettings { Server = "localhost", Limit = 0, ConnectTimeoutSeconds = 15 };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains("--limit", result.Message);
    }

    [Fact]
    public void Validate_Fails_WhenConnectTimeoutIsNotPositive()
    {
        using var _ = new EnvironmentVariableScope(EnvVarName, null);
        var settings = new CommonSettings { Server = "localhost", Limit = 200, ConnectTimeoutSeconds = -1 };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains("--connect-timeout", result.Message);
    }

    [Fact]
    public void Validate_Fails_WhenRetriesIsNegative()
    {
        using var _ = new EnvironmentVariableScope(EnvVarName, null);
        var settings = new CommonSettings { Server = "localhost", Limit = 200, ConnectTimeoutSeconds = 15, Retries = -1 };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains("--retries", result.Message);
    }

    [Fact]
    public void Validate_Fails_WhenRetryDelayIsNegative()
    {
        using var _ = new EnvironmentVariableScope(EnvVarName, null);
        var settings = new CommonSettings { Server = "localhost", Limit = 200, ConnectTimeoutSeconds = 15, RetryDelayMilliseconds = -1 };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains("--retry-delay-ms", result.Message);
    }

    [Fact]
    public void Validate_Succeeds_WhenRetriesAndDelayAreZero()
    {
        using var _ = new EnvironmentVariableScope(EnvVarName, null);
        var settings = new CommonSettings { Server = "localhost", Limit = 200, ConnectTimeoutSeconds = 15, Retries = 0, RetryDelayMilliseconds = 0 };

        Assert.True(settings.Validate().Successful);
    }

    [Fact]
    public void ResolveConnectionString_PrefersExplicitOption_OverEnvironmentVariable()
    {
        using var _ = new EnvironmentVariableScope(EnvVarName, "Data Source=fromEnv");
        var settings = new CommonSettings { ConnectionString = "Data Source=fromOption" };

        Assert.Equal("Data Source=fromOption", settings.ResolveConnectionString());
    }

    [Fact]
    public void ResolveConnectionString_FallsBackToEnvironmentVariable_WhenOptionOmitted()
    {
        using var _ = new EnvironmentVariableScope(EnvVarName, "Data Source=fromEnv");
        var settings = new CommonSettings();

        Assert.Equal("Data Source=fromEnv", settings.ResolveConnectionString());
    }

    [Fact]
    public void ResolveConnectionString_ReturnsNull_WhenNeitherIsSet()
    {
        using var _ = new EnvironmentVariableScope(EnvVarName, null);
        var settings = new CommonSettings();

        Assert.Null(settings.ResolveConnectionString());
    }

    /// <summary>Sets an environment variable for the lifetime of a test and restores its prior value afterward,
    /// so tests don't leak state into each other or into the surrounding process.</summary>
    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _originalValue;

        public EnvironmentVariableScope(string name, string? value)
        {
            _name = name;
            _originalValue = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose() => Environment.SetEnvironmentVariable(_name, _originalValue);
    }
}
