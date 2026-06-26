using ChurchProjection.Infrastructure.Bible;

// Reusable importer for bundled translation XML files (e.g. the Passion Translation). Converts the
// numbered-book XML into the hosted JSON shape the app downloads and caches.
//
//   dotnet run --project tools/BibleImporter -- <xmlPath> <code> [--name "Display Name"] [--out file.json]
//
// Without --out it runs as a dry run (parse + report stats only, no file written).

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: BibleImporter <xmlPath> <code> [--name \"Display Name\"] [--out file.json]");
    return 1;
}

var xmlPath = args[0];
var code = args[1];
var outPath = ReadOption("--out");
var name = ReadOption("--name") ?? code;
var dryRun = outPath is null;

string? ReadOption(string flag)
{
    var idx = Array.FindIndex(args, a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));
    return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : null;
}

if (!File.Exists(xmlPath))
{
    Console.Error.WriteLine($"File not found: {xmlPath}");
    return 1;
}

Console.WriteLine($"Parsing {xmlPath} as translation '{code}'…");
var import = BibleXmlImportParser.ParseFile(xmlPath);

var books = import.Verses.Select(v => v.Book).Distinct().ToList();
var byBook = import.Verses
    .GroupBy(v => v.Book)
    .ToDictionary(g => g.Key, g => (Chapters: g.Select(v => v.Chapter).Distinct().Count(), Verses: g.Count()));

Console.WriteLine();
Console.WriteLine($"Source name : {import.SourceName}");
Console.WriteLine($"Code        : {code}");
Console.WriteLine($"Books       : {books.Count}");
Console.WriteLine($"Verses      : {import.Verses.Count:N0} (non-empty)");
Console.WriteLine();
Console.WriteLine("First / last book sanity check:");
foreach (var b in new[] { books.First(), books.Last() })
    Console.WriteLine($"  {b,-16} chapters={byBook[b].Chapters,-4} verses={byBook[b].Verses}");

var present = books.ToHashSet(StringComparer.OrdinalIgnoreCase);
var missing = ChurchProjection.Core.Bible.BibleBooks.InCanonicalOrder.Where(b => !present.Contains(b)).ToList();
if (missing.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine($"Books with NO content in this file ({missing.Count}):");
    Console.WriteLine("  " + string.Join(", ", missing));
}

if (dryRun)
{
    Console.WriteLine();
    Console.WriteLine("Dry run only (no --out given) — no file written.");
    return 0;
}

var file = BibleXmlImportParser.ToCustomBibleFile(File.ReadAllText(xmlPath), code, name);
File.WriteAllText(outPath!, file.ToJson());
var sizeMb = new FileInfo(outPath!).Length / 1024.0 / 1024.0;

Console.WriteLine();
Console.WriteLine($"Wrote {file.Verses.Count:N0} verses to {outPath} ({sizeMb:F1} MB).");
return 0;
