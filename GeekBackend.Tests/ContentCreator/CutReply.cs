using GeekAPI.Services.Workflow.Providers;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// The failure a reply cut at its limit is, made by the code that judges a real reply
/// (<see cref="OpenAiCompatibleOutcome"/>), so a test fails a request in the words a run would carry.
/// </summary>
internal static class CutReply
{
    /// <summary>What the model had written of its answer before it turned to blanks.</summary>
    internal const string Written =
        """{"tag":"h2","heading":"Frequently Asked Questions","paragraphs":[],"href":null,"children":[{"tag":"h3","heading":"A question","paragraphs":[{"type":"text","runs":[{"text":"An answer."}]}]""";

    /// <summary>
    /// A reply that ended in blanks and reached its limit. The defaults are the blog's questions reply
    /// of the 13:16 UTC run of 2026-10-13: 2,913 tabs, a limit of 3,072 tokens. The ADP tool page's, of
    /// the same run, ended in 2,481 spaces.
    /// </summary>
    internal static ContentGenerationException EndingInBlanks(int blanks = 2913, char blank = '\t', int limit = 3072)
    {
        var choice = new OpenAiChoice
        {
            FinishReason = "length",
            Message = new OpenAiCompatibleMessage("assistant", Written + new string(blank, blanks)),
        };

        return Assert.Throws<ContentGenerationException>(
            () => OpenAiCompatibleOutcome.RequireUsableContent(choice, "OpenAI", limit));
    }
}
