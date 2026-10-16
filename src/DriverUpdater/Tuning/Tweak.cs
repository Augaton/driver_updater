namespace DriverUpdater.Tuning;

public enum Hive { CurrentUser, LocalMachine }

/// <summary>Where a tweak stands on this PC.</summary>
public enum TweakState { Applied, NotApplied, NotApplicable }

/// <summary>The registry, as the tweak engine sees it: a fake one in the tests.</summary>
public interface IRegistry
{
    /// <summary>An int (DWORD), a string, or null when the value does not exist.</summary>
    object? Get(Hive hive, string key, string name);
    void Set(Hive hive, string key, string name, object value);
    void Delete(Hive hive, string key, string name);
    /// <summary>The names of the key's values; empty if the key does not exist.</summary>
    IReadOnlyList<string> ValueNames(Hive hive, string key);
}

/// <summary>
/// One registry value a tweak sets. <see cref="Flag"/> values are items of a ";"-separated
/// list ("VRROptimizeEnable=0;SwapEffectUpgradeEnable=1;"): only that item is set, the
/// others the user may have are kept. <see cref="WhenAbsent"/> is what Windows does when the
/// value does not exist (Game Mode is on by default, for one).
/// </summary>
public sealed record RegistryValue(Hive Hive, string Key, string Name, object Value, bool Flag = false, object? WhenAbsent = null)
{
    /// <summary>As a <see cref="Value"/>: the value must not exist (Windows' default, often).</summary>
    public static readonly object Removed = new();
}

/// <param name="Id">Stable identifier: it names the backup and the administrator command.</param>
/// <param name="InOneClick">Part of "Tout optimiser": safe for everyone. Others need a choice (mouse feel, laptop battery).</param>
/// <param name="MinBuild">The first Windows build that has the setting (22000: Windows 11).</param>
/// <param name="Retired">
/// No longer offered (its effect was not confirmed, or it hurts some PCs); still listed where
/// this app applied it, so that it can be undone.
/// </param>
public sealed record Tweak(string Id, string Group, string Title, string Why, bool NeedsAdmin, bool InOneClick,
    IReadOnlyList<RegistryValue> Values, bool NeedsRestart = false, int MinBuild = 0, bool Retired = false);

/// <summary>
/// Applies and undoes registry tweaks. Before the first change, every value is backed up
/// (including "did not exist"), so undo puts the registry back exactly as it was, not to some
/// default. Backups live next to the values: HKCU's under HKCU, HKLM's under HKLM.
/// </summary>
public static class TweakEngine
{
    public const string BackupKey = @"Software\Driver Updater\Tweaks";
    const string Absent = "absent";

    public static bool IsApplied(Tweak tweak, IRegistry registry) =>
        tweak.Values.All(v => v.Flag ? FlagIn(registry.Get(v.Hive, v.Key, v.Name) as string, (string)v.Value)
            : ReferenceEquals(v.Value, RegistryValue.Removed) ? registry.Get(v.Hive, v.Key, v.Name) is null
            : Same(registry.Get(v.Hive, v.Key, v.Name) ?? v.WhenAbsent, v.Value));

    public static void Apply(Tweak tweak, IRegistry registry)
    {
        foreach (var value in tweak.Values)
        {
            var current = registry.Get(value.Hive, value.Key, value.Name);
            var backup = BackupName(value);
            // Never overwrite a backup: applying twice must still undo to the original.
            if (registry.Get(value.Hive, BackupKey + @"\" + tweak.Id, backup) is null)
                registry.Set(value.Hive, BackupKey + @"\" + tweak.Id, backup, Encode(current));
            if (ReferenceEquals(value.Value, RegistryValue.Removed)) registry.Delete(value.Hive, value.Key, value.Name);
            else registry.Set(value.Hive, value.Key, value.Name, value.Flag ? WithFlag(current as string, (string)value.Value) : value.Value);
        }
    }

    /// <summary>Puts back what <see cref="Apply"/> found; a tweak never applied here is left alone.</summary>
    public static bool Undo(Tweak tweak, IRegistry registry)
    {
        bool restored = false;
        foreach (var value in tweak.Values)
        {
            var backup = BackupName(value);
            if (registry.Get(value.Hive, BackupKey + @"\" + tweak.Id, backup) is not string saved) continue;
            if (Decode(saved) is { } previous) registry.Set(value.Hive, value.Key, value.Name, previous);
            else registry.Delete(value.Hive, value.Key, value.Name);
            registry.Delete(value.Hive, BackupKey + @"\" + tweak.Id, backup);
            restored = true;
        }
        return restored;
    }

    public static bool HasBackup(Tweak tweak, IRegistry registry) =>
        tweak.Values.Any(v => registry.Get(v.Hive, BackupKey + @"\" + tweak.Id, BackupName(v)) is not null);

    static string BackupName(RegistryValue value) => $@"{value.Key}\{value.Name}";

    static string Encode(object? value) => value switch
    {
        null => Absent,
        int number => "dword:" + number,
        string text => "string:" + text,
        _ => throw new NotSupportedException(L.F("Type de valeur non géré : {0}", value.GetType().Name)),
    };

    static object? Decode(string saved) =>
        saved.StartsWith("dword:", StringComparison.Ordinal) ? int.Parse(saved[6..], System.Globalization.CultureInfo.InvariantCulture)
        : saved.StartsWith("string:", StringComparison.Ordinal) ? saved[7..]
        : null;

    static bool Same(object? current, object wanted) => current switch
    {
        int number => wanted is int w && number == w,
        string text => wanted is string w && text == w,
        _ => false,
    };

    // "A=1;B=0;" holds the flag "B=0"; each item compared as a whole.
    static bool FlagIn(string? list, string flag) =>
        list is not null && list.Split(';').Any(item => item.Trim() == flag);

    public static string WithFlag(string? list, string flag)
    {
        var name = flag.Split('=')[0];
        var items = (list ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Where(item => item.Split('=')[0].Trim() != name)
            .Append(flag);
        return string.Join(";", items) + ";";
    }
}
