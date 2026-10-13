using System.Text;
using System.Text.Json;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// One of the operator's FAQ questions, with the answer the operator entered for it.
/// </summary>
/// <param name="Question">The question as written. It is the answer's heading on the page.</param>
/// <param name="Answer">What the operator entered as its answer. Empty when none was entered.</param>
public sealed record GccFaqEntry(string Question, string Answer)
{
    /// <summary>Whether the operator entered an answer. A question without one is not written.</summary>
    public bool HasAnswer => Answer.Length > 0;
}

/// <summary>
/// The operator's FAQ: the questions of the pillar's People Also Ask, the blog's FAQ and a tool page's
/// FAQ, each with the answer the operator entered for it on the Brief.
/// </summary>
/// <remarks>
/// <para>
/// <b>The operator supplies the answer</b> (Jeff, 2026-10-13: "I have answers to questions that I am
/// suppling ... no longer query for answer passages"). The writer is handed each question with its
/// answer and writes that answer in the Brief's voice, adding nothing to it. It is not printed as
/// typed: the operator's note may not be in the voice the rest of the page is written in (Jeff,
/// 2026-10-10).
/// </para>
/// <para>
/// <b>No crawl is searched for a question any more.</b> From 2026-10-10 a tool page's questions, and
/// from 2026-10-11 the pillar's and the blog's, were each searched for in the partners' crawls, three
/// passages a crawl, and the writer was told to answer only where a passage answered. On the 13:16
/// UTC run of 2026-10-13 five questions were shown their passages and left unanswered, and the two
/// requests that decide which questions their passages answer were the two whose replies ended in
/// blanks. The searches, what they filed in the research and the classes that read it are deleted.
/// </para>
/// <para>
/// <b>A question with no answer entered is left out and named,</b> one line for a page's questions
/// together. It is never answered from the writer's own knowledge, and nothing is searched for it.
/// </para>
/// </remarks>
internal static class GccFaqAnswers
{
    /// <summary>
    /// The most characters of the operator's answers one request carries.
    /// </summary>
    /// <remarks>
    /// A request's reply is limited to 3,072 tokens (<c>ContentPromptBuilder.FaqSectionPrompt</c>). An
    /// answer written from the operator's is told to be no longer than it, so the reply is about the
    /// answers again plus each question's heading and its place in the reply's format, some 60 tokens
    /// a question. 6,000 characters is about 1,500 tokens at the four characters a token this prose
    /// never falls below: with eight questions, about 2,000 of the 3,072.
    /// <para>
    /// This leaves nothing out. It decides how many requests a page's questions are spread over, and
    /// every question with an answer is sent in one of them.
    /// </para>
    /// </remarks>
    internal const int MaxAnswerCharsPerCall = 6_000;

    /// <summary>The box on the Brief the pillar's answers are entered in, by the label the form shows.</summary>
    /// <remarks>These three are the form's own labels (content-creator-v2 <c>ContentBriefPanel.tsx</c>
    /// and <c>NicheFramingPanel.tsx</c>). A report tells the operator where an answer goes by the
    /// name on the screen, so change a label there and here together.</remarks>
    internal const string PillarBox = "People Also Ask (direct entry, optional)";

    /// <summary>The box on the Brief the blog's answers are entered in.</summary>
    internal const string BlogBox = "Blog FAQ (optional)";

    /// <summary>The box, in a tool's section of the Brief, that tool page's answers are entered in.</summary>
    internal const string ToolBox = "Tool page FAQ";

    /// <summary>
    /// A list of questions as the Brief saves it, in any of its three shapes: an array of
    /// <c>{ "question", "answer" }</c>, which is what the form has saved since 2026-10-13; an array of
    /// strings; or one string with a question to a line. The last two carry no answer.
    /// </summary>
    /// <remarks>
    /// The one reader of all three boxes (<c>paaQuestions</c>, <c>blogFaqQuestions</c> and a tool's
    /// <c>faqQuestions</c>), so a question is a question and an answer an answer wherever they were
    /// entered. Each question once, the first time it is written: a question is its answer's heading.
    /// A row with no question is not a row, whatever its answer holds.
    /// </remarks>
    internal static IReadOnlyList<GccFaqEntry> Read(JsonElement value)
    {
        var entries = new List<GccFaqEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Add(string? question, string? answer)
        {
            var asked = (question ?? string.Empty).Trim();
            if (asked.Length == 0 || !seen.Add(asked)) return;
            entries.Add(new GccFaqEntry(asked, (answer ?? string.Empty).Trim()));
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            foreach (var line in (value.GetString() ?? string.Empty).Split(['\n', '\r']))
            {
                Add(line, null);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    Add(item.GetString(), null);
                }
                else if (item.ValueKind == JsonValueKind.Object)
                {
                    Add(StringOf(item, "question"), StringOf(item, "answer"));
                }
            }
        }

        return entries;
    }

    /// <summary>The questions of <paramref name="entries"/>, in order, with or without an answer.</summary>
    internal static IReadOnlyList<string> QuestionsOf(IReadOnlyList<GccFaqEntry> entries) =>
        [.. entries.Select(entry => entry.Question)];

    /// <summary>
    /// The questions divided into requests, in the order given: a request takes questions until it
    /// holds <paramref name="maxQuestions"/> or the next one's answer would take its answers past
    /// <see cref="MaxAnswerCharsPerCall"/>. An answer is never divided, so one longer than the limit is
    /// a request by itself.
    /// </summary>
    internal static IReadOnlyList<IReadOnlyList<GccFaqEntry>> Calls(IReadOnlyList<GccFaqEntry> answered, int maxQuestions)
    {
        var calls = new List<IReadOnlyList<GccFaqEntry>>();
        var call = new List<GccFaqEntry>();
        var chars = 0;
        foreach (var entry in answered)
        {
            if (call.Count > 0 && (call.Count >= maxQuestions || chars + entry.Answer.Length > MaxAnswerCharsPerCall))
            {
                calls.Add(call);
                call = [];
                chars = 0;
            }

            call.Add(entry);
            chars += entry.Answer.Length;
        }

        if (call.Count > 0) calls.Add(call);
        return calls;
    }

    /// <summary>
    /// One request's questions as the writer is shown them: each under its label (Q1, Q2, ... in the
    /// order given), with the operator's answer beneath it.
    /// </summary>
    internal static string Render(IReadOnlyList<GccFaqEntry> batch)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < batch.Count; i++)
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.AppendLine($"Q{i + 1}: {batch[i].Question}");
            sb.AppendLine($"The operator's answer to Q{i + 1}:");
            sb.AppendLine(batch[i].Answer);
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// What the operator is told when questions were entered with no answer: how many were left out,
    /// which, and the box on the Brief their answers go in. One line for a page's questions together:
    /// they share one cause and one remedy.
    /// </summary>
    /// <param name="label">Whose questions they are: "the pillar's People Also Ask", or "FAQ" on a tool page.</param>
    /// <param name="box">The box on the Brief, by the label the form shows.</param>
    internal static string NoAnswerEntered(string label, string box, IReadOnlyList<string> questions)
    {
        var named = string.Join("; ", questions.Select(q => $"\"{q}\""));
        return questions.Count == 1
            ? $"{label}: no answer is entered for {named}, and the question was left out. Its answer goes beside it under \"{box}\" on the Brief."
            : $"{label}: no answer is entered for {questions.Count} questions, and all {questions.Count} were left out. "
                + $"Their answers go beside them under \"{box}\" on the Brief. The questions: {named}.";
    }

    private static string? StringOf(JsonElement obj, string name)
    {
        foreach (var property in obj.EnumerateObject())
        {
            if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            return property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
        }

        return null;
    }
}
