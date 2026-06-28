using GeekSa2Read;
using GeekSeo.Application.Interfaces;
using GeekSeo.Application.Models.Seo;
using GeekSeo.Application.Results;

namespace GeekRepository.Repositories.Seo;

public sealed class SiteAnalyzerSiteProfileRepository(Sa2ContentWriterBundleReader bundleReader)
    : ISiteAnalyzer2SiteProfileRepository
{
    public Task<Result<SiteAnalyzer2SiteProfileExport>> GetByIdAsync(Guid siteProfileId, CancellationToken ct = default) =>
        Task.FromResult(Result<SiteAnalyzer2SiteProfileExport>.NotFound("Use content-writer-bundle for Site Analyzer handoff."));

    public async Task<Result<ContentWriterSiteBundle>> GetContentWriterBundleAsync(
        Guid siteProfileId, CancellationToken ct = default)
    {
        var bundle = await bundleReader.GetByProfileIdAsync(siteProfileId, ct);
        return bundle is null
            ? Result<ContentWriterSiteBundle>.NotFound("Site profile not found")
            : Result<ContentWriterSiteBundle>.Success(bundle);
    }
}
