namespace SilenceFill;

internal enum PlaybackCommand { None, Pause, Play }
internal enum SpotifyPlaybackState { Unavailable, Playing, Paused, Other }

internal sealed record EngineObservation(
    DateTime NowUtc,
    bool Enabled,
    bool HasSelectedApps,
    bool SensorHealthy,
    IReadOnlyList<string> TriggerApps,
    SpotifyPlaybackState SpotifyState,
    string? SpotifySessionId,
    int QuietSeconds,
    int SoundDebounceMilliseconds);

internal sealed record PlaybackDecision(
    PlaybackCommand Command,
    string Status,
    string Detail,
    string? TriggerApp,
    bool PausedBySilenceFill);

/// <summary>Owns all pause/resume decisions. Commands change ownership only after success.</summary>
internal sealed class PlaybackEngine
{
    private string? ownedSessionId;
    private DateTime? soundStartedUtc;
    private DateTime? lastSoundUtc;
    private DateTime lastCommandUtc = DateTime.MinValue;
    private bool manualOverrideUntilQuiet;

    public bool PausedBySilenceFill => ownedSessionId != null;

    public PlaybackDecision Evaluate(EngineObservation observation)
    {
        var now = observation.NowUtc;
        var sounding = observation.TriggerApps.Count > 0;
        var trigger = sounding ? string.Join(", ", observation.TriggerApps) : null;
        var quietDelay = TimeSpan.FromSeconds(Math.Clamp(observation.QuietSeconds, 0, 30));
        var debounce = TimeSpan.FromMilliseconds(Math.Clamp(observation.SoundDebounceMilliseconds, 100, 2000));

        if (sounding)
        {
            lastSoundUtc = now;
            soundStartedUtc ??= now;
        }
        else soundStartedUtc = null;

        if (!observation.SensorHealthy)
        {
            if (ownedSessionId != null) lastSoundUtc = now;
            return Decision(PlaybackCommand.None, "Audio unavailable", "Keeping Spotify unchanged until Windows audio recovers.", trigger);
        }

        if (ownedSessionId != null)
        {
            if (observation.SpotifyState == SpotifyPlaybackState.Unavailable ||
                observation.SpotifySessionId != ownedSessionId)
            {
                ownedSessionId = null;
                manualOverrideUntilQuiet = sounding;
                return Decision(PlaybackCommand.None, "Spotify changed", "The previous Spotify session is gone; no resume will be sent.", trigger);
            }
            if (observation.SpotifyState == SpotifyPlaybackState.Playing && CanCommand(now))
            {
                ownedSessionId = null;
                manualOverrideUntilQuiet = sounding;
                return Decision(PlaybackCommand.None, "Manual playback detected", "Leaving Spotify under your control until the triggering sound ends.", trigger);
            }
            if (observation.SpotifyState != SpotifyPlaybackState.Paused && CanCommand(now))
            {
                ownedSessionId = null;
                return Decision(PlaybackCommand.None, "Spotify changed", "Playback state changed; no resume will be sent.", trigger);
            }
        }

        if (!observation.Enabled || !observation.HasSelectedApps)
        {
            manualOverrideUntilQuiet = false;
            if (ownedSessionId != null && CanCommand(now))
                return Decision(PlaybackCommand.Play, "Automation off", "Restoring Spotify because SilenceFill paused it.", null);
            return Decision(PlaybackCommand.None, observation.Enabled ? "Select apps" : "Automation off",
                observation.Enabled ? "Select the apps that should pause Spotify." : "Spotify remains under your control.", null);
        }

        if (manualOverrideUntilQuiet)
        {
            if (!sounding && (!lastSoundUtc.HasValue || now - lastSoundUtc.Value >= quietDelay))
                manualOverrideUntilQuiet = false;
            return Decision(PlaybackCommand.None, "Manual playback", "Waiting for a fresh sound before automating again.", trigger);
        }

        if (sounding)
        {
            if (ownedSessionId != null)
                return Decision(PlaybackCommand.None, "Spotify paused", $"{trigger} is making sound. SilenceFill will resume Spotify after it stays quiet.", trigger);
            if (observation.SpotifyState == SpotifyPlaybackState.Unavailable)
                return Decision(PlaybackCommand.None, "Spotify unavailable", "Open Spotify to enable playback control.", trigger);
            if (observation.SpotifyState != SpotifyPlaybackState.Playing)
                return Decision(PlaybackCommand.None, "Spotify already paused", "SilenceFill will not take ownership of this pause.", trigger);
            if (soundStartedUtc.HasValue && now - soundStartedUtc.Value < debounce)
                return Decision(PlaybackCommand.None, "Checking sound", $"Confirming sound from {trigger} before pausing Spotify.", trigger);
            if (!CanCommand(now) || string.IsNullOrEmpty(observation.SpotifySessionId))
                return Decision(PlaybackCommand.None, "Sound detected", "Waiting for Spotify to be ready.", trigger);
            return Decision(PlaybackCommand.Pause, "Pausing Spotify", $"{trigger} is making sound.", trigger);
        }

        if (ownedSessionId != null)
        {
            var remaining = quietDelay - (now - (lastSoundUtc ?? now));
            if (remaining > TimeSpan.Zero)
                return Decision(PlaybackCommand.None, "Waiting for quiet", $"Spotify resumes in {Math.Ceiling(remaining.TotalSeconds)} s if it stays quiet.", null);
            if (CanCommand(now))
                return Decision(PlaybackCommand.Play, "Resuming Spotify", "Selected apps have stayed quiet.", null);
            return Decision(PlaybackCommand.None, "Waiting for Spotify", "A playback command was just sent.", null);
        }

        return observation.SpotifyState == SpotifyPlaybackState.Unavailable
            ? Decision(PlaybackCommand.None, "Spotify unavailable", "Open Spotify and play music to begin.", null)
            : Decision(PlaybackCommand.None, "Quiet", "Spotify stays as you left it.", null);
    }

    public void CommandCompleted(PlaybackCommand command, bool succeeded, string? sessionId, DateTime nowUtc)
    {
        if (command == PlaybackCommand.None) return;
        lastCommandUtc = nowUtc;
        if (!succeeded) return;
        if (command == PlaybackCommand.Pause && !string.IsNullOrEmpty(sessionId))
            ownedSessionId = sessionId;
        else if (command == PlaybackCommand.Play)
            ownedSessionId = null;
    }

    public void Reset()
    {
        ownedSessionId = null;
        soundStartedUtc = null;
        manualOverrideUntilQuiet = false;
    }

    private bool CanCommand(DateTime now) => now - lastCommandUtc >= TimeSpan.FromSeconds(1.2);

    private PlaybackDecision Decision(PlaybackCommand command, string status, string detail, string? trigger) =>
        new(command, status, detail, trigger, PausedBySilenceFill);
}
