namespace DriverUpdater;

/// <summary>
/// The on/off switch Task Manager uses for startup programs: a 12-byte value under
/// "StartupApproved" next to each entry. Using the same one keeps the app and Windows in
/// agreement, and keeps every change reversible from either side.
/// </summary>
public static class StartupApproval
{
    /// <summary>No value means enabled; otherwise an odd first byte means disabled.</summary>
    public static bool IsEnabled(byte[]? value) => value is not { Length: > 0 } || (value[0] & 1) == 0;

    /// <summary>02 = enabled; 03 followed (from byte 4) by when it was disabled, as Task Manager writes it.</summary>
    public static byte[] Value(bool enabled, DateTime nowUtc)
    {
        var value = new byte[12];
        value[0] = enabled ? (byte)2 : (byte)3;
        if (!enabled) BitConverter.GetBytes(nowUtc.ToFileTimeUtc()).CopyTo(value, 4);
        return value;
    }
}
