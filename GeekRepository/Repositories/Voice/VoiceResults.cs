using GeekApplication.Models.Voice;
using GeekApplication.Results;

namespace GeekRepository.Repositories.Voice;

/// <summary>The refusals both Geek-Receptionist repositories answer with, written once so they carry the same codes.</summary>
internal static class VoiceResults
{
    public static Result<T> Invalid<T>(string message) => Result<T>.Failure(message, VoiceFailureCodes.InvalidRequest);

    public static Result<T> NotFound<T>(string message) => Result<T>.Failure(message, VoiceFailureCodes.NotFound);
}
