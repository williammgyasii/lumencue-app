using ChurchProjection.Core.Bible;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

public class BiblePickerTranslationsTests
{
    private static readonly string[] ExpectedOffered =
    [
        "KJV", "BSB", "WEB", "NET", "ASV", "LSV", "YLT",
        "ESV", "GNT", "TPT", "TLB", "AMPC",
    ];

    private static readonly string[] PaidGone = ["NIV", "NKJV", "NLT", "MSG", "AMP", "CSB"];

    [Fact]
    public void Offered_codes_are_helloao_booth_plus_hosted_and_exclude_paid()
    {
        var codes = BiblePickerTranslations.OfferedCodes;

        foreach (var code in ExpectedOffered)
            Assert.Contains(code, codes);

        foreach (var code in PaidGone)
            Assert.DoesNotContain(code, codes);
    }

    [Fact]
    public void Catalog_short_names_do_not_hide_KJV_or_NET()
    {
        var catalog = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["KJAV"] = "eng_kjv",
            ["NETB"] = "eng_net",
            ["BSB"] = "BSB",
        };

        BiblePickerTranslations.ApplyAliases(catalog);

        Assert.Equal("eng_kjv", catalog["KJV"]);
        Assert.Equal("eng_net", catalog["NET"]);
        Assert.True(BiblePickerTranslations.CanBulkCache(catalog, "KJV"));
        Assert.True(BiblePickerTranslations.CanBulkCache(catalog, "NET"));
    }
}
