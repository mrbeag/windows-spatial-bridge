namespace SpatialBridgeTray;

internal enum BridgeIndicator
{
    Off,
    Error,
    Ready,
    Active
}

internal enum ContextAction
{
    None,
    SetupAudio,
    GetVbCable,
    OpenSoundSettings
}

internal sealed record BridgePresentation(
    BridgeIndicator Indicator,
    string Title,
    string Detail,
    ContextAction Action,
    string ActionText,
    int ActionWidthLogical,
    string TrayStatus);

internal static class BridgePresentationResolver
{
    public static BridgePresentation Resolve(
        string status,
        AudioSetupStatus setupStatus,
        string spatialStatus,
        bool running)
    {
        bool spatialAvailable = spatialStatus.StartsWith("Available", StringComparison.OrdinalIgnoreCase);
        bool hasError = status.StartsWith("Error", StringComparison.OrdinalIgnoreCase);
        bool isLive = running && spatialAvailable
            && status.StartsWith("Active", StringComparison.OrdinalIgnoreCase);
        bool isWaiting = running && spatialAvailable
            && status.StartsWith("Ready", StringComparison.OrdinalIgnoreCase);
        bool isStarting = running
            && status.StartsWith("Starting", StringComparison.OrdinalIgnoreCase);
        bool vbCableMissing = setupStatus.VbCableMissing;
        bool notDefault = setupStatus.FormatsValid && !setupStatus.IsDefault;
        bool setupError = !setupStatus.FormatsValid;

        ContextAction action = vbCableMissing
            ? ContextAction.GetVbCable
            : !setupStatus.Ready ? ContextAction.SetupAudio
            : !spatialAvailable ? ContextAction.OpenSoundSettings
            : ContextAction.None;

        string actionText = action switch
        {
            ContextAction.GetVbCable => "Get VB-CABLE",
            ContextAction.SetupAudio when notDefault => "Enable",
            ContextAction.SetupAudio => "Set up audio",
            ContextAction.OpenSoundSettings => "Sound settings",
            _ => ""
        };

        int actionWidth = action switch
        {
            ContextAction.SetupAudio when notDefault => 76,
            ContextAction.SetupAudio => 96,
            ContextAction.GetVbCable => 108,
            ContextAction.OpenSoundSettings => 108,
            _ => 0
        };

        BridgeIndicator indicator;
        string title;
        string detail;

        if (vbCableMissing)
        {
            indicator = BridgeIndicator.Error;
            title = "VB-CABLE not detected";
            detail = "Install the 16-channel VB-CABLE driver.";
        }
        else if (setupError)
        {
            indicator = BridgeIndicator.Error;
            title = "Audio setup required";
            detail = "Set up VB-CABLE for 7.1.";
        }
        else if (notDefault)
        {
            indicator = BridgeIndicator.Off;
            title = "Off";
            detail = "Not set as default audio source.";
        }
        else if (!spatialAvailable)
        {
            indicator = BridgeIndicator.Error;
            title = "Spatial sound is off";
            detail = "Enable Spatial Sound on the selected headphones.";
        }
        else if (hasError)
        {
            indicator = BridgeIndicator.Error;
            title = "Bridge error";
            detail = status;
        }
        else if (isLive)
        {
            indicator = BridgeIndicator.Active;
            title = "Spatial audio live";
            detail = "Windows Spatial Sound is active.";
        }
        else if (isWaiting)
        {
            indicator = BridgeIndicator.Ready;
            title = "Waiting for game audio";
            detail = "Ready — play game audio to begin.";
        }
        else if (isStarting)
        {
            indicator = BridgeIndicator.Ready;
            title = "Connecting audio…";
            detail = "Opening the Windows Spatial stream.";
        }
        else
        {
            indicator = BridgeIndicator.Error;
            title = "Bridge stopped";
            detail = "Restart the bridge from the tray app.";
        }

        string trayStatus = indicator == BridgeIndicator.Off
            ? "Off — not default audio source"
            : status;

        return new(indicator, title, detail, action, actionText, actionWidth, trayStatus);
    }
}
