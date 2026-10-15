using Xunit;

public sealed class TempCleanerTests : IDisposable
{
    static readonly DateTime Cutoff = DateTime.UtcNow.AddDays(-2);
    static readonly DateTime Old = Cutoff.AddDays(-1);
    static readonly DateTime Recent = Cutoff.AddHours(1);

    // On Windows the cleaner refuses folders reached through 8.3 short names, and %TEMP% often
    // is one (C:\Users\RUNNER~1\...), so build the path from the profile folder as the app does.
    static readonly string Base = OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp")
        : Path.GetTempPath();

    readonly string root = Directory.CreateDirectory(Path.Combine(Base, "tempcleaner-" + Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    string MakeFile(string relativePath, DateTime lastWriteUtc, int size = 10)
    {
        // GetFullPath turns "temp/a.tmp" into "temp\a.tmp" on Windows, as the file system reports it.
        var path = Path.GetFullPath(Path.Combine(root, relativePath));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[size]);
        File.SetLastWriteTimeUtc(path, lastWriteUtc);
        return path;
    }

    TempCleaner.Target TempDir(bool recursive = true) => new(Path.Combine(root, "temp"), recursive);

    static IEnumerable<string> Paths(TempCleaner.ScanResult scan) => scan.Files.Select(c => c.File.FullName);

    [Fact]
    public void Finds_only_files_older_than_the_cutoff()
    {
        var old = MakeFile("temp/old.tmp", Old, size: 100);
        MakeFile("temp/recent.tmp", Recent);

        var scan = TempCleaner.Find([TempDir()], Cutoff);

        Assert.Equal([old], Paths(scan));
        Assert.Equal(100, scan.TotalBytes);
    }

    [Fact]
    public void Non_recursive_target_ignores_subfolders()
    {
        var top = MakeFile("temp/top.tmp", Old);
        MakeFile("temp/sub/nested.tmp", Old);

        var scan = TempCleaner.Find([TempDir(recursive: false)], Cutoff);

        Assert.Equal([top], Paths(scan));
    }

    [Fact]
    public void Never_follows_a_link_out_of_the_temp_folder()
    {
        var outside = MakeFile("precious/document.txt", Old);
        Directory.CreateDirectory(Path.Combine(root, "temp"));
        Directory.CreateSymbolicLink(Path.Combine(root, "temp", "link"), Path.Combine(root, "precious"));

        var scan = TempCleaner.Find([TempDir()], Cutoff);
        TempCleaner.Delete(scan.Files, Cutoff);

        Assert.Empty(scan.Files);
        Assert.True(File.Exists(outside));
    }

    [Fact]
    public void Missing_folder_is_ignored()
    {
        var scan = TempCleaner.Find([new(Path.Combine(root, "absent"), true)], Cutoff);

        Assert.Empty(scan.Files);
        Assert.Empty(scan.RefusedFolders);
    }

    [Fact]
    public void Delete_removes_files_and_reports_freed_bytes()
    {
        var a = MakeFile("temp/a.tmp", Old, size: 30);
        var b = MakeFile("temp/sub/b.tmp", Old, size: 12);

        var result = TempCleaner.Delete(TempCleaner.Find([TempDir()], Cutoff).Files, Cutoff);

        Assert.Equal(new TempCleaner.CleanResult(Deleted: 2, FreedBytes: 42, Skipped: 0), result);
        Assert.False(File.Exists(a));
        Assert.False(File.Exists(b));
    }

    [Fact]
    public void Delete_keeps_a_file_modified_since_the_scan()
    {
        var reused = MakeFile("temp/reused.tmp", Old);
        var scan = TempCleaner.Find([TempDir()], Cutoff);
        File.SetLastWriteTimeUtc(reused, Recent);

        var result = TempCleaner.Delete(scan.Files, Cutoff);

        Assert.Equal(1, result.Skipped);
        Assert.True(File.Exists(reused));
    }

    // The attack an administrator cleaner must survive: between the scan and the delete,
    // another program swaps a scanned folder for a link to files it wants destroyed.
    [WindowsFact]
    public void Delete_refuses_a_folder_swapped_for_a_link_after_the_scan()
    {
        MakeFile("temp/sub/victim.dll", Old);
        var precious = MakeFile("precious/victim.dll", Old);
        var scan = TempCleaner.Find([TempDir()], Cutoff);

        Directory.Delete(Path.Combine(root, "temp", "sub"), recursive: true);
        Directory.CreateSymbolicLink(Path.Combine(root, "temp", "sub"), Path.Combine(root, "precious"));
        var result = TempCleaner.Delete(scan.Files, Cutoff);

        Assert.Equal(new TempCleaner.CleanResult(Deleted: 0, FreedBytes: 0, Skipped: 1), result);
        Assert.True(File.Exists(precious));
    }

    [WindowsFact]
    public void Find_refuses_a_temp_folder_that_is_itself_a_link()
    {
        MakeFile("precious/victim.dll", Old);
        Directory.CreateSymbolicLink(Path.Combine(root, "temp"), Path.Combine(root, "precious"));

        var scan = TempCleaner.Find([TempDir()], Cutoff);

        Assert.Empty(scan.Files);
        Assert.Equal([TempDir().Folder], scan.RefusedFolders);
    }
}

/// <summary>A test that only means something on Windows, and is reported as skipped elsewhere.</summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "Windows uniquement";
    }
}
