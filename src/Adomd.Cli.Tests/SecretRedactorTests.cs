public class SecretRedactorTests
{
    [Theory]
    [InlineData("Data Source=x;Password=hunter2;User ID=me", "Data Source=x;password=***;User ID=me")]
    [InlineData("Data Source=x;Pwd=hunter2", "Data Source=x;pwd=***")]
    [InlineData("Client Secret=abc123;Data Source=x", "client secret=***;Data Source=x")]
    [InlineData("Secret=abc123", "secret=***")]
    [InlineData("Access Token=abc.def.ghi", "access token=***")]
    public void Redact_MasksCredentialValues(string input, string expected)
    {
        Assert.Equal(expected, SecretRedactor.Redact(input));
    }

    [Fact]
    public void Redact_IsCaseInsensitiveForKeyName()
    {
        var result = SecretRedactor.Redact("PASSWORD=hunter2");

        Assert.DoesNotContain("hunter2", result);
    }

    [Fact]
    public void Redact_LeavesUnrelatedTextUnchanged()
    {
        const string message = "A connection cannot be made. Ensure that the server is running.";

        Assert.Equal(message, SecretRedactor.Redact(message));
    }

    [Fact]
    public void Redact_ReturnsNull_WhenInputIsNull()
    {
        Assert.Null(SecretRedactor.Redact(null));
    }

    [Fact]
    public void Redact_HandlesMultipleSecretsInSameMessage()
    {
        var result = SecretRedactor.Redact("Password=hunter2;Data Source=x;Client Secret=abc123");

        Assert.DoesNotContain("hunter2", result);
        Assert.DoesNotContain("abc123", result);
        Assert.Contains("Data Source=x", result);
    }
}
