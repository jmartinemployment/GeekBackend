using GeekApplication.Models.ContentCreator;

namespace GeekAPI.HttpClients;

/// <summary>
/// Reads a project, for the sake of what a project owns: the partner and competitor URLs that
/// grounding is resolved from.
/// </summary>
/// <remarks>
/// Deliberately narrow. <see cref="HttpGccRepository"/> has 43 public methods and mirroring them
/// would make every consumer depend on all of them; a consumer that needs one project needs one
/// method. This exists so <c>GccGroundingResolver</c>'s refusal paths can be asserted without an
/// HTTP client — a gap that was real while the concrete class was the only option.
/// </remarks>
public interface IGccProjectReader
{
    Task<GccProjectDto?> GetProjectAsync(Guid id, CancellationToken ct = default);
}
