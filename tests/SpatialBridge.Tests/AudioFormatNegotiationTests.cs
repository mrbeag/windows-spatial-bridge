namespace SpatialBridgeTray;

using System.Runtime.InteropServices;

public sealed class AudioFormatNegotiationTests
{
    [Fact]
    public void EndpointFormat_MatchesWindowsEightChannelSelection()
    {
        var (endpoint, _) = AudioConfigurator.CreateCableFormats();

        Assert.Equal(18, Marshal.SizeOf<AudioConfigurator.WaveFormatEx>());
        Assert.Equal(40, Marshal.SizeOf<AudioConfigurator.WaveFormatExtensible>());
        Assert.Equal(8, endpoint.Format.Channels);
        Assert.Equal(48000u, endpoint.Format.SamplesPerSec);
        Assert.Equal(24, endpoint.Format.BitsPerSample);
        Assert.Equal(24, endpoint.ValidBitsPerSample);
        Assert.Equal(24, endpoint.Format.BlockAlign);
        Assert.Equal(1_152_000u, endpoint.Format.AvgBytesPerSec);
        Assert.Equal(0u, endpoint.ChannelMask);
        Assert.Equal(new Guid("00000001-0000-0010-8000-00AA00389B71"), endpoint.SubFormat);
    }

    [Fact]
    public void SharedMixFormat_MatchesWindowsEightChannelSelection()
    {
        var (_, mix) = AudioConfigurator.CreateCableFormats();

        Assert.Equal(8, mix.Format.Channels);
        Assert.Equal(48000u, mix.Format.SamplesPerSec);
        Assert.Equal(32, mix.Format.BitsPerSample);
        Assert.Equal(32, mix.ValidBitsPerSample);
        Assert.Equal(32, mix.Format.BlockAlign);
        Assert.Equal(1_536_000u, mix.Format.AvgBytesPerSec);
        Assert.Equal(0u, mix.ChannelMask);
        Assert.Equal(new Guid("00000003-0000-0010-8000-00AA00389B71"), mix.SubFormat);
    }

    [Fact]
    public void BinaryLayouts_MatchTheFormatsWrittenByWindowsSettings()
    {
        var (endpoint, mix) = AudioConfigurator.CreateCableFormats();

        Assert.Equal(
        [
            0xFE, 0xFF, 0x08, 0x00, 0x80, 0xBB, 0x00, 0x00,
            0x00, 0x94, 0x11, 0x00, 0x18, 0x00, 0x18, 0x00,
            0x16, 0x00, 0x18, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x10, 0x00,
            0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71,
        ], ToBytes(endpoint));

        Assert.Equal(
        [
            0xFE, 0xFF, 0x08, 0x00, 0x80, 0xBB, 0x00, 0x00,
            0x00, 0x70, 0x17, 0x00, 0x20, 0x00, 0x20, 0x00,
            0x16, 0x00, 0x20, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x10, 0x00,
            0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71,
        ], ToBytes(mix));
    }

    static byte[] ToBytes<T>(T value) where T : struct
    {
        int size = Marshal.SizeOf<T>();
        IntPtr pointer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(value, pointer, false);
            var bytes = new byte[size];
            Marshal.Copy(pointer, bytes, 0, size);
            return bytes;
        }
        finally { Marshal.FreeHGlobal(pointer); }
    }
}
