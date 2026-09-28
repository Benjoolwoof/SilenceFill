using System.Diagnostics;
using System.Text.Json;
using Windows.Media.Control;

namespace SilenceFill;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

internal sealed class Settings
{
    public HashSet<string> SelectedApps { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int QuietSeconds { get; set; } = 4;
    public bool Enabled { get; set; } = true;

    public static string PathOnDisk => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SilenceFill", "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(PathOnDisk))
            {
                var value = JsonSerializer.Deserialize<Settings>(File.ReadAllText(PathOnDisk));
                if (value != null)
                {
                    value.SelectedApps = new HashSet<string>(value.SelectedApps ?? [], StringComparer.OrdinalIgnoreCase);
                    value.QuietSeconds = Math.Clamp(value.QuietSeconds, 2, 15);
                    return value;
                }
            }
        }
        catch (Exception) { /* Invalid settings should not prevent the app opening. */ }
        return new Settings();
    }

    public void Save()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathOnDisk)!);
        File.WriteAllText(PathOnDisk, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}

internal sealed class MainForm : Form
{
    private readonly Settings settings = Settings.Load();
    private readonly CheckedListBox apps = new() { CheckOnClick = true, Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly Label status = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly CheckBox enabled = new() { Text = "Enabled", AutoSize = true };
    private readonly NumericUpDown quietSeconds = new() { Minimum = 2, Maximum = 15, Width = 48 };
    private readonly NotifyIcon tray = new() { Visible = true, Text = "SilenceFill" };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 400 };
    private readonly SpotifyControl spotify = new();
    private DateTime lastOtherSound = DateTime.UtcNow;
    private DateTime lastRefresh = DateTime.MinValue;
    private DateTime lastSpotifyCommand = DateTime.MinValue;
    private bool pausedByUs;
    private bool closingForReal;
    private bool updatingList;
    private bool busy;

    public MainForm()
    {
        Text = "SilenceFill — Spotify between sounds";
        Width = 510;
        Height = 460;
        MinimumSize = new Size(400, 330);
        StartPosition = FormStartPosition.CenterScreen;

        var intro = new Label
        {
            Text = "Check the open apps that should pause Spotify when they make sound. Spotify resumes after they stay quiet.",
            Dock = DockStyle.Fill,
            AutoSize = false
        };
        var refresh = new Button { Text = "Refresh open apps", AutoSize = true };
        refresh.Click += (_, _) => RefreshApps(force: true);

        var top = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        top.Controls.Add(enabled);
        top.Controls.Add(new Label { Text = "Resume after quiet for", AutoSize = true, Padding = new Padding(10, 5, 0, 0) });
        top.Controls.Add(quietSeconds);
        top.Controls.Add(new Label { Text = "seconds", AutoSize = true, Padding = new Padding(0, 5, 0, 0) });

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), RowCount = 5, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.Controls.Add(intro, 0, 0);
        layout.Controls.Add(top, 0, 1);
        layout.Controls.Add(apps, 0, 2);
        layout.Controls.Add(refresh, 0, 3);
        layout.Controls.Add(status, 0, 4);
        Controls.Add(layout);

        enabled.Checked = settings.Enabled;
        quietSeconds.Value = settings.QuietSeconds;
        enabled.CheckedChanged += (_, _) => { settings.Enabled = enabled.Checked; settings.Save(); };
        quietSeconds.ValueChanged += (_, _) => { settings.QuietSeconds = (int)quietSeconds.Value; settings.Save(); };
        apps.ItemCheck += Apps_ItemCheck;

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open settings", null, (_, _) => ShowSettings());
        menu.Items.Add("Exit", null, (_, _) => { closingForReal = true; Close(); });
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        tray.Icon = Icon;
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => ShowSettings();

        FormClosing += (_, e) =>
        {
            if (!closingForReal)
            {
                e.Cancel = true;
                Hide();
            }
        };
        FormClosed += (_, _) => { timer.Stop(); tray.Visible = false; tray.Dispose(); };

        RefreshApps(force: true);
        timer.Tick += async (_, _) => await TickAsync();
        timer.Start();
    }

    private void ShowSettings()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
        RefreshApps(force: true);
    }

    private void Apps_ItemCheck(object? sender, ItemCheckEventArgs e)
    {
        if (updatingList || apps.Items[e.Index] is not AppItem item) return;
        if (e.NewValue == CheckState.Checked) settings.SelectedApps.Add(item.Name);
        else settings.SelectedApps.Remove(item.Name);
        settings.Save();
        if (settings.SelectedApps.Count == 0) pausedByUs = false;
    }

    private void RefreshApps(bool force = false, IReadOnlyList<AudioSession>? sessions = null)
    {
        if (!force && DateTime.UtcNow - lastRefresh < TimeSpan.FromSeconds(5)) return;
        lastRefresh = DateTime.UtcNow;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    try
                    {
                        if (process.MainWindowHandle != IntPtr.Zero && !IsExcluded(process.ProcessName)) names.Add(process.ProcessName);
                    }
                    catch (Exception) { }
                }
            }
            sessions ??= AudioSessions.Snapshot();
            foreach (var session in sessions)
                if (!IsExcluded(session.ProcessName)) names.Add(session.ProcessName);

            var ordered = names.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
            if (ordered.SequenceEqual(apps.Items.Cast<AppItem>().Select(x => x.Name), StringComparer.OrdinalIgnoreCase)) return;
            updatingList = true;
            apps.BeginUpdate();
            apps.Items.Clear();
            foreach (var name in ordered)
                apps.Items.Add(new AppItem(name), settings.SelectedApps.Contains(name));
        }
        catch (Exception e) { status.Text = "Could not refresh app list: " + e.Message; }
        finally { updatingList = false; apps.EndUpdate(); }
    }

    private static bool IsExcluded(string name) =>
        string.IsNullOrWhiteSpace(name) || name.Equals("Spotify", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("SilenceFill", StringComparison.OrdinalIgnoreCase);

    private async Task TickAsync()
    {
        if (busy) return;
        busy = true;
        try
        {
            var sessions = AudioSessions.Snapshot();
            RefreshApps(sessions: sessions);
            if (!settings.Enabled || settings.SelectedApps.Count == 0)
            {
                status.Text = settings.Enabled ? "Select an app to begin." : "Paused in settings.";
                lastOtherSound = DateTime.UtcNow;
                return;
            }

            // An active session with a real signal counts as sound. Spotify itself is never a trigger.
            bool otherSound = sessions.Any(s => settings.SelectedApps.Contains(s.ProcessName) && s.Peak >= 0.008f);
            var now = DateTime.UtcNow;
            if (otherSound) lastOtherSound = now;

            if (otherSound)
            {
                status.Text = "Selected app is making sound" + (pausedByUs ? " — Spotify paused." : ".");
                if (!pausedByUs && now - lastSpotifyCommand > TimeSpan.FromSeconds(1.5))
                {
                    // Only resume later if Spotify was actually playing when we intervened.
                    bool spotifyPlaying = await spotify.IsPlayingAsync();
                    if (spotifyPlaying && await spotify.PauseAsync())
                    {
                        pausedByUs = true;
                        lastSpotifyCommand = now;
                    }
                }
            }
            else if (pausedByUs)
            {
                var remaining = TimeSpan.FromSeconds(settings.QuietSeconds) - (now - lastOtherSound);
                if (remaining > TimeSpan.Zero)
                    status.Text = $"Quiet — Spotify resumes in {Math.Ceiling(remaining.TotalSeconds)} s.";
                else if (now - lastSpotifyCommand > TimeSpan.FromSeconds(1.5))
                {
                    lastSpotifyCommand = now;
                    if (await spotify.PlayAsync())
                    {
                        status.Text = "Quiet — Spotify resumed.";
                        pausedByUs = false;
                    }
                    else status.Text = "Spotify is unavailable; waiting to resume it.";
                }
            }
            else status.Text = "Quiet — Spotify stays as you left it.";
        }
        catch (Exception e) { status.Text = "Audio check failed: " + e.Message; }
        finally { busy = false; }
    }

    private sealed record AppItem(string Name)
    {
        public override string ToString() => Name;
    }
}

internal sealed class SpotifyControl
{
    private GlobalSystemMediaTransportControlsSessionManager? manager;

    private async Task<GlobalSystemMediaTransportControlsSession?> GetSessionAsync()
    {
        try
        {
            manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            return manager.GetSessions().FirstOrDefault(s =>
                s.SourceAppUserModelId.Contains("Spotify", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception) { }
        return null;
    }

    public async Task<bool> IsPlayingAsync()
    {
        var session = await GetSessionAsync();
        return session?.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
    }

    public async Task<bool> PauseAsync()
    {
        var session = await GetSessionAsync();
        try { return session != null && await session.TryPauseAsync(); }
        catch (Exception) { return false; }
    }

    public async Task<bool> PlayAsync()
    {
        var session = await GetSessionAsync();
        try { return session != null && await session.TryPlayAsync(); }
        catch (Exception) { return false; }
    }
}

