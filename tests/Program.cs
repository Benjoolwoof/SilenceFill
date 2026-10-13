using SilenceFill;

var passed = 0;
void Check(bool value, string message)
{
    if (!value) throw new Exception(message);
    passed++;
}

var time = new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);
EngineObservation O(DateTime at, bool sound, SpotifyPlaybackState spotify = SpotifyPlaybackState.Playing,
    string? session = "spotify:1", bool healthy = true, bool enabled = true, bool selected = true,
    string[]? names = null) => new(at, enabled, selected, healthy,
        names ?? (sound ? ["Browser"] : []), spotify, session, 4, 600);

var engine = new PlaybackEngine();
Check(engine.Evaluate(O(time, true)).Command == PlaybackCommand.None, "Debounce should ignore brief sound");
Check(engine.Evaluate(O(time.AddMilliseconds(400), false)).Command == PlaybackCommand.None, "Short notification must not pause");
Check(engine.Evaluate(O(time.AddSeconds(1), true)).Command == PlaybackCommand.None, "Fresh sound starts a new debounce");
var pauseTime = time.AddSeconds(1.7);
Check(engine.Evaluate(O(pauseTime, true, names: ["Browser", "Game"])).Command == PlaybackCommand.Pause,
    "Overlapping sound should pause once");
engine.CommandCompleted(PlaybackCommand.Pause, true, "spotify:1", pauseTime);
Check(engine.Evaluate(O(pauseTime.AddSeconds(2), true, SpotifyPlaybackState.Paused, names: ["Game"])).Command == PlaybackCommand.None,
    "Second sounding app should keep Spotify paused");
Check(engine.Evaluate(O(pauseTime.AddSeconds(3), false, SpotifyPlaybackState.Paused)).Command == PlaybackCommand.None,
    "Quiet countdown should hold pause");
Check(engine.Evaluate(O(pauseTime.AddSeconds(4), false, SpotifyPlaybackState.Paused, healthy: false)).Command == PlaybackCommand.None,
    "Device loss should never resume");
Check(engine.Evaluate(O(pauseTime.AddSeconds(7), false, SpotifyPlaybackState.Paused)).Command == PlaybackCommand.None,
    "Quiet countdown should restart after device recovery");
Check(engine.Evaluate(O(pauseTime.AddSeconds(8.1), false, SpotifyPlaybackState.Paused)).Command == PlaybackCommand.Play,
    "Resume after recovered quiet period");
engine.CommandCompleted(PlaybackCommand.Play, true, "spotify:1", pauseTime.AddSeconds(8.1));
Check(engine.Evaluate(O(pauseTime.AddSeconds(10), false, SpotifyPlaybackState.Playing)).Command == PlaybackCommand.None,
    "Do not play repeatedly");

engine = new PlaybackEngine();
Check(engine.Evaluate(O(time, true, SpotifyPlaybackState.Paused)).Command == PlaybackCommand.None,
    "Do not own an existing manual pause");
Check(engine.Evaluate(O(time.AddSeconds(1), true, SpotifyPlaybackState.Paused)).Command == PlaybackCommand.None,
    "Do not pause again when already paused");

engine = new PlaybackEngine();
engine.Evaluate(O(time, true));
Check(engine.Evaluate(O(time.AddSeconds(1), true)).Command == PlaybackCommand.Pause, "Pause after debounce");
engine.CommandCompleted(PlaybackCommand.Pause, true, "spotify:1", time.AddSeconds(1));
Check(engine.Evaluate(O(time.AddSeconds(3), true, SpotifyPlaybackState.Playing)).Command == PlaybackCommand.None,
    "Manual play cancels ownership");
Check(engine.Evaluate(O(time.AddSeconds(4), true)).Command == PlaybackCommand.None,
    "Manual play suppresses new pause while sound continues");

engine = new PlaybackEngine();
engine.Evaluate(O(time, true));
engine.CommandCompleted(PlaybackCommand.Pause, true, "spotify:1", time.AddSeconds(1));
Check(engine.Evaluate(O(time.AddSeconds(3), false, SpotifyPlaybackState.Paused, "spotify:2")).Command == PlaybackCommand.None,
    "Spotify restart cancels ownership");
Check(!engine.PausedBySilenceFill, "Restart should release ownership");

engine = new PlaybackEngine();
engine.Evaluate(O(time, true));
engine.CommandCompleted(PlaybackCommand.Pause, true, "spotify:1", time.AddSeconds(1));
Check(engine.Evaluate(O(time.AddSeconds(3), false, SpotifyPlaybackState.Paused, enabled: false)).Command == PlaybackCommand.Play,
    "Disabling should restore a pause we own");

var folder = Path.Combine(Path.GetTempPath(), "SilenceFill-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
try
{
    var path = Path.Combine(folder, "settings.json");
    File.WriteAllText(path, "{\"SelectedApps\":[\"browser\",\"Browser\"],\"QuietSeconds\":50,\"Enabled\":true}");
    var migrated = Settings.Load(path);
    Check(migrated.SelectedApps.Count == 1 && migrated.QuietSeconds == 30 && migrated.SchemaVersion == 2,
        "Legacy settings migration and normalization");
    Storage.WriteAtomic(path, System.Text.Json.JsonSerializer.Serialize(migrated));
    File.WriteAllText(path, "broken");
    Check(Settings.Load(path).SelectedApps.Contains("browser"), "Interrupted settings read recovers backup");
    var correctionPath = Path.Combine(folder, "corrections.json");
    Storage.WriteAtomic(correctionPath, "{\"Artist\\u001fSong\":{\"EnergyOffset\":25.0}}");
    var corrections = Storage.Load(correctionPath, new Dictionary<string, TrackCorrection>());
    Check(corrections["Artist\u001fSong"].EnergyOffset == 25, "Song correction survives reload");
    var previousDataDir = Environment.GetEnvironmentVariable("SILENCEFILL_DATA_DIR");
    Environment.SetEnvironmentVariable("SILENCEFILL_DATA_DIR", folder);
    try
    {
        File.WriteAllText(Storage.PathFor("music-insights.json"),
            "[{\"Title\":\"Legacy song\",\"Artist\":\"Artist\",\"Energy\":42,\"Variation\":9}]");
        var oldHistory = (List<TrackSummary>)typeof(MusicInsightsPanel)
            .GetMethod("LoadHistory", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.Invoke(null, null)!;
        Check(oldHistory.Count == 1 && oldHistory[0].Title == "Legacy song", "Legacy history remains readable");
        Storage.WriteAtomic(Storage.PathFor("music-insights.json"),
            System.Text.Json.JsonSerializer.Serialize(new HistoryDocument { Items = oldHistory }));
        var newHistory = (List<TrackSummary>)typeof(MusicInsightsPanel)
            .GetMethod("LoadHistory", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.Invoke(null, null)!;
        Check(newHistory.Count == 1 && newHistory[0].Energy == 42, "Versioned history survives reload");
        File.WriteAllText(Storage.PathFor("song-corrections.json"),
            "{\"Artist\\u001fSong\":{\"EnergyOffset\":25.0}}");
        var legacyCorrections = (Dictionary<string, TrackCorrection>)typeof(MusicInsightsPanel)
            .GetMethod("LoadCorrections", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.Invoke(null, null)!;
        Check(legacyCorrections["Artist\u001fSong"].EnergyOffset == 25, "Legacy correction remains readable");
        Storage.WriteAtomic(Storage.PathFor("song-corrections.json"),
            System.Text.Json.JsonSerializer.Serialize(new CorrectionsDocument { Items = legacyCorrections }));
        var newCorrections = (Dictionary<string, TrackCorrection>)typeof(MusicInsightsPanel)
            .GetMethod("LoadCorrections", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.Invoke(null, null)!;
        Check(newCorrections["Artist\u001fSong"].EnergyOffset == 25, "Versioned correction survives reload");
        var labelsPath = Storage.PathFor("chart-labels.json");
        var loadLabels = typeof(MusicInsightsPanel)
            .GetMethod("LoadLabels", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        File.WriteAllText(labelsPath,
            "{\"SchemaVersion\":2,\"LowEnergy\":\"Low output\",\"HighEnergy\":\"High output\",\"LowerLeft\":\"Low output / even\"}");
        var migratedLabels = (ChartLabels)loadLabels.Invoke(null, null)!;
        Check(migratedLabels.LowEnergy == "Quieter" && migratedLabels.HighEnergy == "Louder" &&
            migratedLabels.LowerLeft == "Quiet + steady" && migratedLabels.SchemaVersion == 3,
            "Old measurement labels migrate to plain language");
        File.WriteAllText(labelsPath, "{\"SchemaVersion\":2,\"LowEnergy\":\"My own label\"}");
        Check(((ChartLabels)loadLabels.Invoke(null, null)!).LowEnergy == "My own label",
            "User customized axis labels remain intact");
    }
    finally { Environment.SetEnvironmentVariable("SILENCEFILL_DATA_DIR", previousDataDir); }
}
finally { Directory.Delete(folder, true); }

var labels = new ChartLabels();
Check(labels.LowEnergy == "Quieter" && !labels.LowerLeft.Contains("Soft") && !labels.UpperRight.Contains("Energetic"),
    "Signal labels must not claim mood from peak data");
Check(InsightRanges.StyleCutoff(0, time) > time.AddSeconds(-6) && InsightRanges.StyleCutoff(9, time) == DateTime.MinValue,
    "Style range filters actual timestamps");
Check(InsightRanges.HistoryCutoff(0, time) > time.AddMinutes(-16) && InsightRanges.HistoryCutoff(8, time) == DateTime.MinValue,
    "History range filters actual timestamps");

Console.WriteLine($"PASS: {passed} checks");
