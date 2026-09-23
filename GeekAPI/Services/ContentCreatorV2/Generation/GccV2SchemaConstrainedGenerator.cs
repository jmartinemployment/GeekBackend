using GeekAPI.Services.ContentCreatorV2.Write;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using GeekAPI.Services.Workflow.Providers;

namespace GeekAPI.Services.ContentCreatorV2.Generation;

/// <summary>
/// PLACEHOLDER SEAM — Workstream 1 (parallel effort, see plans/master-plan.md) is adding a public
/// JSON-schema-constrained completion method to <c>GccV2CreateLibraryWriter</c>
/// (<c>GeekAPI/Services/Rag/GccV2CreateLibraryWriter.cs</c>), reusing the
/// <c>ContentSectionJsonSchema</c>/<c>JsonSchemaExporter</c> pattern already proven there. As of
/// this reroute, that method had not landed yet (no new commits on <c>GccV2CreateLibraryWriter.cs</c> or
/// <c>GccV2GenerationContracts.cs</c> in the main tree).
///
/// This interface is the seam every Workstream 2 call site should depend on instead of calling
/// <see cref="IContentGenerationProvider.CompleteAsync"/> directly with a hand-built free-text
/// prompt + best-effort JSON parsing via <c>LlmResponseJsonParser</c>. It is NOT a no-op stub: it
/// already uses the <c>JsonSchemaName</c>/<c>JsonSchema</c> fields that exist today on
/// <see cref="ChatCompletionRequest"/> (see <c>GeekAPI/Services/Workflow/Providers/ProviderModels.cs</c>),
/// so schema hints reach the provider now, and it enforces strict deserialization against the
/// requested schema shape.
///
/// TODO(workstream-1-handoff): once <c>GccV2CreateLibraryWriter</c> exposes its real schema-constrained
/// method, delete <see cref="GccV2SchemaConstrainedGenerator"/>'s body and either (a) make it a thin
/// delegator to that method, or (b) repoint the DI registration in
/// <c>GeekAPI/Services/ContentCreatorV2/ServiceRegistration.cs</c> at a new implementation backed by
/// it directly. The call sites (constructor-injected <see cref="IGccV2SchemaConstrainedGenerator"/>)
/// should not need to change shape — only this implementation and its registration.
/// </summary>
public interface IGccV2SchemaConstrainedGenerator
{
    Task<GccV2SchemaConstrainedCompletion<T>> CompleteAsync<T>(
        GccV2SchemaConstrainedRequest request,
        IContentGenerationProvider provider,
        JsonSerializerOptions? deserializeOptions,
        CancellationToken ct) where T : notnull;
}

/// <param name="SystemPrompt">System-role instructions.</param>
/// <param name="UserPrompt">User-role prompt/context.</param>
/// <param name="JsonSchema">
/// Strict JSON schema string the response must conform to — build with
/// <see cref="GccV2AdHocJsonSchema.For{T}"/> for plain DTOs, or reuse
/// <c>ContentSectionJsonSchema.SectionSchema</c>/<c>SectionsArraySchema</c> for
/// <see cref="Workflow.Domain.Entities.Section"/>-shaped results.
/// </param>
/// <param name="SchemaName">Short, stable name surfaced in provider requests/logs/errors.</param>
public sealed record GccV2SchemaConstrainedRequest(
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

public sealed record GccV2SchemaConstrainedCompletion<T>(
    T Value,
    string ModelUsed,
    int? PromptTokens,
    int? CompletionTokens) where T : notnull;

public sealed class GccV2SchemaConstrainedGenerator : IGccV2SchemaConstrainedGenerator
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

    public async Task<GccV2SchemaConstrainedCompletion<T>> CompleteAsync<T>(
        GccV2SchemaConstrainedRequest request,
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

        return new GccV2SchemaConstrainedCompletion<T>(
            value,
            string.IsNullOrWhiteSpace(result.ModelUsed) ? request.Model ?? "" : result.ModelUsed,
            result.PromptTokens,
            result.CompletionTokens);
    }
}
