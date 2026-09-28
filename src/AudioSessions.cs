using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SilenceFill;

internal readonly record struct AudioSession(string ProcessName, bool Active, float Peak);

internal static class AudioSessions
{
    public static IReadOnlyList<AudioSession> Snapshot()
    {
        var result = new List<AudioSession>();
        IMMDeviceEnumerator? enumerator = null;
        IMMDeviceCollection? devices = null;
        try
        {
            var enumeratorType = Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"))
                ?? throw new InvalidOperationException("Windows audio device enumerator is unavailable.");
            enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(enumeratorType)!;
            enumerator.EnumAudioEndpoints(0, 1, out devices); // All active render devices.
            devices.GetCount(out uint count);
            for (uint i = 0; i < count; i++)
            {
                IMMDevice? device = null;
                IAudioSessionManager2? manager = null;
                IAudioSessionEnumerator? sessions = null;
                try
                {
                    devices.Item(i, out device);
                    var iid = typeof(IAudioSessionManager2).GUID;
                    device.Activate(ref iid, 23, IntPtr.Zero, out var managerObject);
                    manager = (IAudioSessionManager2)managerObject;
                    manager.GetSessionEnumerator(out sessions);
                    sessions.GetCount(out int sessionCount);
                    for (int j = 0; j < sessionCount; j++)
                    {
                        IAudioSessionControl? control = null;
                        try
                        {
                            sessions.GetSession(j, out control);
                            var extended = (IAudioSessionControl2)control;
                            extended.GetProcessId(out uint pid);
                            if (pid == 0) continue;
                            string name;
                            try
                            {
                                using var process = Process.GetProcessById((int)pid);
                                name = process.ProcessName;
                            }
                            catch (Exception) { continue; }
                            control.GetState(out int state);
                            float peak = 0;
                            if (state == 1)
                            {
                                try { ((IAudioMeterInformation)control).GetPeakValue(out peak); }
                                catch (COMException) { }
                            }
                            result.Add(new AudioSession(name, state == 1, peak));
                        }
                        catch (COMException) { }
                        finally { Release(control); }
                    }
                }
                catch (COMException) { /* A device may disappear while scanning. */ }
                finally { Release(sessions); Release(manager); Release(device); }
            }
        }
        catch (COMException) { }
        finally { Release(devices); Release(enumerator); }
        return result;
    }

    private static void Release(object? value)
    {
        if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
    }
}

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    void EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
    void GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    void GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    void RegisterEndpointNotificationCallback(IntPtr callback);
    void UnregisterEndpointNotificationCallback(IntPtr callback);
}

[ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection
{
    void GetCount(out uint count);
    void Item(uint index, out IMMDevice device);
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    void Activate(ref Guid iid, uint clsctx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    void OpenPropertyStore(int access, out IntPtr properties);
    void GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    void GetState(out int state);
}

[ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionManager2
{
    void GetAudioSessionControl(ref Guid sessionGuid, uint flags, out IAudioSessionControl control);
    void GetSimpleAudioVolume(ref Guid sessionGuid, uint flags, out IntPtr volume);
    void GetSessionEnumerator(out IAudioSessionEnumerator sessions);
    void RegisterSessionNotification(IntPtr notification);
    void UnregisterSessionNotification(IntPtr notification);
    void RegisterDuckNotification(IntPtr sessionId, IntPtr notification);
    void UnregisterDuckNotification(IntPtr notification);
}

[ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionEnumerator
{
    void GetCount(out int count);
    void GetSession(int index, out IAudioSessionControl control);
}

[ComImport, Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl
{
    void GetState(out int state);
    void GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
    void SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid context);
    void GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
    void SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid context);
    void GetGroupingParam(out Guid group);
    void SetGroupingParam(ref Guid group, ref Guid context);
    void RegisterAudioSessionNotification(IntPtr events);
    void UnregisterAudioSessionNotification(IntPtr events);
}

[ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl2 : IAudioSessionControl
{
    new void GetState(out int state);
    new void GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
    new void SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid context);
    new void GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
    new void SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid context);
    new void GetGroupingParam(out Guid group);
    new void SetGroupingParam(ref Guid group, ref Guid context);
    new void RegisterAudioSessionNotification(IntPtr events);
    new void UnregisterAudioSessionNotification(IntPtr events);
    void GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
    void GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
    void GetProcessId(out uint processId);
    void IsSystemSoundsSession();
    void SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
}

[ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioMeterInformation
{
    void GetPeakValue(out float peak);
    void GetMeteringChannelCount(out uint count);
    void GetChannelsPeakValues(uint count, IntPtr peaks);
    void QueryHardwareSupport(out uint mask);
}

