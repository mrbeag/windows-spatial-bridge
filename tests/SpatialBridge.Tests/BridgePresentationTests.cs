namespace SpatialBridgeTray;

public sealed class BridgePresentationTests
{
    const string SpatialAvailable = "Available — static mask 0xFFFF, 128 dynamic objects";

    [Fact]
    public void MissingDriver_IsRedAndOffersOfficialDownload()
    {
        var setup = new AudioSetupStatus(false,
            "VB-CABLE 16-channel driver not detected — install the VB-CABLE Driver Pack.",
            VbCableMissing: true);

        BridgePresentation result = BridgePresentationResolver.Resolve(
            "Audio setup required", setup, "Not checked", running: false);

        Assert.Equal(BridgeIndicator.Error, result.Indicator);
        Assert.Equal("VB-CABLE not detected", result.Title);
        Assert.Equal(ContextAction.GetVbCable, result.Action);
        Assert.Equal("Get VB-CABLE", result.ActionText);
    }

    [Fact]
    public void MissingDriverDecision_DoesNotDependOnUserFacingSummaryText()
    {
        var setup = new AudioSetupStatus(false, "Localized or revised wording",
            VbCableMissing: true);

        BridgePresentation result = BridgePresentationResolver.Resolve(
            "Audio setup required", setup, "Not checked", running: false);

        Assert.Equal(ContextAction.GetVbCable, result.Action);
        Assert.Equal("VB-CABLE not detected", result.Title);
    }

    [Fact]
    public void SummaryMentioningVbCable_DoesNotMeanDriverIsMissing()
    {
        var setup = new AudioSetupStatus(false,
            "VB-CABLE is installed but requires 7.1 setup.",
            FormatsValid: false, IsDefault: true, VbCableMissing: false);

        BridgePresentation result = BridgePresentationResolver.Resolve(
            "Audio setup required", setup, SpatialAvailable, running: false);

        Assert.Equal(ContextAction.SetupAudio, result.Action);
        Assert.Equal("Audio setup required", result.Title);
    }

    [Fact]
    public void InstalledButWrongFormat_IsSetupNotMissingDriver()
    {
        var setup = new AudioSetupStatus(false,
            "Audio setup required — game endpoint is 2ch/48 kHz/32-bit.",
            FormatsValid: false, IsDefault: true);

        BridgePresentation result = BridgePresentationResolver.Resolve(
            "Audio setup required", setup, SpatialAvailable, running: false);

        Assert.Equal(BridgeIndicator.Error, result.Indicator);
        Assert.Equal("Audio setup required", result.Title);
        Assert.Equal("Set up VB-CABLE for 7.1.", result.Detail);
        Assert.Equal(ContextAction.SetupAudio, result.Action);
        Assert.Equal("Set up audio", result.ActionText);
    }

    [Fact]
    public void ValidButNotDefault_IsOffAndOffersEnable()
    {
        var setup = new AudioSetupStatus(false,
            "Audio setup required — game endpoint is not the default playback device.",
            FormatsValid: true, IsDefault: false);

        BridgePresentation result = BridgePresentationResolver.Resolve(
            "Ready → Headphones", setup, SpatialAvailable, running: true);

        Assert.Equal(BridgeIndicator.Off, result.Indicator);
        Assert.Equal("Off", result.Title);
        Assert.Equal("Not set as default audio source.", result.Detail);
        Assert.Equal(ContextAction.SetupAudio, result.Action);
        Assert.Equal("Enable", result.ActionText);
        Assert.Equal("Off — not default audio source", result.TrayStatus);
    }

    [Fact]
    public void SpatialSoundDisabled_IsRedAndOpensSoundSettings()
    {
        var setup = ReadySetup();

        BridgePresentation result = BridgePresentationResolver.Resolve(
            "Error: enable Spatial Sound on the selected headphones",
            setup, "Unavailable — endpoint has no spatial objects", running: false);

        Assert.Equal(BridgeIndicator.Error, result.Indicator);
        Assert.Equal("Spatial sound is off", result.Title);
        Assert.Equal(ContextAction.OpenSoundSettings, result.Action);
        Assert.Equal("Sound settings", result.ActionText);
    }

    [Theory]
    [InlineData("Starting → Headphones", "Ready", "Connecting audio…")]
    [InlineData("Ready → Headphones", "Ready", "Waiting for game audio")]
    [InlineData("Active → Headphones", "Active", "Spatial audio live")]
    public void RunningStates_MapToExpectedIndicator(string status, string indicator, string title)
    {
        BridgePresentation result = BridgePresentationResolver.Resolve(
            status, ReadySetup(), SpatialAvailable, running: true);

        Assert.Equal(indicator, result.Indicator.ToString());
        Assert.Equal(title, result.Title);
        Assert.Equal(ContextAction.None, result.Action);
    }

    [Fact]
    public void UnexpectedStoppedBridge_IsRed()
    {
        BridgePresentation result = BridgePresentationResolver.Resolve(
            "Stopped", ReadySetup(), SpatialAvailable, running: false);

        Assert.Equal(BridgeIndicator.Error, result.Indicator);
        Assert.Equal("Bridge stopped", result.Title);
    }

    public static IEnumerable<object[]> FullStateMatrix =>
    [
        [false, false, false, true, "Not checked", false,
            "VB-CABLE not detected", "GetVbCable", "Error"],
        [false, false, true, false, SpatialAvailable, false,
            "Audio setup required", "SetupAudio", "Error"],
        [false, true, false, false, SpatialAvailable, false,
            "Off", "SetupAudio", "Off"],
        [true, true, true, false, "Unavailable — disabled", false,
            "Spatial sound is off", "OpenSoundSettings", "Error"],
        [true, true, true, false, SpatialAvailable, true,
            "Waiting for game audio", "None", "Ready"],
    ];

    [Theory]
    [MemberData(nameof(FullStateMatrix))]
    public void SetupAndSpatialCombinations_ResolveConsistently(
        bool ready,
        bool formatsValid,
        bool isDefault,
        bool vbCableMissing,
        string spatial,
        bool running,
        string expectedTitle,
        string expectedAction,
        string expectedIndicator)
    {
        var setup = new AudioSetupStatus(ready, "matrix state", formatsValid,
            isDefault, vbCableMissing);
        string status = running ? "Ready → Headphones" : "Stopped";

        BridgePresentation result = BridgePresentationResolver.Resolve(
            status, setup, spatial, running);

        Assert.Equal(expectedTitle, result.Title);
        Assert.Equal(expectedAction, result.Action.ToString());
        Assert.Equal(expectedIndicator, result.Indicator.ToString());
    }

    [Fact]
    public void EveryCanonicalFlowCombination_UsesSafetyFirstPrecedence()
    {
        string[] statuses =
        [
            "Stopped",
            "Starting → Headphones",
            "Ready → Headphones",
            "Active → Headphones",
            "Error: stream failed"
        ];

        foreach (bool cableMissing in Both())
        foreach (bool formatsValid in Both())
        foreach (bool isDefault in Both())
        foreach (bool spatialAvailable in Both())
        foreach (bool running in Both())
        foreach (string status in statuses)
        {
            bool ready = !cableMissing && formatsValid && isDefault;
            var setup = new AudioSetupStatus(ready, "matrix",
                formatsValid, isDefault, cableMissing);
            string spatial = spatialAvailable ? SpatialAvailable : "Unavailable — disabled";

            BridgePresentation result = BridgePresentationResolver.Resolve(
                status, setup, spatial, running);

            if (cableMissing)
                Assert.Equal(ContextAction.GetVbCable, result.Action);
            else if (!formatsValid)
                Assert.Equal(ContextAction.SetupAudio, result.Action);
            else if (!isDefault)
            {
                Assert.Equal(ContextAction.SetupAudio, result.Action);
                Assert.Equal(BridgeIndicator.Off, result.Indicator);
            }
            else if (!spatialAvailable)
                Assert.Equal(ContextAction.OpenSoundSettings, result.Action);
            else
                Assert.Equal(ContextAction.None, result.Action);
        }
    }

    static bool[] Both() => [false, true];

    static AudioSetupStatus ReadySetup() => new(true,
        "Ready — 7.1/48 kHz and default game endpoint",
        FormatsValid: true, IsDefault: true);
}
