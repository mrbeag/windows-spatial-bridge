namespace SpatialBridgeTray;

public sealed class AppInfoTests
{
    [Fact]
    public void DisplayName_IncludesReleaseVersion()
    {
        Assert.Equal("0.1.17", AppInfo.Version);
        Assert.Equal("Windows Spatial Bridge v0.1.17", AppInfo.DisplayName);
    }
}
