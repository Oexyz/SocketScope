namespace OeXYZ.SocketScope.Updater.Tests;

public sealed class ReleaseVersionTests
{
    [Theory]
    [InlineData("1.0.0", 1, 0, 0)]
    [InlineData("v12.34.56", 12, 34, 56)]
    public void TryParse_AcceptsStableThreePartVersions(
        string value,
        int major,
        int minor,
        int patch)
    {
        Assert.True(ReleaseVersion.TryParse(value, out ReleaseVersion version));
        Assert.Equal(new ReleaseVersion(major, minor, patch), version);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1.0")]
    [InlineData("1.0.0.0")]
    [InlineData("1.0.0-beta")]
    [InlineData("01.0.0")]
    [InlineData("1.-1.0")]
    public void TryParse_RejectsAmbiguousOrPrereleaseVersions(string value)
    {
        Assert.False(ReleaseVersion.TryParse(value, out _));
    }

    [Fact]
    public void Comparison_IsNumeric()
    {
        Assert.True(new ReleaseVersion(2, 0, 0) > new ReleaseVersion(1, 99, 99));
        Assert.True(new ReleaseVersion(1, 2, 1) > new ReleaseVersion(1, 2, 0));
    }

    [Theory]
    [InlineData("win-x64", "OeXYZ-SocketScope-v1.2.3-win-x64.exe")]
    [InlineData("win-arm64", "OeXYZ-SocketScope-v1.2.3-win-arm64.exe")]
    [InlineData("linux-x64", "OeXYZ-SocketScope-v1.2.3-linux-x64.tar.gz")]
    [InlineData("linux-arm64", "OeXYZ-SocketScope-v1.2.3-linux-arm64.tar.gz")]
    public void PackageName_MatchesSingleExecutableReleaseContract(string runtime, string expected)
    {
        Assert.Equal(expected, RuntimeAssetNames.GetPackageName(new ReleaseVersion(1, 2, 3), runtime));
    }
}
