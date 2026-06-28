using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GeekSeo.Application.Interfaces;
using GeekSeo.Application.Models.Seo;
using GeekSeo.Application.Results;

namespace GeekRepository.Repositories.Seo;

public sealed class SiteAnalyzerSiteProfileRepository(IHttpClientFactory httpClientFactory) : ISiteAnalyzer2SiteProfileRepository
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public Task<Result<SiteAnalyzer2SiteProfileExport>> GetByIdAsync(Guid siteProfileId, CancellationToken ct = default) =>
        Task.FromResult(Result<SiteAnalyzer2SiteProfileExport>.NotFound("Use content-writer-bundle for Site Analyzer handoff."));

    public async Task<Result<ContentWriterSiteBundle>> GetContentWriterBundleAsync(
        Guid siteProfileId, CancellationToken ct = default)
    {
        var http = httpClientFactory.CreateClient(SiteAnalyzerApiOptions.HttpClientName);
        var response = await http.GetAsync($"site-profiles/{siteProfileId}/content-writer-bundle", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return Result<ContentWriterSiteBundle>.NotFound("Site profile not found");

        if (!response.IsSuccessStatusCode)
            return Result<ContentWriterSiteBundle>.Failure(await response.Content.ReadAsStringAsync(ct));

        var bundle = await response.Content.ReadFromJsonAsync<ContentWriterSiteBundle>(Json, ct);
        return bundle is null
            ? Result<ContentWriterSiteBundle>.Failure("Empty site bundle response")
            : Result<ContentWriterSiteBundle>.Success(bundle);
    }
}
