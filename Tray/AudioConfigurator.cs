using System.Runtime.InteropServices;

namespace SpatialBridgeTray;

record AudioSetupStatus(
    bool Ready,
    string Summary,
    bool FormatsValid = false,
    bool IsDefault = false,
    bool VbCableMissing = false);

static class AudioSetupInspection
{
    public static AudioSetupStatus Inspect(string? gameEndpointId = null)
    {
        try
        {
            var render = EndpointDiscovery.GetAll("Render");
            var game = AudioConfigurator.FindGameEndpoint(render, gameEndpointId);
            if (game is null) return Evaluate(null, null, null);
            return Evaluate(game, SpatialDetection.GetMixFormat(game.Id), SpatialDetection.GetDefaultRenderId());
        }
        catch (Exception ex)
        {
            return new(false, "Audio device check failed — " + ex.Message);
        }
    }

    internal static AudioSetupStatus Evaluate(
        AudioEndpoint? game,
        AudioFormatInfo? playbackFormat,
        string? defaultRenderId)
    {
        if (game is null)
            return new(false,
                "VB-CABLE 16-channel driver not detected — install the VB-CABLE Driver Pack.",
                VbCableMissing: true);
        if (playbackFormat is null)
            return new(false, "Audio device check failed — the game endpoint format is unavailable.");

        bool isDefault = defaultRenderId?.Equals(game.Id, StringComparison.OrdinalIgnoreCase) == true;
        bool formatsValid = playbackFormat.Is71Float48k;
        bool ready = formatsValid && isDefault;

        if (ready)
            return new(true, "Ready — 7.1/48 kHz and default game endpoint", formatsValid, isDefault);

        var issues = new List<string>();
        if (!formatsValid) issues.Add($"game endpoint is {playbackFormat}");
        if (!isDefault) issues.Add("game endpoint is not the default playback device");
        return new(false, "Audio setup required — " + string.Join(", ", issues) + ".", formatsValid, isDefault);
    }
}

static class AudioConfigurator
{
    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    internal struct WaveFormatEx
    {
        public ushort FormatTag;
        public ushort Channels;
        public uint SamplesPerSec;
        public uint AvgBytesPerSec;
        public ushort BlockAlign;
        public ushort BitsPerSample;
        public ushort ExtraSize;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    internal struct WaveFormatExtensible
    {
        public WaveFormatEx Format;
        public ushort ValidBitsPerSample;
        public uint ChannelMask;
        public Guid SubFormat;
    }

    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    sealed class PolicyConfigClientCom { }

    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr format);
        [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.Bool)] bool defaultFormat, IntPtr format);
        [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
        [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr endpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.Bool)] bool defaultPeriod, IntPtr defaultValue, IntPtr minimumValue);
        [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr period);
        [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr mode);
        [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr mode);
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.Bool)] bool fxStore, IntPtr key, IntPtr value);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.Bool)] bool fxStore, IntPtr key, IntPtr value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.Bool)] bool visible);
    }

    internal static AudioEndpoint? FindGameEndpoint(
        IEnumerable<AudioEndpoint> endpoints,
        string? preferredId = null,
        Func<string, int>? currentChannelCount = null)
    {
        var list = endpoints.Where(x => x.IsVbCableLineOut).ToList();
        var preferred = string.IsNullOrWhiteSpace(preferredId) ? null
            : list.FirstOrDefault(x => x.Id.Equals(preferredId, StringComparison.OrdinalIgnoreCase));
        if (preferred is not null) return preferred;

        var multichannelCable = list.FirstOrDefault(x => x.NativeChannels >= 8);
        if (multichannelCable is not null) return multichannelCable;

        currentChannelCount ??= id => SpatialDetection.GetMixFormat(id).Channels;
        foreach (var endpoint in list)
        {
            try
            {
                if (currentChannelCount(endpoint.Id) >= 8) return endpoint;
            }
            catch { }
        }

        // When only one VB-CABLE Line Out endpoint is installed it is unambiguous,
        // even if it currently reports stereo because it still needs configuration.
        return list.Count == 1 ? list[0] : null;
    }

    public static IReadOnlyList<string> Configure(string? gameEndpointId = null)
    {
        var warnings = new List<string>();
        var game = FindGameEndpoint(EndpointDiscovery.GetAll("Render"), gameEndpointId)
            ?? throw new InvalidOperationException("The VB-CABLE multichannel Line Out endpoint was not found.");
        SetEndpointVisible(game.Id, true);
        AudioFormatInfo current = WaitForMixFormat(game.Id);
        if (!current.Is71Float48k)
        {
            Set71Float48k(game.Id, "Set VB-CABLE to 8 channels, 24-bit, 48 kHz");
            AudioFormatInfo configured = WaitForMixFormat(game.Id, require71: true);
            if (!configured.Is71Float48k)
                throw new InvalidOperationException(
                    $"VB-CABLE still reports {configured} after applying 8 channels, 24-bit, 48 kHz.");
        }
        SetDefaultRender(game.Id);
        return warnings;
    }

    static AudioFormatInfo WaitForMixFormat(string endpointId, bool require71 = false)
    {
        Exception? lastError = null;
        AudioFormatInfo? lastFormat = null;
        for (int attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                lastFormat = SpatialDetection.GetMixFormat(endpointId);
                if (!require71 || lastFormat.Is71Float48k) return lastFormat;
            }
            catch (Exception ex) { lastError = ex; }
            Thread.Sleep(100);
        }
        if (lastFormat is not null) return lastFormat;
        throw new InvalidOperationException(
            "Windows did not activate the VB-CABLE multichannel endpoint after it was enabled.", lastError);
    }

    static IPolicyConfig CreatePolicy() => (IPolicyConfig)(object)new PolicyConfigClientCom();
    static void Check(int result, string operation)
    {
        if (result < 0) throw new COMException($"{operation} failed (0x{result:X8}).", result);
    }

    static void SetDefaultRender(string endpointId)
    {
        var policy = CreatePolicy();
        try
        {
            Check(policy.SetDefaultEndpoint(endpointId, 0), "Set the default console playback device");
            Check(policy.SetDefaultEndpoint(endpointId, 1), "Set the default multimedia playback device");
        }
        finally { Marshal.FinalReleaseComObject(policy); }
    }

    static void Set71Float48k(string endpointId, string operation)
    {
        var policy = CreatePolicy();
        try
        {
            // This exactly matches the pair Windows writes when the user selects
            // "8 channels, 24 bit, 48000 Hz" for VB-CABLE's Line Out pin.
            var (endpointFormat, mixFormat) = CreateCableFormats();
            Check(SetDeviceFormat(policy, endpointId, endpointFormat, mixFormat), operation);
        }
        finally { Marshal.FinalReleaseComObject(policy); }
    }

    static void SetEndpointVisible(string endpointId, bool visible)
    {
        var policy = CreatePolicy();
        try
        {
            Check(policy.SetEndpointVisibility(endpointId, visible),
                visible ? "Enable the VB-CABLE multichannel endpoint" : "Disable the audio endpoint");
        }
        finally { Marshal.FinalReleaseComObject(policy); }
    }

    internal static (WaveFormatExtensible Endpoint, WaveFormatExtensible Mix) CreateCableFormats() =>
        (CreateFormat(24, ieeeFloat: false), CreateFormat(32, ieeeFloat: true));

    static WaveFormatExtensible CreateFormat(int bitsPerSample, bool ieeeFloat)
    {
        int bytesPerSample = bitsPerSample / 8;
        return new WaveFormatExtensible
        {
            Format = new WaveFormatEx
            {
                FormatTag = 0xFFFE,
                Channels = 8,
                SamplesPerSec = 48000,
                AvgBytesPerSec = (uint)(48000 * 8 * bytesPerSample),
                BlockAlign = (ushort)(8 * bytesPerSample),
                BitsPerSample = (ushort)bitsPerSample,
                ExtraSize = 22
            },
            ValidBitsPerSample = (ushort)bitsPerSample,
            // The Line Out pin carries raw channels. Windows uses a zero mask here;
            // a 7.1 speaker-position mask is rejected by this VB-CABLE endpoint.
            ChannelMask = 0,
            SubFormat = new Guid(ieeeFloat
                ? "00000003-0000-0010-8000-00AA00389B71"
                : "00000001-0000-0010-8000-00AA00389B71")
        };
    }

    static int SetDeviceFormat(
        IPolicyConfig policy,
        string endpointId,
        WaveFormatExtensible endpointFormat,
        WaveFormatExtensible mixFormat)
    {
        IntPtr endpointPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<WaveFormatExtensible>());
        IntPtr mixPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<WaveFormatExtensible>());
        try
        {
            Marshal.StructureToPtr(endpointFormat, endpointPointer, false);
            Marshal.StructureToPtr(mixFormat, mixPointer, false);
            return policy.SetDeviceFormat(endpointId, endpointPointer, mixPointer);
        }
        finally
        {
            Marshal.FreeCoTaskMem(endpointPointer);
            Marshal.FreeCoTaskMem(mixPointer);
        }
    }

}
