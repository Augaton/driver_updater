using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace DriverUpdater.Tuning;

/// <summary>The real registry, 64-bit view, for <see cref="TweakEngine"/>.</summary>
public sealed class WindowsRegistry : IRegistry
{
    static RegistryKey Root(Hive hive) =>
        RegistryKey.OpenBaseKey(hive == Hive.CurrentUser ? RegistryHive.CurrentUser : RegistryHive.LocalMachine, RegistryView.Registry64);

    public object? Get(Hive hive, string key, string name)
    {
        using var root = Root(hive);
        using var opened = root.OpenSubKey(key);
        return opened?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) switch
        {
            int number => number,
            string text => text,
            _ => null, // other types are never ours
        };
    }

    public void Set(Hive hive, string key, string name, object value)
    {
        using var root = Root(hive);
        using var opened = root.CreateSubKey(key);
        opened.SetValue(name, value, value is int ? RegistryValueKind.DWord : RegistryValueKind.String);
    }

    public void Delete(Hive hive, string key, string name)
    {
        using var root = Root(hive);
        using var opened = root.OpenSubKey(key, writable: true);
        opened?.DeleteValue(name, throwOnMissingValue: false);
    }
}

/// <summary>
/// Applies the "PC de guerre" tweaks on Windows: registry ones through <see cref="TweakEngine"/>,
/// the power plan and hibernation through powercfg. Tweaks needing administrator rights only
/// run in the administrator helper.
/// </summary>
public static partial class Tuner
{
    static readonly WindowsRegistry Registry = new();
    const string HighPerformance = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    const string UltimateTemplate = "e9a42b02-d5df-448d-aa00-03f14749eb61";
    const string PowerBackup = TweakEngine.BackupKey + @"\" + TweakCatalog.PowerPlan;

    public static TweakState State(Tweak tweak)
    {
        if (Environment.OSVersion.Version.Build < tweak.MinBuild) return TweakState.NotApplicable;
        return tweak.Id switch
        {
            TweakCatalog.PowerPlan => ActivePlan() is { } plan && (plan == HighPerformance || plan == CreatedPlan())
                ? TweakState.Applied : TweakState.NotApplied,
            TweakCatalog.Hibernation => Registry.Get(Hive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled") is 0
                ? TweakState.Applied : TweakState.NotApplied,
            _ => TweakEngine.IsApplied(tweak, Registry) ? TweakState.Applied : TweakState.NotApplied,
        };
    }

    /// <summary>Whether this app applied it, so "Annuler" has something to give back.</summary>
    public static bool CanUndo(Tweak tweak) => tweak.Id switch
    {
        TweakCatalog.PowerPlan => Registry.Get(Hive.CurrentUser, PowerBackup, "previous") is string,
        TweakCatalog.Hibernation => Registry.Get(Hive.LocalMachine, TweakEngine.BackupKey + @"\" + tweak.Id, "was-on") is 1,
        _ => TweakEngine.HasBackup(tweak, Registry),
    };

    public static void Apply(Tweak tweak)
    {
        switch (tweak.Id)
        {
            case TweakCatalog.PowerPlan:
                ApplyPowerPlan();
                break;
            case TweakCatalog.Hibernation:
                if (Registry.Get(Hive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled") is not 0)
                    Registry.Set(Hive.LocalMachine, TweakEngine.BackupKey + @"\" + tweak.Id, "was-on", 1);
                PowerCfg("/hibernate", "off");
                break;
            default:
                TweakEngine.Apply(tweak, Registry);
                break;
        }
        AfterChange(tweak);
    }

    public static void Undo(Tweak tweak)
    {
        switch (tweak.Id)
        {
            case TweakCatalog.PowerPlan:
                if (Registry.Get(Hive.CurrentUser, PowerBackup, "previous") is string previous) PowerCfg("/setactive", previous);
                if (CreatedPlan() is { } created) PowerCfg("/delete", created);
                Registry.Delete(Hive.CurrentUser, PowerBackup, "previous");
                Registry.Delete(Hive.CurrentUser, PowerBackup, "created");
                break;
            case TweakCatalog.Hibernation:
                if (Registry.Get(Hive.LocalMachine, TweakEngine.BackupKey + @"\" + tweak.Id, "was-on") is 1) PowerCfg("/hibernate", "on");
                Registry.Delete(Hive.LocalMachine, TweakEngine.BackupKey + @"\" + tweak.Id, "was-on");
                break;
            default:
                TweakEngine.Undo(tweak, Registry);
                break;
        }
        AfterChange(tweak);
    }

    // "Performances optimales" is a hidden template: Windows makes a copy to use. Where it is
    // refused, "Performances élevées"; machines with modern standby offer neither.
    static void ApplyPowerPlan()
    {
        if (ActivePlan() is { } current && Registry.Get(Hive.CurrentUser, PowerBackup, "previous") is null)
            Registry.Set(Hive.CurrentUser, PowerBackup, "previous", current);
        var created = CreatedPlan();
        if (created is null)
        {
            var (code, output) = PowerCfg("/duplicatescheme", UltimateTemplate);
            created = code == 0 ? PlanGuid().Match(output) is { Success: true } m ? m.Value : null : null;
            if (created is not null) Registry.Set(Hive.CurrentUser, PowerBackup, "created", created);
        }
        if (created is not null && PowerCfg("/setactive", created).ExitCode == 0) return;
        if (PowerCfg("/setactive", HighPerformance).ExitCode == 0) return;
        throw new InvalidOperationException(
            "Ce PC ne propose que le plan équilibré (veille moderne) : choisissez « Performances optimales » dans Paramètres → Alimentation → Mode d'alimentation.");
    }

    static string? CreatedPlan()
    {
        if (Registry.Get(Hive.CurrentUser, PowerBackup, "created") is not string created) return null;
        // Still there? The user may have deleted it from the control panel.
        return PowerCfg("/list").Output.Contains(created, StringComparison.OrdinalIgnoreCase) ? created : null;
    }

    static string? ActivePlan() =>
        PlanGuid().Match(PowerCfg("/getactivescheme").Output) is { Success: true } m ? m.Value.ToLowerInvariant() : null;

    static (int ExitCode, string Output) PowerCfg(params string[] arguments)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "powercfg.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }

    // The mouse settings live in the registry but Windows reads them at sign-in: tell it now.
    static void AfterChange(Tweak tweak)
    {
        if (tweak.Id != "mouse-precision-off") return;
        int Read(string name) => int.TryParse(Registry.Get(Hive.CurrentUser, @"Control Panel\Mouse", name) as string, out var v) ? v : 0;
        int[] mouse = [Read("MouseThreshold1"), Read("MouseThreshold2"), Read("MouseSpeed")];
        const uint SetMouse = 0x0004, UpdateAndBroadcast = 0x03;
        SystemParametersInfoW(SetMouse, 0, mouse, UpdateAndBroadcast);
    }

    [GeneratedRegex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex PlanGuid();

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SystemParametersInfoW(uint action, uint param, int[] value, uint flags);
}
