using System.Text.RegularExpressions;

namespace DriverUpdater.Software;

/// <summary>
/// The runtimes games install and expect: Visual C++, DirectX 9 extras, XNA, OpenAL, PhysX.
/// Installed with winget, from Microsoft's package catalog.
/// </summary>
public sealed partial record GameComponent(string Name, string WingetId, string Why)
{
    /// <summary>Detected from "Apps installées" names, except DirectX which has no entry there.</summary>
    public static readonly IReadOnlyList<GameComponent> All =
    [
        new("Visual C++ 2015-2022 (64 bits)", "Microsoft.VCRedist.2015+.x64", L.T("Presque tous les jeux récents")),
        new("Visual C++ 2015-2022 (32 bits)", "Microsoft.VCRedist.2015+.x86", L.T("Jeux et lanceurs 32 bits")),
        new("Visual C++ 2013 (64 bits)", "Microsoft.VCRedist.2013.x64", L.T("Jeux de 2014 à 2017")),
        new("Visual C++ 2013 (32 bits)", "Microsoft.VCRedist.2013.x86", L.T("Jeux de 2014 à 2017")),
        new("Visual C++ 2012 (64 bits)", "Microsoft.VCRedist.2012.x64", L.T("Jeux de 2013 à 2015")),
        new("Visual C++ 2012 (32 bits)", "Microsoft.VCRedist.2012.x86", L.T("Jeux de 2013 à 2015")),
        new("Visual C++ 2010 (64 bits)", "Microsoft.VCRedist.2010.x64", L.T("Jeux de 2010 à 2013")),
        new("Visual C++ 2010 (32 bits)", "Microsoft.VCRedist.2010.x86", L.T("Jeux de 2010 à 2013")),
        new("Visual C++ 2008 (64 bits)", "Microsoft.VCRedist.2008.x64", L.T("Jeux anciens")),
        new("Visual C++ 2008 (32 bits)", "Microsoft.VCRedist.2008.x86", L.T("Jeux anciens")),
        new("Visual C++ 2005 (64 bits)", "Microsoft.VCRedist.2005.x64", L.T("Jeux très anciens")),
        new("Visual C++ 2005 (32 bits)", "Microsoft.VCRedist.2005.x86", L.T("Jeux très anciens")),
        new("DirectX 9 (juin 2010)", "Microsoft.DirectX", L.T("Bibliothèques D3DX, XAudio et XInput des jeux DirectX 9 à 11")),
        new("XNA Framework 4.0", "Microsoft.XNARedist", L.T("Jeux indépendants XNA (Terraria, Stardew Valley…)")),
        new("OpenAL", "CreativeTechnology.OpenAL", L.T("Son 3D de certains jeux")),
        new("NVIDIA PhysX", "Nvidia.PhysX", L.T("Physique de jeux optimisés NVIDIA")),
    ];

    /// <summary>Whether one of the installed programs' names is this component.</summary>
    public bool IsIn(IEnumerable<string> installedNames)
    {
        var pattern = WingetId switch
        {
            "Microsoft.VCRedist.2015+.x64" => VcRedist2015("x64"),
            "Microsoft.VCRedist.2015+.x86" => VcRedist2015("x86"),
            "Microsoft.DirectX" => null,
            "Microsoft.XNARedist" => XnaPattern(),
            "CreativeTechnology.OpenAL" => OpenAlPattern(),
            "Nvidia.PhysX" => PhysXPattern(),
            _ when WingetId.StartsWith("Microsoft.VCRedist.") => OlderVcRedist(WingetId[19..23], WingetId[^3..]),
            _ => null,
        };
        return pattern is not null && installedNames.Any(pattern.IsMatch);
    }

    // "Microsoft Visual C++ 2015-2022 Redistributable (x64) - 14.44…", also 2015-2019 and 2017.
    static Regex VcRedist2015(string arch) =>
        new($@"Visual C\+\+ 20(15|17|19|22)\S*.*Redistributable.*\b{arch}\b", RegexOptions.IgnoreCase);

    // "…2013 Redistributable (x64)", "…2010  x64 Redistributable", "…2008 Redistributable - x64 9.0…".
    // The 32-bit 2005 one says no architecture at all: 32 bits is whatever does not say x64.
    static Regex OlderVcRedist(string year, string arch) => arch == "x64"
        ? new($@"Visual C\+\+ {year}\b.*\bx64\b", RegexOptions.IgnoreCase)
        : new($@"Visual C\+\+ {year}\b(?!.*\bx64\b)", RegexOptions.IgnoreCase);

    [GeneratedRegex(@"XNA Framework Redistributable 4\.0", RegexOptions.IgnoreCase)]
    private static partial Regex XnaPattern();

    [GeneratedRegex(@"^OpenAL\b", RegexOptions.IgnoreCase)]
    private static partial Regex OpenAlPattern();

    [GeneratedRegex(@"PhysX", RegexOptions.IgnoreCase)]
    private static partial Regex PhysXPattern();
}
