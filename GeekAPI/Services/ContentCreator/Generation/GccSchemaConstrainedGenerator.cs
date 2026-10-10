using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using GeekAPI.Services.Workflow.Providers;

namespace GeekAPI.Services.ContentCreator.Generation;

/// <summary>
/// A completion held to a JSON schema. Call sites depend on this instead of calling
/// <see cref="IContentGenerationProvider.CompleteAsync"/> with a hand-built prompt and parsing the
/// reply best-effort: the schema's name and body go to the provider on
/// <see cref="ChatCompletionRequest"/>, and the reply is deserialized strictly against the requested
/// shape.
/// </summary>
public interface IGccSchemaConstrainedGenerator
{
    Task<GccSchemaConstrainedCompletion<T>> CompleteAsync<T>(
        GccSchemaConstrainedRequest request,
        IContentGenerationProvider provider,
        JsonSerializerOptions? deserializeOptions,
        CancellationToken ct) where T : notnull;
}

/// <param name="SystemPrompt">System-role instructions.</param>
/// <param name="UserPrompt">User-role prompt/context.</param>
/// <param name="JsonSchema">
/// Strict JSON schema string the response must conform to — build with
/// <see cref="GccAdHocJsonSchema.For{T}"/> for plain DTOs, or reuse
/// <c>ContentSectionJsonSchema.SectionSchema</c>/<c>SectionsArraySchema</c> for
/// <see cref="Workflow.Domain.Entities.Section"/>-shaped results.
/// </param>
/// <param name="SchemaName">Short, stable name surfaced in provider requests/logs/errors.</param>
public sealed record GccSchemaConstrainedRequest(
    string SystemPrompt,
    string UserPrompt,
    string JsonSchema,
    string SchemaName,
    string? Model = null,
    double Temperature = 0.4,
    int MaxOutputTokens = 4096,
    /// <summary>Extraction by default: every caller of this shape is pulling structured JSON out of
    /// a crawled page, which is the volume worth moving to a cheaper model.</summary>
    LlmTaskClass TaskClass = LlmTaskClass.Extraction);

public sealed record GccSchemaConstrainedCompletion<T>(
    T Value,
    string ModelUsed,
    int? PromptTokens,
    int? CompletionTokens) where T : notnull;

public sealed class GccSchemaConstrainedGenerator : IGccSchemaConstrainedGenerator
{
    private static readonly JsonSerializerOptions DefaultOptions =
        new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
            // Explicit resolver required: JsonSchemaExporter marks the options read-only, and
            // reflection-based resolution is not picked up implicitly - without this every call
            // throws "must specify a TypeInfoResolver setting before being marked as read-only".
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };

    public async Task<GccSchemaConstrainedCompletion<T>> CompleteAsync<T>(
        GccSchemaConstrainedRequest request,
        IContentGenerationProvider provider,
        JsonSerializerOptions? deserializeOptions,
        CancellationToken ct) where T : notnull
    {
        var chatRequest = new ChatCompletionRequest(
            Messages:
            [
                new ChatMessage(ChatRole.System, request.SystemPrompt),
                new ChatMessage(ChatRole.User, request.UserPrompt),
            ],
            Temperature: request.Temperature,
            MaxOutputTokens: request.MaxOutputTokens,
            Model: request.Model,
            JsonSchemaName: request.SchemaName,
            JsonSchema: request.JsonSchema,
            TaskClass: request.TaskClass);

        var result = await provider.CompleteAsync(chatRequest, ct).ConfigureAwait(false);
        var content = result.Content?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ContentGenerationException(
                $"{request.SchemaName}: schema-constrained completion returned empty content.");
        }

        T value;
        try
        {
            value = JsonSerializer.Deserialize<T>(content, deserializeOptions ?? DefaultOptions)
                    ?? throw new ContentGenerationException(
                        $"{request.SchemaName}: schema-constrained completion deserialized to null.");
        }
        catch (JsonException ex)
        {
            throw new ContentGenerationException(
                $"{request.SchemaName}: provider response failed strict schema deserialization "
                + "even though a JsonSchema was supplied on the request.", ex);
        }

        return new GccSchemaConstrainedCompletion<T>(
            value,
            string.IsNullOrWhiteSpace(result.ModelUsed) ? request.Model ?? "" : result.ModelUsed,
            result.PromptTokens,
            result.CompletionTokens);
    }
}
