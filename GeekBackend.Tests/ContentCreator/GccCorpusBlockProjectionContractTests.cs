using System.Text.Json;
using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.Workflow.Domain.Entities;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// F-R10: the C# half of the block-projection contract.
///
/// GeekAPI cuts quote candidates from a page's blocks with <see cref="GccCorpusBlockMapper"/>, and
/// Geek-Crawler-Rag verifies those quotes against its own Python projection of the same blocks
/// (<c>block_text.render_block_text</c>). Two implementations of "blocks to text" drift, so both are
/// pinned to one fixture, <c>contracts/block-projection/v1.json</c>, copied byte-identically into
/// each repo and compared by the cross-repo workflow. This test holds the mapper to the fixture's
/// <c>csharp</c> strings where a case names a difference, and to its <c>python</c> strings, byte
/// for byte, where it does not. The Python side is pinned by
/// <c>tests/test_block_projection_contract.py</c>.
///
/// A failure here means the mapper's text changed. The fix is never to edit the fixture to match:
/// a new difference must be named in the fixture and normalised by the Library's verify route, or
/// removed from the mapper.
/// </summary>
public sealed class GccCorpusBlockProjectionContractTests
{
    public static IEnumerable<object[]> Cases()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(FindContract()));
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            yield return [c.GetProperty("name").GetString()!];
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void The_mapper_produces_the_fixture_text(string name)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(FindContract()));
        var testCase = doc.RootElement.GetProperty("cases").EnumerateArray()
            .Single(c => c.GetProperty("name").GetString() == name);

        var expected = (testCase.TryGetProperty("csharp", out var csharp)
                ? csharp
                : testCase.GetProperty("python"))
            .EnumerateArray()
            .Select(s => s.GetString()!)
            .ToList();

        var paragraphs = GccCorpusBlockMapper.MapBlocks(testCase.GetProperty("blocks").Clone(), null);

        Assert.Equal(expected, Flatten(paragraphs));
    }

    /// <summary>
    /// The strings a quote can be cut from, one per item, in document order: the same unit the
    /// Python projection produces per block.
    /// </summary>
    private static List<string> Flatten(IReadOnlyList<Paragraph> paragraphs)
    {
        static string Text(IReadOnlyList<Run> runs) => string.Concat(runs.Select(r => r.Text));

        var texts = new List<string>();
        foreach (var paragraph in paragraphs)
        {
            switch (paragraph)
            {
                case TextParagraph p:
                    texts.Add(Text(p.Runs));
                    break;
                case ListParagraph p:
                    texts.AddRange(p.Items.Select(Text));
                    break;
                case QuoteParagraph p:
                    texts.Add(Text(p.Runs));
                    break;
                case CodeParagraph p:
                    texts.Add(p.Code);
                    break;
                case DefinitionParagraph p:
                    foreach (var item in p.Items)
                    {
                        texts.Add(Text(item.Term));
                        if (item.Definition.Count > 0)
                        {
                            texts.Add(Text(item.Definition));
                        }
                    }
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unhandled paragraph type {paragraph.GetType().Name}: add it here so the "
                        + "contract covers it.");
            }
        }
        return texts;
    }

    /// <summary>
    /// Walks up from the test assembly to the repository root, stopping there so the other repo's
    /// copy is never read by accident -- the two copies are compared by the cross-repo workflow.
    /// </summary>
    private static string FindContract()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "contracts", "block-projection", "v1.json");
            if (File.Exists(candidate)) return candidate;

            var isRepoRoot = Directory.Exists(Path.Combine(dir.FullName, ".git"))
                || dir.GetFiles("*.sln").Length > 0
                || dir.GetFiles("*.slnx").Length > 0;
            if (isRepoRoot) break;

            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            "contracts/block-projection/v1.json was not found above " + AppContext.BaseDirectory
            + ". It is the F-R10 fixture shared with Geek-Crawler-Rag and must be committed in this "
            + "repository, byte-identical to that repo's copy.");
    }
}
