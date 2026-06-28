using GeekSeo.Application.Interfaces.Seo;
using GeekSeo.Application.Models.Seo;
using GeekSeo.Application.Results;

namespace GeekRepository.Repositories.Seo;

public sealed class SiteAnalyzerAnalysisRunRepository(SiteAnalyzerAnalysisRunReader reader) : IAnalysisRunRepository
{
    public Task<Result<IReadOnlyList<AnalysisRunSummary>>> ListByProjectAsync(
        Guid projectId, CancellationToken ct = default) =>
        reader.ListByProjectAsync(projectId, ct);

    public Task<Result<ContentWriterSerpExport>> GetContentWriterExportAsync(
        Guid runId, CancellationToken ct = default) =>
        reader.GetContentWriterExportAsync(runId, ct);
}
