using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DriverUpdater.Admin;

/// <summary>Which exe a running process comes from, read with the least access Windows offers.</summary>
public static class NativeProcess
{
    const uint QueryLimitedInformation = 0x1000;

    /// <summary>
    /// Full path of the process's exe, or null if it is gone. Works on elevated and most
    /// protected processes too, unlike <c>Process.MainModule</c>, and costs far less.
    /// </summary>
    public static string? ImagePath(int processId)
    {
        using var handle = OpenProcess(QueryLimitedInformation, false, (uint)processId);
        if (handle.IsInvalid) return null;
        var buffer = new char[1024];
        uint length = (uint)buffer.Length;
        return QueryFullProcessImageNameW(handle, 0, buffer, ref length) ? new string(buffer, 0, (int)length) : null;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool QueryFullProcessImageNameW(SafeProcessHandle process, uint flags, [Out] char[] name, ref uint size);
}
