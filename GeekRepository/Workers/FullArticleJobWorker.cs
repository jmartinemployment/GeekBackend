using System.Text.Json;
using GeekApplication.Interfaces.Seo;
using GeekApplication.Models.Seo;
using GeekApplication.Results;

namespace GeekRepository.Workers;

public sealed class FullArticleJobWorker(
    IServiceProvider services,
    ILogger<FullArticleJobWorker> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "FullArticleJobWorker iteration failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private async Task ProcessPendingAsync(CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var jobs = scope.ServiceProvider.GetRequiredService<IBackgroundJobRepository>();
        var ai = scope.ServiceProvider.GetRequiredService<IAIProvider>();
        var documents = scope.ServiceProvider.GetRequiredService<IContentDocumentRepository>();

        var pending = await jobs.GetPendingAsync("full_article", 1, ct);
        if (!pending.IsSuccess || pending.Value is null || pending.Value.Count == 0)
            return;

        var job = pending.Value[0];
        await jobs.UpdateProgressAsync(job.Id, 10, ct);

        var payload = JsonSerializer.Deserialize<FullArticleJobPayload>(job.PayloadJson, JsonOptions);
        if (payload is null)
        {
            await jobs.MarkFailedAsync(job.Id, "Invalid job payload", ct);
            return;
        }

        var systemPrompt =
            "You are an expert SEO content writer. Return only valid HTML for a blog article body. " +
            "Include one H1, multiple H2/H3 sections, and natural use of the target keyword. No markdown fences.";

        var userPrompt =
            $"Write a comprehensive article targeting the keyword \"{payload.Keyword}\" for readers in {payload.Location}. " +
            $"Title: {payload.Title}. Aim for roughly 1200-1500 words.";

        var aiResult = await ai.CompleteAsync(new AIRequest
        {
            SystemPrompt = systemPrompt,
            UserPrompt = userPrompt,
            MaxTokens = 4096,
            Temperature = 0.6,
        }, ct);

        if (!aiResult.IsSuccess || aiResult.Value is null)
        {
            await jobs.MarkFailedAsync(job.Id, aiResult.Error ?? "AI generation failed", ct);
            return;
        }

        await jobs.UpdateProgressAsync(job.Id, 70, ct);

        var html = aiResult.Value.Content.Trim();
        if (!html.Contains("<h1", StringComparison.OrdinalIgnoreCase))
            html = $"<h1>{payload.Title}</h1>\n{html}";

        var create = await documents.CreateAsync(
            job.UserId,
            new CreateContentDocumentRequest
            {
                ProjectId = payload.ProjectId,
                Title = payload.Title,
                TargetKeyword = payload.Keyword,
                TargetLocation = payload.Location,
            },
            ct);

        if (!create.IsSuccess || create.Value is null)
        {
            await jobs.MarkFailedAsync(job.Id, create.Error ?? "Failed to create document", ct);
            return;
        }

        var docId = create.Value.Id;
        var wordCount = html.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        var updated = await documents.UpdateContentAsync(
            docId,
            new UpdateContentRequest { ContentHtml = html },
            wordCount,
            ct);

        if (!updated.IsSuccess)
        {
            await jobs.MarkFailedAsync(job.Id, updated.Error ?? "Failed to save content", ct);
            return;
        }

        await jobs.MarkCompleteAsync(job.Id, docId, ct);
        logger.LogInformation("Full article job {JobId} completed → document {DocumentId}", job.Id, docId);
    }
}
