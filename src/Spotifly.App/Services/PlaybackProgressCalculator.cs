namespace Spotifly.App.Services;

public static class PlaybackProgressCalculator
{
    public static double Normalize(long? progressMs, long? durationMs)
    {
        if (!progressMs.HasValue || !durationMs.HasValue || durationMs.Value <= 0)
            return 0;
        return Math.Clamp(progressMs.Value / (double)durationMs.Value, 0, 1);
    }
}
