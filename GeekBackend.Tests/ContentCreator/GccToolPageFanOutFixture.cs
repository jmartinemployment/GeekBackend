using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.Workflow.Services;
using GeekApplication.Interfaces.ContentWriterV3;
using GeekAPI.Services.Workflow.Services.PromptBuilders;
using GeekAPI.Services.Workflow.Services.SchemaBuilders;
using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.Workflow.Providers;
using GeekApplication.Models.ContentCreator;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// A <see cref="GccGenerateService"/> with a project that declares partner URLs, for the per-partner
/// tool-page fan-out.
/// </summary>
/// <remarks>
/// The fan-out reads partner URLs off the project, so the existing tool-page fixtures — which hand the
/// service a null project — cannot exercise it. This supplies one and nothing else; the model provider
/// is scripted to fail, because these tests are about which partners are attempted and how a refusal is
/// reported, not about what gets written.
/// </remarks>
internal sealed record GccToolPageFanOutFixture(GccGenerateService Service, GccCreateDto Create)
{
    public static GccToolPageFanOutFixture WithPartners(params string[] partnerUrls)
    {
        var projectId = Guid.NewGuid();
        var project = new GccProjectDto(
            Id: projectId,
            ClientId: Guid.NewGuid(),
            Name: "Acme",
            Code: null,
            Description: null,
            Status: "active",
            SiteUrl: "https://acme.test",
            ProjectSiteRunId: null,
            Department: "accounting",
            PartnerUrls: partnerUrls,
            CompetitorUrls: [],
            StartDate: DateOnly.FromDateTime(DateTime.UtcNow),
            DueDate: null,
            FinishedDate: null,
            EstimatedHours: null,
            Budget: null,
            BudgetCurrency: null,
            CreatedAtUtc: DateTime.UtcNow,
            UpdatedAtUtc: DateTime.UtcNow);

        var projects = new GccCompetitorAnalysisResolverTests.FakeProjects(project);
        var provider = new RefusingProvider();

        var service = new GccGenerateService(
            new ContentPromptBuilder(),
            TestContentTypePrompts.Registry(),
            new FakeProviderFactory(provider),
            new SoftwareApplicationSchemaBuilder(),
            new BlogPostingSchemaBuilder(),
            new ArticleSchemaBuilder(new SoftwareApplicationSchemaBuilder()),
            Options.Create(new CompanyProfileOptions()),
            NullLogger<GccGenerateService>.Instance,
            GccCompetitorAnalysisResolverTests.Build(
                projects,
                new GccCompetitorAnalysisResolverTests.FakePages(),
                new GccCompetitorAnalysisResolverTests.FakeRag()),
            GccPartnerExtractionFakes.Scripted(
                new FakeProviderFactory(provider), GccPartnerExtractionFakes.EmptyPageExtraction),
            projects,
            new GccPublisherProfileResolver(
                projects,
                new GccCompetitorAnalysisResolverTests.FakePages(),
                NullLogger<GccPublisherProfileResolver>.Instance),
            new GccKnownToolsResolver(
                new GccCompetitorAnalysisResolverTests.FakePages(),
                NullLogger<GccKnownToolsResolver>.Instance));

        var create = new GccCreateDto(
            Id: Guid.NewGuid(),
            ClientId: Guid.NewGuid(),
            OwnerUserId: Guid.NewGuid(),
            StartingContentType: "tool",
            Topic: "Accounts Payable: Automated Data Entry & Processing",
            Notes: "notes",
            ProjectSiteRunId: null,
            SiteSectionJson: null,
            BriefJson: null,
            ResearchJson: null,
            Status: "draft",
            CreatedAtUtc: DateTime.UtcNow,
            UpdatedAtUtc: DateTime.UtcNow,
            Department: "accounting",
            ProjectId: projectId);

        return new GccToolPageFanOutFixture(service, create);
    }

    /// <summary>Fails every call, so every partner's page refuses for the same stated reason.</summary>
    private sealed class RefusingProvider : IContentGenerationProvider
    {
        public LlmProviderType ProviderType => LlmProviderType.OpenAi;

        public Task<ChatCompletionResult> CompleteAsync(
            ChatCompletionRequest request, CancellationToken cancellationToken = default) =>
            throw new ContentGenerationException("scripted provider failure");
    }

    private sealed class FakeProviderFactory(IContentGenerationProvider provider) : IContentProviderFactory
    {
        public IContentGenerationProvider Get(LlmProviderType providerType) => provider;
        public IContentGenerationProvider GetDefault() => provider;
    }
}
