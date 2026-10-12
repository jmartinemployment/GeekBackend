using GeekAPI.Services.Workflow.Providers;
using Microsoft.ML.Tokenizers;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// How many tokens a request is, counted here before it is sent, the way OpenAI counts what it is
/// sent.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why.</b> What a model accepts is a number of tokens in one request
/// (<see cref="OpenAiModelLimits"/>), and until 2026-10-12 nothing counted a request before it was
/// paid for. Sizes were worked out from characters after a run, which is how a merge that added
/// 191,000 characters of evidence was tested for merging correctly and never sized
/// (2026-10-10: a pillar and a blog refused at 141,637 against 128,000).
/// </para>
/// <para>
/// <b>How it was checked.</b> Against the 40 requests of the 08:17 UTC run of 2026-10-07, whose sizes
/// OpenAI reported. The 16 with no reply format, from 245 to 46,078 tokens, came to OpenAI's figure
/// exactly. The 24 with a reply format came to it less a fixed number for that format: 383 for
/// "sections" and 368 for "section" as those two were that day, the same on every request whatever
/// its size.
/// </para>
/// <para>
/// <b>The reply format is counted as its own text, which is not how OpenAI counts it.</b> OpenAI
/// rewrites a format before it counts it and does not say how. So the part of a count that is the
/// format is near and not exact, a few hundred tokens of a request that is tens of thousands; the
/// messages are exact. The run's record carries this count beside OpenAI's own for every request
/// (<see cref="GccRecordingProvider"/>), so the difference is read from a run and never assumed.
/// </para>
/// <para>
/// Every model in <see cref="OpenAiModelLimits"/> counts with the same encoding (o200k_base). Plain
/// code: no network, and the same request gives the same count.
/// </para>
/// </remarks>
public static class GccRequestSize
{
    /// <summary>What each message adds beside its role and its text.</summary>
    private const int TokensPerMessage = 3;

    /// <summary>What the start of the reply adds to every request.</summary>
    private const int TokensForReplyStart = 3;

    private static readonly Lazy<Tokenizer> Encoding = new(() => TiktokenTokenizer.CreateForEncoding("o200k_base"));

    /// <summary>The tokens <paramref name="text"/> is.</summary>
    public static int TokensOf(string? text) =>
        string.IsNullOrEmpty(text) ? 0 : Encoding.Value.CountTokens(text);

    /// <summary>
    /// The tokens <paramref name="request"/> is as sent: its messages and its reply format. Not the
    /// room reserved for its reply, which a caller adds (<see cref="ChatCompletionRequest.MaxOutputTokens"/>).
    /// </summary>
    public static int TokensOf(ChatCompletionRequest request)
    {
        var tokens = TokensForReplyStart;
        foreach (var message in request.Messages)
        {
            tokens += TokensPerMessage + TokensOf(message.RoleString) + TokensOf(message.Content);
        }

        if (request.JsonSchema is { Length: > 0 } schema)
        {
            tokens += TokensOf(schema) + TokensOf(request.JsonSchemaName ?? "response");
        }

        return tokens;
    }
}
