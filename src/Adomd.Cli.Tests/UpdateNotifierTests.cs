public class UpdateNotifierTests
{
    [Theory]
    [InlineData("v0.2.1", "0.2.1")]
    [InlineData("0.2.1", "0.2.1")]
    [InlineData("V1.0.0", "1.0.0")]
    [InlineData("v1.2.3-rc1", "1.2.3")]
    [InlineData("0.2.1+abc123", "0.2.1")]
    [InlineData("v2.0.0-beta+sha.9f8", "2.0.0")]
    [InlineData("  v3.4.5  ", "3.4.5")]
    public void TryParseVersion_ParsesTagsAndStripsMetadata(string raw, string expected)
    {
        Assert.True(UpdateNotifier.TryParseVersion(raw, out var version));
        Assert.Equal(Version.Parse(expected), version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-version")]
    [InlineData("v")]
    public void TryParseVersion_ReturnsFalse_ForInvalidInput(string? raw)
    {
        Assert.False(UpdateNotifier.TryParseVersion(raw, out var version));
        Assert.Null(version);
    }

    [Fact]
    public void IsNewer_True_WhenLatestGreater()
    {
        Assert.True(UpdateNotifier.IsNewer(Version.Parse("0.2.1"), Version.Parse("0.2.2")));
        Assert.True(UpdateNotifier.IsNewer(Version.Parse("0.2.1"), Version.Parse("1.0.0")));
    }

    [Fact]
    public void IsNewer_False_WhenLatestEqualOrOlder()
    {
        Assert.False(UpdateNotifier.IsNewer(Version.Parse("0.2.1"), Version.Parse("0.2.1")));
        Assert.False(UpdateNotifier.IsNewer(Version.Parse("0.2.2"), Version.Parse("0.2.1")));
    }

    [Theory]
    [InlineData("1", false, true)]
    [InlineData("true", false, true)]
    [InlineData("yes", false, true)]
    [InlineData(null, true, true)]
    [InlineData("", true, true)]
    public void IsDisabled_True_WhenOptedOutOrStderrRedirected(string? optOut, bool redirected, bool expected)
    {
        Assert.Equal(expected, UpdateNotifier.IsDisabled(optOut, redirected));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("False")]
    public void IsDisabled_False_WhenNotOptedOutAndStderrInteractive(string? optOut)
    {
        Assert.False(UpdateNotifier.IsDisabled(optOut, stderrRedirected: false));
    }
}
