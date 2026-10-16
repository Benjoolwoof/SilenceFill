using System.Reflection;
using SilenceFill;

internal static class Preview
{
    [STAThread]
    private static void Main(string[] args)
    {
        SilenceFill.Program.InitializeUi();
        var folder = Path.GetFullPath(args.Length == 0 ? "work/SilenceFill-v2/previews" : args[0]);
        Directory.CreateDirectory(folder);
        var data = Path.Combine(folder, "test-data", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("SILENCEFILL_DATA_DIR", data);
        Directory.CreateDirectory(data);
        Storage.WriteAtomic(Settings.PathOnDisk, System.Text.Json.JsonSerializer.Serialize(new Settings
        {
            Enabled = false, SetupCompleted = true, SelectedApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "chrome" }
        }));
        using var form = new MainForm();
        form.Show();
        Application.DoEvents();
        Console.WriteLine($"Preview display DPI: {form.DeviceDpi}; maximized window: {form.Size}");
        Capture("launch-maximized.png");
        if (form.WindowState != FormWindowState.Maximized) throw new Exception("Launch did not maximize the window.");
        form.WindowState = FormWindowState.Normal;
        form.Hide();
        form.ShowSettings();
        Application.DoEvents();
        if (!form.Visible || form.WindowState != FormWindowState.Maximized) throw new Exception("Reopening a hidden window did not maximize it.");
        form.WindowState = FormWindowState.Minimized;
        form.ShowSettings();
        Application.DoEvents();
        if (form.WindowState != FormWindowState.Maximized) throw new Exception("Reopening a minimized window did not maximize it.");
        form.WindowState = FormWindowState.Normal;
        form.Size = PreviewSize(820, 650);
        Thread.Sleep(800);
        Application.DoEvents();
        var serviceField = typeof(MainForm).GetField("service", BindingFlags.Instance | BindingFlags.NonPublic)!;
        ((AutomationService?)serviceField.GetValue(form))?.Dispose();
        serviceField.SetValue(form, null);
        var choices = (Dictionary<string, AppChoice>)typeof(MainForm).GetField("choices", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        var updateApps = typeof(MainForm).GetMethod("UpdateApps", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var selfName = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
        choices[selfName] = new AppChoice(selfName, "Icon lifetime check", true);
        updateApps.Invoke(form, null);
        var retainedIcons = (List<Icon>)typeof(MainForm).GetField("ownedAppIcons", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        if (retainedIcons.Count == 0 || retainedIcons.Any(icon => icon.Handle == IntPtr.Zero))
            throw new Exception("App icons were disposed while the list still needs them.");
        choices.Remove(selfName);
        for (var i = 1; i <= 10; i++)
            choices[$"preview-app-{i}"] = new AppChoice($"preview-app-{i}", $"Preview app {i}", true);
        updateApps.Invoke(form, null);
        ((Label)typeof(MainForm).GetField("status", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!).Text = "Automation off";
        ((Label)typeof(MainForm).GetField("detail", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!).Text = "Spotify remains under your control.";
        ((Label)typeof(MainForm).GetField("trigger", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!).Text = "Triggering app: none";
        var settings = (Settings)typeof(MainForm).GetField("settings", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        var sensitivity = (DropSelect)typeof(MainForm).GetField("sensitivity", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        sensitivity.SelectedIndex = 0;
        if (settings.Sensitivity != "Sensitive") throw new Exception("Sensitivity selection did not update settings.");
        sensitivity.SelectedIndex = 1;
        var openMenu = typeof(DropSelect).GetMethod("OpenMenu", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var detectionMenu = (ContextMenuStrip)typeof(DropSelect).GetField("menu", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(sensitivity)!;
        openMenu.Invoke(sensitivity, null);
        Application.DoEvents();
        detectionMenu.Close();
        Application.DoEvents();
        if (detectionMenu.IsDisposed) throw new Exception("Dismissing Detection disposed its active menu.");
        openMenu.Invoke(sensitivity, null);
        Application.DoEvents();
        detectionMenu.Items[0].PerformClick();
        Application.DoEvents();
        if (detectionMenu.IsDisposed || settings.Sensitivity != "Sensitive")
            throw new Exception("Detection could not be reopened after dismissal.");
        sensitivity.SelectedIndex = 1;
        var quiet = (ThemedStepper)typeof(MainForm).GetField("quiet", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        quiet.Value = 6;
        if (settings.QuietSeconds != 6) throw new Exception("Quiet delay stepper did not update settings.");
        quiet.Value = 4;
        var tabs = form.Controls.OfType<ThemedTabControl>().Single();
        form.ShowSettings();
        Application.DoEvents();
        Capture("main-maximized.png");
        form.WindowState = FormWindowState.Normal;
        form.Size = new Size(2874, 1380);
        Application.DoEvents();
        Capture("main-wide.png");
        form.Size = PreviewSize(820, 650);
        Application.DoEvents();
        CaptureDialog(() => typeof(MainForm).GetMethod("ShowFirstRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null), "welcome.png");
        Capture("main-normal.png");
        form.Size = PreviewSize(540, 480);
        Application.DoEvents();
        var appList = (FlowLayoutPanel)typeof(MainForm).GetField("apps", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        var appScroll = (AppScrollIndicator)typeof(MainForm).GetField("appScroll", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        if (appList.VerticalScroll.Maximum > appList.VerticalScroll.LargeChange && !appScroll.Visible)
            throw new Exception("The app list has more items but no scroll indicator.");
        Capture("main-narrow.png");
        appList.AutoScrollPosition = new Point(0, 55);
        Application.DoEvents();
        if (appList.VerticalScroll.Value == 0) throw new Exception("The app list cannot scroll when narrow.");
        appList.AutoScrollPosition = Point.Empty;
        var flipApp = typeof(AppRow).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var chromeRow = appList.Controls.OfType<AppRow>().First(x => x.Choice.Name == "chrome");
        flipApp.Invoke(chromeRow, [EventArgs.Empty]);
        Application.DoEvents();
        if (settings.SelectedApps.Contains("chrome")) throw new Exception("App card did not remove monitoring.");
        chromeRow = appList.Controls.OfType<AppRow>().First(x => x.Choice.Name == "chrome");
        flipApp.Invoke(chromeRow, [EventArgs.Empty]);
        Application.DoEvents();
        if (!settings.SelectedApps.Contains("chrome")) throw new Exception("App card did not restore monitoring.");
        var automation = (AutomationToggle)typeof(MainForm).GetField("enabled", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        settings.Enabled = false;
        automation.IsOn = false;
        var flipAutomation = typeof(AutomationToggle).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!;
        flipAutomation.Invoke(automation, [EventArgs.Empty]);
        if (!settings.Enabled || !automation.IsOn) throw new Exception("Automation switch did not enable monitoring.");
        flipAutomation.Invoke(automation, [EventArgs.Empty]);
        if (settings.Enabled || automation.IsOn) throw new Exception("Automation switch did not pause monitoring.");
        form.Size = PreviewSize(820, 650);
        var panel = (MusicInsightsPanel)typeof(MainForm).GetField("insights", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        var now = DateTime.UtcNow;
        for (var i = 0; i < 35; i++) panel.Observe(new NowPlaying("Introduction and Rondo Capriccioso, Op. 28", "Camille Saint-Saëns", true),
            (float)(0.02 + 0.15 * Math.Abs(Math.Sin(i / 3.0))), now.AddMilliseconds(-14000 + i * 400));
        ((Button)typeof(MusicInsightsPanel).GetField("resetCorrection", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(panel)!).Visible = true;
        tabs.SelectedIndex = 1;
        Application.DoEvents();
        Capture("analytics-signal.png");
        CaptureDialog(() => typeof(MusicInsightsPanel).GetMethod("DeleteHistory", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(panel, null), "delete-history.png");
        var historyMenu = (ContextMenuStrip)typeof(MusicInsightsPanel).GetField("historyMenu", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(panel)!;
        historyMenu.Show(panel.PointToScreen(new Point(100, 100)));
        Application.DoEvents();
        using (var bitmap = new Bitmap(historyMenu.Width, historyMenu.Height))
        {
            historyMenu.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(folder, "history-tools.png"));
        }
        historyMenu.Hide();
        CaptureDialog(() => typeof(MusicInsightsPanel).GetMethod("EditLabels", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(panel, null), "customize-chart.png");
        form.Size = PreviewSize(540, 480);
        Application.DoEvents();
        Capture("analytics-narrow.png");
        form.Size = PreviewSize(820, 650);
        var history = (List<TrackSummary>)typeof(MusicInsightsPanel).GetField("history", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(panel)!;
        history.Add(new TrackSummary { Title = "Sample recording A", Artist = "Preview", StartedUtc = now.AddHours(-5), Energy = 24, Variation = 15, SampleCount = 100 });
        history.Add(new TrackSummary { Title = "Sample recording B", Artist = "Preview", StartedUtc = now.AddHours(-3), Energy = 67, Variation = 42, SampleCount = 110 });
        history.Add(new TrackSummary { Title = "Sample recording C", Artist = "Preview", StartedUtc = now.AddHours(-1), Energy = 38, Variation = 33, SampleCount = 90 });
        var view = (DropSelect)typeof(MusicInsightsPanel).GetField("view", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(panel)!;
        view.SelectedIndex = 1;
        Application.DoEvents();
        Capture("analytics-history.png");
        tabs.SelectedIndex = 2;
        var diagnosticBox = (TextBox)typeof(MainForm).GetField("diagnostics", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        diagnosticBox.Lines = ["10:42:11  Quiet — Spotify stays as you left it.",
            "10:41:33  Waiting for quiet — Spotify resumes in 4 s.",
            "10:41:28  Spotify paused — browser is making sound."];
        Application.DoEvents();
        Capture("diagnostics.png");
        var tray = (NotifyIcon)typeof(MainForm).GetField("tray", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        var menu = tray.ContextMenuStrip!;
        menu.Show(form.PointToScreen(new Point(50, 50)));
        Application.DoEvents();
        using (var bitmap = new Bitmap(menu.Width, menu.Height))
        {
            menu.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(folder, "tray-menu.png"));
        }
        menu.Hide();
        settings.Theme = "Light";
        typeof(MainForm).GetMethod("ApplyTheme", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);
        tabs.SelectedIndex = 0;
        Application.DoEvents();
        Capture("main-light.png");
        tabs.SelectedIndex = 1;
        view.SelectedIndex = 0;
        Application.DoEvents();
        Capture("analytics-light.png");
        form.Hide();
        Console.WriteLine("PASS: DPI-aware layout, launch/reopen, and preview interaction checks");
        Size PreviewSize(int width, int height) => new(DpiScale.Pixels(form, width), DpiScale.Pixels(form, height));
        void Capture(string name)
        {
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(folder, name));
            CheckTextBounds(form);
        }
        void CheckTextBounds(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                if (!child.Visible) continue;
                if (child is Label label && !string.IsNullOrEmpty(label.Text) && label.Height < label.Font.Height)
                    throw new Exception($"Text is clipped at {form.DeviceDpi} DPI: {label.Text} ({label.Height} < {label.Font.Height}).");
                if (child is Label or Button && !string.IsNullOrEmpty(child.Text))
                    for (var ancestor = child.Parent; ancestor != null; ancestor = ancestor.Parent)
                        if (!ancestor.ClientRectangle.Contains(ancestor.RectangleToClient(child.RectangleToScreen(child.ClientRectangle))))
                            throw new Exception($"Control is clipped at {form.DeviceDpi} DPI: {child.Text}; {child.Bounds}; ancestor {ancestor.GetType().Name} {ancestor.ClientRectangle}; relative {ancestor.RectangleToClient(child.RectangleToScreen(child.ClientRectangle))}.");
                CheckTextBounds(child);
            }
        }
        void CaptureDialog(Action show, string name)
        {
            var captured = false;
            Exception? captureError = null;
            using var timer = new System.Windows.Forms.Timer { Interval = 100 };
            timer.Tick += (_, _) =>
            {
                var dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(x => x != form);
                if (dialog == null || captured) return;
                captured = true;
                try
                {
                    using var bitmap = new Bitmap(dialog.Width, dialog.Height);
                    dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                    bitmap.Save(Path.Combine(folder, name));
                    CheckTextBounds(dialog);
                }
                catch (Exception error) { captureError = error; }
                finally { dialog.Close(); }
            };
            timer.Start();
            show();
            timer.Stop();
            if (!captured) throw new Exception($"Dialog was not captured: {name}.");
            if (captureError != null) throw new Exception($"Dialog layout failed: {name}.", captureError);
        }
    }
}
