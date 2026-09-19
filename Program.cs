using System.Runtime.InteropServices;

static class Native
{
    public const int CLSCTX_ALL = 23;
    public const int AUDCLNT_SHAREMODE_SHARED = 0;
    public const int AUDCLNT_STREAMFLAGS_LOOPBACK = 0x00020000;
    public const int AUDCLNT_STREAMFLAGS_EVENTCALLBACK = 0x00040000;
    public const uint AUDCLNT_BUFFERFLAGS_SILENT = 0x2;
    public const ushort VT_BLOB = 65;
    public const uint WAIT_OBJECT_0 = 0;

    public static readonly Guid CLSID_MMDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    public static readonly Guid IID_IAudioClient = new("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");
    public static readonly Guid IID_IAudioCaptureClient = new("C8ADBD64-E71E-48A0-A4DE-185C395CD317");
    public static readonly Guid IID_ISpatialAudioClient = new("BBF8E066-AAAA-49BE-9A4D-FD2A858EA27F");
    public static readonly Guid IID_ISpatialAudioObjectRenderStream = new("BAB5F473-B423-477B-85F5-B5A332A04153");

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr CreateEventW(IntPtr attributes, [MarshalAs(UnmanagedType.Bool)] bool manualReset,
        [MarshalAs(UnmanagedType.Bool)] bool initialState, string? name);

    [DllImport("kernel32.dll")]
    public static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint WaitForMultipleObjects(uint count, IntPtr[] handles,
        [MarshalAs(UnmanagedType.Bool)] bool waitAll, uint milliseconds);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr handle);

    public static string Hr(int hr) => $"0x{unchecked((uint)hr):X8}";
    public static void Check(int hr, string operation)
    {
        if (hr < 0) throw new COMException($"{operation} failed ({Hr(hr)})", hr);
    }
}

[StructLayout(LayoutKind.Explicit, Size = 24)]
struct PropVariantBlob
{
    [FieldOffset(0)] public ushort vt;
    [FieldOffset(8)] public uint cbSize;
    [FieldOffset(16)] public IntPtr pBlobData;
}

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceEnumerator
{
    [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
    [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
    [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
    [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDevice
{
    [PreserveSig]
    int Activate(ref Guid iid, int clsCtx, IntPtr activationParams,
        [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    [PreserveSig] int OpenPropertyStore(int access, out IntPtr properties);
    [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    [PreserveSig] int GetState(out int state);
}

[ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioClient
{
    [PreserveSig]
    int Initialize(int shareMode, int streamFlags, long bufferDuration, long periodicity,
        IntPtr format, IntPtr sessionGuid);
    [PreserveSig] int GetBufferSize(out uint frames);
    [PreserveSig] int GetStreamLatency(out long latency);
    [PreserveSig] int GetCurrentPadding(out uint frames);
    [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closestMatch);
    [PreserveSig] int GetMixFormat(out IntPtr format);
    [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
    [PreserveSig] int Start();
    [PreserveSig] int Stop();
    [PreserveSig] int Reset();
    [PreserveSig] int SetEventHandle(IntPtr eventHandle);
    [PreserveSig] int GetService(ref Guid iid, out IntPtr service);
}

[ComImport, Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioCaptureClient
{
    [PreserveSig]
    int GetBuffer(out IntPtr data, out uint frames, out uint flags,
        out ulong devicePosition, out ulong qpcPosition);
    [PreserveSig] int ReleaseBuffer(uint framesRead);
    [PreserveSig] int GetNextPacketSize(out uint frames);
}

[ComImport, Guid("BBF8E066-AAAA-49BE-9A4D-FD2A858EA27F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface ISpatialAudioClient
{
    [PreserveSig] int GetStaticObjectPosition(uint type, out float x, out float y, out float z);
    [PreserveSig] int GetNativeStaticObjectTypeMask(out uint mask);
    [PreserveSig] int GetMaxDynamicObjectCount(out uint value);
    [PreserveSig] int GetSupportedAudioObjectFormatEnumerator(out IntPtr enumerator);
    [PreserveSig] int GetMaxFrameCount(IntPtr objectFormat, out uint frameCountPerBuffer);
    [PreserveSig] int IsAudioObjectFormatSupported(IntPtr objectFormat);
    [PreserveSig] int IsSpatialAudioStreamAvailable(ref Guid streamUuid, IntPtr auxiliaryInfo);
    [PreserveSig]
    int ActivateSpatialAudioStream(ref PropVariantBlob activationParams, ref Guid iid,
        [MarshalAs(UnmanagedType.IUnknown)] out object stream);
}

[ComImport, Guid("BAB5F473-B423-477B-85F5-B5A332A04153"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface ISpatialAudioObjectRenderStream
{
    [PreserveSig] int GetAvailableDynamicObjectCount(out uint value);
    [PreserveSig] int GetService(ref Guid iid, out IntPtr service);
    [PreserveSig] int Start();
    [PreserveSig] int Stop();
    [PreserveSig] int Reset();
    [PreserveSig] int BeginUpdatingAudioObjects(out uint availableDynamicObjectCount, out uint frameCountPerBuffer);
    [PreserveSig] int EndUpdatingAudioObjects();
    [PreserveSig]
    int ActivateSpatialAudioObject(uint type,
        [MarshalAs(UnmanagedType.Interface)] out ISpatialAudioObject audioObject);
}

[ComImport, Guid("DDE28967-521B-46E5-8F00-BD6F2BC8AB1D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface ISpatialAudioObject
{
    [PreserveSig] int GetBuffer(out IntPtr buffer, out uint bufferLength);
    [PreserveSig] int SetEndOfStream(uint frameCount);
    [PreserveSig] int IsActive([MarshalAs(UnmanagedType.Bool)] out bool isActive);
    [PreserveSig] int GetAudioObjectType(out uint audioObjectType);
    [PreserveSig] int SetPosition(float x, float y, float z);
    [PreserveSig] int SetVolume(float volume);
}

sealed class FloatRing
{
    readonly float[] data;
    int read;
    int count;

    public FloatRing(int capacity) => data = new float[capacity];
    public int Count => count;

    public void Write(float[] source, int length)
    {
        if (length >= data.Length)
        {
            Array.Copy(source, length - data.Length, data, 0, data.Length);
            read = 0;
            count = data.Length;
            return;
        }
        while (count + length > data.Length) { read = (read + 1) % data.Length; count--; }
        int write = (read + count) % data.Length;
        int first = Math.Min(length, data.Length - write);
        Array.Copy(source, 0, data, write, first);
        Array.Copy(source, first, data, 0, length - first);
        count += length;
    }

    public int Read(float[] destination, int requested)
    {
        int length = Math.Min(requested, count);
        int first = Math.Min(length, data.Length - read);
        Array.Copy(data, read, destination, 0, first);
        Array.Copy(data, 0, destination, first, length - first);
        read = (read + length) % data.Length;
        count -= length;
        if (length < requested) Array.Clear(destination, length, requested - length);
        return length;
    }

    public int DiscardOldest(int requested)
    {
        int length = Math.Min(requested, count);
        read = (read + length) % data.Length;
        count -= length;
        return length;
    }
}

static class Program
{
    const int Channels = 8;
    const int SampleRate = 48000;

    // Input order is standard 7.1: FL, FR, FC, LFE, BL, BR, SL, SR.
    static readonly uint[] SpatialTypes = [0x2, 0x4, 0x8, 0x10, 0x80, 0x100, 0x20, 0x40];
    static readonly string[] ChannelNames = ["FL", "FR", "FC", "LFE", "BL", "BR", "SL", "SR"];

    static IntPtr MakeMonoFloatFormat()
    {
        IntPtr p = Marshal.AllocCoTaskMem(18);
        for (int i = 0; i < 18; i++) Marshal.WriteByte(p, i, 0);
        Marshal.WriteInt16(p, 0, 3); // WAVE_FORMAT_IEEE_FLOAT
        Marshal.WriteInt16(p, 2, 1);
        Marshal.WriteInt32(p, 4, SampleRate);
        Marshal.WriteInt32(p, 8, SampleRate * 4);
        Marshal.WriteInt16(p, 12, 4);
        Marshal.WriteInt16(p, 14, 32);
        return p;
    }

    static bool ExtensibleIsFloat(IntPtr p)
    {
        ushort tag = unchecked((ushort)Marshal.ReadInt16(p, 0));
        if (tag == 3) return true;
        if (tag != 0xFFFE || Marshal.ReadInt16(p, 16) < 22) return false;
        byte[] guid = new byte[16];
        Marshal.Copy(p + 24, guid, 0, 16);
        return new Guid(guid) == new Guid("00000003-0000-0010-8000-00AA00389B71");
    }

    static int GreatestCommonDivisor(int left, int right)
    {
        while (right != 0) (left, right) = (right, left % right);
        return Math.Abs(left);
    }

    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: SpatialBridge.exe <game-render-endpoint-id> <headphone-render-endpoint-id>");
            Console.Error.WriteLine("Start the bridge through SpatialBridgeTray.exe so the configured endpoints are supplied automatically.");
            return 2;
        }

        string gameRenderId = args[0];
        string renderId = args[1];
        object? enumObject = null, captureAudioObject = null, captureClientObject = null;
        object? spatialClientObject = null, spatialStreamObject = null;
        IntPtr captureFormat = IntPtr.Zero, objectFormat = IntPtr.Zero, activationData = IntPtr.Zero;
        IntPtr captureEvent = IntPtr.Zero, spatialEvent = IntPtr.Zero;
        var spatialObjects = new ISpatialAudioObject?[Channels];

        try
        {
            Console.WriteLine("SpatialBridge: VB-CABLE 7.1 -> Windows Spatial Audio");
            Console.WriteLine("No game injection. Press Ctrl+C to stop.\n");

            Type enumType = Type.GetTypeFromCLSID(Native.CLSID_MMDeviceEnumerator, true)!;
            enumObject = Activator.CreateInstance(enumType)!;
            var enumerator = (IMMDeviceEnumerator)enumObject;

            Native.Check(enumerator.GetDevice(gameRenderId, out IMMDevice captureDevice), "Open 7.1 game render endpoint");
            Native.Check(enumerator.GetDevice(renderId, out IMMDevice renderDevice), "Open selected render endpoint");

            Guid audioIid = Native.IID_IAudioClient;
            Native.Check(captureDevice.Activate(ref audioIid, Native.CLSCTX_ALL, IntPtr.Zero, out captureAudioObject),
                "Activate capture IAudioClient");
            var captureAudio = (IAudioClient)captureAudioObject;
            Native.Check(captureAudio.GetMixFormat(out captureFormat), "Get capture format");
            ushort captureChannels = unchecked((ushort)Marshal.ReadInt16(captureFormat, 2));
            uint captureRate = unchecked((uint)Marshal.ReadInt32(captureFormat, 4));
            ushort captureBits = unchecked((ushort)Marshal.ReadInt16(captureFormat, 14));
            if (captureChannels != Channels || captureRate != SampleRate || captureBits != 32 || !ExtensibleIsFloat(captureFormat))
                throw new InvalidOperationException($"The 7.1 game endpoint must be 8-channel, 48 kHz, 32-bit float; it is {captureChannels}ch/{captureRate}Hz/{captureBits}-bit.");

            captureEvent = Native.CreateEventW(IntPtr.Zero, false, false, null);
            if (captureEvent == IntPtr.Zero) throw new InvalidOperationException("Could not create capture event.");

            Native.Check(captureAudio.Initialize(Native.AUDCLNT_SHAREMODE_SHARED,
                Native.AUDCLNT_STREAMFLAGS_LOOPBACK | Native.AUDCLNT_STREAMFLAGS_EVENTCALLBACK, 0, 0,
                captureFormat, IntPtr.Zero), "Initialize 7.1 game-endpoint loopback");
            Native.Check(captureAudio.SetEventHandle(captureEvent), "Set game-endpoint loopback event");
            Native.Check(captureAudio.GetBufferSize(out uint captureBufferFrames),
                "Get game-endpoint loopback buffer size");
            Guid captureIid = Native.IID_IAudioCaptureClient;
            Native.Check(captureAudio.GetService(ref captureIid, out IntPtr capturePtr), "Get game-endpoint loopback client");
            captureClientObject = Marshal.GetObjectForIUnknown(capturePtr);
            Marshal.Release(capturePtr);
            var capture = (IAudioCaptureClient)captureClientObject;

            Guid spatialIid = Native.IID_ISpatialAudioClient;
            Native.Check(renderDevice.Activate(ref spatialIid, Native.CLSCTX_ALL, IntPtr.Zero, out spatialClientObject),
                "Activate selected-output spatial client");
            var spatialClient = (ISpatialAudioClient)spatialClientObject;
            objectFormat = MakeMonoFloatFormat();
            Native.Check(spatialClient.IsAudioObjectFormatSupported(objectFormat), "Check mono spatial-object format");
            Native.Check(spatialClient.GetMaxFrameCount(objectFormat, out uint maxSpatialFrames),
                "Get maximum spatial frame count");

            spatialEvent = Native.CreateEventW(IntPtr.Zero, false, false, null);
            if (spatialEvent == IntPtr.Zero) throw new InvalidOperationException("Could not create spatial render event.");

            activationData = Marshal.AllocCoTaskMem(40);
            for (int i = 0; i < 40; i++) Marshal.WriteByte(activationData, i, 0);
            Marshal.WriteIntPtr(activationData, 0, objectFormat);
            Marshal.WriteInt32(activationData, 8, unchecked((int)0x1FE)); // FL FR FC LFE SL SR BL BR
            Marshal.WriteInt32(activationData, 12, 0); // min dynamic
            Marshal.WriteInt32(activationData, 16, 0); // max dynamic
            Marshal.WriteInt32(activationData, 20, 6); // AudioCategory_GameEffects
            Marshal.WriteIntPtr(activationData, 24, spatialEvent);
            Marshal.WriteIntPtr(activationData, 32, IntPtr.Zero);

            var pv = new PropVariantBlob { vt = Native.VT_BLOB, cbSize = 40, pBlobData = activationData };
            Guid streamIid = Native.IID_ISpatialAudioObjectRenderStream;
            Native.Check(spatialClient.ActivateSpatialAudioStream(ref pv, ref streamIid, out spatialStreamObject),
                "Activate Windows Spatial stream");
            var spatialStream = (ISpatialAudioObjectRenderStream)spatialStreamObject;

            bool quit = false;
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; quit = true; };
            int spatialFrames = checked((int)maxSpatialFrames);
            int captureCapacityFrames = checked((int)captureBufferFrames);
            int provisionalPrefillFrames = Math.Max(captureCapacityFrames, spatialFrames * 2);
            int ringCapacityFrames = checked(provisionalPrefillFrames + captureCapacityFrames * 2);
            var ring = new FloatRing(ringCapacityFrames * Channels);
            float[] captureBuffer = [];
            float[] interleaved = [];
            float[][] mono = Enumerable.Range(0, Channels).Select(_ => Array.Empty<float>()).ToArray();
            long underrunFrames = 0;
            long discardedFrames = 0;
            long lastReport = Environment.TickCount64;
            long lastAudibleSample = long.MinValue / 2;
            bool audioActive = false;
            int observedCapturePacketFrames = 0;

            void DrainCapture()
            {
                while (true)
                {
                    Native.Check(capture.GetNextPacketSize(out uint packetFrames), "Get next game-endpoint loopback packet size");
                    if (packetFrames == 0) break;
                    Native.Check(capture.GetBuffer(out IntPtr source, out uint frames, out uint flags, out _, out _),
                        "Read game-endpoint loopback packet");
                    observedCapturePacketFrames = observedCapturePacketFrames == 0
                        ? checked((int)frames)
                        : GreatestCommonDivisor(observedCapturePacketFrames, checked((int)frames));
                    int samples = checked((int)frames * Channels);
                    if (captureBuffer.Length < samples) captureBuffer = new float[samples];
                    if ((flags & Native.AUDCLNT_BUFFERFLAGS_SILENT) != 0) Array.Clear(captureBuffer, 0, samples);
                    else
                    {
                        Marshal.Copy(source, captureBuffer, 0, samples);
                        for (int sample = 0; sample < samples; sample++)
                        {
                            if (MathF.Abs(captureBuffer[sample]) > 0.00003f)
                            {
                                lastAudibleSample = Environment.TickCount64;
                                break;
                            }
                        }
                    }
                    ring.Write(captureBuffer, samples);
                    Native.Check(capture.ReleaseBuffer(frames), "Release game-endpoint loopback packet");
                }
            }

            Native.Check(captureAudio.Start(), "Start game-endpoint loopback capture");

            // Anchor the two independently scheduled streams with the minimum complete
            // reserve before starting Spatial Audio. Silent capture packets count too.
            long prefillDeadline = Environment.TickCount64 + 1000;
            while (ring.Count / Channels < provisionalPrefillFrames && Environment.TickCount64 < prefillDeadline)
            {
                if (Native.WaitForSingleObject(captureEvent, 100) == Native.WAIT_OBJECT_0) DrainCapture();
            }
            if (ring.Count / Channels < provisionalPrefillFrames || observedCapturePacketFrames == 0)
                throw new InvalidOperationException("The 7.1 game endpoint did not supply loopback frames in time.");

            int captureFrames = observedCapturePacketFrames;
            int schedulingGranularity = GreatestCommonDivisor(captureFrames, spatialFrames);

            // Keep one complete shared-engine packet. Loopback delivers 480-frame packets
            // on this endpoint and Spatial Audio requests 480 frames at once; retaining less
            // than a packet creates unavoidable partial-block underruns.
            int targetQueuedFrames = Math.Max(schedulingGranularity, captureFrames - schedulingGranularity);
            int prefillFrames = checked(targetQueuedFrames + spatialFrames);
            int startupExcessSamples = Math.Max(0, ring.Count - prefillFrames * Channels);
            discardedFrames += ring.DiscardOldest(startupExcessSamples) / Channels;
            Native.Check(spatialStream.Start(), "Start Windows Spatial stream");

            Console.WriteLine("READY: Windows Spatial stream started with 8 static objects");
            Console.WriteLine("AUDIO_IDLE");
            Console.WriteLine("Input : 7.1 game endpoint loopback — 8ch float, 48 kHz");
            Console.WriteLine("Output: selected device — Windows Spatial Audio static 7.1 objects");
            Console.WriteLine($"Low-latency queue: {targetQueuedFrames} frames ({targetQueuedFrames * 1000.0 / SampleRate:F1} ms), capture {captureFrames}, spatial {spatialFrames}");
            Console.WriteLine("Channel map: FL FR FC LFE BL BR SL SR\n");

            IntPtr[] audioEvents = [captureEvent, spatialEvent];
            while (!quit)
            {
                uint waitResult = Native.WaitForMultipleObjects((uint)audioEvents.Length, audioEvents, false, 100);
                if (waitResult == Native.WAIT_OBJECT_0)
                {
                    DrainCapture();
                    continue;
                }
                if (waitResult != Native.WAIT_OBJECT_0 + 1) continue;

                // Capture may also be ready. Drain it before filling the spatial block.
                DrainCapture();

                Native.Check(spatialStream.BeginUpdatingAudioObjects(out _, out uint frameCount),
                    "Begin spatial update");
                int neededSamples = checked((int)frameCount * Channels);
                if (interleaved.Length < neededSamples) interleaved = new float[neededSamples];

                // Keep only the mathematically required block-size reserve plus the block
                // being rendered. Any older audio is stale and would become growing delay.
                int maxQueuedSamples = checked((targetQueuedFrames + (int)frameCount) * Channels);
                int staleSamples = Math.Max(0, ring.Count - maxQueuedSamples);
                staleSamples -= staleSamples % Channels;
                discardedFrames += ring.DiscardOldest(staleSamples) / Channels;
                int received = ring.Read(interleaved, neededSamples);
                if (received < neededSamples) underrunFrames += (neededSamples - received) / Channels;

                for (int channel = 0; channel < Channels; channel++)
                {
                    if (spatialObjects[channel] == null)
                    {
                        Native.Check(spatialStream.ActivateSpatialAudioObject(SpatialTypes[channel], out var obj),
                            $"Activate {ChannelNames[channel]} object");
                        spatialObjects[channel] = obj;
                    }
                    Native.Check(spatialObjects[channel]!.GetBuffer(out IntPtr destination, out uint byteCount),
                        $"Get {ChannelNames[channel]} buffer");
                    int objectFrames = checked((int)byteCount / sizeof(float));
                    if (mono[channel].Length < objectFrames) mono[channel] = new float[objectFrames];
                    for (int frame = 0; frame < objectFrames; frame++)
                        mono[channel][frame] = frame < frameCount ? interleaved[frame * Channels + channel] : 0f;
                    Marshal.Copy(mono[channel], 0, destination, objectFrames);
                }
                Native.Check(spatialStream.EndUpdatingAudioObjects(), "End spatial update");

                long now = Environment.TickCount64;
                bool activeNow = now - lastAudibleSample < 1500;
                if (activeNow != audioActive)
                {
                    audioActive = activeNow;
                    Console.WriteLine(audioActive ? "AUDIO_ACTIVE" : "AUDIO_IDLE");
                }

                if (now - lastReport >= 5000)
                {
                    Console.WriteLine($"Running — buffered {ring.Count / Channels} frames, total underruns {underrunFrames} frames, stale frames discarded {discardedFrames}");
                    lastReport = now;
                }
            }

            spatialStream.Stop();
            captureAudio.Stop();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            return 1;
        }
        finally
        {
            foreach (var obj in spatialObjects)
                if (obj != null && Marshal.IsComObject(obj)) Marshal.FinalReleaseComObject(obj);
            if (spatialStreamObject != null && Marshal.IsComObject(spatialStreamObject)) Marshal.FinalReleaseComObject(spatialStreamObject);
            if (spatialClientObject != null && Marshal.IsComObject(spatialClientObject)) Marshal.FinalReleaseComObject(spatialClientObject);
            if (captureClientObject != null && Marshal.IsComObject(captureClientObject)) Marshal.FinalReleaseComObject(captureClientObject);
            if (captureAudioObject != null && Marshal.IsComObject(captureAudioObject)) Marshal.FinalReleaseComObject(captureAudioObject);
            if (enumObject != null && Marshal.IsComObject(enumObject)) Marshal.FinalReleaseComObject(enumObject);
            if (activationData != IntPtr.Zero) Marshal.FreeCoTaskMem(activationData);
            if (objectFormat != IntPtr.Zero) Marshal.FreeCoTaskMem(objectFormat);
            if (captureFormat != IntPtr.Zero) Marshal.FreeCoTaskMem(captureFormat);
            if (captureEvent != IntPtr.Zero) Native.CloseHandle(captureEvent);
            if (spatialEvent != IntPtr.Zero) Native.CloseHandle(spatialEvent);
        }
    }
}
