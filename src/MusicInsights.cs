using System.Drawing.Drawing2D;
using System.Text.Json;
using System.Text;

namespace SilenceFill;

internal sealed record NowPlaying(string Title, string Artist, bool IsPlaying);

internal sealed class TrackSummary
{
    public int SchemaVersion { get; set; } = 2;
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public DateTime StartedUtc { get; set; }
    public double Energy { get; set; }
    public double Variation { get; set; }
    public int SampleCount { get; set; }
    public DateTime? EndedUtc { get; set; }
}

internal sealed class HistoryDocument
{
    public int SchemaVersion { get; set; } = 2;
    public List<TrackSummary> Items { get; set; } = [];
}

internal enum StyleMetric { Energy, Dynamics, Pulse, Peak }

internal sealed record StylePoint(DateTime Time, double Energy, double Dynamics, double Pulse, double Peak)
{
    public double Value(StyleMetric metric) => metric switch
    {
        StyleMetric.Energy => Energy,
        StyleMetric.Dynamics => Dynamics,
        StyleMetric.Pulse => Pulse,
        StyleMetric.Peak => Peak,
        _ => Energy
    };
}

internal sealed class ChartLabels
{
    public string LowEnergy { get; set; } = "Quieter";
    public string HighEnergy { get; set; } = "Louder";
    public string LowVariation { get; set; } = "Steady volume";
    public string HighVariation { get; set; } = "Changing volume";
    public string UpperLeft { get; set; } = "Loud + steady";
    public string UpperRight { get; set; } = "Loud + changing";
    public string LowerLeft { get; set; } = "Quiet + steady";
    public string LowerRight { get; set; } = "Quiet + changing";
    public string HistoryLow { get; set; } = "Quieter";
    public string HistoryHigh { get; set; } = "Louder";
    public StyleMetric HorizontalMetric { get; set; } = StyleMetric.Dynamics;
    public StyleMetric VerticalMetric { get; set; } = StyleMetric.Energy;
    public int DotIntervalSeconds { get; set; } = 2;
    public int StylePeriodIndex { get; set; } = 9;
    public int HistoryPeriodIndex { get; set; } = 3;
    public int SchemaVersion { get; set; } = 3;
}

internal sealed class TrackCorrection
{
    public double? EnergyOffset { get; set; }
    public double? DynamicsOffset { get; set; }
    public double? PulseOffset { get; set; }
    public double? PeakOffset { get; set; }

    public double? Value(StyleMetric metric) => metric switch
    {
        StyleMetric.Energy => EnergyOffset,
        StyleMetric.Dynamics => DynamicsOffset,
        StyleMetric.Pulse => PulseOffset,
        StyleMetric.Peak => PeakOffset,
        _ => null
    };

    public void Set(StyleMetric metric, double value)
    {
        value = Math.Clamp(value, -100, 100);
        switch (metric)
        {
            case StyleMetric.Energy: EnergyOffset = value; break;
            case StyleMetric.Dynamics: DynamicsOffset = value; break;
            case StyleMetric.Pulse: PulseOffset = value; break;
            case StyleMetric.Peak: PeakOffset = value; break;
        }
    }
}

internal sealed class CorrectionsDocument
{
    public int SchemaVersion { get; set; } = 2;
    public Dictionary<string, TrackCorrection> Items { get; set; } = [];
}

internal sealed class MusicInsightsPanel : UserControl
{
    private AppPalette palette = AppPalette.Light;
    private readonly ContextMenuStrip historyMenu = new();
    private readonly WriteQueue writes = new();
    private readonly List<TrackSummary> history = LoadHistory();
    private readonly Dictionary<string, TrackCorrection> corrections = LoadCorrections();
    private ChartLabels labels = LoadLabels();
    private readonly Label trackLabel = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label note = new() { Dock = DockStyle.Fill, ForeColor = Color.FromArgb(95, 105, 115), AutoEllipsis = true };
    private readonly DropSelect view = new() { Width = 205 };
    private readonly DropSelect period = new() { Width = 128 };
    private readonly ThemedStepper dotInterval = new() { Minimum = 1, Maximum = 30, Width = 64 };
    private readonly Label dotIntervalLabel = new() { Text = "Move dot every", AutoSize = true, Padding = new Padding(0, 6, 0, 0) };
    private readonly Label dotIntervalUnit = new() { Text = "seconds", AutoSize = true, Padding = new Padding(0, 6, 0, 0) };
    private readonly Button resetCorrection = new() { Text = "Reset song position", AutoSize = true };
    private readonly InsightCanvas chart;
    private readonly List<StylePoint> trail = [];
    private readonly Queue<(DateTime Time, double Peak)> recentPeaks = new();
    private string? currentKey;
    private string currentTitle = "";
    private string currentArtist = "";
    private DateTime currentStarted;
    private double sum;
    private double sumSquares;
    private int samples;
    private bool playing;
    private DateTime lastTrailPoint;
    private int stylePeriodIndex = 9;
    private int historyPeriodIndex = 3;
    private static FontFamily UiFontFamily => SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif;

    private static string HistoryPath => Storage.PathFor("music-insights.json");
    private static string LabelsPath => Storage.PathFor("chart-labels.json");
    private static string CorrectionsPath => Storage.PathFor("song-corrections.json");

    public MusicInsightsPanel()
    {
        Dock = DockStyle.Fill;
        BackColor = Color.White;
        chart = new InsightCanvas(this) { Dock = DockStyle.Fill };
        stylePeriodIndex = Math.Clamp(labels.StylePeriodIndex, 0, 9);
        historyPeriodIndex = Math.Clamp(labels.HistoryPeriodIndex, 0, 8);

        var header = new Label
        {
            Text = "Music insights",
            Dock = DockStyle.Fill,
            Font = new Font(UiFontFamily, 18, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, 0, 8)
        };
        FlowLayoutPanel Group(params Control[] children)
        {
            var group = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false, Margin = new Padding(0, 0, 12, 10) };
            foreach (var child in children)
            {
                child.Margin = new Padding(0, 0, ReferenceEquals(child, children[^1]) ? 0 : 8, 0);
                group.Controls.Add(child);
            }
            return group;
        }
        var cadenceGroup = Group(dotIntervalLabel, dotInterval, dotIntervalUnit);
        view.Items.AddRange(["Song's sound activity", "Volume over time"]);
        view.SelectedIndex = 0;
        view.SelectedIndexChanged += (_, _) =>
        {
            UpdatePeriods();
            dotIntervalLabel.Visible = dotInterval.Visible = dotIntervalUnit.Visible = view.SelectedIndex == 0;
            cadenceGroup.Visible = view.SelectedIndex == 0;
            note.Text = view.SelectedIndex == 0
                ? "Loudness only, not mood. Drag the dot to adjust."
                : "Measured Spotify volume. Hover for song and time.";
            UpdateDescription();
            chart.Invalidate();
        };
        view.AccessibleName = "Choose music insight view";
        period.AccessibleName = "Choose time period";
        var selector = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty,
            FlowDirection = FlowDirection.LeftToRight };
        selector.Controls.Add(Group(new Label { Text = "Show", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, view));
        selector.Controls.Add(Group(new Label { Text = "Time period", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, period));
        var editLabels = new Button { Text = "Customize chart", AutoSize = true, Margin = new Padding(0, 0, 12, 10) };
        editLabels.Click += (_, _) => EditLabels();
        selector.Controls.Add(editLabels);
        selector.SetFlowBreak(editLabels, true);
        var historyActions = new Button { Text = "History tools ▾", AutoSize = true, Margin = new Padding(0, 0, 12, 10) };
        historyMenu.Items.Add("Export history…", null, (_, _) => ExportHistory());
        historyMenu.Items.Add("Delete history…", null, (_, _) => DeleteHistory());
        historyActions.Click += (_, _) => historyMenu.Show(historyActions, 0, historyActions.Height);
        selector.Controls.Add(historyActions);
        dotInterval.Value = Math.Clamp(labels.DotIntervalSeconds, 1, 30);
        dotInterval.AccessibleName = "Seconds between dot updates";
        dotInterval.ValueChanged += (_, _) =>
        {
            labels.DotIntervalSeconds = (int)dotInterval.Value;
            SaveLabels();
        };
        selector.Controls.Add(cadenceGroup);
        period.SelectedIndexChanged += (_, _) =>
        {
            if (view.SelectedIndex == 0) stylePeriodIndex = period.SelectedIndex;
            else historyPeriodIndex = period.SelectedIndex;
            labels.StylePeriodIndex = stylePeriodIndex;
            labels.HistoryPeriodIndex = historyPeriodIndex;
            SaveLabels();
            UpdateDescription();
            chart.Invalidate();
        };
        UpdatePeriods();

        note.Text = "Loudness only, not mood. Drag the dot to adjust.";
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24, 20, 24, 20),
            ColumnCount = 1,
            RowCount = 5
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(selector, 0, 1);
        var trackRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0, 0, 0, 8) };
        trackRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        trackRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        resetCorrection.Click += (_, _) => ResetSongCorrection();
        trackLabel.Margin = Padding.Empty;
        trackRow.Controls.Add(trackLabel, 0, 0);
        trackRow.Controls.Add(resetCorrection, 1, 0);
        layout.Controls.Add(trackRow, 0, 2);
        chart.Margin = Padding.Empty;
        note.Margin = new Padding(0, 10, 0, 0);
        note.TextAlign = ContentAlignment.MiddleLeft;
        layout.Controls.Add(chart, 0, 3);
        layout.Controls.Add(note, 0, 4);
        Controls.Add(layout);
        Resize += (_, _) =>
        {
            var compact = Width < 650 || Height < 520;
            layout.Padding = compact ? new Padding(16, 12, 16, 12) : new Padding(24, 20, 24, 20);
            layout.RowStyles[0].Height = compact ? 38 : 48;
            layout.RowStyles[1].Height = compact ? 124 : 88;
            layout.RowStyles[2].Height = compact ? 40 : 50;
            layout.RowStyles[4].Height = compact ? 52 : 44;
        };
        UpdateDescription();
    }

    public void ApplyTheme(AppPalette selectedPalette)
    {
        palette = selectedPalette;
        BackColor = palette.Background;
        ForeColor = palette.Text;
        AppTheme.Apply(this, palette);
        AppTheme.StyleMenu(historyMenu, palette);
        note.ForeColor = palette.Muted;
        chart.BackColor = palette.Plot;
        chart.Invalidate();
    }

    private void UpdatePeriods()
    {
        period.Items.Clear();
        if (view.SelectedIndex == 0)
        {
            period.Items.AddRange(["Last 5 seconds", "Last 10 seconds", "Last 15 seconds", "Last 30 seconds",
                "Last minute", "Last 2 minutes", "Last 5 minutes", "Last 10 minutes", "Last 20 minutes", "Full song"]);
            period.SelectedIndex = stylePeriodIndex;
        }
        else
        {
            period.Items.AddRange(["Past 15 minutes", "Past hour", "Past 6 hours", "Today", "Past 24 hours", "Past 7 days", "Past 30 days", "Past 90 days", "All time"]);
            period.SelectedIndex = historyPeriodIndex;
        }
    }

    public void Observe(NowPlaying? media, float peak, DateTime now)
    {
        playing = media?.IsPlaying == true;
        if (media != null)
        {
            var key = media.Artist + "\u001f" + media.Title;
            if (key != currentKey)
            {
                FinishCurrent();
                currentKey = key;
                currentTitle = media.Title;
                currentArtist = media.Artist;
                currentStarted = now;
                sum = sumSquares = 0;
                samples = 0;
                trail.Clear();
                recentPeaks.Clear();
                lastTrailPoint = DateTime.MinValue;
            }
        }
        if (playing && currentKey != null)
        {
            var value = Math.Clamp((double)peak, 0, 1);
            sum += value;
            sumSquares += value * value;
            samples++;
            recentPeaks.Enqueue((now, value));
            while (recentPeaks.Count > 0 && now - recentPeaks.Peek().Time > TimeSpan.FromSeconds(12))
                recentPeaks.Dequeue();
            if (recentPeaks.Count >= 5 && now - lastTrailPoint >= TimeSpan.FromSeconds(labels.DotIntervalSeconds))
            {
                var recentSum = recentPeaks.Sum(p => p.Peak);
                var recentSquares = recentPeaks.Sum(p => p.Peak * p.Peak);
                var peakValue = recentPeaks.Max(p => p.Peak);
                var changes = recentPeaks.Zip(recentPeaks.Skip(1), (a, b) => Math.Abs(b.Peak - a.Peak)).Average();
                trail.Add(new StylePoint(now, EnergyFromPeak(recentSum / recentPeaks.Count),
                    VariationFromSamples(recentSum, recentSquares, recentPeaks.Count),
                    Math.Clamp(Math.Sqrt(changes) * 145, 0, 100), EnergyFromPeak(peakValue)));
                lastTrailPoint = now;
            }
        }
        UpdateDescription();
        chart.Invalidate();
    }

    private void UpdateDescription()
    {
        if (currentKey == null)
        {
            trackLabel.Text = "Open Spotify and play a song to begin.";
            resetCorrection.Visible = false;
            return;
        }
        var name = string.IsNullOrWhiteSpace(currentArtist) ? currentTitle : $"{currentTitle} — {currentArtist}";
        if (view.SelectedIndex == 0)
        {
            var point = trail.LastOrDefault();
            var estimate = point == null ? "Gathering audio…" : $"{EnergyName(point.Energy)}, {VariationName(point.Dynamics)}";
            trackLabel.Text = $"{name}  ·  {estimate}" + (CurrentCorrection == null ? "" : "  ·  Adjusted") + (playing ? "" : "  ·  Paused");
        }
        else
        {
            var filtered = FilteredTracks();
            trackLabel.Text = $"{filtered.Count} songs · {filtered.Sum(x => x.SampleCount)} measured samples" +
                (filtered.Any(x => x.SampleCount == 0) ? " (older sample counts unknown)" : "");
        }
        resetCorrection.Visible = view.SelectedIndex == 0 && CurrentCorrection != null;
    }

    private double CurrentEnergy => samples == 0 ? 0 : EnergyFromPeak(sum / samples);
    private double CurrentVariation => samples == 0 ? 0 : VariationFromSamples(sum, sumSquares, samples);

    private static double EnergyFromPeak(double average) => Math.Clamp(Math.Sqrt(average) * 100, 0, 100);

    private static double VariationFromSamples(double total, double squares, int count)
    {
        if (count < 2) return 0;
        var mean = total / count;
        var deviation = Math.Sqrt(Math.Max(0, squares / count - mean * mean));
        return Math.Clamp(deviation / (mean + 0.02) * 55, 0, 100);
    }

    private static string EnergyName(double value) => value < 30 ? "quieter volume" : value < 60 ? "moderate volume" : "louder volume";
    private static string VariationName(double value) => value < 25 ? "mostly steady" : value < 60 ? "some changes" : "many changes";

    private TrackCorrection? CurrentCorrection => currentKey != null && corrections.TryGetValue(currentKey, out var value) ? value : null;

    private double CorrectedEnergy(string title, string artist, double automatic)
    {
        var key = artist + "\u001f" + title;
        return corrections.TryGetValue(key, out var correction)
            ? Math.Clamp(automatic + (correction.EnergyOffset ?? 0), 0, 100) : automatic;
    }

    private void FinishCurrent()
    {
        if (currentKey == null || samples < 5) return;
        history.Add(new TrackSummary
        {
            Title = currentTitle,
            Artist = currentArtist,
            StartedUtc = currentStarted,
            Energy = CurrentEnergy,
            Variation = CurrentVariation
            ,SampleCount = samples,
            EndedUtc = DateTime.UtcNow
        });
        Save();
    }

    public void Save()
    {
        writes.Save(HistoryPath, new HistoryDocument { Items = history.ToList() });
    }

    public void CloseSession()
    {
        if (currentKey != null)
        {
            FinishCurrent();
            currentKey = null;
        }
        else Save();
        writes.Dispose();
        historyMenu.Dispose();
    }

    private static List<TrackSummary> LoadHistory()
    {
        foreach (var candidate in new[] { HistoryPath, HistoryPath + ".bak" })
        {
            try
            {
                if (!File.Exists(candidate)) continue;
                var json = File.ReadAllText(candidate);
                using var document = JsonDocument.Parse(json);
                var loaded = document.RootElement.ValueKind == JsonValueKind.Array
                    ? JsonSerializer.Deserialize<List<TrackSummary>>(json)
                    : JsonSerializer.Deserialize<HistoryDocument>(json)?.Items;
                if (loaded != null) return loaded;
            }
            catch (Exception) { }
        }
        return [];
    }

    private static Dictionary<string, TrackCorrection> LoadCorrections()
    {
        foreach (var candidate in new[] { CorrectionsPath, CorrectionsPath + ".bak" })
        {
            try
            {
                if (!File.Exists(candidate)) continue;
                var json = File.ReadAllText(candidate);
                using var document = JsonDocument.Parse(json);
                var loaded = document.RootElement.TryGetProperty("Items", out _)
                    ? JsonSerializer.Deserialize<CorrectionsDocument>(json)?.Items
                    : JsonSerializer.Deserialize<Dictionary<string, TrackCorrection>>(json);
                if (loaded != null) return new Dictionary<string, TrackCorrection>(loaded, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception) { }
        }
        return new Dictionary<string, TrackCorrection>(StringComparer.OrdinalIgnoreCase);
    }

    private void SaveCorrections()
    {
        writes.Save(CorrectionsPath, new CorrectionsDocument { Items = new Dictionary<string, TrackCorrection>(corrections) });
    }

    private void ResetSongCorrection()
    {
        if (currentKey == null || !corrections.Remove(currentKey)) return;
        SaveCorrections();
        UpdateDescription();
        chart.Invalidate();
    }

    private static ChartLabels LoadLabels()
    {
        foreach (var candidate in new[] { LabelsPath, LabelsPath + ".bak" })
        {
            try
            {
                if (!File.Exists(candidate)) continue;
                var json = File.ReadAllText(candidate);
                var value = JsonSerializer.Deserialize<ChartLabels>(json) ?? new ChartLabels();
                using var document = JsonDocument.Parse(json);
                var oldVersion = document.RootElement.TryGetProperty(nameof(ChartLabels.SchemaVersion), out var schema)
                    ? schema.GetInt32() : 0;
                if (oldVersion == 0)
                {
                    value.StylePeriodIndex = value.StylePeriodIndex switch
                    {
                        0 => 1, 1 => 3, 2 => 4, 3 => 5, 4 => 6, _ => 9
                    };
                }
                if (oldVersion < 3)
                {
                    if (value.LowEnergy is "Gentle" or "Low output") value.LowEnergy = "Quieter";
                    if (value.HighEnergy is "Driving" or "High output") value.HighEnergy = "Louder";
                    if (value.LowVariation is "Even" or "Even signal") value.LowVariation = "Steady volume";
                    if (value.HighVariation is "Dynamic" or "Changing signal") value.HighVariation = "Changing volume";
                    if (value.UpperLeft is "Bold & steady" or "High output / even") value.UpperLeft = "Loud + steady";
                    if (value.UpperRight is "Bold & shifting" or "High output / changing") value.UpperRight = "Loud + changing";
                    if (value.LowerLeft is "Soft & steady" or "Low output / even") value.LowerLeft = "Quiet + steady";
                    if (value.LowerRight is "Soft & shifting" or "Low output / changing") value.LowerRight = "Quiet + changing";
                    if (value.HistoryLow is "Low energy" or "Low output") value.HistoryLow = "Quieter";
                    if (value.HistoryHigh is "High energy" or "High output") value.HistoryHigh = "Louder";
                    value.SchemaVersion = 3;
                }
                return value;
            }
            catch (Exception) { }
        }
        return new ChartLabels();
    }

    private void SaveLabels()
    {
        writes.Save(LabelsPath, labels);
    }

    private void ExportHistory()
    {
        using var dialog = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv", FileName = "SilenceFill-history.csv" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
        var rows = new List<string> { "started_local,ended_local,title,artist,samples,measured_output,signal_variation" };
        foreach (var item in history)
            rows.Add(string.Join(",", Csv(item.StartedUtc.ToLocalTime().ToString("O")),
                Csv(item.EndedUtc?.ToLocalTime().ToString("O") ?? ""), Csv(item.Title), Csv(item.Artist),
                item.SampleCount, item.Energy.ToString(System.Globalization.CultureInfo.InvariantCulture),
                item.Variation.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        try { File.WriteAllLines(dialog.FileName, rows, Encoding.UTF8); }
        catch (Exception e) { AppTheme.ShowMessage(FindForm()!, palette, "Export failed", e.Message); }
    }

    private void DeleteHistory()
    {
        if (AppTheme.ShowMessage(FindForm()!, palette, "Delete history",
            "Delete all locally stored listening history? Your chart labels and song corrections will stay.", true) != DialogResult.Yes) return;
        history.Clear();
        sum = sumSquares = 0;
        samples = 0;
        trail.Clear();
        recentPeaks.Clear();
        lastTrailPoint = DateTime.MinValue;
        writes.PurgeAndSave(HistoryPath, new HistoryDocument { Items = [] });
        UpdateDescription();
        chart.Invalidate();
    }

    private void EditLabels()
    {
        using var dialog = new Form
        {
            Text = "Customize chart",
            Width = 450,
            Height = 610,
            MinimumSize = new Size(410, 570),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.Sizable,
            ShowInTaskbar = false
        };
        var fields = new (string Caption, string Value)[]
        {
            ("Top axis", labels.HighEnergy), ("Bottom axis", labels.LowEnergy),
            ("Left axis", labels.LowVariation), ("Right axis", labels.HighVariation),
            ("Upper left quadrant", labels.UpperLeft), ("Upper right quadrant", labels.UpperRight),
            ("Lower left quadrant", labels.LowerLeft), ("Lower right quadrant", labels.LowerRight),
            ("History chart high", labels.HistoryHigh), ("History chart low", labels.HistoryLow)
        };
        var inputs = new List<TextBox>();
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 2, RowCount = fields.Length + 3 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < fields.Length; i++)
        {
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            table.Controls.Add(new Label { Text = fields[i].Caption, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, i);
            var input = new TextBox { Text = fields[i].Value, MaxLength = 28, Dock = DockStyle.Fill, AccessibleName = fields[i].Caption };
            table.Controls.Add(input, 1, i);
            inputs.Add(input);
        }
        var horizontalMetric = new DropSelect { Dock = DockStyle.Fill, AccessibleName = "Horizontal measurement" };
        var verticalMetric = new DropSelect { Dock = DockStyle.Fill, AccessibleName = "Vertical measurement" };
        var measures = new object[] { "Overall loudness", "Volume changes", "Pulses", "Biggest moments" };
        horizontalMetric.Items.AddRange(measures);
        verticalMetric.Items.AddRange(measures);
        horizontalMetric.SelectedIndex = (int)labels.HorizontalMetric;
        verticalMetric.SelectedIndex = (int)labels.VerticalMetric;
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        table.Controls.Add(new Label { Text = "Horizontal measure", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, fields.Length);
        table.Controls.Add(horizontalMetric, 1, fields.Length);
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        table.Controls.Add(new Label { Text = "Vertical measure", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, fields.Length + 1);
        table.Controls.Add(verticalMetric, 1, fields.Length + 1);
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var save = new Button { Text = "Save", DialogResult = DialogResult.OK, AutoSize = true, Tag = "primary" };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        table.Controls.Add(buttons, 0, fields.Length + 2);
        table.SetColumnSpan(buttons, 2);
        dialog.AcceptButton = save;
        dialog.CancelButton = cancel;
        dialog.Controls.Add(table);
        dialog.BackColor = palette.Background;
        dialog.ForeColor = palette.Text;
        AppTheme.Apply(dialog, palette);
        dialog.Shown += (_, _) => AppTheme.ApplyTitleBar(dialog, palette);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var values = inputs.Select((input, index) => string.IsNullOrWhiteSpace(input.Text) ? fields[index].Value : input.Text.Trim()).ToArray();
        var xMetric = horizontalMetric.SelectedIndex >= 0 ? (StyleMetric)horizontalMetric.SelectedIndex : StyleMetric.Dynamics;
        var yMetric = verticalMetric.SelectedIndex >= 0 ? (StyleMetric)verticalMetric.SelectedIndex : StyleMetric.Energy;
        if (xMetric == yMetric)
        {
            AppTheme.ShowMessage(FindForm()!, palette, "Customize chart", "Choose different measurements for the two axes.");
            return;
        }
        if (xMetric != labels.HorizontalMetric && values[2] == labels.LowVariation && values[3] == labels.HighVariation)
            (values[2], values[3]) = DefaultAxisLabels(xMetric);
        if (yMetric != labels.VerticalMetric && values[1] == labels.LowEnergy && values[0] == labels.HighEnergy)
            (values[1], values[0]) = DefaultAxisLabels(yMetric);
        if (xMetric != labels.HorizontalMetric || yMetric != labels.VerticalMetric)
        {
            if (values[4] == labels.UpperLeft) values[4] = $"{values[0]} / {values[2]}";
            if (values[5] == labels.UpperRight) values[5] = $"{values[0]} / {values[3]}";
            if (values[6] == labels.LowerLeft) values[6] = $"{values[1]} / {values[2]}";
            if (values[7] == labels.LowerRight) values[7] = $"{values[1]} / {values[3]}";
        }
        labels = new ChartLabels
        {
            HighEnergy = values[0], LowEnergy = values[1], LowVariation = values[2], HighVariation = values[3],
            UpperLeft = values[4], UpperRight = values[5], LowerLeft = values[6], LowerRight = values[7],
            HistoryHigh = values[8], HistoryLow = values[9],
            HorizontalMetric = xMetric,
            VerticalMetric = yMetric,
            DotIntervalSeconds = (int)dotInterval.Value,
            StylePeriodIndex = stylePeriodIndex,
            HistoryPeriodIndex = historyPeriodIndex
        };
        SaveLabels();
        chart.Invalidate();
    }

    private static (string Low, string High) DefaultAxisLabels(StyleMetric metric) => metric switch
    {
        StyleMetric.Dynamics => ("Steady volume", "Changing volume"),
        StyleMetric.Pulse => ("Few pulses", "Many pulses"),
        StyleMetric.Peak => ("Smaller peaks", "Bigger peaks"),
        _ => ("Quieter", "Louder")
    };

    private void Draw(Graphics graphics, Rectangle bounds)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(palette.Plot);
        if (view.SelectedIndex == 0) DrawStyle(graphics, bounds);
        else DrawHistory(graphics, bounds);
    }

    private void DrawStyle(Graphics g, Rectangle bounds)
    {
        if (trail.Count == 0)
        {
            DrawCentered(g, bounds, "Play a song for a few seconds to see its measured output.");
            return;
        }
        var plot = StylePlot(bounds);
        var halfWidth = plot.Width / 2;
        var halfHeight = plot.Height / 2;
        FillQuadrant(g, new Rectangle(plot.Left, plot.Top, halfWidth, halfHeight), palette.Quadrants[0]);
        FillQuadrant(g, new Rectangle(plot.Left + halfWidth, plot.Top, plot.Width - halfWidth, halfHeight), palette.Quadrants[1]);
        FillQuadrant(g, new Rectangle(plot.Left, plot.Top + halfHeight, halfWidth, plot.Height - halfHeight), palette.Quadrants[2]);
        FillQuadrant(g, new Rectangle(plot.Left + halfWidth, plot.Top + halfHeight, plot.Width - halfWidth, plot.Height - halfHeight), palette.Quadrants[3]);
        DrawGrid(g, plot);
        using (var center = new Pen(palette.Muted, 1.5f))
        {
            g.DrawLine(center, plot.Left + halfWidth, plot.Top, plot.Left + halfWidth, plot.Bottom);
            g.DrawLine(center, plot.Left, plot.Top + halfHeight, plot.Right, plot.Top + halfHeight);
        }
        using var axisFont = new Font(UiFontFamily, 9);
        using var axisBrush = new SolidBrush(palette.Muted);
        g.DrawString(labels.HighEnergy, axisFont, axisBrush, bounds.Left + 6, plot.Top);
        g.DrawString(labels.LowEnergy, axisFont, axisBrush, bounds.Left + 6, plot.Bottom - 18);
        g.DrawString(labels.LowVariation, axisFont, axisBrush, plot.Left, plot.Bottom + 9);
        var rightSize = g.MeasureString(labels.HighVariation, axisFont);
        g.DrawString(labels.HighVariation, axisFont, axisBrush, plot.Right - rightSize.Width, plot.Bottom + 9);
        DrawQuadrantLabel(g, labels.UpperLeft, new Rectangle(plot.Left, plot.Top, halfWidth, halfHeight));
        DrawQuadrantLabel(g, labels.UpperRight, new Rectangle(plot.Left + halfWidth, plot.Top, plot.Width - halfWidth, halfHeight));
        DrawQuadrantLabel(g, labels.LowerLeft, new Rectangle(plot.Left, plot.Top + halfHeight, halfWidth, plot.Height - halfHeight),
            StringAlignment.Far, StringAlignment.Far);
        DrawQuadrantLabel(g, labels.LowerRight, new Rectangle(plot.Left + halfWidth, plot.Top + halfHeight, plot.Width - halfWidth, plot.Height - halfHeight),
            StringAlignment.Center, StringAlignment.Far);

        var cutoff = InsightRanges.StyleCutoff(stylePeriodIndex, DateTime.UtcNow);
        var visible = trail.Where(p => p.Time >= cutoff).ToList();
        if (visible.Count == 0)
        {
            DrawCentered(g, plot, "No measured points in this time period.");
            return;
        }
        if (visible.Count > 800)
            visible = visible.Where((_, i) => i % (int)Math.Ceiling(visible.Count / 800.0) == 0)
                .Append(visible[^1]).ToList();
        var points = visible.Select(p => MapStylePoint(plot, p)).ToArray();
        for (var i = 1; i < points.Length; i++)
        {
            var opacity = 50 + (int)(170.0 * i / points.Length);
            using var segment = new Pen(Color.FromArgb(opacity, palette.Trail), 2.5f);
            g.DrawLine(segment, points[i - 1], points[i]);
        }
        if (points.Length > 0)
        {
            using var start = new SolidBrush(Color.FromArgb(160, palette.Trail));
            g.FillEllipse(start, points[0].X - 4, points[0].Y - 4, 8, 8);
        }
        var latest = MapStylePoint(plot, visible[^1]);
        using var halo = new SolidBrush(Color.FromArgb(85, palette.Accent));
        using var dot = new SolidBrush(palette.Accent);
        using var outline = new Pen(palette.AccentText, 2);
        g.FillEllipse(halo, latest.X - 20, latest.Y - 20, 40, 40);
        g.FillEllipse(dot, latest.X - 9, latest.Y - 9, 18, 18);
        g.DrawEllipse(outline, latest.X - 9, latest.Y - 9, 18, 18);
    }

    private void DrawHistory(Graphics g, Rectangle bounds)
    {
        var tracks = FilteredTracks();
        if (tracks.Count == 0)
        {
            DrawCentered(g, bounds, "No songs tracked in this time period yet.");
            return;
        }
        var plot = HistoryPlot(bounds);
        DrawGrid(g, plot);
        using var axisFont = new Font(UiFontFamily, 9);
        using var axisBrush = new SolidBrush(palette.Muted);
        g.DrawString(labels.HistoryHigh, axisFont, axisBrush, bounds.Left + 7, plot.Top);
        g.DrawString(labels.HistoryLow, axisFont, axisBrush, bounds.Left + 7, plot.Bottom - 18);
        var start = HistoryStartUtc(tracks);
        var end = DateTime.UtcNow;
        g.DrawString(start.ToLocalTime().ToString("g"), axisFont, axisBrush, plot.Left, plot.Bottom + 10);
        var recent = "Now";
        var recentSize = g.MeasureString(recent, axisFont);
        g.DrawString(recent, axisFont, axisBrush, plot.Right - recentSize.Width, plot.Bottom + 10);
        var span = Math.Max(1, (end - start).TotalSeconds);
        var display = tracks.Count <= 300 ? tracks : tracks.Where((_, i) => i % (int)Math.Ceiling(tracks.Count / 300.0) == 0).Append(tracks[^1]).ToList();
        var points = display.Select(track => new PointF(
            plot.Left + (float)(Math.Clamp((track.StartedUtc - start).TotalSeconds / span, 0, 1) * plot.Width),
            plot.Bottom - (float)(Math.Clamp(track.Energy, 0, 100) / 100 * plot.Height))).ToArray();
        using var line = new Pen(palette.Accent, 3);
        using var dot = new SolidBrush(palette.Accent);
        if (points.Length > 1) g.DrawLines(line, points);
        foreach (var point in points) g.FillEllipse(dot, point.X - 5, point.Y - 5, 10, 10);
        var last = tracks[^1];
        var label = $"{EnergyName(last.Energy)}  ·  {last.Title}";
        using var labelFont = new Font(UiFontFamily, 9, FontStyle.Bold);
        using var labelFormat = new StringFormat { Trimming = StringTrimming.EllipsisCharacter };
        g.DrawString(label, labelFont, axisBrush, new RectangleF(plot.Left, bounds.Bottom - 22, plot.Width, 20), labelFormat);
    }

    private List<TrackSummary> FilteredTracks()
    {
        var cutoff = HistoryCutoffUtc();
        var tracks = history.Where(track => track.StartedUtc >= cutoff)
            .Select(track => new TrackSummary { Title = track.Title, Artist = track.Artist,
                StartedUtc = track.StartedUtc, Energy = CorrectedEnergy(track.Title, track.Artist, track.Energy),
                Variation = track.Variation, SampleCount = track.SampleCount, EndedUtc = track.EndedUtc }).ToList();
        if (samples >= 5)
            tracks.Add(new TrackSummary { Title = currentTitle, Artist = currentArtist,
                StartedUtc = DateTime.UtcNow, Energy = CorrectedEnergy(currentTitle, currentArtist, CurrentEnergy),
                Variation = CurrentVariation, SampleCount = samples });
        return tracks;
    }

    private DateTime HistoryCutoffUtc() => InsightRanges.HistoryCutoff(historyPeriodIndex, DateTime.UtcNow);

    private DateTime HistoryStartUtc(List<TrackSummary> tracks)
    {
        var cutoff = HistoryCutoffUtc();
        return cutoff == DateTime.MinValue ? tracks.Min(track => track.StartedUtc) : cutoff;
    }

    private static Rectangle HistoryPlot(Rectangle bounds) => new(bounds.Left + 94, bounds.Top + 16,
        Math.Max(20, bounds.Width - 120), Math.Max(20, bounds.Height - 58));

    private string? TooltipAt(Point location, Rectangle bounds)
    {
        if (view.SelectedIndex == 0)
        {
            var cutoff = InsightRanges.StyleCutoff(stylePeriodIndex, DateTime.UtcNow);
            var nearest = trail.Where(x => x.Time >= cutoff)
                .Select(x => (Point: x, Position: MapStylePoint(StylePlot(bounds), x)))
                .OrderBy(x => Math.Pow(x.Position.X - location.X, 2) + Math.Pow(x.Position.Y - location.Y, 2))
                .FirstOrDefault();
            if (nearest.Point == null || Math.Abs(nearest.Position.X - location.X) > 14 || Math.Abs(nearest.Position.Y - location.Y) > 14)
                return null;
            return $"{nearest.Point.Time.ToLocalTime():g}\nMeasured output: {nearest.Point.Energy:0}%\nSignal variation: {nearest.Point.Dynamics:0}%";
        }
        var tracks = FilteredTracks();
        if (tracks.Count == 0) return null;
        var plot = HistoryPlot(bounds);
        var start = HistoryStartUtc(tracks);
        var span = Math.Max(1, (DateTime.UtcNow - start).TotalSeconds);
        var nearestTrack = tracks.Select(x => (Track: x, X: plot.Left + (float)(Math.Clamp((x.StartedUtc - start).TotalSeconds / span, 0, 1) * plot.Width),
            Y: plot.Bottom - (float)(Math.Clamp(x.Energy, 0, 100) / 100 * plot.Height)))
            .OrderBy(x => Math.Pow(x.X - location.X, 2) + Math.Pow(x.Y - location.Y, 2)).First();
        if (Math.Abs(nearestTrack.X - location.X) > 14 || Math.Abs(nearestTrack.Y - location.Y) > 14) return null;
        return $"{nearestTrack.Track.Title} — {nearestTrack.Track.Artist}\n{nearestTrack.Track.StartedUtc.ToLocalTime():g}\n{nearestTrack.Track.SampleCount} measured samples";
    }

    private static Rectangle StylePlot(Rectangle bounds) => new(bounds.Left + 91, bounds.Top + 16,
        Math.Max(20, bounds.Width - 126), Math.Max(20, bounds.Height - 58));

    private PointF MapStylePoint(Rectangle plot, StylePoint point)
    {
        var correction = CurrentCorrection;
        var xOffset = correction?.Value(labels.HorizontalMetric) ?? 0;
        var yOffset = correction?.Value(labels.VerticalMetric) ?? 0;
        return new PointF(
            plot.Left + (float)(Math.Clamp(point.Value(labels.HorizontalMetric) + xOffset, 0, 100) / 100 * plot.Width),
            plot.Bottom - (float)(Math.Clamp(point.Value(labels.VerticalMetric) + yOffset, 0, 100) / 100 * plot.Height));
    }

    private bool CanMoveDot(Point location, Rectangle bounds) =>
        view.SelectedIndex == 0 && currentKey != null && trail.Count > 0 && StylePlot(bounds).Contains(location);

    private void MoveDot(Point location, Rectangle bounds, bool save)
    {
        if (currentKey == null || trail.Count == 0) return;
        var plot = StylePlot(bounds);
        if (!corrections.TryGetValue(currentKey, out var correction))
            corrections[currentKey] = correction = new TrackCorrection();
        correction.Set(labels.HorizontalMetric, (double)(location.X - plot.Left) / plot.Width * 100 - trail[^1].Value(labels.HorizontalMetric));
        correction.Set(labels.VerticalMetric, (double)(plot.Bottom - location.Y) / plot.Height * 100 - trail[^1].Value(labels.VerticalMetric));
        if (save) SaveCorrections();
        UpdateDescription();
        chart.Invalidate();
    }

    private static void FillQuadrant(Graphics g, Rectangle area, Color color)
    {
        using var brush = new SolidBrush(color);
        g.FillRectangle(brush, area);
    }

    private void DrawQuadrantLabel(Graphics g, string label, Rectangle area,
        StringAlignment alignment = StringAlignment.Center, StringAlignment lineAlignment = StringAlignment.Center)
    {
        using var brush = new SolidBrush(palette.QuadrantText);
        using var font = new Font(UiFontFamily, 9, FontStyle.Bold);
        using var format = new StringFormat { Alignment = alignment, LineAlignment = lineAlignment,
            Trimming = StringTrimming.EllipsisCharacter };
        g.DrawString(label, font, brush, new RectangleF(area.Left + 8, area.Top + 8, Math.Max(1, area.Width - 16), Math.Max(1, area.Height - 16)), format);
    }

    private void DrawGrid(Graphics g, Rectangle plot)
    {
        using var grid = new Pen(palette.Grid, 1);
        for (var i = 0; i <= 4; i++)
        {
            var x = plot.Left + i * plot.Width / 4f;
            var y = plot.Top + i * plot.Height / 4f;
            g.DrawLine(grid, x, plot.Top, x, plot.Bottom);
            g.DrawLine(grid, plot.Left, y, plot.Right, y);
        }
    }

    private void DrawCentered(Graphics g, Rectangle bounds, string message)
    {
        using var brush = new SolidBrush(palette.Muted);
        using var font = new Font(UiFontFamily, 11);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(message, font, brush, bounds, format);
    }

    private sealed class InsightCanvas : Control
    {
        private readonly MusicInsightsPanel owner;
        private readonly ToolTip tip = new();
        private bool movingDot;

        public InsightCanvas(MusicInsightsPanel owner)
        {
            this.owner = owner;
            DoubleBuffered = true;
            AccessibleName = "Music insight chart";
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            owner.Draw(e.Graphics, ClientRectangle);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || !owner.CanMoveDot(e.Location, ClientRectangle)) return;
            movingDot = true;
            Capture = true;
            owner.MoveDot(e.Location, ClientRectangle, save: false);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Cursor = movingDot || owner.CanMoveDot(e.Location, ClientRectangle) ? Cursors.Hand : Cursors.Default;
            if (movingDot) owner.MoveDot(e.Location, ClientRectangle, save: false);
            else tip.SetToolTip(this, owner.TooltipAt(e.Location, ClientRectangle));
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!movingDot) return;
            movingDot = false;
            Capture = false;
            owner.MoveDot(e.Location, ClientRectangle, save: true);
        }
    }
}
