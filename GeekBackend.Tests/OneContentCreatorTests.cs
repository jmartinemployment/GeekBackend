namespace GeekBackend.Tests;

/// <summary>
/// There is one Content Creator (Jeff, 2026-10-10: "clean this mess up into one").
/// </summary>
/// <remarks>
/// <para>
/// GeekAPI held four generations of content generation, each built beside the last and none removed.
/// <c>ContentCreatorV2</c> was 141 files with no controller left and nothing able to start a job in it,
/// still registered at startup and running a hosted worker. <c>ContentWriterV3</c> was the first
/// generator. The live code reached into both, so neither could be deleted without the other parts
/// noticing, and three separate pieces of code wrote a pillar.
/// </para>
/// <para>
/// Both were deleted on 2026-10-10 and the few files the live code used were moved into
/// <c>Services/ContentCreator</c> under their own names. These tests fail if a second copy starts again:
/// a folder, a namespace, or a type that carries a version in its name. A name is read as a live claim
/// (see <c>CLAUDE.md</c>), so the name is what is checked, in comments as well as code.
/// </para>
/// <para>
/// Not covered, deliberately: <c>content-creator-v2</c> is the live frontend's repository name and
/// appears in CORS origins and in references to its files; <c>HttpContentWriterV3Repository</c> and the
/// <c>GeekApplication.*.ContentWriterV3</c> namespaces are the GeekRepository client and shared models,
/// which this cleanup did not touch.
/// </para>
/// </remarks>
public sealed class OneContentCreatorTests
{
    private static readonly string Root = FindRoot();

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GeekAPI", "GeekAPI.csproj")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException("GeekAPI/GeekAPI.csproj was not found above the test output directory.");
    }

    private static IEnumerable<string> ApiSources() =>
        Directory.EnumerateFiles(Path.Combine(Root, "GeekAPI"), "*.cs", SearchOption.AllDirectories)
            .Where(path =>
            {
                var normalized = path.Replace('\\', '/');
                return !normalized.Contains("/bin/", StringComparison.Ordinal)
                    && !normalized.Contains("/obj/", StringComparison.Ordinal);
            });

    private static string Relative(string path) => Path.GetRelativePath(Root, path).Replace('\\', '/');

    [Theory]
    [InlineData("Services", "ContentCreatorV2")]
    [InlineData("Services", "ContentWriterV3")]
    [InlineData("Controllers", "ContentCreatorV2")]
    [InlineData("Controllers", "ContentWriterV3")]
    public void No_folder_holds_a_second_content_generator(string parent, string name)
    {
        var under = Path.Combine(Root, "GeekAPI", parent);
        var found = Directory.EnumerateDirectories(under)
            .Select(Path.GetFileName)
            .Where(dir => dir is not null && dir.StartsWith(name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // A renamed copy ("ContentCreatorV2-Disabled") still compiles: the project takes every .cs file
        // under it, so a suffix on the folder switches nothing off.
        Assert.True(
            found.Count == 0,
            $"GeekAPI/{parent} has {string.Join(", ", found)}. There is one Content Creator, in Services/ContentCreator.");
    }

    [Theory]
    [InlineData("Services.ContentCreatorV2")]
    [InlineData("Services.ContentWriterV3")]
    [InlineData("Controllers.ContentCreatorV2")]
    [InlineData("Controllers.ContentWriterV3")]
    public void No_source_names_a_deleted_namespace(string fragment)
    {
        var offenders = ApiSources()
            .Where(path => File.ReadAllText(path).Contains(fragment, StringComparison.Ordinal))
            .Select(Relative)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"{fragment} is named in: {string.Join(", ", offenders)}. That namespace was deleted on 2026-10-10.");
    }

    [Fact]
    public void No_source_names_a_type_with_a_version_in_it()
    {
        // "GccV2" anywhere: a declaration, a reference, or a comment that would send a reader looking
        // for a class that is gone.
        var offenders = ApiSources()
            .Where(path => File.ReadAllText(path).Contains("GccV2", StringComparison.Ordinal))
            .Select(Relative)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"GccV2 is named in: {string.Join(", ", offenders)}. The types that survived were renamed without it.");
    }

    [Fact]
    public void The_startup_registers_no_second_generator()
    {
        var program = File.ReadAllText(Path.Combine(Root, "GeekAPI", "Program.cs"));

        Assert.DoesNotContain("AddContentCreatorV2", program, StringComparison.Ordinal);
        Assert.DoesNotContain("gcc-v2-realtime", program, StringComparison.Ordinal);
        Assert.DoesNotContain("IContentGeneratorFactory", program, StringComparison.Ordinal);
        Assert.DoesNotContain("ClaudeContentGenerator", program, StringComparison.Ordinal);
    }

    [Fact]
    public void The_hub_token_hook_and_the_user_id_mapping_are_still_wired()
    {
        // These two lived inside the deleted module while every hub depended on them: without the first
        // no browser can connect to a hub, and without the second a push addressed to a user reaches
        // nobody. Generate progress arrives through both.
        var program = File.ReadAllText(Path.Combine(Root, "GeekAPI", "Program.cs"));

        Assert.Contains("JwtHubQueryToken.AcceptAccessTokenFromQuery(options)", program, StringComparison.Ordinal);
        Assert.Contains("IUserIdProvider, SubUserIdProvider>", program, StringComparison.Ordinal);
        Assert.Contains("AddSignalR()", program, StringComparison.Ordinal);
    }
}
