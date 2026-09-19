namespace SpatialBridgeTray;

public sealed class AudioSetupInspectionTests
{
    [Fact]
    public void MissingEndpoint_IsReportedAsMissingDriver()
    {
        AudioSetupStatus result = AudioSetupInspection.Evaluate(null, null, null);

        Assert.False(result.Ready);
        Assert.False(result.FormatsValid);
        Assert.True(result.VbCableMissing);
        Assert.StartsWith("VB-CABLE", result.Summary);
    }

    [Fact]
    public void InstalledStereoEndpoint_IsReportedAsNeedingSetup()
    {
        AudioEndpoint endpoint = Cable();

        AudioSetupStatus result = AudioSetupInspection.Evaluate(
            endpoint, new AudioFormatInfo(2, 48000, 32), endpoint.Id);

        Assert.False(result.Ready);
        Assert.False(result.FormatsValid);
        Assert.True(result.IsDefault);
        Assert.False(result.VbCableMissing);
        Assert.StartsWith("Audio setup required", result.Summary);
        Assert.DoesNotContain("not detected", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CorrectFormatButWrongDefault_IsOff()
    {
        AudioSetupStatus result = AudioSetupInspection.Evaluate(
            Cable(), new AudioFormatInfo(8, 48000, 32), "other");

        Assert.False(result.Ready);
        Assert.True(result.FormatsValid);
        Assert.False(result.IsDefault);
    }

    [Fact]
    public void CorrectFormatAndDefault_IsReady()
    {
        AudioEndpoint endpoint = Cable();

        AudioSetupStatus result = AudioSetupInspection.Evaluate(
            endpoint, new AudioFormatInfo(8, 48000, 32), endpoint.Id);

        Assert.True(result.Ready);
        Assert.True(result.FormatsValid);
        Assert.True(result.IsDefault);
    }

    [Fact]
    public void UnreadableFormat_IsNotReportedAsMissingDriver()
    {
        AudioSetupStatus result = AudioSetupInspection.Evaluate(Cable(), null, "cable");

        Assert.False(result.Ready);
        Assert.False(result.VbCableMissing);
        Assert.StartsWith("Audio device check failed", result.Summary);
        Assert.DoesNotContain("not detected", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    static AudioEndpoint Cable() => new("cable", "Spatial Bridge",
        "VBCableInst.NTamd64", 16, "VB-Audio Virtual Cable", FormFactor: 2);
}
