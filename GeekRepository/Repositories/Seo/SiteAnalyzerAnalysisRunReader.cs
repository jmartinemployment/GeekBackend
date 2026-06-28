using System.Net.Http.Json;
using System.Text.Json;
using GeekSeo.Application.Models.Seo;
using GeekSeo.Application.Results;

namespace GeekRepository.Repositories.Seo;

/// <summary>
/// Reads keyword analysis rows from SiteAnalyzer Postgres via the SiteAnalyzer Railway Api.
/// </summary>
public sealed class SiteAnalyzerAnalysisRunReader(IHttpClientFactory httpClientFactory)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public async Task<Result<IReadOnlyList<AnalysisRunSummary>>> ListByProjectAsync(
        Guid projectId,
        CancellationToken ct = default)
    {
        var http = httpClientFactory.CreateClient(SiteAnalyzerApiOptions.HttpClientName);
        var response = await http.GetAsync($"analysis-runs?projectId={projectId}", ct);
        if (!response.IsSuccessStatusCode)
            return Result<IReadOnlyList<AnalysisRunSummary>>.Failure(await response.Content.ReadAsStringAsync(ct));

        var rows = await response.Content.ReadFromJsonAsync<List<AnalysisRunSummary>>(Json, ct);
        return Result<IReadOnlyList<AnalysisRunSummary>>.Success(rows ?? []);
    }

    public async Task<Result<ContentWriterSerpExport>> GetContentWriterExportAsync(
        Guid runId,
        CancellationToken ct = default)
    {
        var http = httpClientFactory.CreateClient(SiteAnalyzerApiOptions.HttpClientName);
        var response = await http.GetAsync($"analysis-runs/{runId}/content-writer-export", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return Result<ContentWriterSerpExport>.NotFound("Analysis run not found");

        if (!response.IsSuccessStatusCode)
            return Result<ContentWriterSerpExport>.Failure(await response.Content.ReadAsStringAsync(ct));

        var export = await response.Content.ReadFromJsonAsync<ContentWriterSerpExport>(Json, ct);
        return export is null
            ? Result<ContentWriterSerpExport>.Failure("Empty analysis run export response")
            : Result<ContentWriterSerpExport>.Success(export);
    }
}

public static class SiteAnalyzerApiOptions
{
    public const string HttpClientName = "SiteAnalyzerApi";

    public static string ResolveBaseUrl() =>
        Environment.GetEnvironmentVariable("SITE_ANALYZER2_API_URL")?.Trim().TrimEnd('/')
        ?? "https://geek-siteanalyzer-production.up.railway.app";
}
