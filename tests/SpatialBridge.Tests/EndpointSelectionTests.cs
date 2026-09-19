namespace SpatialBridgeTray;

public sealed class EndpointSelectionTests
{
    [Fact]
    public void PreferredRenamedEndpoint_WinsEvenBeforeFormatSetup()
    {
        AudioEndpoint preferred = Cable("preferred", "My 7.1 Input", nativeChannels: 2);
        AudioEndpoint other = Cable("other", "CABLE Input", nativeChannels: 16);

        AudioEndpoint? result = AudioConfigurator.FindGameEndpoint(
            [other, preferred], preferred.Id, _ => 2);

        Assert.Same(preferred, result);
    }

    [Fact]
    public void NativeMultichannelEndpoint_IsSelected()
    {
        AudioEndpoint stereo = Cable("stereo", "CABLE Input", nativeChannels: 2);
        AudioEndpoint multichannel = Cable("multi", "Renamed device", nativeChannels: 16);

        AudioEndpoint? result = AudioConfigurator.FindGameEndpoint(
            [stereo, multichannel], currentChannelCount: _ => 2);

        Assert.Same(multichannel, result);
    }

    [Fact]
    public void CurrentMultichannelFormat_IsSelectedWhenNativeMetadataIsUnavailable()
    {
        AudioEndpoint first = Cable("first", "First cable", nativeChannels: 0);
        AudioEndpoint second = Cable("second", "Second cable", nativeChannels: 0);

        AudioEndpoint? result = AudioConfigurator.FindGameEndpoint(
            [first, second], currentChannelCount: id => id == second.Id ? 8 : 2);

        Assert.Same(second, result);
    }

    [Fact]
    public void SingleInstalledCable_IsReturnedEvenWhenItStillReportsStereo()
    {
        AudioEndpoint cable = Cable("only", "Spatial Bridge", nativeChannels: 2);

        AudioEndpoint? result = AudioConfigurator.FindGameEndpoint(
            [cable], currentChannelCount: _ => 2);

        Assert.Same(cable, result);
    }

    [Fact]
    public void RenamedStereoCable_IsRecognizedByStableDeviceDescription()
    {
        var cable = new AudioEndpoint(
            "renamed", "Anything the user wants", DriverSection: "",
            NativeChannels: 2, DeviceName: "VB-Audio Virtual Cable", FormFactor: 2);

        AudioEndpoint? result = AudioConfigurator.FindGameEndpoint(
            [cable], currentChannelCount: _ => 2);

        Assert.True(cable.IsVbCable);
        Assert.Same(cable, result);
    }

    [Theory]
    [InlineData("VBCableInst.NTamd64")]
    [InlineData("vbcableinst.ntamd64")]
    [InlineData("OEM.VBCableInst.NTamd64")]
    public void DriverSectionVariants_AreRecognized(string driverSection)
    {
        var cable = new AudioEndpoint("cable", "Renamed", driverSection, 2,
            "VB-Audio Virtual Cable", FormFactor: 2);

        Assert.True(cable.IsVbCable);
    }

    [Fact]
    public void MultipleUnconfiguredCablesWithoutPreference_AreNotGuessed()
    {
        AudioEndpoint? result = AudioConfigurator.FindGameEndpoint(
            [Cable("one", "One", 2), Cable("two", "Two", 2)],
            currentChannelCount: _ => 2);

        Assert.Null(result);
    }

    [Fact]
    public void NonVbAudioDevices_AreNeverSelected()
    {
        var headphones = new AudioEndpoint("headphones", "Headphones", "UsbAudio", 8);

        AudioEndpoint? result = AudioConfigurator.FindGameEndpoint(
            [headphones], currentChannelCount: _ => 8);

        Assert.Null(result);
    }

    [Fact]
    public void FriendlyNameAlone_CannotImpersonateVbCable()
    {
        var fake = new AudioEndpoint("fake", "VB-CABLE", "UsbAudio", 8,
            "Unrelated USB Audio Device");

        Assert.False(fake.IsVbCable);
        Assert.Null(AudioConfigurator.FindGameEndpoint([fake], currentChannelCount: _ => 8));
    }

    [Fact]
    public void RenamedLineOutPin_IsSelectedAndSpeakersPinIsIgnored()
    {
        var lineOut = new AudioEndpoint("line", "Spatial Bridge",
            "VBCableInst.NTamd64", 16, "VB-Audio Virtual Cable", FormFactor: 2);
        var speakers = new AudioEndpoint("speakers", "CABLE Input",
            "VBCableInst.NTamd64", 2, "VB-Audio Virtual Cable", FormFactor: 1);

        AudioEndpoint? result = AudioConfigurator.FindGameEndpoint(
            [lineOut, speakers], preferredId: lineOut.Id, currentChannelCount: _ => 2);

        Assert.True(lineOut.IsVbCableLineOut);
        Assert.False(speakers.IsVbCableLineOut);
        Assert.Same(lineOut, result);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(0x10000001, false)]
    [InlineData(2, false)]
    [InlineData(4, false)]
    [InlineData(8, false)]
    public void EndpointState_MustBeExactlyActive(int state, bool active)
    {
        Assert.Equal(active, EndpointDiscovery.IsActiveDeviceState(state));
    }

    static AudioEndpoint Cable(string id, string name, int nativeChannels) =>
        new(id, name, "VBCableInst.NTamd64", nativeChannels,
            "VB-Audio Virtual Cable", FormFactor: 2);
}
