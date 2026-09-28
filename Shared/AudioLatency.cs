internal sealed record AudioLatencyProfile(int Milliseconds, string Label, string Description);

internal static class AudioLatency
{
    public const int DefaultMilliseconds = 20;

    public static IReadOnlyList<AudioLatencyProfile> Profiles { get; } =
    [
        new(10, "Low — 10 ms", "Lowest stable latency on the tested path"),
        new(20, "Balanced — 20 ms", "More protection against occasional crackling"),
        new(30, "Safe — 30 ms", "Most protection for less consistent audio devices")
    ];

    public static bool IsSupported(int milliseconds) =>
        Profiles.Any(profile => profile.Milliseconds == milliseconds);

    public static int Normalize(int milliseconds) =>
        IsSupported(milliseconds) ? milliseconds : DefaultMilliseconds;

    public static int FramesForMilliseconds(int milliseconds, int sampleRate)
    {
        if (milliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(milliseconds));
        if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        return checked((sampleRate * milliseconds + 999) / 1000);
    }

    public static int QueueFrames(int milliseconds, int sampleRate, int captureFrames, int spatialFrames)
    {
        if (captureFrames <= 0) throw new ArgumentOutOfRangeException(nameof(captureFrames));
        if (spatialFrames <= 0) throw new ArgumentOutOfRangeException(nameof(spatialFrames));

        int granularity = GreatestCommonDivisor(captureFrames, spatialFrames);
        int minimumFrames = Math.Max(granularity, captureFrames - granularity);
        int requestedFrames = FramesForMilliseconds(milliseconds, sampleRate);
        int frames = Math.Max(minimumFrames, requestedFrames);
        return checked(((frames + granularity - 1) / granularity) * granularity);
    }

    public static int GreatestCommonDivisor(int left, int right)
    {
        while (right != 0) (left, right) = (right, left % right);
        return Math.Abs(left);
    }
}
