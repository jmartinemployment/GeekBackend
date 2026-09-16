using GeekAPI.Services.ContentCreatorV2.ToolPages;

namespace GeekBackend.Tests.ContentCreatorV2;

/// <summary>
/// Tool-page spawn is idempotent on (createId, partnerSlug): GccV2ToolPageSpawnService slugifies each
/// partner name, compares it against the slugs recovered from existing tool jobs' briefs, and skips
/// any already present.
///
/// That guarantee rests entirely on a slug round-trip — the same partner name must always produce the
/// same slug, and a spawned brief must parse back to that exact slug. If either drifts, the same
/// partner spawns twice on every retry. This covers the round-trip directly rather than mocking the
/// repository, provider, agent-team and context resolvers the full spawn requires.
/// </summary>
public sealed class GccV2ToolPageSpawnIdempotencyTests
{
    [Theory]
    [InlineData("ApprovalMax")]
    [InlineData("Plooto")]
    [InlineData("Bill.com")]
    [InlineData("A-Team Software")]
    public void Slugify_is_stable_for_the_same_partner_name(string name)
    {
        Assert.Equal(
            GccV2ToolSlugHelper.SlugifyToolName(name),
            GccV2ToolSlugHelper.SlugifyToolName(name));
    }

    [Theory]
    [InlineData("ApprovalMax", "approvalmax")]
    [InlineData("  ApprovalMax  ", "approvalmax")]
    [InlineData("APPROVALMAX", "approvalmax")]
    public void Slugify_normalizes_case_and_padding_so_a_partner_cannot_spawn_twice(
        string name, string expected)
    {
        Assert.Equal(expected, GccV2ToolSlugHelper.SlugifyToolName(name));
    }

    [Fact]
    public void Distinct_partners_do_not_collide_onto_one_slug()
    {
        var slugs = new[] { "ApprovalMax", "Plooto", "Dext", "Xero" }
            .Select(GccV2ToolSlugHelper.SlugifyToolName)
            .ToList();

        Assert.Equal(slugs.Count, slugs.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    /// <summary>
    /// The skip check reads slugs back out of persisted briefs, so a brief written by spawn must parse
    /// back to the slug spawn used. A drift here silently disables the idempotency guard.
    /// </summary>
    [Fact]
    public void Spawned_brief_parses_back_to_the_slug_it_was_written_with()
    {
        var slug = GccV2ToolSlugHelper.SlugifyToolName("ApprovalMax");
        var brief = GccV2ToolPageTargetParser.SerializePartnerBriefSlice(
            "ApprovalMax", slug, "https://approvalmax.com", null, 1);

        var parsed = GccV2ToolPageTargetParser.Parse(brief);

        Assert.NotNull(parsed);
        Assert.True(parsed!.IsPartner);
        Assert.Equal(slug, parsed.Slug);
    }

    [Fact]
    public void Non_partner_target_is_not_counted_as_an_existing_partner_slug()
    {
        var overview = GccV2ToolPageTargetParser.MergeOverviewTarget(null, "expense automation");
        var parsed = GccV2ToolPageTargetParser.Parse(overview);

        Assert.NotNull(parsed);
        Assert.False(parsed!.IsPartner);
    }
}
