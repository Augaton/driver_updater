using DriverUpdater.Tuning;

/// <summary>The registry, in memory, as the tweak engine sees it.</summary>
sealed class FakeRegistry : IRegistry
{
    public readonly Dictionary<(Hive, string, string), object> Values = [];
    public object? Get(Hive hive, string key, string name) => Values.GetValueOrDefault((hive, key.ToLowerInvariant(), name.ToLowerInvariant()));
    public void Set(Hive hive, string key, string name, object value) => Values[(hive, key.ToLowerInvariant(), name.ToLowerInvariant())] = value;
    public void Delete(Hive hive, string key, string name) => Values.Remove((hive, key.ToLowerInvariant(), name.ToLowerInvariant()));
    public IReadOnlyList<string> ValueNames(Hive hive, string key) =>
        Values.Keys.Where(k => k.Item1 == hive && k.Item2 == key.ToLowerInvariant()).Select(k => k.Item3).ToList();
}
