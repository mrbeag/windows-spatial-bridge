using Microsoft.Win32;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SpatialBridgeTray;

static class AppInfo
{
    public static string Version { get; } = ResolveVersion();
    public static string DisplayName { get; } = $"Windows Spatial Bridge v{Version}";

    static string ResolveVersion()
    {
        string? informational = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        string? version = informational?.Split('+', 2)[0];
        return string.IsNullOrWhiteSpace(version) ? "0.0.0" : version;
    }
}

record AudioEndpoint(
    string Id,
    string Name,
    string DriverSection = "",
    int NativeChannels = 0,
    string DeviceName = "",
    int FormFactor = -1)
{
    // DriverSection is the preferred stable identity. DeviceName is the signed
    // driver's underlying device description and remains available when the user
    // renames the Windows endpoint. Do not identify the cable by its friendly name.
    public bool IsVbCable =>
        DriverSection.Contains("VBCableInst", StringComparison.OrdinalIgnoreCase)
        || DeviceName.StartsWith("VB-Audio Virtual Cable", StringComparison.OrdinalIgnoreCase);

    // EndpointFormFactor.LineLevel == 2. VB-CABLE v3 exposes this separate pin
    // for raw multichannel formats (up to 16 channels). This is the bridge input.
    public bool IsVbCableLineOut => IsVbCable && FormFactor == 2;
}

record SpatialCapability(bool Enabled, uint StaticObjectMask, uint MaxDynamicObjects, string Summary)
{
    public static SpatialCapability Unavailable(string reason) => new(false, 0, 0, reason);
}

record AudioFormatInfo(int Channels, int SampleRate, int BitsPerSample)
{
    public bool Is71Float48k => Channels == 8 && SampleRate == 48000 && BitsPerSample == 32;
    public override string ToString() => $"{Channels}ch/{SampleRate / 1000} kHz/{BitsPerSample}-bit";
}

static class SpatialDetection
{
    const uint ClsctxAll = 23;
    static readonly Guid SpatialAudioClientId = new("BBF8E066-AAAA-49BE-9A4D-FD2A858EA27F");

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    sealed class MMDeviceEnumeratorCom { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice endpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice endpoint);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        [PreserveSig] int OpenPropertyStore(uint access, out IntPtr properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }

    [ComImport, Guid("BBF8E066-AAAA-49BE-9A4D-FD2A858EA27F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISpatialAudioClient
    {
        [PreserveSig] int GetStaticObjectPosition(uint type, out float x, out float y, out float z);
        [PreserveSig] int GetNativeStaticObjectTypeMask(out uint mask);
        [PreserveSig] int GetMaxDynamicObjectCount(out uint count);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    struct WaveFormatEx
    {
        public ushort FormatTag;
        public ushort Channels;
        public uint SamplesPerSec;
        public uint AvgBytesPerSec;
        public ushort BlockAlign;
        public ushort BitsPerSample;
        public ushort ExtraSize;
    }

    [ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioClient
    {
        [PreserveSig] int Initialize(int shareMode, uint flags, long duration, long periodicity, IntPtr format, IntPtr sessionGuid);
        [PreserveSig] int GetBufferSize(out uint frames);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint frames);
        [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);
        [PreserveSig] int GetMixFormat(out IntPtr format);
    }

    public static AudioFormatInfo GetMixFormat(string endpointId)
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        object? instance = null;
        IntPtr format = IntPtr.Zero;
        try
        {
            enumerator = (IMMDeviceEnumerator)(object)new MMDeviceEnumeratorCom();
            Marshal.ThrowExceptionForHR(enumerator.GetDevice(endpointId, out device));
            Guid iid = typeof(IAudioClient).GUID;
            Marshal.ThrowExceptionForHR(device.Activate(ref iid, ClsctxAll, IntPtr.Zero, out instance));
            Marshal.ThrowExceptionForHR(((IAudioClient)instance).GetMixFormat(out format));
            var value = Marshal.PtrToStructure<WaveFormatEx>(format);
            return new(value.Channels, (int)value.SamplesPerSec, value.BitsPerSample);
        }
        finally
        {
            if (format != IntPtr.Zero) Marshal.FreeCoTaskMem(format);
            if (instance is not null && Marshal.IsComObject(instance)) Marshal.FinalReleaseComObject(instance);
            if (device is not null && Marshal.IsComObject(device)) Marshal.FinalReleaseComObject(device);
            if (enumerator is not null && Marshal.IsComObject(enumerator)) Marshal.FinalReleaseComObject(enumerator);
        }
    }

    public static string GetDefaultRenderId()
    {
        var enumerator = (IMMDeviceEnumerator)(object)new MMDeviceEnumeratorCom();
        IMMDevice? device = null;
        try
        {
            Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(0, 1, out device!));
            Marshal.ThrowExceptionForHR(device.GetId(out string id));
            return id;
        }
        finally
        {
            if (device is not null && Marshal.IsComObject(device)) Marshal.FinalReleaseComObject(device);
            Marshal.FinalReleaseComObject(enumerator);
        }
    }

    public static SpatialCapability Check(string endpointId)
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        object? instance = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)(object)new MMDeviceEnumeratorCom();
            Marshal.ThrowExceptionForHR(enumerator.GetDevice(endpointId, out device));
            Guid iid = SpatialAudioClientId;
            Marshal.ThrowExceptionForHR(device.Activate(ref iid, ClsctxAll, IntPtr.Zero, out instance));
            var spatial = (ISpatialAudioClient)instance;
            Marshal.ThrowExceptionForHR(spatial.GetNativeStaticObjectTypeMask(out uint mask));
            Marshal.ThrowExceptionForHR(spatial.GetMaxDynamicObjectCount(out uint maxObjects));
            bool enabled = mask != 0 || maxObjects != 0;
            string summary = enabled
                ? $"Available — static mask 0x{mask:X}, {maxObjects} dynamic objects"
                : "Not enabled on this output";
            return new(enabled, mask, maxObjects, summary);
        }
        catch (Exception ex)
        {
            return SpatialCapability.Unavailable("Unavailable — " + ex.Message);
        }
        finally
        {
            if (instance is not null && Marshal.IsComObject(instance)) Marshal.FinalReleaseComObject(instance);
            if (device is not null && Marshal.IsComObject(device)) Marshal.FinalReleaseComObject(device);
            if (enumerator is not null && Marshal.IsComObject(enumerator)) Marshal.FinalReleaseComObject(enumerator);
        }
    }
}

static class StartupRegistration
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "WindowsSpatialBridge";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string value
                && value.Contains(Application.ExecutablePath, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (enabled) key.SetValue(ValueName, $"\"{Application.ExecutablePath}\"");
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}

static class EndpointDiscovery
{
    const string AudioRoot = @"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio";
    const string InterfaceNameKey = "{a45c254e-df1c-4efd-8020-67d146a850e0},2";
    const string FriendlyNameKey = "{a45c254e-df1c-4efd-8020-67d146a850e0},14";
    const string DeviceNameKey = "{b3f8fa53-0004-438e-9003-51a46e139bfc},6";
    const string DriverSectionKey = "{a8b865dd-2e3d-4094-ad97-e593a70c75d6},6";
    const string NativeFormatKey = "{f19f064d-082c-4e27-bc73-6882a1bb8e4c},0";
    const string FormFactorKey = "{1da5d803-d492-4edd-8c23-e0c0ffee7f0e},0";

    public static List<AudioEndpoint> GetActive(string flow) => Get(flow, includeDisabled: false);
    public static List<AudioEndpoint> GetAll(string flow) => Get(flow, includeDisabled: true);
    internal static bool IsActiveDeviceState(int state) => state == 1;

    static List<AudioEndpoint> Get(string flow, bool includeDisabled)
    {
        var result = new List<AudioEndpoint>();
        using var root = Registry.LocalMachine.OpenSubKey($@"{AudioRoot}\{flow}");
        if (root == null) return result;
        string prefix = flow == "Render" ? "{0.0.0.00000000}." : "{0.0.1.00000000}.";

        foreach (string endpointKeyName in root.GetSubKeyNames())
        {
            using var endpoint = root.OpenSubKey(endpointKeyName);
            if (endpoint?.GetValue("DeviceState") is not int state
                || (!includeDisabled && !IsActiveDeviceState(state))) continue;
            using var properties = endpoint.OpenSubKey("Properties");
            if (properties == null) continue;
            string interfaceName = properties.GetValue(InterfaceNameKey) as string ?? "";
            string friendlyName = properties.GetValue(FriendlyNameKey) as string ?? "";
            string deviceName = properties.GetValue(DeviceNameKey) as string ?? "";
            string driverSection = properties.GetValue(DriverSectionKey) as string ?? "";
            int nativeChannels = ReadNativeChannels(properties.GetValue(NativeFormatKey) as byte[]);
            int formFactor = properties.GetValue(FormFactorKey) is int value ? value : -1;
            string primaryName = friendlyName.Length > 0 ? friendlyName : interfaceName;
            string name = primaryName.Length > 0 && deviceName.Length > 0 && primaryName != deviceName
                ? $"{primaryName} ({deviceName})"
                : primaryName.Length > 0 ? primaryName : deviceName;
            if (name.Length > 0)
                result.Add(new(prefix + endpointKeyName, name, driverSection,
                    nativeChannels, deviceName, formFactor));
        }
        return result.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    static int ReadNativeChannels(byte[]? value)
    {
        // MMDevice stores PKEY_AudioEngine_DeviceFormat as a PROPVARIANT header
        // followed by WAVEFORMATEX/WAVEFORMATEXTENSIBLE. Channel count is the
        // second WORD in that format, at byte offset 10 in the registry value.
        return value is { Length: >= 12 } ? BitConverter.ToUInt16(value, 10) : 0;
    }
}

sealed class BridgeConfiguration
{
    public string GameRenderId { get; set; } = "";
    public string GameRenderName { get; set; } = "";
    public string OutputId { get; set; } = "";
    public string OutputName { get; set; } = "";
}

static class ConfigurationStore
{
    public static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowsSpatialBridge");
    public static readonly string FilePath = Path.Combine(DirectoryPath, "audio-endpoints.json");

    public static BridgeConfiguration? Load()
    {
        if (!File.Exists(FilePath)) return null;
        return JsonSerializer.Deserialize<BridgeConfiguration>(File.ReadAllText(FilePath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    public static void UpdateEndpoints(AudioEndpoint gameInput, AudioEndpoint output)
    {
        Directory.CreateDirectory(DirectoryPath);
        JsonObject root;
        try { root = JsonNode.Parse(File.Exists(FilePath) ? File.ReadAllText(FilePath) : "{}")?.AsObject() ?? []; }
        catch { root = []; }
        root[nameof(BridgeConfiguration.GameRenderId)] = gameInput.Id;
        root[nameof(BridgeConfiguration.GameRenderName)] = gameInput.Name;
        root.Remove("CableCaptureId");
        root.Remove("CableCaptureName");
        root[nameof(BridgeConfiguration.OutputId)] = output.Id;
        root[nameof(BridgeConfiguration.OutputName)] = output.Name;
        File.WriteAllText(FilePath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}

sealed class StatusDot : Control
{
    Color stateColor = Color.FromArgb(112, 112, 112);
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color StateColor { get => stateColor; set { stateColor = value; Invalidate(); } }

    public StatusDot()
    {
        Width = 30;
        Dock = DockStyle.Left;
        TabStop = false;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var glow = new SolidBrush(Color.FromArgb(26, stateColor));
        using var fill = new SolidBrush(stateColor);
        e.Graphics.FillEllipse(glow, 2, Height / 2 - 10, 20, 20);
        e.Graphics.FillEllipse(fill, 7, Height / 2 - 5, 10, 10);
    }
}

static class IconFactory
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool DestroyIcon(IntPtr handle);

    public static Icon Create(Color color)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            using var brush = new SolidBrush(color);
            using var edge = new Pen(Color.FromArgb(240, 255, 255, 255), 1.8f);
            using var pen = new Pen(Color.White, 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            graphics.FillEllipse(brush, 2, 2, 28, 28);
            graphics.DrawEllipse(edge, 2, 2, 28, 28);
            graphics.DrawLine(pen, 10, 13, 10, 19);
            graphics.DrawLine(pen, 16, 9, 16, 23);
            graphics.DrawLine(pen, 22, 12, 22, 20);
        }
        IntPtr handle = bitmap.GetHicon();
        try { return (Icon)Icon.FromHandle(handle).Clone(); }
        finally { DestroyIcon(handle); }
    }
}

sealed class StatusCard : Panel
{
    readonly StatusDot indicator = new();
    readonly Label valueLabel = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        Font = new Font("Segoe UI", 9.25f),
        ForeColor = Color.FromArgb(194, 194, 194),
        AutoEllipsis = true
    };

    public StatusCard(string title)
    {
        Height = 52;
        Dock = DockStyle.Fill;
        BackColor = Color.FromArgb(27, 27, 27);
        Margin = new Padding(0, 3, 0, 3);
        Padding = new Padding(14, 0, 14, 0);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        var titleLabel = new Label
        {
            Text = title,
            Dock = DockStyle.Left,
            Width = 148,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 9.25f, FontStyle.Bold),
            ForeColor = Color.FromArgb(241, 245, 249)
        };
        Controls.Add(valueLabel);
        Controls.Add(titleLabel);
        Controls.Add(indicator);
        Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(Color.FromArgb(59, 59, 59));
            e.Graphics.DrawLine(pen, 14, Height - 1, Width - 14, Height - 1);
        };
    }

    public void SetValue(string value, Color stateColor)
    {
        valueLabel.Text = value;
        indicator.StateColor = stateColor;
        AccessibleName = valueLabel.AccessibleName = value;
    }
}

sealed class SettingsForm : Form
{
    static readonly Color WindowBackground = Color.FromArgb(18, 18, 18);
    static readonly Color Surface = Color.FromArgb(27, 27, 27);
    static readonly Color SurfaceRaised = Color.FromArgb(38, 38, 38);
    static readonly Color Border = Color.FromArgb(59, 59, 59);
    static readonly Color PrimaryText = Color.FromArgb(242, 242, 242);
    static readonly Color MutedText = Color.FromArgb(166, 166, 166);
    static readonly Color Accent = Color.FromArgb(122, 122, 122);
    static readonly Color Success = Color.FromArgb(52, 211, 153);
    static readonly Color Warning = Color.FromArgb(251, 191, 36);
    static readonly Color Danger = Color.FromArgb(248, 113, 113);
    static readonly Color Neutral = Color.FromArgb(110, 110, 110);

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    readonly ComboBox outputBox = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Dock = DockStyle.Fill,
        Font = new Font("Segoe UI", 10.5f),
        FlatStyle = FlatStyle.Flat,
        BackColor = SurfaceRaised,
        ForeColor = PrimaryText,
        Margin = new Padding(0, 5, 0, 0),
        AccessibleName = "Headphones"
    };
    readonly TrafficSignal signal = new() { Dock = DockStyle.Fill };
    readonly Label liveTitle = new() { Dock = DockStyle.Fill, Text = "Bridge stopped", Font = new Font("Segoe UI", 12f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
    readonly Label liveDetail = new() { Dock = DockStyle.Fill, Text = "Open the tray menu to finish setup.", Font = new Font("Segoe UI", 8.75f), ForeColor = MutedText, AutoEllipsis = false, TextAlign = ContentAlignment.MiddleLeft };
    readonly ToolTip detailTip = new() { AutoPopDelay = 18000, InitialDelay = 450, ReshowDelay = 150 };
    readonly Button actionButton = new ModernButton() { Visible = false };
    readonly Button closeButton = new ModernButton() { Text = "Close" };
    readonly TrayContext controller;
    readonly Action<bool> setCompactView;
    readonly ColumnStyle actionColumn = new(SizeType.Absolute, 0);
    ContextAction contextAction;

    internal float ActionColumnWidth => actionColumn.Width;
    internal string ActionText => actionButton.Text;

    public SettingsForm(TrayContext controller)
    {
        this.controller = controller;
        Text = AppInfo.DisplayName;
        Icon = IconFactory.Create(Accent);
        Font = new Font("Segoe UI", 9.5f);
        ForeColor = PrimaryText;
        BackColor = WindowBackground;
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = true;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(460, 292);

        var outputLabel = new Label
        {
            Text = "Headphones",
            AutoSize = true,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = PrimaryText,
            Margin = new Padding(0, 0, 0, 0)
        };
        Label HelpText(string text) => new()
        {
            Text = text,
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            Font = new Font("Segoe UI", 8.25f),
            ForeColor = MutedText,
            Margin = Padding.Empty,
            TextAlign = ContentAlignment.MiddleLeft
        };
        var outputHelp = HelpText("Choose the Spatial Sound-enabled headphones or DAC you want to hear.");

        StyleButton(actionButton, primary: true);
        StyleButton(closeButton);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 6, 0, 0)
        };
        buttons.Controls.Add(closeButton);

        var hero = new SurfacePanel { Dock = DockStyle.Fill, Padding = new Padding(12, 8, 12, 8), Margin = Padding.Empty };
        var heroLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3, BackColor = Surface };
        heroLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 54));
        heroLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        heroLayout.ColumnStyles.Add(actionColumn);
        heroLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 16));
        heroLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        heroLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        heroLayout.Controls.Add(new Label { Text = "STATUS", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 7.5f, FontStyle.Bold), ForeColor = MutedText }, 1, 0);
        heroLayout.Controls.Add(liveTitle, 1, 1);
        heroLayout.Controls.Add(liveDetail, 1, 2);
        heroLayout.Controls.Add(signal, 0, 0);
        heroLayout.SetRowSpan(signal, 3);
        actionButton.Dock = DockStyle.None;
        actionButton.Anchor = AnchorStyles.Right;
        actionButton.Margin = new Padding(8, 0, 0, 0);
        actionButton.Font = new Font("Segoe UI", 9f);
        heroLayout.Controls.Add(actionButton, 2, 0);
        heroLayout.SetRowSpan(actionButton, 3);
        hero.Controls.Add(heroLayout);

        var outputSurface = new SurfacePanel { Dock = DockStyle.Fill, Padding = new Padding(16, 8, 16, 7), Margin = Padding.Empty };
        var outputLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Surface };
        outputLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        outputLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        outputLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        outputLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        outputLayout.Controls.Add(outputLabel, 0, 0);
        outputLayout.Controls.Add(outputBox, 0, 1);
        outputLayout.Controls.Add(outputHelp, 0, 2);
        outputSurface.Controls.Add(outputLayout);

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14, 0, 14, 12),
            RowCount = 8,
            ColumnCount = 1,
            BackColor = BackColor
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 8));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 94));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 8));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        content.Controls.Add(hero, 0, 0);
        content.Controls.Add(outputSurface, 0, 2);
        content.Controls.Add(buttons, 0, 7);

        Controls.Add(content);
        detailTip.SetToolTip(outputBox, outputHelp.Text);
        detailTip.SetToolTip(closeButton, "Close settings. The bridge remains in the notification area.");

        actionButton.Click += async (_, _) => await RunContextAction();
        outputBox.SelectionChangeCommitted += async (_, _) => await ApplySelection();
        setCompactView = compact =>
        {
            outputSurface.Visible = buttons.Visible = !compact;
            content.Padding = compact ? new Padding(10) : new Padding(14, 0, 14, 12);
            float[] expandedRows = [82, 8, 94, 0, 0, 0, 0, 44];
            for (int row = 0; row < 8; row++)
                content.RowStyles[row].Height = compact ? (row == 0 ? 82 : 0) : expandedRows[row];
            ClientSize = compact ? new Size(384, 102) : new Size(420, 240);
        };
        setCompactView(true);
        closeButton.Click += (_, _) => Hide();
        CancelButton = closeButton;
    }

    public void ShowExpanded()
    {
        setCompactView(true);
    }

    public void ShowCompact()
    {
        setCompactView(true);
    }

    const int WmSysCommand = 0x0112;
    const int ScClose = 0xF060;

    protected override void WndProc(ref Message message)
    {
        // X and Alt+F4 hide the app. A direct WM_CLOSE/ENDSESSION from Windows
        // Installer is allowed through so an upgrade can shut the process down.
        if (message.Msg == WmSysCommand && ((int)message.WParam & 0xFFF0) == ScClose)
        {
            Hide();
            return;
        }
        base.WndProc(ref message);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int enabled = 1;
        if (DwmSetWindowAttribute(Handle, 20, ref enabled, sizeof(int)) != 0)
            DwmSetWindowAttribute(Handle, 19, ref enabled, sizeof(int));
    }

    static void StyleButton(Button button, bool primary = false)
    {
        button.AutoSize = false;
        button.Size = new Size(122, 38);
        button.Margin = new Padding(0, 0, 10, 0);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = primary ? Accent : SurfaceRaised;
        button.ForeColor = PrimaryText;
        button.Font = new Font("Segoe UI", 9.25f);
        button.Cursor = Cursors.Hand;
    }

    static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        float diameter = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    sealed class SurfacePanel : Panel
    {
        public SurfacePanel()
        {
            BackColor = Surface;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent?.BackColor ?? WindowBackground);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = RoundedRectangle(new RectangleF(.5f, .5f, Width - 1, Height - 1), 11 * DeviceDpi / 96f);
            using var fill = new SolidBrush(BackColor);
            using var border = new Pen(Border);
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);
        }
    }

    sealed class ModernButton : Button
    {
        bool hovered;
        bool pressed;

        public ModernButton() => SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent?.BackColor ?? Surface);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            bool primary = BackColor.ToArgb() == Accent.ToArgb();
            Color fillColor = !Enabled ? Color.FromArgb(48, 48, 48)
                : primary ? (pressed ? Color.FromArgb(91, 91, 91) : hovered ? Color.FromArgb(143, 143, 143) : Accent)
                : pressed ? Color.FromArgb(55, 55, 55) : hovered ? Color.FromArgb(48, 48, 48) : SurfaceRaised;
            using var path = RoundedRectangle(new RectangleF(.5f, .5f, Width - 1, Height - 1), 6 * DeviceDpi / 96f);
            using var fill = new SolidBrush(fillColor);
            using var border = new Pen(primary && Enabled ? fillColor : Border);
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle,
                Enabled ? ForeColor : Color.FromArgb(105, 105, 105), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (Focused && ShowFocusCues)
            {
                var focus = ClientRectangle;
                focus.Inflate(-5, -5);
                ControlPaint.DrawFocusRectangle(e.Graphics, focus, ForeColor, fillColor);
            }
        }
    }

    sealed class TrafficSignal : Control
    {
        int active = -1;
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public int ActiveLight { get => active; set { active = value; Invalidate(); } }

        public TrafficSignal()
        {
            TabStop = false;
            AccessibleName = "Bridge stopped";
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float scale = DeviceDpi / 96f;
            float diameter = 28 * scale;
            float left = (Width - diameter) / 2;
            float top = (Height - diameter) / 2;
            if (active < 0)
            {
                using var unlit = new SolidBrush(Color.FromArgb(34, 34, 34));
                using var rim = new Pen(Color.FromArgb(76, 76, 76), Math.Max(1f, scale));
                e.Graphics.FillEllipse(unlit, left, top, diameter, diameter);
                e.Graphics.DrawEllipse(rim, left, top, diameter, diameter);
                return;
            }

            Color[] colors = [Danger, Warning, Success];
            using var glow = new SolidBrush(Color.FromArgb(35, colors[active]));
            using var lamp = new SolidBrush(colors[active]);
            e.Graphics.FillEllipse(glow, left - 5 * scale, top - 5 * scale, diameter + 10 * scale, diameter + 10 * scale);
            e.Graphics.FillEllipse(lamp, left, top, diameter, diameter);
            using var highlight = new SolidBrush(Color.FromArgb(155, Color.White));
            e.Graphics.FillEllipse(highlight, left + 7 * scale, top + 5 * scale, 6 * scale, 4 * scale);
        }
    }

    public void RefreshView(IReadOnlyList<AudioEndpoint> outputs, string? selectedId, string status,
        AudioSetupStatus setupStatus, string spatialStatus, bool running)
    {
        outputBox.BeginUpdate();
        outputBox.DisplayMember = nameof(AudioEndpoint.Name);
        outputBox.Items.Clear();
        outputBox.Items.AddRange(outputs.Cast<object>().ToArray());
        int index = outputs.ToList().FindIndex(x => x.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase));
        outputBox.SelectedIndex = index >= 0 ? index : outputs.Count > 0 ? 0 : -1;
        outputBox.EndUpdate();

        BridgePresentation presentation = BridgePresentationResolver.Resolve(
            status, setupStatus, spatialStatus, running);
        liveTitle.Text = presentation.Title;
        liveTitle.ForeColor = presentation.Indicator switch
        {
            BridgeIndicator.Off => MutedText,
            BridgeIndicator.Active => Success,
            BridgeIndicator.Ready => Warning,
            _ => Danger
        };
        liveDetail.Text = presentation.Detail;
        signal.ActiveLight = presentation.Indicator switch
        {
            BridgeIndicator.Off => -1,
            BridgeIndicator.Error => 0,
            BridgeIndicator.Ready => 1,
            BridgeIndicator.Active => 2,
            _ => -1
        };
        signal.AccessibleName = liveTitle.Text;
        detailTip.SetToolTip(liveTitle, status);
        detailTip.SetToolTip(liveDetail,
            presentation.Indicator == BridgeIndicator.Error ? status : liveDetail.Text);
        contextAction = presentation.Action;
        bool showAction = contextAction != ContextAction.None;
        actionButton.Visible = showAction;
        actionButton.Text = presentation.ActionText;
        float dpiScale = DeviceDpi / 96f;
        int actionWidth = (int)Math.Round(presentation.ActionWidthLogical * dpiScale);
        actionButton.Size = new Size(actionWidth, (int)Math.Round(30 * dpiScale));
        // Control.Visible reads false while an ancestor (the compact window) is
        // hidden. Use the intended state so a background refresh cannot collapse
        // the action column before the window is opened again.
        actionColumn.Width = showAction ? actionWidth + (int)Math.Round(8 * dpiScale) : 0;
        detailTip.SetToolTip(actionButton, contextAction switch
        {
            ContextAction.SetupAudio => "Windows will ask for administrator permission.",
            ContextAction.GetVbCable => "Open the official VB-Audio download page.",
            ContextAction.OpenSoundSettings => "Turn on Spatial Sound for the selected headphones.",
            _ => ""
        });

    }

    async Task RunContextAction()
    {
        switch (contextAction)
        {
            case ContextAction.SetupAudio:
                await ApplySelection();
                break;
            case ContextAction.GetVbCable:
                Process.Start(new ProcessStartInfo("https://vb-audio.com/Cable/") { UseShellExecute = true });
                break;
            case ContextAction.OpenSoundSettings:
                Process.Start(new ProcessStartInfo("ms-settings:sound") { UseShellExecute = true });
                break;
        }
    }

    async Task ApplySelection()
    {
        if (outputBox.SelectedItem is AudioEndpoint output)
            await controller.ApplyListeningDeviceAsync(output);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            detailTip.Dispose();
            Icon?.Dispose();
        }
        base.Dispose(disposing);
    }
}

sealed class TrayContext : ApplicationContext
{
    readonly NotifyIcon trayIcon;
    readonly ToolStripMenuItem statusItem = new("Stopped") { Enabled = false };
    readonly ToolStripMenuItem spatialItem = new("Spatial endpoint: unknown") { Enabled = false };
    readonly ToolStripMenuItem outputMenu = new("Headphones");
    readonly Icon offIcon = IconFactory.Create(Color.FromArgb(72, 72, 72));
    readonly Icon stoppedIcon = IconFactory.Create(Color.FromArgb(239, 68, 68));
    readonly Icon readyIcon = IconFactory.Create(Color.FromArgb(245, 158, 11));
    readonly Icon activeIcon = IconFactory.Create(Color.FromArgb(16, 185, 129));
    readonly SettingsForm settings;
    readonly System.Windows.Forms.Timer processTimer = new() { Interval = 1000 };
    List<AudioEndpoint> gameInputs = [];
    List<AudioEndpoint> outputs = [];
    string? selectedGameInputId;
    string? selectedOutputId;
    Process? bridgeProcess;
    string status = "Stopped";
    bool bridgeDesired;
    bool exiting;
    SpatialCapability spatialCapability = SpatialCapability.Unavailable("Not checked");
    AudioSetupStatus setupStatus = new(false, "Not checked");

    public TrayContext()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem(AppInfo.DisplayName) { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(statusItem);
        menu.Items.Add(spatialItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(outputMenu);
        menu.Items.Add(new ToolStripSeparator());
        var settingsItem = menu.Items.Add("Open status");
        var exitItem = menu.Items.Add("Exit");

        trayIcon = new NotifyIcon
        {
            Icon = stoppedIcon,
            Text = AppInfo.DisplayName,
            ContextMenuStrip = menu,
            Visible = true
        };
        settings = new SettingsForm(this);
        _ = settings.Handle;
        settings.FormClosed += (_, _) => ExitApplication();
        trayIcon.DoubleClick += (_, _) => ShowSettings();
        settingsItem.Click += (_, _) => ShowSettings();
        exitItem.Click += (_, _) => ExitApplication();
        processTimer.Tick += (_, _) => CheckProcess();
        processTimer.Start();

        RefreshDevices();
        var saved = ConfigurationStore.Load();
        selectedGameInputId = saved?.GameRenderId;
        if (string.IsNullOrWhiteSpace(selectedGameInputId))
            selectedGameInputId = AudioConfigurator.FindGameEndpoint(gameInputs)?.Id;
        selectedOutputId = saved?.OutputId;
        RefreshSetupStatus();
        RefreshSpatialStatus();
        RebuildMenu();
        if (saved != null && setupStatus.Ready && spatialCapability.Enabled)
        {
            bridgeDesired = true;
            StartBridge();
        }
        else
        {
            bridgeDesired = false;
            SetStatus(saved == null ? "Audio setup required" : !setupStatus.Ready
                ? "Audio setup required" : "Enable Spatial Sound on the selected headphones");
            ShowSettings(expanded: true);
        }
    }

    void RefreshDevices()
    {
        var saved = ConfigurationStore.Load();
        var render = EndpointDiscovery.GetActive("Render");
        // Include the Line Out pin even when the user disabled it. The elevated
        // setup action can enable and configure that exact endpoint without
        // touching the separate VB-CABLE Speakers pin.
        gameInputs = EndpointDiscovery.GetAll("Render")
            .Where(x => x.IsVbCableLineOut)
            .ToList();
        outputs = render
            .Where(x => !x.IsVbCable)
            .ToList();
    }

    void RebuildMenu()
    {
        if (exiting || settings.IsDisposed) return;
        outputMenu.DropDownItems.Clear();
        foreach (var output in outputs)
        {
            var item = new ToolStripMenuItem(output.Name)
            {
                Checked = output.Id.Equals(selectedOutputId, StringComparison.OrdinalIgnoreCase),
                CheckOnClick = false,
                Tag = output
            };
            item.Click += async (_, _) => await ApplyListeningDeviceAsync((AudioEndpoint)item.Tag!);
            outputMenu.DropDownItems.Add(item);
        }
        bool running = bridgeProcess is { HasExited: false };
        BridgePresentation presentation = BridgePresentationResolver.Resolve(
            status, setupStatus, spatialCapability.Summary, running);
        statusItem.Text = presentation.TrayStatus;
        spatialItem.Text = spatialCapability.Enabled ? "Spatial sound available" : "Spatial sound unavailable";
        trayIcon.Icon = presentation.Indicator switch
        {
            BridgeIndicator.Off => offIcon,
            BridgeIndicator.Active => activeIcon,
            BridgeIndicator.Ready => readyIcon,
            _ => stoppedIcon
        };
        trayIcon.Text = ("Spatial Bridge — " + presentation.TrayStatus)
            [..Math.Min(63, ("Spatial Bridge — " + presentation.TrayStatus).Length)];
        settings.RefreshView(outputs, selectedOutputId, status, setupStatus, spatialCapability.Summary,
            running);
    }

    public void SelectDevices(AudioEndpoint input, AudioEndpoint output, bool restart)
    {
        RefreshDevices();
        selectedGameInputId = input.Id;
        selectedOutputId = output.Id;
        ConfigurationStore.UpdateEndpoints(input, output);
        RefreshSetupStatus();
        RefreshSpatialStatus();
        RebuildMenu();
        if (restart)
        {
            StopBridgeProcess();
            StartBridge();
        }
    }

    public void StartBridge()
    {
        if (bridgeProcess is { HasExited: false }) return;
        RefreshDevices();
        var saved = ConfigurationStore.Load();
        if (saved == null || string.IsNullOrWhiteSpace(saved.OutputId))
        {
            SetStatus("Error: run audio setup or select an output");
            ShowSettings(expanded: true);
            return;
        }
        var gameInput = gameInputs
            .SingleOrDefault(x => x.Id.Equals(saved.GameRenderId, StringComparison.OrdinalIgnoreCase))
            ?? AudioConfigurator.FindGameEndpoint(gameInputs);
        var output = outputs.SingleOrDefault(x => x.Id.Equals(saved.OutputId, StringComparison.OrdinalIgnoreCase));
        if (gameInput == null || output == null)
        {
            SetStatus("Error: configured endpoint is unavailable");
            ShowSettings(expanded: true);
            return;
        }
        selectedOutputId = output.Id;
        selectedGameInputId = gameInput.Id;
        RefreshSetupStatus();
        RefreshSpatialStatus();
        if (!setupStatus.Ready)
        {
            bridgeDesired = false;
            SetStatus("Audio setup required");
            ShowSettings(expanded: true);
            return;
        }
        if (!spatialCapability.Enabled)
        {
            bridgeDesired = false;
            SetStatus("Error: enable Spatial Sound on the selected headphones");
            ShowSettings(expanded: true);
            return;
        }
        bridgeDesired = true;

        string? executable = FindBridgeExecutable();
        if (executable == null)
        {
            SetStatus("Error: SpatialBridge.exe was not found");
            ShowSettings(expanded: true);
            return;
        }
        if (Process.GetProcessesByName("SpatialBridge").Any())
        {
            SetStatus("Bridge already running outside tray app");
            return;
        }

        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        // Capture the game-facing 7.1 render endpoint directly. WASAPI loopback
        // bypasses VB-CABLE's buffered Input -> Output transfer path.
        info.ArgumentList.Add(gameInput.Id);
        info.ArgumentList.Add(output.Id);
        bridgeProcess = new Process { StartInfo = info, EnableRaisingEvents = true };
        bridgeProcess.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data)) return;
            if (e.Data.StartsWith("AUDIO_ACTIVE", StringComparison.OrdinalIgnoreCase)) SetStatus($"Active → {output.Name}");
            else if (e.Data.StartsWith("AUDIO_IDLE", StringComparison.OrdinalIgnoreCase)) SetStatus($"Ready → {output.Name}");
            else if (e.Data.StartsWith("READY", StringComparison.OrdinalIgnoreCase)) SetStatus($"Ready → {output.Name}");
            else if (e.Data.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase)) SetStatus(e.Data);
        };
        bridgeProcess.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) SetStatus("Error: " + e.Data); };
        bridgeProcess.Exited += (_, _) => SetStatus(bridgeDesired ? "Reconnecting…" : "Stopped");
        try
        {
            bridgeProcess.Start();
            bridgeProcess.BeginOutputReadLine();
            bridgeProcess.BeginErrorReadLine();
            SetStatus($"Starting → {output.Name}");
        }
        catch (Exception ex) { bridgeProcess?.Dispose(); bridgeProcess = null; SetStatus("Error: " + ex.Message); }
    }

    void StopBridgeProcess(bool updateStatus = true)
    {
        if (bridgeProcess is { HasExited: false })
        {
            try { bridgeProcess.Kill(entireProcessTree: true); bridgeProcess.WaitForExit(2000); } catch { }
        }
        bridgeProcess?.Dispose();
        bridgeProcess = null;
        if (updateStatus) SetStatus("Stopped");
    }

    void CheckProcess()
    {
        bool wasReady = setupStatus.Ready;
        bool wasDefault = setupStatus.IsDefault;
        string previousSetupSummary = setupStatus.Summary;
        RefreshSetupStatus();
        if (wasReady != setupStatus.Ready || wasDefault != setupStatus.IsDefault
            || !previousSetupSummary.Equals(setupStatus.Summary, StringComparison.Ordinal))
            RebuildMenu();

        if (bridgeProcess is { HasExited: true })
        {
            bridgeProcess.Dispose();
            bridgeProcess = null;
            if (bridgeDesired) StartBridge();
            else SetStatus("Stopped");
        }
    }

    string? FindBridgeExecutable()
    {
        string baseDirectory = AppContext.BaseDirectory;
        string[] candidates = [
            Path.Combine(baseDirectory, "SpatialBridge.exe"),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", "bin", "Release", "net10.0-windows", "SpatialBridge.exe")),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", "bin", "Release", "net10.0-windows", "win-x64", "publish", "SpatialBridge.exe"))
        ];
        return candidates.FirstOrDefault(File.Exists);
    }

    void ShowSettings(bool expanded = false)
    {
        RefreshDevices();
        RefreshSetupStatus();
        RefreshSpatialStatus();
        RebuildMenu();
        if (expanded) settings.ShowExpanded();
        else settings.ShowCompact();
        settings.Show();
        settings.Activate();
    }

    void SetStatus(string value)
    {
        if (settings.InvokeRequired)
        {
            settings.BeginInvoke(new Action(() => SetStatus(value)));
            return;
        }
        status = value;
        RebuildMenu();
    }

    void RefreshSpatialStatus()
    {
        spatialCapability = string.IsNullOrWhiteSpace(selectedOutputId)
            ? SpatialCapability.Unavailable("Select an output")
            : SpatialDetection.Check(selectedOutputId);
    }

    void RefreshSetupStatus()
    {
        var saved = ConfigurationStore.Load();
        setupStatus = AudioSetupInspection.Inspect(saved?.GameRenderId);
    }

    public async Task ApplyListeningDeviceAsync(AudioEndpoint output)
    {
        RefreshDevices();
        var input = AudioConfigurator.FindGameEndpoint(gameInputs, selectedGameInputId);
        if (input is null)
        {
            SetStatus("Error: VB-CABLE 16-channel driver not detected — install the VB-CABLE Driver Pack");
            var answer = MessageBox.Show(settings,
                "The VB-CABLE 16-channel driver was not detected. Install the signed VB-CABLE Driver Pack, then reopen this app.\n\nOpen the official VB-Audio download page?",
                "VB-CABLE is required", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (answer == DialogResult.Yes)
                Process.Start(new ProcessStartInfo("https://vb-audio.com/Cable/") { UseShellExecute = true });
            return;
        }

        var gameEndpoint = AudioConfigurator.FindGameEndpoint(EndpointDiscovery.GetAll("Render"), input.Id);
        if (gameEndpoint is null)
        {
            var answer = MessageBox.Show(settings,
                "The VB-CABLE 16-channel playback endpoint was not found. Install the signed VB-CABLE Driver Pack, then reopen this app.\n\nOpen the official VB-Audio download page?",
                "VB-CABLE is required", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (answer == DialogResult.Yes)
                Process.Start(new ProcessStartInfo("https://vb-audio.com/Cable/") { UseShellExecute = true });
            return;
        }

        SelectDevices(input, output, restart: false);
        if (setupStatus.Ready)
        {
            if (!spatialCapability.Enabled)
            {
                SetStatus("Error: enable Spatial Sound on the selected headphones");
                return;
            }
            StopBridgeProcess();
            StartBridge();
            return;
        }

        bridgeDesired = false;
        StopBridgeProcess();
        try
        {
            var info = new ProcessStartInfo(Application.ExecutablePath)
            {
                UseShellExecute = true,
                Verb = "runas"
            };
            info.ArgumentList.Add("--configure-audio");
            info.ArgumentList.Add("--game-endpoint");
            info.ArgumentList.Add(gameEndpoint.Id);
            using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not launch audio configuration.");
            await process.WaitForExitAsync();
            if (process.ExitCode != 0) throw new InvalidOperationException($"Audio configuration exited with code {process.ExitCode}.");
            RefreshDevices();
            RefreshSetupStatus();
            RefreshSpatialStatus();
            SetStatus("Audio configured — starting bridge");
            if (setupStatus.Ready && spatialCapability.Enabled) StartBridge();
            else SetStatus(!setupStatus.Ready ? "Audio setup incomplete" : "Enable Spatial Sound on the selected headphones");
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            SetStatus("Configuration cancelled");
        }
        catch (Exception ex)
        {
            SetStatus("Error configuring audio: " + ex.Message);
            MessageBox.Show(settings, ex.Message, "Audio configuration failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    void ExitApplication()
    {
        if (exiting) return;
        exiting = true;
        bridgeDesired = false;
        StopBridgeProcess(updateStatus: false);
        trayIcon.Visible = false;
        trayIcon.Dispose();
        offIcon.Dispose();
        stoppedIcon.Dispose();
        readyIcon.Dispose();
        activeIcon.Dispose();
        settings.Dispose();
        ExitThread();
    }
}

static class Program
{
    static string? OptionValue(string[] args, string option)
    {
        int index = Array.FindIndex(args, value => value.Equals(option, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    [STAThread]
    static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Contains("--configure-audio", StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var warnings = AudioConfigurator.Configure(
                    OptionValue(args, "--game-endpoint"));
                string diagnosticsDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "WindowsSpatialBridge");
                Directory.CreateDirectory(diagnosticsDirectory);
                string diagnosticsPath = Path.Combine(diagnosticsDirectory, "last-configuration.txt");
                File.WriteAllLines(diagnosticsPath, warnings.Count == 0
                    ? ["Configuration completed without warnings."]
                    : warnings);
                return 0;
            }
            catch (Exception ex)
            {
                string diagnosticsDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "WindowsSpatialBridge");
                Directory.CreateDirectory(diagnosticsDirectory);
                File.WriteAllText(Path.Combine(diagnosticsDirectory, "last-configuration.txt"), ex.ToString());
                MessageBox.Show(ex.Message, "Windows Spatial Bridge configuration failed",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
        Application.Run(new TrayContext());
        return 0;
    }
}
