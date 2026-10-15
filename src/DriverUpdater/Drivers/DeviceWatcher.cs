using System.Management;

namespace DriverUpdater.Drivers;

/// <summary>
/// Tells when a device is plugged in or removed. Windows pushes these events
/// (Win32_DeviceChangeEvent): nothing is polled, nothing runs while nothing changes.
/// </summary>
public sealed class DeviceWatcher : IDisposable
{
    readonly ManagementEventWatcher watcher = new(new WqlEventQuery("SELECT * FROM Win32_DeviceChangeEvent"));

    /// <summary>Raised on a background thread, often several times for one plug-in.</summary>
    public event Action? Changed;

    public DeviceWatcher()
    {
        watcher.EventArrived += (_, e) =>
        {
            e.NewEvent.Dispose();
            Changed?.Invoke();
        };
        watcher.Start();
    }

    public void Dispose()
    {
        watcher.Stop();
        watcher.Dispose();
    }
}
