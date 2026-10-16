using System.Runtime.InteropServices;

namespace SilenceFill;

internal sealed class MainForm : Form
{
    private readonly object settingsLock = new();
    private readonly Settings settings = Settings.Load();
    private readonly WriteQueue writes = new();
    private readonly MusicInsightsPanel insights = new();
    private readonly Label heading = new() { Dock = DockStyle.Fill, Font = new Font(SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif, 19, FontStyle.Bold) };
    private readonly Label status = new() { Dock = DockStyle.Fill, Font = new Font(SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif, 13, FontStyle.Bold), AutoEllipsis = true };
    private readonly Label detail = new() { Dock = DockStyle.Fill, AutoEllipsis = true };
    private readonly Label trigger = new() { Dock = DockStyle.Fill, AutoEllipsis = true };
    private readonly Label setup = new() { Dock = DockStyle.Fill, AutoEllipsis = true };
    private readonly AutomationToggle enabled = new();
    private readonly ThemedStepper quiet = new() { Minimum = 0, Maximum = 30, Width = 64 };
    private readonly DropSelect sensitivity = new() { Width = 118 };
    private readonly TextBox search = new() { PlaceholderText = "Find an app…", Dock = DockStyle.Fill };
    private readonly FlowLayoutPanel apps = new() { AutoScroll = true,
        WrapContents = false, FlowDirection = FlowDirection.TopDown, Padding = Padding.Empty };
    private readonly Panel appHost = new() { Dock = DockStyle.Fill };
    private readonly AppScrollIndicator appScroll = new();
    private readonly Label appSummary = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Tag = "muted" };
    private readonly ImageList appIcons = new() { ImageSize = new Size(20, 20), ColorDepth = ColorDepth.Depth32Bit };
    private readonly List<Icon> ownedAppIcons = [];
    private readonly TextBox diagnostics = new() { Dock = DockStyle.Fill, ReadOnly = true, Multiline = true,
        ScrollBars = ScrollBars.None, AccessibleName = "Recent decisions. Use arrow keys or mouse wheel to scroll." };
    private readonly NotifyIcon tray = new() { Visible = true, Text = "SilenceFill" };
    private readonly ToolStripMenuItem trayToggle = new("Pause automation");
    private readonly List<string> recentDecisions = [];
    private readonly TableLayoutPanel stateCard;
    private readonly ThemedTabControl tabs;
    private readonly Dictionary<string, AppChoice> choices = new(StringComparer.OrdinalIgnoreCase);
    private AutomationService? service;
    private bool closingForReal;
    private bool compactLayout;
    private string lastLogKey = "";

    public MainForm()
    {
        SuspendLayout();
        Text = "SilenceFill  ·  Spotify between sounds";
        Size = new Size(820, 650);
        MinimumSize = new Size(540, 480);
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;
        AutoScaleMode = AutoScaleMode.Dpi;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        Font = new Font("Segoe UI", 9.5f);
        heading.Text = "SilenceFill";
        status.Text = "Checking Spotify and Windows audio…";
        detail.Text = "Monitoring starts automatically.";
        trigger.Text = "Triggering app: none";
        setup.Text = settings.SetupCompleted ? "Choose apps below to pause Spotify when they make sound." :
            "Welcome. Play music in Spotify, then select the apps whose sound should pause it.";

        tabs = new ThemedTabControl { Dock = DockStyle.Fill };
        var fill = new Panel();
        var analytics = new Panel();
        var diagnosticsTab = new Panel();
        tabs.AddPages(("Silence Fill", fill), ("Analytics", analytics), ("Diagnostics", diagnosticsTab));
        analytics.Controls.Add(insights);
        Controls.Add(tabs);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24, 20, 24, 20), ColumnCount = 1, RowCount = 6 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 94));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        heading.Margin = new Padding(0, 0, 0, 8);
        heading.TextAlign = ContentAlignment.MiddleLeft;
        layout.Controls.Add(heading, 0, 0);
        var state = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty,
            BackColor = Color.FromArgb(233, 243, 250), Padding = new Padding(12), RowCount = 3 };
        stateCard = state;
        state.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        state.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        state.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        status.Margin = trigger.Margin = detail.Margin = Padding.Empty;
        status.TextAlign = trigger.TextAlign = detail.TextAlign = ContentAlignment.MiddleLeft;
        state.Controls.Add(status, 0, 0); state.Controls.Add(trigger, 0, 1); state.Controls.Add(detail, 0, 2);
        layout.Controls.Add(state, 0, 1);
        trigger.Tag = "muted";
        detail.Tag = "muted";
        setup.Tag = "muted";
        setup.Margin = new Padding(0, 6, 0, 4);
        setup.TextAlign = ContentAlignment.MiddleLeft;
        layout.Controls.Add(setup, 0, 2);
        var controls = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8), ColumnCount = 2, RowCount = 1 };
        controls.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 222));
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        enabled.IsOn = settings.Enabled;
        enabled.Anchor = AnchorStyles.Left;
        enabled.Margin = Padding.Empty;
        controls.Controls.Add(enabled, 0, 0);
        var delay = new FlowLayoutPanel { Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Height = 32, WrapContents = false, Margin = new Padding(16, 0, 0, 0) };
        delay.Controls.Add(new Label { Text = "Quiet delay", AutoSize = true,
            Margin = new Padding(0, 0, 10, 0), Padding = new Padding(0, 6, 0, 0) });
        quiet.Value = settings.QuietSeconds;
        quiet.Margin = Padding.Empty;
        quiet.AccessibleName = "Seconds of quiet before Spotify resumes";
        delay.Controls.Add(quiet);
        delay.Controls.Add(new Label { Text = "seconds", AutoSize = true,
            Margin = new Padding(8, 0, 0, 0), Padding = new Padding(0, 6, 0, 0) });
        controls.Controls.Add(delay, 1, 0);
        layout.Controls.Add(controls, 0, 3);
        var appHeader = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10), ColumnCount = 2, RowCount = 2 };
        appHeader.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        appHeader.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        appHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        appHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 208));
        appHeader.Controls.Add(new Label { Text = "Apps to monitor", Font = new Font(Font, FontStyle.Bold),
            Dock = DockStyle.Fill, Margin = Padding.Empty, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        appSummary.Margin = Padding.Empty;
        appHeader.Controls.Add(appSummary, 1, 0);
        search.Dock = DockStyle.None;
        search.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        search.Margin = new Padding(0, 0, 16, 0);
        appHeader.Controls.Add(search, 0, 1);
        var advanced = new FlowLayoutPanel { Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Height = 30, WrapContents = false, Margin = Padding.Empty };
        advanced.Controls.Add(new Label { Text = "Detection", AutoSize = true,
            Margin = new Padding(0, 0, 12, 0), Padding = new Padding(0, 6, 0, 0) });
        sensitivity.Items.AddRange(["Sensitive", "Balanced", "Relaxed"]);
        sensitivity.SelectedItem = settings.Sensitivity;
        sensitivity.AccessibleName = "Sound detection sensitivity";
        sensitivity.Margin = Padding.Empty;
        advanced.Controls.Add(sensitivity);
        appHeader.Controls.Add(advanced, 1, 1);
        layout.Controls.Add(appHeader, 0, 4);
        appHost.Controls.Add(appScroll);
        appHost.Controls.Add(apps);
        appHost.Margin = Padding.Empty;
        appHost.SizeChanged += (_, _) => FitAppViewport();
        FitAppViewport();
        layout.Controls.Add(appHost, 0, 5);
        apps.AccessibleName = "Apps monitored by SilenceFill";
        apps.SizeChanged += (_, _) => FitAppRows();
        apps.Scroll += (_, _) => UpdateAppScroll();
        apps.MouseWheel += (_, _) => BeginInvoke(UpdateAppScroll);
        appScroll.PositionRequested += fraction =>
        {
            var max = AppScrollMaximum();
            apps.AutoScrollPosition = new Point(0, (int)(fraction * max));
            UpdateAppScroll();
        };
        fill.Controls.Add(layout);
        void UpdateSpacing()
        {
            compactLayout = fill.Width < P(650) || fill.Height < P(500);
            layout.Padding = compactLayout ? new Padding(P(16), P(12), P(16), P(12)) : new Padding(P(24), P(20), P(24), P(20));
            layout.RowStyles[0].Height = Math.Max(P(compactLayout ? 38 : 48), heading.Font.Height + heading.Margin.Vertical);
            layout.RowStyles[1].Height = P(compactLayout ? 80 : 94);
            layout.RowStyles[2].Height = P(compactLayout ? 40 : 38);
            layout.RowStyles[3].Height = P(compactLayout ? 48 : 58);
            layout.RowStyles[4].Height = P(compactLayout ? 72 : 80);
            state.Padding = new Padding(P(compactLayout ? 10 : 12));
            state.RowStyles[0].Height = P(compactLayout ? 24 : 28);
            state.RowStyles[1].Height = P(compactLayout ? 18 : 20);
            delay.Margin = new Padding(P(compactLayout ? 12 : 24), 0, 0, 0);
            FitAppRows();
        }
        fill.SizeChanged += (_, _) => UpdateSpacing();
        UpdateSpacing();

        var diagnosticLayout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24, 20, 24, 20), RowCount = 3 };
        diagnosticLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        diagnosticLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        diagnosticLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        diagnosticLayout.Controls.Add(new Label { Text = "Recent automation decisions (no audio is recorded)", Dock = DockStyle.Fill, Font = new Font(Font.FontFamily, 13, FontStyle.Bold) }, 0, 0);
        diagnosticLayout.Controls.Add(diagnostics, 0, 1);
        var diagnosticActions = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 12, 0, 0) };
        var export = new Button { Text = "Export diagnostic log…", AutoSize = true, Tag = "primary" };
        export.Click += (_, _) => ExportDiagnostics();
        diagnosticActions.Controls.Add(export);
        var older = new Button { Text = "Older ▲", AutoSize = true };
        older.Click += (_, _) => ScrollDiagnostics(2);
        diagnosticActions.Controls.Add(older);
        var newer = new Button { Text = "Newer ▼", AutoSize = true };
        newer.Click += (_, _) => ScrollDiagnostics(3);
        diagnosticActions.Controls.Add(newer);
        diagnosticLayout.Controls.Add(diagnosticActions, 0, 2);
        diagnosticsTab.Controls.Add(diagnosticLayout);

        enabled.Toggled += (_, _) => ToggleEnabled();
        quiet.ValueChanged += (_, _) => ChangeSettings(s => s.QuietSeconds = (int)quiet.Value);
        sensitivity.SelectedIndexChanged += (_, _) => ChangeSettings(s => s.Sensitivity = sensitivity.SelectedItem?.ToString() ?? "Balanced");
        search.TextChanged += (_, _) => UpdateApps();

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open SilenceFill", null, (_, _) => ShowSettings());
        trayToggle.Click += (_, _) => ToggleEnabled();
        menu.Items.Add(trayToggle);
        var appearance = new ToolStripMenuItem("Appearance");
        foreach (var theme in new[] { "System", "Light", "Dark" })
        {
            var option = new ToolStripMenuItem(theme) { Checked = theme == settings.Theme };
            option.Click += (_, _) =>
            {
                ChangeSettings(s => s.Theme = theme);
                foreach (ToolStripMenuItem item in appearance.DropDownItems) item.Checked = item == option;
                ApplyTheme();
            };
            appearance.DropDownItems.Add(option);
        }
        menu.Items.Add(appearance);
        menu.Items.Add("Exit", null, (_, _) => { closingForReal = true; Close(); });
        tray.Icon = Icon;
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => ShowSettings();
        FormClosing += (_, e) => { if (!closingForReal) { e.Cancel = true; Hide(); } };
        FormClosed += (_, _) => { service?.Dispose(); insights.CloseSession(); writes.Dispose(); tray.Visible = false; tray.Dispose(); appIcons.Dispose();
            foreach (var appIcon in ownedAppIcons) appIcon.Dispose(); };
        ApplyTheme();
        Shown += (_, _) => { AppTheme.ApplyTitleBar(this, AppTheme.Resolve(settings.Theme)); if (!settings.SetupCompleted) BeginInvoke(ShowFirstRun); };
        service = new AutomationService(GetSettings, update =>
        {
            try { if (IsHandleCreated && !IsDisposed) BeginInvoke(() => ApplyUpdate(update)); }
            catch (InvalidOperationException) { }
        });
        AutoScaleDimensions = new SizeF(96, 96);
        ResumeLayout(true);
        UpdateSpacing();
        FitAppViewport();
        ApplyTheme();
        DpiChanged += (_, _) =>
        {
            BeginInvoke(() => { UpdateSpacing(); FitAppViewport(); ApplyTheme(); });
        };
    }

    private int P(int logical) => DpiScale.Pixels(this, logical);

    public void ShowSettings()
    {
        WindowState = FormWindowState.Maximized;
        Show();
        Activate();
    }

    private void ShowFirstRun()
    {
        using var dialog = new Form
        {
            Text = "Welcome to SilenceFill", Size = new Size(440, 250),
            MinimumSize = new Size(400, 230), StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.Sizable, ShowInTaskbar = false
        };
        dialog.SuspendLayout();
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        layout.Controls.Add(new Label { Text = "Set up SilenceFill", Dock = DockStyle.Fill,
            Font = new Font(Font.FontFamily, 15, FontStyle.Bold) }, 0, 0);
        layout.Controls.Add(new Label { Text = "1. Start music in Spotify.\n2. Select the open apps whose sound should pause it.\n3. Choose how long Spotify waits before resuming.\n\nSilenceFill stays in the tray when you close its window.", Dock = DockStyle.Fill }, 0, 1);
        var start = new Button { Text = "Choose apps", DialogResult = DialogResult.OK, AutoSize = true, Anchor = AnchorStyles.Right };
        layout.Controls.Add(start, 0, 2);
        dialog.AcceptButton = start;
        dialog.Controls.Add(layout);
        start.Tag = "primary";
        dialog.BackColor = AppTheme.Resolve(settings.Theme).Background;
        AppTheme.Apply(dialog, AppTheme.Resolve(settings.Theme));
        dialog.AutoScaleMode = AutoScaleMode.Dpi;
        dialog.AutoScaleDimensions = new SizeF(96, 96);
        dialog.ResumeLayout(true);
        AppTheme.Apply(dialog, AppTheme.Resolve(settings.Theme));
        dialog.Shown += (_, _) => AppTheme.ApplyTitleBar(dialog, AppTheme.Resolve(settings.Theme));
        dialog.ShowDialog(this);
        ChangeSettings(_ => { });
        search.Focus();
    }

    private Settings GetSettings() { lock (settingsLock) return settings.Copy(); }

    private void ToggleEnabled()
    {
        ChangeSettings(s => s.Enabled = !s.Enabled);
        enabled.IsOn = settings.Enabled;
        ApplyTheme();
    }

    private void ChangeSettings(Action<Settings> change)
    {
        lock (settingsLock) { change(settings); settings.SetupCompleted = true; writes.Save(Settings.PathOnDisk, settings.Copy()); }
        setup.Text = "Select any open app below. SilenceFill acts only when a selected app makes sound.";
        trayToggle.Text = settings.Enabled ? "Pause automation" : "Resume automation";
    }

    private void ApplyUpdate(AutomationUpdate update)
    {
        status.Text = update.Decision.Status;
        detail.Text = update.Decision.Detail;
        trigger.Text = "Triggering app: " + (update.Decision.TriggerApp ?? "none");
        tray.Text = ("SilenceFill: " + update.Decision.Status)[..Math.Min(63, ("SilenceFill: " + update.Decision.Status).Length)];
        trayToggle.Text = settings.Enabled ? "Pause automation" : "Resume automation";
        insights.Observe(update.Spotify.Media, update.Audio.Sessions.Where(s => s.ProcessName.Equals("Spotify", StringComparison.OrdinalIgnoreCase))
            .Select(s => s.Peak).DefaultIfEmpty(0).Max(), update.TimeUtc);
        var logKey = update.Decision.Status + "|" + update.Decision.Detail + "|" + update.Decision.TriggerApp;
        if (logKey != lastLogKey)
        {
            lastLogKey = logKey;
            recentDecisions.Add($"{update.TimeUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}  {update.Decision.Status} — {update.Decision.Detail}");
            if (recentDecisions.Count > 100) recentDecisions.RemoveAt(0);
            diagnostics.Lines = recentDecisions.ToArray();
        }
        var selectedNames = GetSettings().SelectedApps;
        var allNames = update.AvailableApps.Concat(selectedNames).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (allNames.Any(n => !choices.ContainsKey(n)) || choices.Keys.Except(allNames, StringComparer.OrdinalIgnoreCase).Any() ||
            allNames.Any(n => choices.TryGetValue(n, out var item) && item.IsRunning != update.AvailableApps.Contains(n, StringComparer.OrdinalIgnoreCase)))
        {
            choices.Clear();
            foreach (var name in allNames) choices[name] = AppCatalog.Describe(name,
                update.AvailableApps.Contains(name, StringComparer.OrdinalIgnoreCase));
            UpdateApps();
        }
    }

    private void UpdateApps()
    {
        var selected = GetSettings().SelectedApps;
        var filter = search.Text.Trim();
        var visible = choices.Values
            .Where(x => filter.Length == 0 || x.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
                x.DisplayName.Contains(filter, StringComparison.CurrentCultureIgnoreCase))
            .OrderByDescending(x => selected.Contains(x.Name))
            .ThenBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToArray();
        appSummary.Text = $"{selected.Count} monitored  ·  {choices.Count} available";
        apps.SuspendLayout();
        foreach (Control old in apps.Controls.Cast<Control>().ToArray()) { apps.Controls.Remove(old); old.Dispose(); }
        foreach (var item in visible)
        {
            if (!appIcons.Images.ContainsKey(item.Name))
            {
                var icon = AppCatalog.IconFor(item.Name);
                appIcons.Images.Add(item.Name, icon ?? SystemIcons.Application);
                if (icon is not null) ownedAppIcons.Add(icon);
            }
            var row = new AppRow(item) { Selected = selected.Contains(item.Name), AppIcon = appIcons.Images[item.Name],
                Font = Font, Palette = AppTheme.Resolve(settings.Theme) };
            row.Toggled += (_, _) =>
            {
                ChangeSettings(s => { if (s.SelectedApps.Contains(item.Name)) s.SelectedApps.Remove(item.Name);
                    else s.SelectedApps.Add(item.Name); });
                row.Selected = GetSettings().SelectedApps.Contains(item.Name);
                BeginInvoke(UpdateApps);
            };
            apps.Controls.Add(row);
        }
        if (visible.Length == 0)
        {
            var empty = new Label { Text = filter.Length > 0 ? "No apps match your search." :
                "Open an app and it will appear here. Selected apps stay listed when closed.",
                AutoSize = false, Height = P(54), TextAlign = ContentAlignment.MiddleLeft, Tag = "muted" };
            apps.Controls.Add(empty);
            empty.ForeColor = AppTheme.Resolve(settings.Theme).Muted;
        }
        apps.ResumeLayout();
        FitAppRows();
        if (apps.IsHandleCreated) BeginInvoke(UpdateAppScroll);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int message, IntPtr wParam, IntPtr lParam);

    private void FitAppRows()
    {
        var width = Math.Max(P(160), appHost.ClientSize.Width - P(14) - apps.Padding.Horizontal - P(2));
        foreach (Control row in apps.Controls)
        {
            if (row.Width != width) row.Width = width;
            if (row is AppRow)
            {
                row.Height = Math.Max(P(compactLayout ? 58 : 64), row.Font.Height * 2 + P(12));
                row.Margin = new Padding(0, 0, 0, P(compactLayout ? 8 : 10));
            }
        }
        apps.AutoScrollMinSize = new Size(0, AppContentHeight());
        if (apps.IsHandleCreated) UpdateAppScroll();
    }

    private void FitAppViewport()
    {
        apps.SetBounds(0, 0, Math.Max(1, appHost.ClientSize.Width - P(14)), appHost.ClientSize.Height);
        appScroll.SetBounds(Math.Max(0, appHost.ClientSize.Width - P(10)), 0, P(10), appHost.ClientSize.Height);
    }

    private int AppContentHeight() => apps.Padding.Vertical + apps.Controls.Cast<Control>()
        .Sum(row => row.Height + row.Margin.Vertical) -
        (apps.Controls.Count > 0 ? apps.Controls[apps.Controls.Count - 1].Margin.Bottom : 0);

    private int AppScrollMaximum() => Math.Max(0, AppContentHeight() - apps.ClientSize.Height);

    private void UpdateAppScroll()
    {
        if (!apps.IsHandleCreated || apps.IsDisposed) return;
        var max = AppScrollMaximum();
        var position = Math.Min(max, Math.Abs(apps.AutoScrollPosition.Y));
        if (Math.Abs(apps.AutoScrollPosition.Y) > max) apps.AutoScrollPosition = new Point(0, max);
        var share = max == 0 ? 1 : (double)apps.ClientSize.Height / (apps.ClientSize.Height + max);
        appScroll.SetPosition(max == 0 ? 0 : (double)position / max, share);
        ShowScrollBar(apps.Handle, 0, false);
        ShowScrollBar(apps.Handle, 1, false);
    }

    [DllImport("user32.dll")]
    private static extern bool ShowScrollBar(IntPtr hWnd, int wBar, bool bShow);

    private void ScrollDiagnostics(int direction)
    {
        if (diagnostics.IsHandleCreated) SendMessage(diagnostics.Handle, 0x115, (IntPtr)direction, IntPtr.Zero);
    }

    private void ExportDiagnostics()
    {
        using var dialog = new SaveFileDialog { Filter = "Text log (*.txt)|*.txt", FileName = "SilenceFill-diagnostics.txt" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { File.WriteAllLines(dialog.FileName, recentDecisions); }
        catch (Exception e) { AppTheme.ShowMessage(this, AppTheme.Resolve(settings.Theme), "Export failed", e.Message); }
    }

    private void ApplyTheme()
    {
        var palette = AppTheme.Resolve(settings.Theme);
        BackColor = palette.Background;
        ForeColor = palette.Text;
        AppTheme.Apply(this, palette);
        stateCard.BackColor = palette.Raised;
        status.ForeColor = palette.Accent;
        trigger.ForeColor = detail.ForeColor = setup.ForeColor = palette.Muted;
        apps.BackColor = palette.Background;
        diagnostics.BackColor = palette.Surface;
        search.BackColor = palette.Input;
        insights.ApplyTheme(palette);
        if (tray.ContextMenuStrip is { } menu) AppTheme.StyleMenu(menu, palette);
        AppTheme.ApplyTitleBar(this, palette);
    }
}
