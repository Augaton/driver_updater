using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace DriverUpdater.Admin;

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

    /// <summary>
    /// Who may open the pipe: this account, and administrators. On a standard account Windows
    /// asks for an administrator's password, and the helper then runs as that other account.
    /// Which process actually connected is checked right after, by its process ID.
    /// </summary>
    static PipeSecurity PipeAccess()
    {
        var user = WindowsIdentity.GetCurrent().User!;
        var security = new PipeSecurity();
        security.SetOwner(user);
        security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        return security;
    }

    /// <summary>
    /// Whether only administrators and Windows itself can change the app's folder: owned by
    /// one of them, and no write access for anyone else. Program Files passes.
    /// </summary>
    internal static bool AppFolderIsProtected()
    {
        var trusted = new HashSet<SecurityIdentifier>
        {
            new(WellKnownSidType.BuiltinAdministratorsSid, null),
            new(WellKnownSidType.LocalSystemSid, null),
            new("S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464"), // TrustedInstaller
            new(WellKnownSidType.CreatorOwnerSid, null), // stands for whoever creates a file: only those above can
        };
        const FileSystemRights GenericAll = (FileSystemRights)0x10000000, GenericWrite = (FileSystemRights)0x40000000;
        const FileSystemRights Writes = FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.WriteAttributes
            | FileSystemRights.WriteExtendedAttributes | FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles
            | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership | GenericAll | GenericWrite;
        // In the parent, what lets someone rename the app's folder away and put their own in its place.
        const FileSystemRights Swaps = FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions
            | FileSystemRights.TakeOwnership | GenericAll;

        bool OnlyTrusted(DirectoryInfo folder, FileSystemRights rights)
        {
            var security = folder.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
            if (security.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner || !trusted.Contains(owner)) return false;
            return security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>()
                .Where(rule => rule.AccessControlType == AccessControlType.Allow && (rule.FileSystemRights & rights) != 0)
                .All(rule => trusted.Contains((SecurityIdentifier)rule.IdentityReference));
        }

        try
        {
            var app = new DirectoryInfo(AppContext.BaseDirectory);
            return OnlyTrusted(app, Writes) && (app.Parent is null || OnlyTrusted(app.Parent, Swaps));
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Runs <paramref name="command"/> as administrator and returns its result.</summary>
    /// <param name="ask">Answers the helper's yes/no questions, e.g. "go on without a restore point?".</param>
    public static async Task<T> RunAsync<T>(string command, IEnumerable<string> args, IProgress<string> progress,
        Func<string, bool> ask, string? exePath = null)
    {
        // The helper runs these very files as administrator: from a folder any program can write
        // to (a portable copy in Downloads), one could swap a file in before the user consents.
        if (exePath is null && !AppFolderIsProtected())
            throw new InvalidOperationException(
                L.T("Les actions administrateur sont désactivées pour cette copie : n'importe quel programme peut modifier les fichiers ") +
                L.T("de son dossier. Installez Driver Updater avec son installateur, ou placez-le dans un dossier que seuls les administrateurs modifient."));

        var pipeName = AdminCommand.NewPipeName();
        using var pipe = NamedPipeServerStreamAcl.Create(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance, 0, 0, PipeAccess());

        var start = new ProcessStartInfo(exePath ?? Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" };
        foreach (var argument in new[] { Argument, pipeName, command }.Concat(args)) start.ArgumentList.Add(argument);
        Process helper;
        try
        {
            helper = Process.Start(start)!;
        }
        catch (Win32Exception e) when (e.NativeErrorCode == ErrorCancelled)
        {
            throw new OperationCanceledException(L.T("Autorisation administrateur refusée."));
        }

        using (helper)
        {
            var connected = pipe.WaitForConnectionAsync();
            if (await Task.WhenAny(connected, helper.WaitForExitAsync()) != connected)
                throw new InvalidOperationException(L.F("Le module administrateur s'est arrêté (code {0}).", helper.ExitCode));
            await connected;

            // Another program of this account could have raced to the pipe: talk only to ours.
            if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out uint clientId) || clientId != helper.Id)
                throw new InvalidOperationException(L.T("Connexion inattendue au module administrateur."));

            using var reader = new StreamReader(pipe, leaveOpen: true);
            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            while (await reader.ReadLineAsync() is { } line)
            {
                var message = JsonSerializer.Deserialize<ElevationMessage>(line)
                    ?? throw new InvalidDataException(L.T("Message vide du module administrateur."));
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
            throw new InvalidOperationException(L.T("Le module administrateur s'est arrêté sans répondre."));
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
        if (!AdminCommand.IsPipeName(pipeName)) throw new ArgumentException(L.T("Nom de canal invalide."));
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
