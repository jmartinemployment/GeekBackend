using GeekAPI.Services.ContentCreatorV2;
using GeekAPI.Services.ContentCreatorV2.GeekCrawler;
using GeekAPI.Services.ContentCreatorV2.Generation;
using GeekAPI.Services.GeekCrawler;
using GeekAPI.Services.Rag;
using GeekApplication.Models.ContentCreator;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeekBackend.Tests.ContentCreatorV2;

public sealed class GccV2FallbackCorrectnessTests
{
    [Fact]
    public void StubPolicy_production_with_flag_true_fails_startup()
    {
        var previousFlag = Environment.GetEnvironmentVariable(GccV2StubConnectionPolicy.AllowEnvName);
        var previousAsp = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        var previousOverride = Environment.GetEnvironmentVariable(GccV2StubConnectionPolicy.EnvironmentOverrideName);
        try
        {
            GccV2StubConnectionPolicy.ResetForTests();
            Environment.SetEnvironmentVariable(GccV2StubConnectionPolicy.AllowEnvName, "true");
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");
            Environment.SetEnvironmentVariable(GccV2StubConnectionPolicy.EnvironmentOverrideName, null);

            var ex = Assert.Throws<InvalidOperationException>(
                () => GccV2StubConnectionPolicy.ValidateAtStartup(NullLogger.Instance));
            Assert.Contains(GccV2StubConnectionPolicy.AllowEnvName, ex.Message, StringComparison.Ordinal);
            Assert.False(GccV2StubConnectionPolicy.AreStubsAllowed());
        }
        finally
        {
            Environment.SetEnvironmentVariable(GccV2StubConnectionPolicy.AllowEnvName, previousFlag);
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", previousAsp);
            Environment.SetEnvironmentVariable(GccV2StubConnectionPolicy.EnvironmentOverrideName, previousOverride);
            GccV2StubConnectionPolicy.ResetForTests();
        }
    }

    [Fact]
    public void StubPolicy_development_with_flag_true_allows_stubs()
    {
        var previousFlag = Environment.GetEnvironmentVariable(GccV2StubConnectionPolicy.AllowEnvName);
        var previousAsp = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        try
        {
            GccV2StubConnectionPolicy.ResetForTests();
            Environment.SetEnvironmentVariable(GccV2StubConnectionPolicy.AllowEnvName, "true");
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");

            GccV2StubConnectionPolicy.ValidateAtStartup(NullLogger.Instance);
            Assert.True(GccV2StubConnectionPolicy.AreStubsAllowed());
        }
        finally
        {
            Environment.SetEnvironmentVariable(GccV2StubConnectionPolicy.AllowEnvName, previousFlag);
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", previousAsp);
            GccV2StubConnectionPolicy.ResetForTests();
        }
    }

    [Fact]
    public void SeedHtml_stamp_sets_retrievalMode_and_digest()
    {
        var runId = Guid.NewGuid();
        var pageId = Guid.NewGuid();
        var html = "<html><body><p>Seed body long enough for extract provenance.</p></body></html>";
        var page = new GccQuoteablePage(
            "https://example.com/",
            "Example",
            [],
            ["Seed body long enough for extract provenance."]);

        var stamped = GccV2SeedHtmlProvenance.StampSeedHtml(page, runId, pageId, html, DateTimeOffset.UtcNow);

        Assert.Equal(GccQuoteablePage.RetrievalModeSeedHtml, stamped.RetrievalMode);
        Assert.Equal(runId.ToString("D"), stamped.RunId);
        Assert.Equal(pageId.ToString("D"), stamped.PageId);
        Assert.False(string.IsNullOrWhiteSpace(stamped.SourceDigest));
        Assert.Equal(GccV2SeedHtmlProvenance.DigestUtf8(html), stamped.SourceDigest);
    }

    [Fact]
    public void SeedHtml_foreign_run_owner_fails_closed()
    {
        var ex = Assert.Throws<UnauthorizedAccessException>(
            () => GccV2SeedHtmlProvenance.EnsureRunAuthorized("user-a", "user-b", Guid.NewGuid()));
        Assert.Contains("not authorized", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SeedHtml_empty_body_fails_closed()
    {
        var page = new GccQuoteablePage("https://example.com/", "Empty", [], []);
        var ex = Assert.Throws<InvalidOperationException>(
            () => GccV2SeedHtmlProvenance.StampSeedHtml(
                page, Guid.NewGuid(), Guid.NewGuid(), "", DateTimeOffset.UtcNow));
        Assert.Contains("empty body", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(GccV2CapabilitiesNegotiationReason.CapabilitiesV2Only, "capabilities_v2_only")]
    [InlineData(GccV2CapabilitiesNegotiationReason.AgentV3, "agent_v3")]
    public void NegotiationReason_codes_are_stable(
        GccV2CapabilitiesNegotiationReason reason,
        string expected)
    {
        var negotiation = new GccV2CapabilitiesNegotiation(
            RagProducerCapabilities.RequiredExecutionVersion, null, reason);
        Assert.Equal(expected, negotiation.NegotiationReasonCode);
    }

    [Fact]
    public void A1_empty_refresh_token_is_not_stub_success()
    {
        var previousFlag = Environment.GetEnvironmentVariable(GccV2StubConnectionPolicy.AllowEnvName);
        var previousAsp = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        try
        {
            GccV2StubConnectionPolicy.ResetForTests();
            Environment.SetEnvironmentVariable(GccV2StubConnectionPolicy.AllowEnvName, null);
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");

            var ok = GccV2StubConnectionPolicy.TryResolveStubFetch(
                "connected",
                [],
                "GSC",
                out var error,
                out var code);
            Assert.False(ok);
            Assert.Equal("missing_refresh_token", code);
            Assert.Contains("empty token is not a stub", error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable(GccV2StubConnectionPolicy.AllowEnvName, previousFlag);
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", previousAsp);
            GccV2StubConnectionPolicy.ResetForTests();
        }
    }

    [Fact]
    public void A1_stub_status_requires_stubs_allowed()
    {
        var previousFlag = Environment.GetEnvironmentVariable(GccV2StubConnectionPolicy.AllowEnvName);
        var previousAsp = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        try
        {
            GccV2StubConnectionPolicy.ResetForTests();
            Environment.SetEnvironmentVariable(GccV2StubConnectionPolicy.AllowEnvName, null);
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");

            var ok = GccV2StubConnectionPolicy.TryResolveStubFetch(
                "stub",
                [],
                "Drive",
                out var error,
                out var code);
            Assert.False(ok);
            Assert.Equal("stub_disabled", code);
            Assert.Contains("disabled", error, StringComparison.OrdinalIgnoreCase);

            GccV2StubConnectionPolicy.ResetForTests();
            Environment.SetEnvironmentVariable(GccV2StubConnectionPolicy.AllowEnvName, "true");
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
            Assert.True(GccV2StubConnectionPolicy.TryResolveStubFetch(
                "stub", [], "Drive", out var allowedError, out _));
            Assert.Null(allowedError);
        }
        finally
        {
            Environment.SetEnvironmentVariable(GccV2StubConnectionPolicy.AllowEnvName, previousFlag);
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", previousAsp);
            GccV2StubConnectionPolicy.ResetForTests();
        }
    }

    [Fact]
    public void A1_connectors_no_longer_or_empty_token_with_stub()
    {
        var root = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "GeekAPI"));
        string[] paths =
        [
            Path.Combine(root, "Services/ContentCreatorV2/Context/GccV2GscContextConnector.cs"),
            Path.Combine(root, "Services/ContentCreatorV2/Context/GccV2DriveContextConnector.cs"),
            Path.Combine(root, "Services/ContentCreatorV2/Context/GccV2SharePointContextConnector.cs"),
            Path.Combine(root, "Services/ContentCreatorV2/Context/GccV2DriveKnowledgeService.cs"),
            Path.Combine(root, "Services/ContentCreatorV2/Context/GccV2SharePointKnowledgeService.cs"),
            Path.Combine(root, "Controllers/ContentCreatorV2/GccV2GscController.cs"),
            Path.Combine(root, "Controllers/ContentCreatorV2/GccV2TaskAgentsController.cs"),
        ];
        foreach (var path in paths)
        {
            Assert.True(File.Exists(path), path);
            var source = File.ReadAllText(path);
            Assert.DoesNotContain(
                "Status == \"stub\" || connection.EncryptedRefreshToken.Length == 0",
                source,
                StringComparison.Ordinal);
            Assert.Contains("TryResolveStubFetch", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A2_failed_query_result_is_not_empty_success()
    {
        var failed = new GeekCrawlerRagQueryResult
        {
            RunId = Guid.NewGuid(),
            Pages = [],
            Failed = true,
            Error = "RAG query failed (503).",
            Warning = "RAG query failed (503).",
        };
        Assert.True(failed.Failed);
        Assert.Empty(failed.Pages);
        Assert.DoesNotContain("Continuing without it", failed.Warning, StringComparison.Ordinal);

        var root = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..",
            "GeekAPI", "Services", "GeekCrawler", "HttpGeekCrawlerRagClient.cs"));
        var source = File.ReadAllText(root);
        Assert.DoesNotContain("Continuing without it", source, StringComparison.Ordinal);
        Assert.Contains("Failed = true", source, StringComparison.Ordinal);
    }

    [Fact]
    public void A3_soft_disable_is_not_citeable_shaped_in_source()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..",
            "GeekAPI", "Services", "Rag", "RagGenerateService.cs"));
        var source = File.ReadAllText(path);
        Assert.Contains("PromptVersion = \"rag-generate/unavailable\"", source, StringComparison.Ordinal);
        Assert.Contains("Canonical PLAN/WRITE cannot continue without citeable RAG", source, StringComparison.Ordinal);
        var softIdx = source.IndexOf("SoftDisabled = true", StringComparison.Ordinal);
        var requireIdx = source.LastIndexOf("if (request.RequireCiteable)", softIdx);
        Assert.True(requireIdx > 0 && requireIdx < softIdx,
            "RequireCiteable must throw before SoftDisabled success-shaped return.");
    }

    [Fact]
    public void Create_drafting_uses_create_library_not_rag_generate_v3()
    {
        string Rel(params string[] parts) => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", Path.Combine(parts)));

        foreach (var relative in new[]
                 {
                     new[] { "GeekAPI", "Services", "ContentCreatorV2", "Plan", "GccV2PlanService.cs" },
                     new[] { "GeekAPI", "Services", "ContentCreatorV2", "Write", "GccV2WriteService.cs" },
                     new[] { "GeekAPI", "Services", "ContentCreatorV2", "Validate", "GccV2ValidateService.cs" },
                 })
        {
            var source = File.ReadAllText(Rel(relative));
            Assert.Contains("CreateLibraryDraft = true", source, StringComparison.Ordinal);
            Assert.Contains(
                "RagProducerCapabilities.CreateLibraryExecutionVersion",
                source,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "RagProducerCapabilities.AgentExecutionVersion",
                source,
                StringComparison.Ordinal);
        }

        var generate = File.ReadAllText(Rel("GeekAPI", "Services", "Rag", "RagGenerateService.cs"));
        Assert.Contains("if (request.CreateLibraryDraft)", generate, StringComparison.Ordinal);
        Assert.Contains("DraftFromCreateLibraryAsync", generate, StringComparison.Ordinal);
        Assert.Contains("gcc-create-library.v1", generate, StringComparison.Ordinal);
        Assert.Contains("SoftDisabled = false", generate, StringComparison.Ordinal);
    }

}
