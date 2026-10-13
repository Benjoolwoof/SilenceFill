using System.Text.Json;
using System.Threading.Channels;

namespace SilenceFill;

internal static class Storage
{
    public static string Folder => Environment.GetEnvironmentVariable("SILENCEFILL_DATA_DIR") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SilenceFill");
    public static string PathFor(string name) => Path.Combine(Folder, name);

    public static T Load<T>(string path, T fallback)
    {
        foreach (var candidate in new[] { path, path + ".bak" })
        {
            try
            {
                if (File.Exists(candidate))
                    return JsonSerializer.Deserialize<T>(File.ReadAllText(candidate)) ?? fallback;
            }
            catch (Exception) { }
        }
        return fallback;
    }

    public static void WriteAtomic(string path, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, json);
        if (File.Exists(path)) File.Replace(temp, path, path + ".bak", true);
        else File.Move(temp, path);
    }
}

internal sealed class WriteQueue : IDisposable
{
    private readonly Channel<(string Path, string Json, bool PurgeBackup)> channel = Channel.CreateUnbounded<(string, string, bool)>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly Task worker;

    public WriteQueue() => worker = Task.Run(async () =>
    {
        await foreach (var item in channel.Reader.ReadAllAsync())
        {
            try
            {
                Storage.WriteAtomic(item.Path, item.Json);
                if (item.PurgeBackup) File.Delete(item.Path + ".bak");
            }
            catch (Exception) { /* The app remains usable; the next change retries saving. */ }
        }
    });

    public void Save<T>(string path, T value) => channel.Writer.TryWrite((path, JsonSerializer.Serialize(value), false));
    public void PurgeAndSave<T>(string path, T value) => channel.Writer.TryWrite((path, JsonSerializer.Serialize(value), true));

    public void Dispose()
    {
        channel.Writer.TryComplete();
        try { worker.Wait(TimeSpan.FromSeconds(3)); } catch (Exception) { }
    }
}

internal sealed class Settings
{
    public int SchemaVersion { get; set; } = 2;
    public HashSet<string> SelectedApps { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int QuietSeconds { get; set; } = 4;
    public bool Enabled { get; set; } = true;
    public string Sensitivity { get; set; } = "Balanced";
    public string Theme { get; set; } = "System";
    public bool SetupCompleted { get; set; }

    public static string PathOnDisk => Storage.PathFor("settings.json");
    public static Settings Load(string? path = null)
    {
        var value = Storage.Load(path ?? PathOnDisk, new Settings());
        value.SelectedApps = new HashSet<string>(value.SelectedApps ?? [], StringComparer.OrdinalIgnoreCase);
        value.QuietSeconds = Math.Clamp(value.QuietSeconds, 0, 30);
        value.Sensitivity = value.Sensitivity is "Sensitive" or "Balanced" or "Relaxed" ? value.Sensitivity : "Balanced";
        value.Theme = value.Theme is "System" or "Light" or "Dark" ? value.Theme : "System";
        value.SchemaVersion = 2;
        return value;
    }

    public Settings Copy() => new()
    {
        SchemaVersion = SchemaVersion,
        SelectedApps = new HashSet<string>(SelectedApps, StringComparer.OrdinalIgnoreCase),
        QuietSeconds = QuietSeconds,
        Enabled = Enabled,
        Sensitivity = Sensitivity,
        Theme = Theme,
        SetupCompleted = SetupCompleted
    };

    public (float Peak, int Debounce) Detection => Sensitivity switch
    {
        "Sensitive" => (0.004f, 250),
        "Relaxed" => (0.018f, 1000),
        _ => (0.008f, 600)
    };
}
