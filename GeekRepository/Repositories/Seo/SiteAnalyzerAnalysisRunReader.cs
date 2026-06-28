using Dapper;
using GeekSa2Read;
using GeekSeo.Application.Models.Seo;
using GeekSeo.Application.Results;
using Npgsql;

namespace GeekRepository.Repositories.Seo;

/// <summary>
/// Reads keyword analysis export from <c>sa2</c> via <c>SITE_ANALYZER2_DATABASE_URL</c>.
/// </summary>
public sealed class SiteAnalyzerAnalysisRunReader(Sa2ContentWriterExportReader exportReader)
{
    public async Task<Result<IReadOnlyList<AnalysisRunSummary>>> ListByProjectAsync(
        Guid projectId,
        CancellationToken ct = default)
    {
        var connectionString = SiteAnalyzer2Connection.TryResolve();
        if (string.IsNullOrWhiteSpace(connectionString))
            return Result<IReadOnlyList<AnalysisRunSummary>>.Failure("SITE_ANALYZER2_DATABASE_URL is not configured.");

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);

        var rows = (await conn.QueryAsync<RunSummaryRow>(
            new CommandDefinition(
                """
                SELECT "Id", "ProjectId", "Keyword", "TargetSiteUrl", "Status", "CreatedAt"
                FROM sa2.analysis_runs
                WHERE "ProjectId" = @ProjectId
                ORDER BY "CreatedAt" DESC
                """,
                new { ProjectId = projectId },
                cancellationToken: ct))).ToList();

        IReadOnlyList<AnalysisRunSummary> summaries = rows.Select(r => new AnalysisRunSummary
        {
            Id = r.Id,
            ProjectId = r.ProjectId,
            Keyword = r.Keyword,
            TargetSiteUrl = r.TargetSiteUrl,
            Status = r.Status,
            CreatedAt = new DateTimeOffset(DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc)),
            SerpSeResultsCount = 0,
            OrganicResultCount = 0,
            ContentWritingReady = false,
        }).ToList();

        return Result<IReadOnlyList<AnalysisRunSummary>>.Success(summaries);
    }

    public async Task<Result<ContentWriterSerpExport>> GetContentWriterExportAsync(
        Guid runId,
        CancellationToken ct = default)
    {
        var export = await exportReader.GetExportAsync(runId, ct);
        return export is null
            ? Result<ContentWriterSerpExport>.NotFound("Analysis run not found")
            : Result<ContentWriterSerpExport>.Success(export);
    }

    private sealed class RunSummaryRow
    {
        public Guid Id { get; init; }
        public Guid ProjectId { get; init; }
        public string Keyword { get; init; } = string.Empty;
        public string TargetSiteUrl { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public DateTime CreatedAt { get; init; }
    }
}
