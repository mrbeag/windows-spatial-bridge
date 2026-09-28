using System.Text.Json;

namespace SpatialBridgeTray;

public sealed class AudioLatencyTests
{
    [Theory]
    [InlineData(10, 480)]
    [InlineData(20, 960)]
    [InlineData(30, 1440)]
    public void SupportedProfilesProduceExpectedQueueAt48Khz(int milliseconds, int expectedFrames)
    {
        int frames = AudioLatency.QueueFrames(milliseconds, 48_000, captureFrames: 480, spatialFrames: 480);

        Assert.Equal(expectedFrames, frames);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(30)]
    public void PublishedProfilesAreSupported(int milliseconds)
    {
        Assert.True(AudioLatency.IsSupported(milliseconds));
        Assert.Contains(AudioLatency.Profiles, profile => profile.Milliseconds == milliseconds);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(40)]
    public void UnsupportedValuesFallBackToLowLatency(int milliseconds)
    {
        Assert.Equal(AudioLatency.DefaultMilliseconds, AudioLatency.Normalize(milliseconds));
    }

    [Fact]
    public void ExistingConfigurationWithoutLatencyKeepsTenMillisecondDefault()
    {
        const string json = """{"GameRenderId":"game","OutputId":"headphones"}""";

        BridgeConfiguration configuration = JsonSerializer.Deserialize<BridgeConfiguration>(json)!;

        Assert.Equal(10, configuration.LatencyMilliseconds);
    }

    [Fact]
    public void QueueRoundsUpToWholeSchedulingBlocks()
    {
        int frames = AudioLatency.QueueFrames(10, 44_100, captureFrames: 480, spatialFrames: 240);

        Assert.Equal(480, frames);
    }
}
