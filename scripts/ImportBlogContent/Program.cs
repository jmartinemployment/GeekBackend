using System.CommandLine;
using System.Text.Json;
using ImportBlogContent;

var baseUrlOption = new Option<string>("--base", () => "https://www.geekatyourspot.com", "Site base URL");
var outOption = new Option<FileInfo?>("--out", "Write manifest JSON to this file");
var manifestOption = new Option<FileInfo>("--manifest", "Manifest JSON file") { IsRequired = true };
var apiOption = new Option<string>("--api", () => "http://localhost:8080", "GeekAPI base URL");
var apiKeyOption = new Option<string?>("--api-key", "GeekAPI key (or set GEEK_BACKEND_API_KEY)");
var dryRunOption = new Option<bool>("--dry-run", () => false, "Parse only, do not POST");
var replaceOption = new Option<bool>("--replace", () => false, "Update posts that already exist for the same slug+lang");

var discoverCmd = new Command("discover", "Crawl live site and build import manifest");
discoverCmd.AddOption(baseUrlOption);
discoverCmd.AddOption(outOption);
discoverCmd.SetHandler(async (baseUrl, outFile) =>
{
    using var http = CreateHttpClient();
    var entries = await ManifestDiscovery.DiscoverAsync(http, baseUrl);
    Console.WriteLine($"Discovered {entries.Count} article URLs.");

    var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
    if (outFile is not null)
    {
        await File.WriteAllTextAsync(outFile.FullName, json);
        Console.WriteLine($"Wrote {outFile.FullName}");
    }
    else
    {
        Console.WriteLine(json);
    }
}, baseUrlOption, outOption);

var parseCmd = new Command("parse", "Parse one URL and print payload stats (debug)");
var parseUrlOption = new Option<string>("--url", "Page URL") { IsRequired = true };
var parseTypeOption = new Option<string>("--post-type", () => "BlogPosting", "Schema.org post type");
parseCmd.AddOption(parseUrlOption);
parseCmd.AddOption(parseTypeOption);
parseCmd.SetHandler(async (url, postType) =>
{
    using var http = CreateHttpClient();
    var html = await http.GetStringAsync(url);
    var parsed = PageParser.Parse(html, postType, url);
    Console.WriteLine($"title={parsed.Title.Length} chars");
    Console.WriteLine($"body={parsed.BodyHtml.Length} chars");
    Console.WriteLine($"schema={parsed.SchemaMetadataJson.Length} chars");
    Console.WriteLine($"publishedAt={parsed.PublishedAt}");
    Console.WriteLine($"schema={parsed.SchemaMetadataJson}");
}, parseUrlOption, parseTypeOption);

var importCmd = new Command("import", "Import manifest entries into geek_blog via GeekAPI");
importCmd.AddOption(manifestOption);
importCmd.AddOption(apiOption);
importCmd.AddOption(apiKeyOption);
importCmd.AddOption(dryRunOption);
importCmd.AddOption(replaceOption);
importCmd.SetHandler(async (manifest, api, apiKey, dryRun, replace) =>
{
    var key = apiKey ?? Environment.GetEnvironmentVariable("GEEK_BACKEND_API_KEY");
    if (string.IsNullOrWhiteSpace(key))
    {
        Console.Error.WriteLine("API key required: --api-key or GEEK_BACKEND_API_KEY");
        Environment.ExitCode = 1;
        return;
    }

    var json = await File.ReadAllTextAsync(manifest.FullName);
    var entries = JsonSerializer.Deserialize<List<ImportManifestEntry>>(json) ?? [];
    using var http = CreateHttpClient(api);
    var importer = new BlogImporter(http, key);

    var preflight = await importer.PreflightAsync(entries);
    Console.WriteLine($"Preflight: {preflight.New} new, {preflight.Existing} already in geek_blog ({preflight.Total} total).");

    if (preflight.Existing > 0 && !replace && !dryRun)
    {
        Console.WriteLine(
            "Existing slugs will be skipped. Re-run with --replace to update them in place.");
    }

    if (replace && !dryRun)
    {
        Console.WriteLine(
            $"WARNING: --replace will overwrite {preflight.Existing} existing post(s) with freshly scraped HTML.");
    }

    var result = await importer.ImportAsync(entries, dryRun, replace);

    if (dryRun)
    {
        Console.WriteLine($"Dry-run complete: {result.DryRun} entries parsed (no writes).");
    }
    else
    {
        Console.WriteLine(
            $"Done: {result.Created} created, {result.Updated} updated, {result.SkippedExisting} skipped (existing), {result.Errors.Count} errors.");
    }

    foreach (var error in result.Errors)
        Console.Error.WriteLine(error);

    Environment.ExitCode = result.Errors.Count > 0 ? 1 : 0;
}, manifestOption, apiOption, apiKeyOption, dryRunOption, replaceOption);

var root = new RootCommand("Import live geekatyourspot.com content into geek_blog");
root.AddCommand(discoverCmd);
root.AddCommand(parseCmd);
root.AddCommand(importCmd);

return await root.InvokeAsync(args);

static HttpClient CreateHttpClient(string? apiBase = null)
{
    var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    client.DefaultRequestHeaders.UserAgent.ParseAdd("GeekBlogImporter/1.0 (+https://www.geekatyourspot.com)");
    if (apiBase is not null)
        client.BaseAddress = new Uri(apiBase.TrimEnd('/') + "/");
    return client;
}
