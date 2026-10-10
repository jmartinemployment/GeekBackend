using System.Text;
using GeekAPI.Services.Workflow.Services;
using GeekApplication.Models.ContentCreator;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// Where a generated page lives: <c>{base}/{department}/{descriptor}/{slug}</c>.
/// </summary>
/// <remarks>
/// <para>
/// Jeff, 2026-10-02: <c>/tools/accounting/accounts-payable</c>. Two directories above the page —
/// the <b>department</b> and the <b>descriptor</b> — and the descriptor is the new one.
/// </para>
/// <para>
/// <b>Why the descriptor earns a directory.</b> A tool page is one product against one problem, and the
/// slug is the product name. So Dext for accounts payable and Dext for expense management resolved to
/// the same URL: <c>/tools/{department}/dext</c>, twice. The descriptor is what distinguishes them, and
/// it is also what lets one partner carry a page per problem without the pages cannibalising each other.
/// </para>
/// <para>
/// <b>Why the department is derived, not read.</b> <c>gcc_creates.Department</c> is a bare
/// <c>varchar(64)</c> that defaults to <c>"marketing"</c>, <c>GccController</c> substitutes
/// <c>"marketing"</c> when it is blank, and no component in content-creator-v2 ever sets it — so every
/// live create is <c>marketing</c> and accounts-payable pages were headed for
/// <c>/tools/marketing/dext</c>. The operator's researched taxonomy path already opens with a real
/// department (<c>Accounting → Cash Flow Forecasting → Accounts Receivable</c>), so the path is the
/// better source and <see cref="Departments.IsValid"/> — which had zero callers — is what decides
/// whether to trust it.
/// </para>
/// <para>
/// <b>One builder.</b> Six sites assembled this string by hand before this existed, which is how a
/// canonical tag and a JSON-LD <c>url</c> for the same page come to disagree.
/// </para>
/// </remarks>
public static class GccContentPath
{
    /// <summary>
    /// The department for a create: its taxonomy path's first level when that is a real department,
    /// otherwise whatever the create carries.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A create with no taxonomy path is filed under its own column, which is never empty — it defaults
    /// to <c>"marketing"</c> and the operator has no UI to set it — so a refusal for a missing path
    /// would block a publish over a field nobody can fill in.
    /// </para>
    /// <para>
    /// A path whose first level is not a department is a different case: the operator typed it, and can
    /// change it. No Generate starts on one (<see cref="DepartmentRefusal"/>, read by the brief's own
    /// gate), so the column decides here only for a page whose brief was changed after it was written.
    /// Until 2026-10-10 nothing said so, and such a page was filed under "marketing" without a word.
    /// </para>
    /// </remarks>
    public static string DepartmentFor(GccCreateDto? create)
    {
        var fromCreate = string.IsNullOrWhiteSpace(create?.Department)
            ? "marketing"
            : create!.Department.Trim().ToLowerInvariant();

        var path = GccNicheFramingReader.TaxonomyPath(create?.BriefJson);
        if (path.Count == 0) return fromCreate;

        var candidate = Slugify(path[0]);
        return candidate.Length > 0 && Departments.IsValid(candidate) ? candidate : fromCreate;
    }

    /// <summary>
    /// Why a brief's taxonomy path cannot file a page, as a message, or null when it can: no path, or a
    /// path whose first level is one of <see cref="Departments.Slugs"/>.
    /// </summary>
    /// <remarks>
    /// The first level is the department directory of every address the run builds: the page's own,
    /// and each link to a tool page. The later levels are the operator's record of where the keyword
    /// sits and are read by nothing; the directory under the department comes from the topic
    /// (<see cref="DescriptorFor"/>).
    /// </remarks>
    public static string? DepartmentRefusal(string? briefJson)
    {
        var path = GccNicheFramingReader.TaxonomyPath(briefJson);
        if (path.Count == 0) return null;

        var candidate = Slugify(path[0]);
        if (candidate.Length > 0 && Departments.IsValid(candidate)) return null;

        return $"brief: the taxonomy path's first level is '{path[0]}', which is not a department. "
            + "It files the page, so it must be one of: "
            + $"{string.Join(", ", Departments.Slugs.Select(Departments.DisplayName))}.";
    }

    /// <summary>
    /// The descriptor directory for a create, or empty when its topic carries no descriptor.
    /// </summary>
    /// <remarks>
    /// Empty is a real answer, not a failure: a topic with no colon is all keyword
    /// (<see cref="GccTopic.Parse"/>), and such a page sits directly under its department the way every
    /// page did before this. Never substituted with a placeholder — a directory nobody chose is worse
    /// than one level fewer.
    /// </remarks>
    public static string DescriptorFor(GccCreateDto? create) =>
        Slugify(GccTopic.DescriptorOf(create?.Topic));

    /// <summary>
    /// The full public URL for a page.
    /// </summary>
    /// <param name="baseUrl">The content type's base — article, blog or tool.</param>
    /// <param name="create">The create, for its department and descriptor. Null yields the old shape.</param>
    /// <param name="slug">The page's own slug, already built by its caller.</param>
    public static string For(string baseUrl, GccCreateDto? create, string slug)
    {
        var sb = new StringBuilder(baseUrl.TrimEnd('/'));

        var department = DepartmentFor(create);
        if (department.Length > 0) sb.Append('/').Append(department);

        var descriptor = DescriptorFor(create);
        if (descriptor.Length > 0) sb.Append('/').Append(descriptor);

        var trimmed = (slug ?? string.Empty).Trim('/');
        if (trimmed.Length > 0) sb.Append('/').Append(trimmed);

        return sb.ToString();
    }

    /// <summary>
    /// The page's path on the publisher's site, with no scheme or host -- what a link from another page
    /// on that site is set to. Built from <see cref="For"/>, so it cannot name a different address.
    /// </summary>
    public static string PathFor(string baseUrl, GccCreateDto? create, string slug) =>
        PathOf(For(baseUrl, create, slug));

    /// <summary>The path of a URL or of a path, always starting with a slash and never ending in one.</summary>
    public static string PathOf(string? urlOrPath)
    {
        var value = (urlOrPath ?? string.Empty).Trim();
        var path = Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? uri.AbsolutePath
            : value.Split('?', '#')[0];
        path = "/" + path.Trim('/');
        return path;
    }

    /// <summary>
    /// The directory a page sits in, with no slug — what a section index would be.
    /// </summary>
    public static string DirectoryFor(string baseUrl, GccCreateDto? create) =>
        For(baseUrl, create, string.Empty);

    /// <summary>
    /// Lowercase, alphanumeric, single hyphens. Matches how every other slug in this path is built, so
    /// "Accounts Payable" and "Automated Data Entry &amp; Processing" become directory names rather than
    /// escaped query noise.
    /// </summary>
    public static string Slugify(string? value)
    {
        var text = (value ?? string.Empty).Trim().ToLowerInvariant();
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c)) sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }

        return sb.ToString().Trim('-');
    }
}
