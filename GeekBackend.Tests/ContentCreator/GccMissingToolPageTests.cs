using System.Text.Json;
using GeekAPI.Services.ContentCreator;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// A pillar or a blog that links a tool page the project does not have says so on itself.
/// </summary>
/// <remarks>
/// The 2:32 PM run of 2026-10-05 wrote a pillar and a blog that both link
/// <c>/tools/accounting/accounts-payable/approvalmax</c> and refused the Approvalmax tool page in the
/// same run. Every declared partner's path is a link the check allows; whether the page behind it
/// exists is known only when the run is over.
/// </remarks>
public sealed class GccMissingToolPageTests
{
    private const string RampPath = "/tools/accounting/accounts-payable/ramp";
    private const string ApprovalmaxPath = "/tools/accounting/accounts-payable/approvalmax";

    private static readonly GccPartnerToolPage[] Partners =
    [
        new("ramp.com", "Ramp", "ramp", RampPath),
        new("approvalmax.com", "Approvalmax", "approvalmax", ApprovalmaxPath),
    ];

    private static GccGenerationCoordinator.GeneratedPiece Page(string type, string name, params string[] links)
    {
        var runs = string.Join(",", links.Select(l => "{\"text\":\"a tool\",\"href\":\"" + l + "\"}"));
        var body =
            """{"title":"T","warnings":["already said"],"body":{"lede":{"tag":"h2","heading":"L","paragraphs":[{"type":"text","runs":["""
            + runs
            + """]}],"href":null,"children":[]},"sections":[]}}""";
        return new GccGenerationCoordinator.GeneratedPiece(type, body, name);
    }

    private static IReadOnlyList<string> Warnings(GccGenerationCoordinator.GeneratedPiece piece) =>
        GccGenerationCoordinator.WarningsOf(piece.BodyJson);

    [Fact]
    public void A_page_linking_a_tool_page_the_run_refused_says_so_and_the_tool_pages_are_left_alone()
    {
        GccGenerationCoordinator.GeneratedPiece[] pieces =
        [
            Page("pillar", "AP: Approvals", RampPath, ApprovalmaxPath),
            Page("blog", "AP: Approvals", ApprovalmaxPath),
            Page("tool", "Ramp"),
        ];

        var named = GccGenerationCoordinator.NameMissingToolPages(pieces, Partners, [], toolPagesAskedFor: true);

        foreach (var longForm in named.Take(2))
        {
            Assert.Equal(
                [
                    "already said",
                    "Links to the Approvalmax tool page, and this project has no Approvalmax tool page: this run did "
                    + "not write it -- see what was not written. Generate it, or take the link out, before this is "
                    + "published.",
                ],
                Warnings(longForm));
        }

        // Ramp was written by this run, so its link leads somewhere. The tool page is the piece it was.
        Assert.Same(pieces[2], named[2]);
        // The document under the warning is untouched.
        using var before = JsonDocument.Parse(pieces[0].BodyJson);
        using var after = JsonDocument.Parse(named[0].BodyJson);
        Assert.Equal(
            before.RootElement.GetProperty("body").GetRawText(), after.RootElement.GetProperty("body").GetRawText());
    }

    [Fact]
    public void A_tool_page_the_project_already_has_is_not_missing_whatever_its_capitals()
    {
        GccGenerationCoordinator.GeneratedPiece[] pieces = [Page("pillar", "AP: Approvals", ApprovalmaxPath + "/")];

        var named = GccGenerationCoordinator.NameMissingToolPages(pieces, Partners, ["APPROVALMAX"], toolPagesAskedFor: false);

        Assert.Same(pieces[0], named[0]);
    }

    [Fact]
    public void A_pillar_written_before_any_tool_page_names_every_one_it_links()
    {
        GccGenerationCoordinator.GeneratedPiece[] pieces = [Page("pillar", "AP: Approvals", RampPath, ApprovalmaxPath + "/")];

        var named = GccGenerationCoordinator.NameMissingToolPages(pieces, Partners, [], toolPagesAskedFor: false);

        Assert.Contains(
            "Links to 2 tool pages this project does not have (Ramp, Approvalmax): they have not been generated. "
            + "Generate them, or take the links out, before this is published.",
            Warnings(named[0]));
    }

    [Fact]
    public void A_page_that_links_no_partner_tool_page_is_left_as_it_was()
    {
        GccGenerationCoordinator.GeneratedPiece[] pieces = [Page("blog", "AP: Approvals", "#consultationAppointment2xl")];

        Assert.Same(pieces[0], GccGenerationCoordinator.NameMissingToolPages(pieces, Partners, [], true)[0]);
    }

    [Fact]
    public void A_warning_is_added_to_the_envelope_and_a_body_with_no_envelope_is_left_alone()
    {
        var withNone = GccGenerationCoordinator.WithWarning("""{"body":{}}""", "first");
        var withOne = GccGenerationCoordinator.WithWarning(withNone, "second");

        Assert.Equal(["first", "second"], GccGenerationCoordinator.WarningsOf(withOne));
        Assert.Equal("[1,2]", GccGenerationCoordinator.WithWarning("[1,2]", "ignored"));
        Assert.Equal("not json", GccGenerationCoordinator.WithWarning("not json", "ignored"));
    }
}
