namespace DriverUpdater;

public static class Errors
{
    /// <summary>
    /// The message, with the Windows error code when there is one (e.g. 0x8024402C = no
    /// network). Late-bound COM can raise such codes as any exception type; codes in
    /// 0x8013xxxx are .NET's own and say nothing more than the message.
    /// </summary>
    public static string Describe(Exception e) =>
        (uint)e.HResult >> 16 == 0x8013 ? e.Message : $"{e.Message} (0x{e.HResult:X8})";
}
