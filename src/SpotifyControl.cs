using System.Diagnostics;
using Windows.Media.Control;

namespace SilenceFill;

internal sealed record SpotifySnapshot(SpotifyPlaybackState State, string? SessionId, NowPlaying? Media);

internal sealed class SpotifyControl
{
    private GlobalSystemMediaTransportControlsSessionManager? manager;
    private DateTime lastMediaUtc;
    private NowPlaying? cachedMedia;

    private async Task<GlobalSystemMediaTransportControlsSession?> SessionAsync()
    {
        try
        {
            manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            return manager.GetSessions().FirstOrDefault(s =>
                s.SourceAppUserModelId.Contains("Spotify", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception) { manager = null; return null; }
    }

    public async Task<SpotifySnapshot> SnapshotAsync(DateTime now)
    {
        var session = await SessionAsync();
        if (session == null) return new(SpotifyPlaybackState.Unavailable, null, null);
        try
        {
            var status = session.GetPlaybackInfo().PlaybackStatus;
            var state = status switch
            {
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => SpotifyPlaybackState.Playing,
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => SpotifyPlaybackState.Paused,
                _ => SpotifyPlaybackState.Other
            };
            if (now - lastMediaUtc >= TimeSpan.FromSeconds(2))
            {
                var media = await session.TryGetMediaPropertiesAsync();
                cachedMedia = string.IsNullOrWhiteSpace(media?.Title) ? null :
                    new NowPlaying(media.Title.Trim(), media.Artist?.Trim() ?? "", state == SpotifyPlaybackState.Playing);
                lastMediaUtc = now;
            }
            var id = session.SourceAppUserModelId + ":" + SpotifyProcessIdentity();
            return new(state, id, cachedMedia == null ? null : cachedMedia with { IsPlaying = state == SpotifyPlaybackState.Playing });
        }
        catch (Exception) { manager = null; return new(SpotifyPlaybackState.Unavailable, null, null); }
    }

    private static string SpotifyProcessIdentity()
    {
        try
        {
            var values = new List<string>();
            foreach (var process in Process.GetProcessesByName("Spotify"))
            {
                using (process)
                {
                    try { values.Add($"{process.Id}:{process.StartTime.ToUniversalTime().Ticks}"); }
                    catch (Exception) { values.Add(process.Id.ToString()); }
                }
            }
            values.Sort(StringComparer.Ordinal);
            return string.Join(",", values);
        }
        catch (Exception) { return "unknown"; }
    }

    public async Task<bool> CommandAsync(PlaybackCommand command, string? expectedId)
    {
        var snapshot = await SnapshotAsync(DateTime.UtcNow);
        if (snapshot.SessionId != expectedId) return false;
        if (command == PlaybackCommand.Pause && snapshot.State != SpotifyPlaybackState.Playing) return false;
        if (command == PlaybackCommand.Play && snapshot.State != SpotifyPlaybackState.Paused) return false;
        var session = await SessionAsync();
        if (session == null) return false;
        try { return command == PlaybackCommand.Pause ? await session.TryPauseAsync() : await session.TryPlayAsync(); }
        catch (Exception) { return false; }
    }
}
