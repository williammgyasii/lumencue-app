namespace ChurchProjection.Core.Bible;

/// <summary>
/// Booth picker after API.Bible: helloao codes we can cache once, plus hosted JSON we already ship.
/// </summary>
public static class BiblePickerTranslations
{
    public static readonly (string Code, string Name)[] HelloaoBooth =
    [
        ("KJV", "King James Version"),
        ("BSB", "Berean Standard Bible"),
        ("WEB", "World English Bible"),
        ("NET", "NET Bible"),
        ("ASV", "American Standard Version"),
        ("LSV", "Literal Standard Version"),
        ("YLT", "Young's Literal Translation"),
    ];

    public static readonly (string Code, string Name, string Url)[] Hosted =
    [
        ("TPT", "The Passion Translation",
            "https://raw.githubusercontent.com/williammgyasii/lumencue-releases/main/bibles/translations/TPT.json"),
        ("TLB", "The Living Bible",
            "https://raw.githubusercontent.com/williammgyasii/lumencue-releases/main/bibles/translations/TLB.json"),
        ("AMPC", "Amplified Bible, Classic Edition",
            "https://raw.githubusercontent.com/williammgyasii/lumencue-releases/main/bibles/translations/AMPC.json"),
        ("ESV", "English Standard Version",
            "https://raw.githubusercontent.com/williammgyasii/lumencue-releases/main/bibles/translations/ESV.json"),
        ("GNT", "Good News Translation",
            "https://raw.githubusercontent.com/williammgyasii/lumencue-releases/main/bibles/translations/GNT.json"),
    ];

    public static IReadOnlyList<(string Code, string Name)> Offered =>
        HelloaoBooth.Concat(Hosted.Select(h => (h.Code, h.Name))).ToList();

    public static IReadOnlyList<string> OfferedCodes =>
        Offered.Select(o => o.Code).ToList();

    /// <summary>Names for translations a booth may still have on disk after we stopped offering them.</summary>
    public static readonly IReadOnlyDictionary<string, string> CachedNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["NIV"] = "New International Version",
            ["NKJV"] = "New King James Version",
            ["NLT"] = "New Living Translation",
            ["MSG"] = "The Message",
            ["AMP"] = "Amplified Bible",
            ["CSB"] = "Christian Standard Bible",
        };

    public static IReadOnlyList<(string Code, string Name)> WithCached(IEnumerable<string> completeCodes)
    {
        var offered = Offered.ToList();
        var seen = new HashSet<string>(offered.Select(o => o.Code), StringComparer.OrdinalIgnoreCase);
        foreach (var code in completeCodes)
        {
            if (string.IsNullOrWhiteSpace(code) || !seen.Add(code))
                continue;
            var name = CachedNames.TryGetValue(code, out var known) ? known : code;
            offered.Add((code, name));
        }

        return offered;
    }

    /// <summary>
    /// Helloao catalog short names are KJAV / NETB. Booth codes stay KJV / NET.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Aliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["KJV"] = "eng_kjv",
            ["NET"] = "eng_net",
            ["BSB"] = "BSB",
            ["WEB"] = "ENGWEBP",
            ["ASV"] = "eng_asv",
            ["LSV"] = "eng_lsv",
            ["YLT"] = "eng_ylt",
        };

    public static void ApplyAliases(IDictionary<string, string> catalog)
    {
        foreach (var (code, id) in Aliases)
        {
            if (!catalog.ContainsKey(code))
                catalog[code] = id;
        }
    }

    public static bool CanBulkCache(IReadOnlyDictionary<string, string> catalog, string translation) =>
        catalog.ContainsKey(translation);
}
