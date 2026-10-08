using GeekAPI.Services.ContentCreator;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// The brief's evidence rows: one retrieval question per failure (Geek-Crawler-Rag
/// plans/retrieval-from-the-brief.md, 2026-10-08). Measured on Tipalti that day: the pain points
/// found the vendor describing three of six failures and the fix for none of those three; solution
/// descriptions in the vendor's own vocabulary found the product page for all six. The rows carry
/// that vocabulary into retrieval. They are read like every other framing field and merged per
/// tool the way pain points are: added, with a restated problem replaced by the tool's own row.
/// </summary>
public class GccNicheFramingEvidenceTests
{
    private static readonly string[] PartnerUrls = ["https://tipalti.com/", "https://melio.com/"];

    private const string Brief = """
        {
          "angle": "problem_solution",
          "nicheFraming": {
            "taxonomyPath": "Accounting -> Accounts Payable -> Automated Payment Execution",
            "coreProblem": "The uncontrolled handoff between an approved invoice and the moment cash leaves the bank.",
            "painPoints": "Invoices sit in inboxes.\n\nApproval is informal.",
            "automationToPitch": "A bill-payment control system.",
            "evidence": [
              { "problem": "Approval is informal.", "solution": "Approval workflows route each bill to the right approver with an audit trail.", "terms": ["approval workflow", "audit trail"] },
              { "problem": "", "solution": "", "terms": [] }
            ],
            "perTool": {
              "tipalti.com": {
                "coreProblem": "International vendors, multiple entities, contractors and marketplaces need controlled global payment execution.",
                "evidence": [
                  { "problem": "The company cannot reliably reconcile global payments back to the right vendor, entity or period.", "solution": "Automated payment reconciliation syncs payment results with the ERP, general ledger and sub-ledgers.", "terms": "payment reconciliation, multi-entity, sub-ledger" },
                  { "problem": "Approval is informal.", "solution": "Entity-specific approval workflows with routing by amount, department and currency.", "terms": ["approval workflow", "multi-entity"] }
                ]
              }
            }
          }
        }
        """;

    [Fact]
    public void Category_rows_are_read_and_a_blank_row_is_not_a_row()
    {
        var category = GccNicheFramingReader.ForCategory(Brief);

        Assert.NotNull(category);
        var row = Assert.Single(category!.Evidence);
        Assert.Equal("Approval is informal.", row.Problem);
        Assert.StartsWith("Approval workflows route", row.Solution, StringComparison.Ordinal);
        Assert.Equal(["approval workflow", "audit trail"], row.Terms);
        Assert.Equal("approval workflow audit trail", row.Keyword);
        Assert.Equal(row.Solution, row.Need);
    }

    [Fact]
    public void A_tools_rows_are_added_to_the_categorys_and_a_restated_problem_is_replaced_by_the_tools_own()
    {
        var tipalti = GccNicheFramingReader.ForProduct(Brief, PartnerUrls, "Tipalti");

        Assert.NotNull(tipalti);
        Assert.Equal(2, tipalti!.Evidence.Count);
        // The category's "Approval is informal." row is replaced in place by Tipalti's own, which
        // names the solution Tipalti's page should search for.
        Assert.Equal("Approval is informal.", tipalti.Evidence[0].Problem);
        Assert.StartsWith("Entity-specific approval workflows", tipalti.Evidence[0].Solution, StringComparison.Ordinal);
        // Tipalti's reconciliation row is added after the category's rows.
        Assert.StartsWith("The company cannot reliably reconcile", tipalti.Evidence[1].Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void Terms_read_from_a_comma_separated_string_as_well_as_an_array()
    {
        var tipalti = GccNicheFramingReader.ForProduct(Brief, PartnerUrls, "Tipalti");

        var reconciliation = tipalti!.Evidence.Single(r => r.Problem.StartsWith("The company cannot", StringComparison.Ordinal));
        Assert.Equal(["payment reconciliation", "multi-entity", "sub-ledger"], reconciliation.Terms);
        Assert.Equal("payment reconciliation multi-entity sub-ledger", reconciliation.Keyword);
    }

    [Fact]
    public void A_tool_without_rows_inherits_the_categorys()
    {
        var melio = GccNicheFramingReader.ForProduct(Brief, PartnerUrls, "Melio");

        Assert.NotNull(melio);
        var row = Assert.Single(melio!.Evidence);
        Assert.Equal("Approval is informal.", row.Problem);
    }

    [Fact]
    public void A_brief_without_rows_has_none_and_is_still_framing()
    {
        const string brief = """
            { "nicheFraming": { "coreProblem": "A problem.", "painPoints": "", "automationToPitch": "" } }
            """;

        var category = GccNicheFramingReader.ForCategory(brief);

        Assert.NotNull(category);
        Assert.Empty(category!.Evidence);
        Assert.True(category.HasAny);
    }

    [Fact]
    public void Rows_alone_are_framing_and_a_row_without_a_solution_asks_its_problem()
    {
        const string brief = """
            { "nicheFraming": { "evidence": [ { "problem": "Payments are executed ad hoc.", "terms": ["scheduled payment runs"] } ] } }
            """;

        var category = GccNicheFramingReader.ForCategory(brief);

        Assert.NotNull(category);
        Assert.True(category!.HasAny);
        var row = Assert.Single(category.Evidence);
        Assert.Equal("Payments are executed ad hoc.", row.Need);
        Assert.Equal("scheduled payment runs", row.Keyword);
    }
}
