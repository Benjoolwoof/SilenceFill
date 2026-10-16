using System.Threading;

namespace SilenceFill;

internal static class Program
{
    private static EventWaitHandle? showExisting;

    internal static void InitializeUi() => ApplicationConfiguration.Initialize();

    [STAThread]
    private static void Main()
    {
        using var singleton = new Mutex(true, @"Local\SilenceFill.Singleton", out var first);
        if (!first)
        {
            try { EventWaitHandle.OpenExisting(@"Local\SilenceFill.Show").Set(); }
            catch (Exception) { }
            return;
        }
        InitializeUi();
        showExisting = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\SilenceFill.Show");
        using var form = new MainForm();
        using var cancellation = new CancellationTokenSource();
        var listener = Task.Run(() =>
        {
            while (!cancellation.IsCancellationRequested)
            {
                if (!showExisting.WaitOne(500) || form.IsDisposed) continue;
                try { form.BeginInvoke(form.ShowSettings); } catch (InvalidOperationException) { }
            }
        });
        Application.Run(form);
        cancellation.Cancel();
        showExisting.Set();
        try { listener.Wait(1000); } catch (Exception) { }
        showExisting.Dispose();
    }
}
