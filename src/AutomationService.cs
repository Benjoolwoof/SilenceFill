namespace SilenceFill;

internal sealed record AutomationUpdate(PlaybackDecision Decision, AudioScanResult Audio,
    SpotifySnapshot Spotify, IReadOnlyList<string> AvailableApps, DateTime TimeUtc);

internal sealed class AutomationService : IDisposable
{
    private readonly Func<Settings> getSettings;
    private readonly Action<AutomationUpdate> onUpdate;
    private readonly PlaybackEngine engine = new();
    private readonly SpotifyControl spotify = new();
    private readonly CancellationTokenSource cancellation = new();
    private readonly Task loop;
    private IReadOnlyList<string> availableApps = [];
    private DateTime lastAppRefresh;
    private bool legacyRunning;

    public AutomationService(Func<Settings> getSettings, Action<AutomationUpdate> onUpdate)
    {
        this.getSettings = getSettings;
        this.onUpdate = onUpdate;
        loop = Task.Run(RunAsync);
    }

    private async Task RunAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(400));
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.UtcNow;
                var settings = getSettings();
                var audio = AudioSessions.Scan();
                var media = await spotify.SnapshotAsync(now);
                if (now - lastAppRefresh >= TimeSpan.FromSeconds(5))
                {
                    availableApps = AppCatalog.Find(audio.Sessions);
                    legacyRunning = AppCatalog.HasOtherSilenceFillProcess();
                    lastAppRefresh = now;
                }
                var (threshold, debounce) = settings.Detection;
                var triggers = audio.Sessions.Where(x => settings.SelectedApps.Contains(x.ProcessName) && x.Peak >= threshold)
                    .Select(x => x.ProcessName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                var observation = new EngineObservation(now, settings.Enabled && !legacyRunning, settings.SelectedApps.Count > 0,
                    audio.Healthy, triggers, media.State, media.SessionId, settings.QuietSeconds, debounce);
                var decision = engine.Evaluate(observation);
                if (legacyRunning && decision.Command == PlaybackCommand.None)
                    decision = decision with { Status = "Older SilenceFill running",
                        Detail = "Exit the older SilenceFill from its tray icon before this version can automate Spotify." };
                if (decision.Command != PlaybackCommand.None)
                {
                    var succeeded = await spotify.CommandAsync(decision.Command, media.SessionId);
                    engine.CommandCompleted(decision.Command, succeeded, media.SessionId, now);
                    if (!succeeded) decision = decision with { Status = "Spotify command failed", Detail = "Windows did not accept the playback command; retrying when safe." };
                }
                onUpdate(new AutomationUpdate(decision, audio, media, availableApps, now));
            }
            catch (Exception e)
            {
                var now = DateTime.UtcNow;
                var safe = engine.Evaluate(new EngineObservation(now, false, true, false, [],
                    SpotifyPlaybackState.Unavailable, null, 4, 600));
                try { onUpdate(new AutomationUpdate(safe with { Status = "Monitor retrying", Detail = e.Message },
                    new AudioScanResult([], false, e.Message), new SpotifySnapshot(SpotifyPlaybackState.Unavailable, null, null), availableApps, now)); }
                catch (Exception) { }
            }
            try { if (!await timer.WaitForNextTickAsync(cancellation.Token)) break; }
            catch (OperationCanceledException) { break; }
        }
    }

    public void Dispose()
    {
        cancellation.Cancel();
        try { loop.Wait(TimeSpan.FromSeconds(2)); } catch (Exception) { }
        cancellation.Dispose();
    }
}
