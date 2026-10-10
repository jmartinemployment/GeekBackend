using GeekAPI.Services.ContentCreator;
using GeekApplication.Models.ContentCreator;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// <c>{base}/{department}/{descriptor}/{slug}</c> — Jeff, 2026-10-02:
/// <c>/tools/accounting/accounts-payable</c>.
/// </summary>
/// <remarks>
/// The descriptor directory exists because the slug is the product name, so one partner's pages for two
/// different problems resolved to the same URL. The department is derived from the researched taxonomy
/// path rather than read off the create, because nothing in the frontend sets that column and it
/// defaults to "marketing" — so accounts-payable pages were headed for <c>/tools/marketing/dext</c>.
/// </remarks>
public class GccContentPathTests
{
    private static GccCreateDto Create(string topic, string? taxonomyPath, string department = "marketing") =>
        new(
            Id: Guid.NewGuid(),
            ClientId: Guid.NewGuid(),
            OwnerUserId: Guid.NewGuid(),
            StartingContentType: "tool",
            Topic: topic,
            Notes: null,
            ProjectSiteRunId: null,
            SiteSectionJson: null,
            BriefJson: taxonomyPath is null
                ? null
                : $"{{\"nicheFraming\":{{\"taxonomyPath\":\"{taxonomyPath}\",\"coreProblem\":\"x\"}}}}",
            ResearchJson: null,
            Status: "draft",
            CreatedAtUtc: DateTime.UtcNow,
            UpdatedAtUtc: DateTime.UtcNow,
            Department: department,
            ProjectId: Guid.NewGuid());

    [Fact]
    public void The_shape_Jeff_asked_for()
    {
        var create = Create(
            "Accounts Payable: Automated Data Entry & Processing",
            "Accounting -> Cash Flow Forecasting -> Accounts Payable");

        Assert.Equal(
            "https://x.test/tools/accounting/accounts-payable/dext",
            GccContentPath.For("https://x.test/tools", create, "dext"));
    }

    [Fact]
    public void The_descriptor_is_what_stops_one_partners_pages_colliding()
    {
        // The defect this directory exists for. The slug is the product name, so without the descriptor
        // these two are the same URL.
        var ap = Create("Accounts Payable: Automated Data Entry & Processing", "Accounting");
        var expenses = Create("Expense Management: Automated Receipt Capture", "Accounting");

        var apUrl = GccContentPath.For("https://x.test/tools", ap, "dext");
        var expensesUrl = GccContentPath.For("https://x.test/tools", expenses, "dext");

        Assert.NotEqual(apUrl, expensesUrl);
        Assert.EndsWith("/accounting/accounts-payable/dext", apUrl, StringComparison.Ordinal);
        Assert.EndsWith("/accounting/expense-management/dext", expensesUrl, StringComparison.Ordinal);
    }

    [Fact]
    public void The_department_comes_from_the_taxonomy_path_not_the_create_column()
    {
        // Every live create is "marketing" because nothing sets that column, so an accounting page
        // published to /tools/marketing/. The researched path opens with a real department.
        var create = Create("Accounts Payable: Automated Data Entry", "Accounting -> Cash Flow Forecasting", department: "marketing");

        Assert.Equal("accounting", GccContentPath.DepartmentFor(create));
    }

    [Fact]
    public void A_taxonomy_path_whose_first_level_is_not_a_department_is_not_trusted()
    {
        // Departments.Slugs is a closed set of five. A path opening with something else is the operator
        // describing their own taxonomy, not naming a department, so the create's column still decides.
        var create = Create("Accounts Payable: Automated Data Entry", "Finance Ops -> Receivables", department: "sales");

        Assert.Equal("sales", GccContentPath.DepartmentFor(create));
    }

    [Theory]
    [InlineData("Finance Ops -> Receivables", "Finance Ops")]
    [InlineData("Human Resources -> Onboarding", "Human Resources")]
    [InlineData("Accounts Receivable", "Accounts Receivable")]
    public void A_first_level_that_is_not_a_department_is_a_refusal_that_names_it_and_the_five(
        string taxonomyPath, string firstLevel)
    {
        // It was ignored without a word, and the run's pages were filed under the create's column,
        // which is "marketing" on every live create.
        var refusal = GccContentPath.DepartmentRefusal(Create("Topic: Keyword", taxonomyPath).BriefJson);

        Assert.NotNull(refusal);
        Assert.Contains($"'{firstLevel}'", refusal!, StringComparison.Ordinal);
        Assert.Contains("Accounting, Customer Service, Human Resource, Marketing, Sales", refusal, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Accounting -> Cash Flow Forecasting -> Accounts Receivable")]
    [InlineData("accounting")]
    [InlineData("Customer Service > Returns")]
    [InlineData("Human Resource › Onboarding")]
    public void A_first_level_that_is_a_department_is_not_refused_whatever_follows_it(string taxonomyPath) =>
        Assert.Null(GccContentPath.DepartmentRefusal(Create("Topic: Keyword", taxonomyPath).BriefJson));

    [Fact]
    public void A_brief_with_no_taxonomy_path_is_not_refused()
    {
        Assert.Null(GccContentPath.DepartmentRefusal(null));
        Assert.Null(GccContentPath.DepartmentRefusal("""{"nicheFraming":{"coreProblem":"x"}}"""));
        Assert.Null(GccContentPath.DepartmentRefusal("""{"nicheFraming":{"taxonomyPath":"  "}}"""));
    }

    [Fact]
    public void No_taxonomy_path_falls_back_rather_than_refusing()
    {
        var create = Create("Accounts Payable: Automated Data Entry", taxonomyPath: null, department: "accounting");

        Assert.Equal("accounting", GccContentPath.DepartmentFor(create));
        Assert.Equal(
            "https://x.test/tools/accounting/accounts-payable/dext",
            GccContentPath.For("https://x.test/tools", create, "dext"));
    }

    [Fact]
    public void A_topic_with_no_descriptor_sits_directly_under_its_department()
    {
        // Empty is a real answer, not a failure: a topic with no colon is all keyword. One level fewer
        // beats a directory nobody chose.
        var create = Create("Automated Data Entry & Processing", "Accounting");

        Assert.Equal("", GccContentPath.DescriptorFor(create));
        Assert.Equal(
            "https://x.test/tools/accounting/dext",
            GccContentPath.For("https://x.test/tools", create, "dext"));
    }

    [Fact]
    public void A_one_word_tail_is_not_a_descriptor_split()
    {
        // GccTopic.Parse's guard: "Marketing: AI" is a topic ending in a colon, not a descriptor plus a
        // one-word keyword. Splitting it would publish /marketing-ai/ and rank for "AI".
        var create = Create("Marketing: AI", "Marketing");

        Assert.Equal("", GccContentPath.DescriptorFor(create));
        Assert.Equal("Marketing: AI", GccTargetKeyword.FromTopic(create.Topic));
    }

    [Fact]
    public void The_directory_form_omits_the_slug()
    {
        var create = Create("Accounts Payable: Automated Data Entry", "Accounting");

        Assert.Equal(
            "https://x.test/use-cases/accounting/accounts-payable",
            GccContentPath.DirectoryFor("https://x.test/use-cases", create));
    }

    [Fact]
    public void A_trailing_slash_on_the_base_does_not_double_up()
    {
        var create = Create("Accounts Payable: Automated Data Entry", "Accounting");

        Assert.Equal(
            "https://x.test/tools/accounting/accounts-payable/dext",
            GccContentPath.For("https://x.test/tools/", create, "dext"));
    }

    [Theory]
    [InlineData("Accounts Payable", "accounts-payable")]
    [InlineData("Automated Data Entry & Processing", "automated-data-entry-processing")]
    [InlineData("  Cash  Flow   Forecasting  ", "cash-flow-forecasting")]
    public void Slugs_are_lowercase_alphanumeric_with_single_hyphens(string input, string expected)
    {
        Assert.Equal(expected, GccContentPath.Slugify(input));
    }
}
