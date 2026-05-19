using System.Text.Json;
using System.Text.RegularExpressions;
using GeekApplication.Infrastructure;
using GeekApplication.Interfaces.Seo;
using GeekApplication.Models.Seo;
using GeekApplication.Results;

namespace GeekApplication.Services.Seo;

public sealed partial class AIWritingService(
    IBackgroundJobRepository jobs,
    IProjectRepository projects,
    IContentDocumentService documents,
    IContentDocumentRepository documentRepo,
    IAIProvider ai) : IAIWritingService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task<Result<BackgroundJobStatus>> EnqueueFullArticleAsync(
        Guid userId, FullArticleRequest request, CancellationToken ct = default)
    {
        var project = await projects.GetByIdAsync(request.ProjectId, ct);
        if (!project.IsSuccess || project.Value is null || project.Value.UserId != userId)
            return Result<BackgroundJobStatus>.Failure("Access denied");

        var payload = JsonSerializer.Serialize(new FullArticleJobPayload
        {
            ProjectId = request.ProjectId,
            Keyword = request.Keyword,
            Location = request.Location,
            Title = string.IsNullOrWhiteSpace(request.Title) ? request.Keyword : request.Title,
        }, JsonOptions);

        var created = await jobs.CreateAsync(new CreateBackgroundJobRequest
        {
            UserId = userId,
            ProjectId = request.ProjectId,
            JobType = "full_article",
            PayloadJson = payload,
        }, ct);

        if (!created.IsSuccess || created.Value is null)
            return Result<BackgroundJobStatus>.Failure(created.Error ?? "Failed to create job");

        var job = created.Value;
        return Result<BackgroundJobStatus>.Success(new BackgroundJobStatus
        {
            JobId = job.Id,
            JobType = job.JobType,
            Status = job.Status,
            ProgressPercent = job.ProgressPercent,
        });
    }

    public async Task<Result<WritingTextResult>> GenerateOutlineAsync(
        Guid userId, WritingOutlineRequest request, CancellationToken ct = default)
    {
        _ = userId;
        var response = await ai.CompleteAsync(new AIRequest
        {
            SystemPrompt =
                "You are an SEO content strategist. Output a detailed article outline as HTML using h2 and h3 only. No preamble.",
            UserPrompt =
                $"Keyword: {request.Keyword}\nTarget words: {request.Brief.TargetWordCount}\nTerms to cover: {string.Join(", ", request.Brief.RecommendedTerms)}\nSuggested sections: {string.Join("; ", request.Brief.SuggestedHeadings)}",
            MaxTokens = 2048,
            Temperature = 0.5,
        }, ct);

        return ToWritingResult(response);
    }

    public async Task<Result<WritingTextResult>> GenerateDraftAsync(
        Guid userId, WritingDraftRequest request, CancellationToken ct = default)
    {
        _ = userId;
        var target = request.TargetWordCount > 0 ? request.TargetWordCount : request.Brief.TargetWordCount;
        var response = await ai.CompleteAsync(new AIRequest
        {
            SystemPrompt =
                "You write SEO articles in HTML (h1 once, multiple h2/h3, paragraphs). Natural tone. No markdown fences.",
            UserPrompt =
                $"Keyword: {request.Keyword}\nOutline:\n{request.Outline}\n\nWrite ~{target} words. Include terms: {string.Join(", ", request.Brief.RecommendedTerms.Take(10))}",
            MaxTokens = 8192,
            Temperature = 0.7,
        }, ct);

        return ToWritingResult(response);
    }

    public async Task<Result<WritingTextResult>> HumanizeAsync(
        Guid userId, HumanizeRequest request, CancellationToken ct = default)
    {
        var access = await documents.EnsureAccessAsync(userId, request.DocumentId, ct);
        if (!access.IsSuccess)
            return Result<WritingTextResult>.Failure(access.Error ?? "Access denied");

        var response = await ai.CompleteAsync(new AIRequest
        {
            SystemPrompt =
                "Rewrite the HTML to sound more human and conversational while keeping headings and structure. Return HTML only.",
            UserPrompt = request.ContentHtml,
            MaxTokens = 8192,
            Temperature = 0.8,
        }, ct);

        return ToWritingResult(response);
    }

    public async Task<Result<AiDetectionResult>> DetectAsync(
        Guid userId, DetectAiRequest request, CancellationToken ct = default)
    {
        var access = await documents.EnsureAccessAsync(userId, request.DocumentId, ct);
        if (!access.IsSuccess)
            return Result<AiDetectionResult>.Failure(access.Error ?? "Access denied");

        var plain = StripHtml(request.ContentHtml);
        var response = await ai.CompleteAsync(new AIRequest
        {
            SystemPrompt =
                "Estimate how likely text was AI-generated. Reply ONLY with JSON: {\"aiProbability\":0.0-1.0,\"summary\":\"one sentence\"}",
            UserPrompt = plain.Length > 8000 ? plain[..8000] : plain,
            MaxTokens = 256,
            Temperature = 0,
        }, ct);

        if (!response.IsSuccess || response.Value is null)
            return Result<AiDetectionResult>.Failure(response.Error ?? "Detection failed");

        try
        {
            var json = JsonDocument.Parse(response.Value.Content);
            var prob = json.RootElement.GetProperty("aiProbability").GetDouble();
            var summary = json.RootElement.TryGetProperty("summary", out var s) ? s.GetString() ?? "" : "";
            var clamped = Math.Clamp(prob, 0, 1);
            await documentRepo.UpdateAiDetectionScoreAsync(request.DocumentId, (decimal)clamped, ct);
            return Result<AiDetectionResult>.Success(new AiDetectionResult
            {
                AiProbability = clamped,
                Summary = summary,
            });
        }
        catch
        {
            return Result<AiDetectionResult>.Failure("Could not parse AI detection response");
        }
    }

    private static Result<WritingTextResult> ToWritingResult(Result<AIResponse> response)
    {
        if (!response.IsSuccess || response.Value is null)
            return Result<WritingTextResult>.Failure(response.Error ?? "AI request failed");
        return Result<WritingTextResult>.Success(new WritingTextResult
        {
            Content = AiHtmlSanitizer.ToHtmlFragment(response.Value.Content),
        });
    }

    private static string StripHtml(string html) => HtmlTagRegex().Replace(html, " ");

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlTagRegex();
}
