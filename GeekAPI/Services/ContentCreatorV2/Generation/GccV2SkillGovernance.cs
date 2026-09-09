using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GeekAPI.Auth;
using GeekAPI.HttpClients;

namespace GeekAPI.Services.ContentCreatorV2.Generation;

public sealed class GccV2SkillAdminPolicy
{
    private readonly HashSet<Guid> _adminIds;

    public GccV2SkillAdminPolicy(IConfiguration configuration)
    {
        var configured = configuration["GccV2Skills:AdminUserIds"]
            ?? Environment.GetEnvironmentVariable("GEEK_CONTENT_CREATOR_ADMIN_USER_IDS")
            ?? "";
        _adminIds = configured.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => Guid.TryParse(value, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty).ToHashSet();
    }

    public bool IsAuthorized(ICurrentUserContext user) => user.IsAuthenticated && _adminIds.Contains(user.UserId);
    public string ConfigurationHint => "Configure GccV2Skills:AdminUserIds or GEEK_CONTENT_CREATOR_ADMIN_USER_IDS.";
}

public sealed record GccV2GitHubSkillImportRequest(
    string RepositoryUrl,
    string ImmutableRef,
    string SkillPath,
    string SemanticVersion = "1.0.0",
    IReadOnlyList<string>? Stages = null,
    IReadOnlyList<string>? ContentTypes = null,
    IReadOnlyList<string>? RequiredTools = null,
    int Order = 100,
    string ActivationMode = "automatic");

public sealed class GccV2GitHubSkillImporter(IHttpClientFactory clients)
{
    public const int MaxFiles = 128;
    public const int MaxFileBytes = 512 * 1024;
    public const int MaxPackageBytes = 4 * 1024 * 1024;
    private static readonly string[] AllowedRoots = ["SKILL.md", "references/", "assets/", "scripts/"];
    private static readonly string[] PermanentTerms =
    [
        "llamaparse", "llama parse", "llamacloud", "llama cloud", "llama_cloud_api_key",
        "llama_parse", "@llamaindex/cloud", "cloud.llamaindex.ai/api/parsing",
        "api.cloud.llamaindex.ai",
    ];
    private static readonly (string Rule, string[] Terms)[] ScanRules =
    [
        ("credential-access", ["~/.ssh", ".aws/credentials", "keychain", "credential"]),
        ("environment-harvesting", ["process.env", "os.environ", "getenv(", "env |"]),
        ("network-exfiltration", ["curl ", "wget ", "http.post", "requests.post", "webhook"]),
        ("process-execution", ["subprocess", "child_process", "process.start", "system(", "exec("]),
        ("dynamic-code", ["eval(", "compile(", "importlib", "reflection.emit"]),
        ("dependency-install", ["npm install", "pnpm add", "pip install", "brew install", "apt-get"]),
        ("external-tooling-request", ["mcp", "plugin", "plugins/"]),
        ("instruction-override", ["ignore previous", "ignore all prior", "system prompt", "developer message"]),
    ];

    public async Task<ImportGccV2SkillCommand> ImportAsync(
        GccV2GitHubSkillImportRequest request, string actor, string? sourceIp, string? requestId,
        CancellationToken ct)
    {
        var (owner, repo) = ParseRepository(request.RepositoryUrl);
        if (!IsImmutableCommit(request.ImmutableRef))
            throw new GccV2SkillImportException("immutableRef must be a full 40-character Git commit SHA.");
        var immutableRef = request.ImmutableRef.ToLowerInvariant();
        var skillPath = NormalizePath(request.SkillPath)
            ?? throw new GccV2SkillImportException("skillPath is invalid.");
        ValidateSemanticVersion(request.SemanticVersion);

        var http = clients.CreateClient(nameof(GccV2GitHubSkillImporter));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await http.GetAsync(
            $"https://codeload.github.com/{owner}/{repo}/zip/{immutableRef}",
            HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxPackageBytes * 2L)
            throw new GccV2SkillImportException("GitHub archive exceeds the compressed size limit.");
        await using var archiveStream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var compressed = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await archiveStream.ReadAsync(buffer, timeout.Token);
            if (read == 0) break;
            if (compressed.Length + read > MaxPackageBytes * 2L)
                throw new GccV2SkillImportException("GitHub archive exceeds the compressed size limit.");
            await compressed.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
        }
        compressed.Position = 0;
        using var archive = new ZipArchive(compressed, ZipArchiveMode.Read, leaveOpen: false);

        var files = new List<ImportGccV2SkillFile>();
        var findings = new List<ImportGccV2SkillFinding>();
        long totalBytes = 0;
        var archivePrefix = $"{repo}-{immutableRef}/";
        var selectedPrefix = archivePrefix + skillPath.TrimEnd('/') + "/";

        foreach (var entry in archive.Entries.OrderBy(x => x.FullName, StringComparer.Ordinal))
        {
            if (entry.FullName.EndsWith('/')) continue;
            if (!entry.FullName.StartsWith(selectedPrefix, StringComparison.Ordinal)) continue;
            var relative = entry.FullName[selectedPrefix.Length..];
            var normalized = NormalizePath(relative);
            if (normalized is null || !string.Equals(relative.Replace('\\', '/'), normalized, StringComparison.Ordinal))
                throw new GccV2SkillImportException($"Archive contains an unsafe path: {relative}");
            if (!AllowedRoots.Any(root => normalized == root || normalized.StartsWith(root, StringComparison.Ordinal)))
                throw new GccV2SkillImportException($"Unsupported skill path: {normalized}");
            if (IsSymlink(entry)) throw new GccV2SkillImportException($"Symlinks are prohibited: {normalized}");
            if (LooksLikeArchive(normalized)) throw new GccV2SkillImportException($"Nested archives are prohibited: {normalized}");
            if (entry.Length < 0 || entry.Length > MaxFileBytes)
                throw new GccV2SkillImportException($"File exceeds {MaxFileBytes} bytes: {normalized}");
            if (entry.Length > 64 * Math.Max(1, entry.CompressedLength) && entry.Length > 16 * 1024)
                throw new GccV2SkillImportException($"Suspicious archive compression ratio: {normalized}");
            totalBytes += entry.Length;
            if (totalBytes > MaxPackageBytes) throw new GccV2SkillImportException("Expanded package exceeds the size limit.");
            if (files.Count >= MaxFiles) throw new GccV2SkillImportException($"Package exceeds {MaxFiles} files.");

            await using var stream = entry.Open();
            using var memory = new MemoryStream((int)entry.Length);
            await stream.CopyToAsync(memory, timeout.Token);
            var bytes = memory.ToArray();
            if (LooksExecutable(bytes))
                throw new GccV2SkillImportException($"Executable content is prohibited: {normalized}");
            var textRequired = !normalized.StartsWith("assets/", StringComparison.Ordinal)
                || IsTextAsset(normalized);
            var text = textRequired ? StrictUtf8(bytes, normalized) : "base64:" + Convert.ToBase64String(bytes);
            if (textRequired && ContainsControlBytes(bytes))
                throw new GccV2SkillImportException($"Binary content is prohibited in text file: {normalized}");
            var digest = Hash(bytes);
            files.Add(new ImportGccV2SkillFile(normalized, MediaType(normalized), bytes.LongLength, digest, text));
            if (textRequired) Scan(normalized, text, findings);
        }

        if (files.Count == 0) throw new GccV2SkillImportException("No files found at skillPath.");
        var skill = files.SingleOrDefault(x => x.RelativePath == "SKILL.md")
            ?? throw new GccV2SkillImportException("SKILL.md is required at the skill root.");
        var manifest = ParseManifest(skill.Content);
        var canonicalName = CanonicalName(manifest.Name);
        var packageDigest = Hash(Encoding.UTF8.GetBytes(string.Join("\n",
            files.Select(x => $"{x.RelativePath}|{x.ByteCount}|{x.Sha256}"))));
        var manifestDigest = Hash(Encoding.UTF8.GetBytes(string.Join("\n",
            files.Select(x => $"{x.RelativePath}|{x.MediaType}|{x.ByteCount}|{x.Sha256}"))));
        var stages = NormalizeScopes(request.Stages, GccV2SkillCatalog.Definitions.SelectMany(x => x.SupportedStages));
        var contentTypes = NormalizeScopes(request.ContentTypes, GccV2ContentTypeRagMapper.CanonicalContentTypes);
        var applicability = (from stage in stages from contentType in contentTypes
            select new ImportGccV2SkillApplicability(stage, contentType, request.Order, "[]",
                JsonSerializer.Serialize(request.RequiredTools ?? []), request.ActivationMode)).ToList();

        return new ImportGccV2SkillCommand(
            canonicalName, DisplayName(canonicalName), manifest.Description,
            $"https://github.com/{owner}/{repo}", skillPath, owner, request.SemanticVersion,
            immutableRef, packageDigest, manifestDigest,
            manifest.License ?? "unspecified", manifest.Compatibility ?? "", false,
            findings.Any(x => x.PermanentRejection), files, applicability, findings,
            actor, sourceIp, requestId);
    }

    private static (string Owner, string Repo) ParseRepository(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            throw new GccV2SkillImportException("Only https://github.com/{owner}/{repository} sources are allowed.");
        var parts = uri.AbsolutePath.Trim('/').Split('/');
        if (parts.Length != 2) throw new GccV2SkillImportException("Repository URL must identify one GitHub repository.");
        var repo = parts[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? parts[1][..^4] : parts[1];
        if (parts[0].Length == 0 || repo.Length == 0 || parts.Any(x => x is "." or ".."))
            throw new GccV2SkillImportException("Invalid GitHub repository URL.");
        return (parts[0], repo);
    }

    private static void Scan(string path, string content, List<ImportGccV2SkillFinding> findings)
    {
        var lower = content.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        foreach (var term in PermanentTerms.Where(lower.Contains))
            findings.Add(new("critical", "gcc-static-v1", "prohibited-hosted-parser", path,
                LineOf(lower, term), $"Permanently prohibited dependency or capability: {term}", true));
        foreach (var (rule, terms) in ScanRules)
            foreach (var term in terms.Where(lower.Contains))
                findings.Add(new("high", "gcc-static-v1", rule, path, LineOf(lower, term),
                    $"Review required for detected pattern: {term}", false));
        if (path.Split('/').Any(segment => segment.StartsWith('.')))
            findings.Add(new("high", "gcc-static-v1", "hidden-file", path, null, "Hidden files are prohibited.", false));
        if (!string.Equals(content, content.Normalize(NormalizationForm.FormC), StringComparison.Ordinal))
            findings.Add(new("medium", "gcc-static-v1", "unicode-normalization", path, null,
                "Content is not canonical Unicode NFC.", false));
    }

    private static SkillManifest ParseManifest(string content)
    {
        var normalized = content.Replace("\r\n", "\n");
        if (!normalized.StartsWith("---\n", StringComparison.Ordinal))
            throw new GccV2SkillImportException("SKILL.md requires YAML frontmatter.");
        var end = normalized.IndexOf("\n---\n", 4, StringComparison.Ordinal);
        if (end < 0) throw new GccV2SkillImportException("SKILL.md frontmatter is not terminated.");
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in normalized[4..end].Split('\n'))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            values[line[..colon].Trim()] = line[(colon + 1)..].Trim().Trim('"', '\'');
        }
        if (!values.TryGetValue("name", out var name) || string.IsNullOrWhiteSpace(name))
            throw new GccV2SkillImportException("SKILL.md frontmatter requires name.");
        if (!values.TryGetValue("description", out var description) || string.IsNullOrWhiteSpace(description))
            throw new GccV2SkillImportException("SKILL.md frontmatter requires description.");
        if (string.IsNullOrWhiteSpace(normalized[(end + 5)..]))
            throw new GccV2SkillImportException("SKILL.md requires an instruction body.");
        return new(name, description, values.GetValueOrDefault("license"), values.GetValueOrDefault("compatibility"));
    }

    private static IReadOnlyList<string> NormalizeScopes(IReadOnlyList<string>? values, IEnumerable<string> defaults)
    {
        var result = (values is { Count: > 0 } ? values : defaults).Select(x => x.Trim())
            .Where(x => x.Length > 0).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        if (result.Count == 0) throw new GccV2SkillImportException("At least one applicability scope is required.");
        return result;
    }

    private static bool IsImmutableCommit(string value) =>
        value.Length == 40 && value.All(Uri.IsHexDigit);
    private static void ValidateSemanticVersion(string value)
    {
        var parts = value.Split('.');
        if (parts.Length != 3 || parts.Any(x => !int.TryParse(x, out _)))
            throw new GccV2SkillImportException("semanticVersion must be MAJOR.MINOR.PATCH.");
    }
    private static string CanonicalName(string value)
    {
        var name = value.Trim().ToLowerInvariant();
        if (name.Length is < 1 or > 128 || name.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '-'))
            || name.StartsWith('-') || name.EndsWith('-') || name.Contains("--", StringComparison.Ordinal))
            throw new GccV2SkillImportException("Skill name must be canonical lowercase kebab-case.");
        return name;
    }
    private static string DisplayName(string value) => string.Join(' ', value.Split('-')
        .Select(x => char.ToUpperInvariant(x[0]) + x[1..]));
    private static string? NormalizePath(string value)
    {
        var path = Uri.UnescapeDataString(value).Replace('\\', '/').Trim('/');
        return string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path)
            || path.Split('/').Any(x => x is "." or ".." || string.IsNullOrWhiteSpace(x)) ? null : path;
    }
    private static bool IsSymlink(ZipArchiveEntry entry) => ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000;
    private static bool LooksLikeArchive(string path) =>
        new[] { ".zip", ".tar", ".tgz", ".gz", ".7z", ".rar", ".bz2", ".xz" }
            .Any(x => path.EndsWith(x, StringComparison.OrdinalIgnoreCase));
    private static bool ContainsControlBytes(byte[] bytes) => bytes.Any(b => b == 0);
    private static bool LooksExecutable(byte[] bytes) =>
        bytes.AsSpan().StartsWith("MZ"u8)
        || bytes.AsSpan().StartsWith(new byte[] { 0x7f, (byte)'E', (byte)'L', (byte)'F' })
        || bytes.AsSpan().StartsWith(new byte[] { 0xcf, 0xfa, 0xed, 0xfe })
        || bytes.AsSpan().StartsWith(new byte[] { 0xfe, 0xed, 0xfa, 0xcf });
    private static bool IsTextAsset(string path) =>
        new[] { ".md", ".txt", ".json", ".yaml", ".yml", ".css", ".html", ".svg" }
            .Any(x => path.EndsWith(x, StringComparison.OrdinalIgnoreCase));
    private static string StrictUtf8(byte[] bytes, string path)
    {
        try { return new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { throw new GccV2SkillImportException($"Invalid UTF-8 text: {path}"); }
    }
    private static string MediaType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".md" => "text/markdown", ".json" => "application/json", ".yaml" or ".yml" => "application/yaml",
        ".txt" => "text/plain", ".css" => "text/css", ".html" => "text/html", ".svg" => "image/svg+xml",
        ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".gif" => "image/gif",
        ".webp" => "image/webp",
        _ => "text/plain",
    };
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static int LineOf(string text, string term) => text[..text.IndexOf(term, StringComparison.Ordinal)].Count(c => c == '\n') + 1;
    private sealed record SkillManifest(string Name, string Description, string? License, string? Compatibility);
}

public sealed class GccV2SkillImportException(string message) : Exception(message);
