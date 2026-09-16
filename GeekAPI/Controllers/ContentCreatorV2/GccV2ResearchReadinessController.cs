using GeekAPI.Auth;
using GeekAPI.Services.ContentCreatorV2.GeekCrawler;
using GeekAPI.Services.GeekCrawler;
using GeekApplication.Models.GeekCrawler;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.ContentCreatorV2;

/// <summary>
/// Tells the Create wizard which entered URLs already have indexed crawl evidence.
///
/// Partner evidence is mandatory and nothing in Create crawls partners — the resolver looks up runs
/// that already exist. Without this, an operator could enter a partner URL, complete the whole wizard,
/// and only discover at PLAN that no evidence exists for it.
/// </summary>
[ApiController]
[Route("api/geek-content-creator-v2/research-readiness")]
public sealed class GccV2ResearchReadinessController(
    ICurrentUserContext user,
    GccV2GeekCrawlerResearchResolver resolver) : ControllerBase
{
    public sealed record SeedReadinessRequest(
        IReadOnlyList<string>? PartnerUrls,
        IReadOnlyList<string>? CompetitorUrls);

    public sealed record SeedReadinessResponse(
        IReadOnlyList<GccV2SeedReadiness> Partners,
        IReadOnlyList<GccV2SeedReadiness> Competitors,
        bool PartnerReady);

    [HttpPost]
    public async Task<ActionResult<SeedReadinessResponse>> Check(
        SeedReadinessRequest request,
        CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var owner = user.UserId.ToString("D");

        var partners = await resolver.CheckSeedReadinessAsync(
            owner, CrawlTypes.Partner, request.PartnerUrls ?? [], ct);
        var competitors = await resolver.CheckSeedReadinessAsync(
            owner, CrawlTypes.Competitors, request.CompetitorUrls ?? [], ct);

        // Partner evidence is the mandatory half: at least one entered partner must be ready.
        // Competitors are never required, so their readiness is informational only.
        var partnerReady = partners.Count > 0 && partners.Any(p => p.Ready);

        return Ok(new SeedReadinessResponse(partners, competitors, partnerReady));
    }
}
