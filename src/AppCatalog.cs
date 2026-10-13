using System.Diagnostics;

namespace SilenceFill;

internal sealed record AppChoice(string Name, string DisplayName, bool IsRunning)
{
    public override string ToString()
    {
        var label = DisplayName == Name ? Name : $"{DisplayName}  ({Name})";
        return IsRunning ? label : label + "  — not running";
    }
}

internal static class AppCatalog
{
    public static bool HasOtherSilenceFillProcess()
    {
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id != Environment.ProcessId && process.ProcessName.StartsWith("SilenceFill", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                catch (Exception) { }
            }
        }
        return false;
    }

    public static IReadOnlyList<string> Find(IReadOnlyList<AudioSession> sessions)
    {
        var names = new HashSet<string>(sessions.Select(s => s.ProcessName), StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try { if (process.MainWindowHandle != IntPtr.Zero) names.Add(process.ProcessName); }
                catch (Exception) { }
            }
        }
        return names.Where(n => !Excluded(n)).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public static bool Excluded(string name) => string.IsNullOrWhiteSpace(name) ||
        name.Equals("Spotify", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("SilenceFill", StringComparison.OrdinalIgnoreCase);

    public static AppChoice Describe(string name, bool running = true)
    {
        try
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    try
                    {
                        var description = FileVersionInfo.GetVersionInfo(process.MainModule!.FileName).FileDescription?.Trim();
                        if (!string.IsNullOrWhiteSpace(description) && !description.Equals(name, StringComparison.OrdinalIgnoreCase))
                            return new(name, description, running);
                    }
                    catch (Exception) { }
                }
            }
        }
        catch (Exception) { }
        return new(name, name, running);
    }

    public static Icon? IconFor(string name)
    {
        try
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    try { return Icon.ExtractAssociatedIcon(process.MainModule!.FileName); }
                    catch (Exception) { }
                }
            }
        }
        catch (Exception) { }
        return null;
    }
}
