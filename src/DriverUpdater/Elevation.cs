using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;

namespace DriverUpdater;

/// <summary>One line on the pipe between the window and the administrator helper.</summary>
public sealed record ElevationMessage(string Kind, string? Text = null, JsonElement? Data = null)
{
    public const string Progress = "progress", Ask = "ask", Result = "result", Error = "error";
}

/// <summary>
/// The window runs without administrator rights. For each action that needs them it starts
/// the same exe again as administrator (Windows asks the user), with the action on the command
/// line; the helper does that one thing, reports through a private pipe, and exits. No
/// elevated process stays around for other programs to talk to.
/// </summary>
public static class Elevation
{
    public const string Argument = "--admin";
    const int ErrorCancelled = 1223; // the user said no to the Windows prompt

    /// <summary>Runs <paramref name="command"/> as administrator and returns its result.</summary>
    /// <param name="ask">Answers the helper's yes/no questions, e.g. "go on without a restore point?".</param>
    public static async Task<T> RunAsync<T>(string command, IEnumerable<string> args, IProgress<string> progress,
        Func<string, bool> ask, string? exePath = null)
    {
        var pipeName = AdminCommand.NewPipeName();
        // CurrentUserOnly: only this Windows account may open the pipe.
        using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | PipeOptions.FirstPipeInstance);

        var start = new ProcessStartInfo(exePath ?? Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" };
        foreach (var argument in new[] { Argument, pipeName, command }.Concat(args)) start.ArgumentList.Add(argument);
        Process helper;
        try
        {
            helper = Process.Start(start)!;
        }
        catch (Win32Exception e) when (e.NativeErrorCode == ErrorCancelled)
        {
            throw new OperationCanceledException("Autorisation administrateur refusée.");
        }

        using (helper)
        {
            var connected = pipe.WaitForConnectionAsync();
            if (await Task.WhenAny(connected, helper.WaitForExitAsync()) != connected)
                throw new InvalidOperationException($"Le module administrateur s'est arrêté (code {helper.ExitCode}).");
            await connected;

            // Another program of this account could have raced to the pipe: talk only to ours.
            if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out uint clientId) || clientId != helper.Id)
                throw new InvalidOperationException("Connexion inattendue au module administrateur.");

            using var reader = new StreamReader(pipe, leaveOpen: true);
            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            while (await reader.ReadLineAsync() is { } line)
            {
                var message = JsonSerializer.Deserialize<ElevationMessage>(line)
                    ?? throw new InvalidDataException("Message vide du module administrateur.");
                switch (message.Kind)
                {
                    case ElevationMessage.Progress:
                        progress.Report(message.Text ?? "");
                        break;
                    case ElevationMessage.Ask:
                        await writer.WriteLineAsync(ask(message.Text ?? "") ? "yes" : "no");
                        break;
                    case ElevationMessage.Result:
                        return message.Data is { } data ? data.Deserialize<T>()! : default!;
                    case ElevationMessage.Error:
                        throw new InvalidOperationException(message.Text);
                }
            }
            throw new InvalidOperationException("Le module administrateur s'est arrêté sans répondre.");
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetNamedPipeClientProcessId(SafeHandle pipe, out uint clientProcessId);
}

/// <summary>The helper's end of the pipe.</summary>
public sealed class HelperChannel : IDisposable
{
    readonly NamedPipeClientStream pipe;
    readonly StreamReader reader;
    readonly StreamWriter writer;

    HelperChannel(NamedPipeClientStream pipe, bool fromThisApp)
    {
        this.pipe = pipe;
        FromThisApp = fromThisApp;
        reader = new StreamReader(pipe, leaveOpen: true);
        writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
    }

    /// <summary>
    /// Whether the process at the other end runs this very exe, i.e. is Driver Updater's
    /// window. Any program can start the helper with a valid command and get Windows to ask
    /// "allow Driver Updater?"; a user would say yes to that name. Only our window may then
    /// actually command it.
    /// </summary>
    public bool FromThisApp { get; }

    public static async Task<HelperChannel> ConnectAsync(string pipeName)
    {
        if (!AdminCommand.IsPipeName(pipeName)) throw new ArgumentException("Nom de canal invalide.");
        // Identification only: the window, which runs without rights, may check who we are
        // but can never act as us (impersonate this administrator process).
        var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous,
            TokenImpersonationLevel.Identification);
        await pipe.ConnectAsync(10_000); // milliseconds
        // Both paths read the same way, so short (8.3) or long spellings compare equal.
        bool fromThisApp = GetNamedPipeServerProcessId(pipe.SafePipeHandle, out uint server)
            && NativeProcess.ImagePath((int)server) is { } windowExe
            && NativeProcess.ImagePath(Environment.ProcessId) is { } ownExe
            && string.Equals(windowExe, ownExe, StringComparison.OrdinalIgnoreCase);
        return new HelperChannel(pipe, fromThisApp);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetNamedPipeServerProcessId(SafeHandle pipe, out uint serverProcessId);

    public void Progress(string text) => Send(new(ElevationMessage.Progress, text));
    public void Result(object? data) => Send(new(ElevationMessage.Result, Data: JsonSerializer.SerializeToElement(data)));
    public void Error(string text) => Send(new(ElevationMessage.Error, text));

    /// <summary>Asks the user, in the window, a yes/no question. Only a "yes" answer counts.</summary>
    public async Task<bool> AskAsync(string question)
    {
        Send(new(ElevationMessage.Ask, question));
        return await reader.ReadLineAsync() == "yes";
    }

    void Send(ElevationMessage message) => writer.WriteLine(JsonSerializer.Serialize(message));

    public void Dispose()
    {
        writer.Dispose();
        reader.Dispose();
        pipe.Dispose();
    }
}
