using Microsoft.Extensions.DependencyInjection;
using GeekApplication.Interfaces;
using GeekApplication.Interfaces.ContentCreator;
using GeekApplication.Interfaces.ContentWriterV3;
using GeekApplication.Interfaces.ContentWriterV4;
using GeekRepository.Infrastructure;
using GeekRepository.Repositories;
using GeekRepository.Repositories.Blog;
using GeekRepository.Repositories.Content;
using GeekRepository.Repositories.ContentCreator;
using GeekRepository.Repositories.ContentWriterV3;
using GeekRepository.Repositories.ContentWriterV4;

namespace GeekRepository;

public static class ServiceRegistration
{
    public static IServiceCollection AddGeekRepository(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddSingleton<IDbConnectionFactory>(
            _ => new NpgsqlConnectionFactory(connectionString));

        services.AddScoped<AmbientDbContext>();
        services.AddScoped<IAmbientDbContext>(sp => sp.GetRequiredService<AmbientDbContext>());
        services.AddScoped<IUnitOfWork, SqlUnitOfWork>();

        services.AddScoped<ICaseStudyRepository, CaseStudyRepository>();
        services.AddScoped<IDepartmentRepository, DepartmentRepository>();
        services.AddScoped<IUseCaseRepository, UseCaseRepository>();
        services.AddScoped<IBlogRepository, BlogRepository>();
        services.AddScoped<IWebPostRepository, WebPostRepository>();

        // Content Writer V3 Repositories
        services.AddScoped<IWorkspaceRepository, WorkspaceRepository>();
        services.AddScoped<IClientRepository, ClientRepository>();
        services.AddScoped<IContentCampaignRepository, ContentCampaignRepository>();
        services.AddScoped<IContentAssetRepository, ContentAssetRepository>();
        services.AddScoped<IContentAssetVersionRepository, ContentAssetVersionRepository>();
        services.AddScoped<IStrategyBriefRepository, StrategyBriefRepository>();
        services.AddScoped<IPublicationRepository, PublicationRepository>();
        services.AddScoped<IPainPointRepository, PainPointRepository>();
        services.AddScoped<IPainPointEvidenceLinkRepository, PainPointEvidenceLinkRepository>();
        services.AddScoped<IResearchRunRepository, ResearchRunRepository>();
        services.AddScoped<IResearchSourceRepository, ResearchSourceRepository>();
        services.AddScoped<IResearchEvidenceRepository, ResearchEvidenceRepository>();
        services.AddScoped<IReconciliationProposalRepository, ReconciliationProposalRepository>();
        services.AddScoped<IKeywordCandidateRepository, KeywordCandidateRepository>();
        services.AddScoped<IJobRepository, JobRepository>();
        services.AddScoped<IReviewCommentRepository, ReviewCommentRepository>();
        services.AddScoped<IApprovalEventRepository, ApprovalEventRepository>();
        services.AddScoped<IPublicationEventRepository, PublicationEventRepository>();
        services.AddScoped<IClientProfileRepository, ClientProfileRepository>();
        services.AddScoped<IClientProfileVersionRepository, ClientProfileVersionRepository>();
        services.AddScoped<IClientBrandVoiceLinkRepository, ClientBrandVoiceLinkRepository>();

        // Content Writer V4
        services.AddScoped<IBrandVoiceRepository, BrandVoiceRepository>();
        services.AddScoped<ISocialScheduleRepository, SocialScheduleRepository>();

        // Geek Content Creator
        services.AddScoped<IGccCreateRepository, GccCreateRepository>();
        services.AddScoped<IGccArtifactRepository, GccArtifactRepository>();
        services.AddScoped<IGccArtifactVersionRepository, GccArtifactVersionRepository>();
        services.AddScoped<IGccApprovalEventRepository, GccApprovalEventRepository>();
        services.AddScoped<IGccSiteAnalysisRepository, GccSiteAnalysisRepository>();

        services.AddGeekSeoData();

        return services;
    }
}
