namespace LyricSyncForPlex.Core.Models;

public sealed record Config
{
    private const double MinPlaybackSpeed = 0.5;
    private const double MaxPlaybackSpeed = 2.0;

    public string PlexBaseUrl { get; set; } = "";
    public string PlexToken { get; set; } = "";
    public int SyncedLyricLines { get; set; } = 3;
    public bool ShowJapaneseTransliteration { get; set; }
    public double PlaybackSpeed { get; set; } = 1.0;

    public static double ClampPlaybackSpeed(double playbackSpeed) =>
        double.IsNaN(playbackSpeed) || double.IsInfinity(playbackSpeed)
            ? 1.0
            : double.Clamp(playbackSpeed, MinPlaybackSpeed, MaxPlaybackSpeed);

    public void Normalize()
    {
        PlaybackSpeed = ClampPlaybackSpeed(PlaybackSpeed);
    }
}
