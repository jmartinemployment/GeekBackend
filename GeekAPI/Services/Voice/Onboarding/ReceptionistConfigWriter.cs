using GeekAPI.Services.Receptionist.Relay;
using GeekAPI.Services.Voice.Mcp;
using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using GeekApplication.Results;

namespace GeekAPI.Services.Voice.Onboarding;

/// <summary>One step of /signup's receptionist questions, as the portal's server sends it: the tenant it is for and what was answered.</summary>
public sealed record ReceptionistConfigStepRequest(Guid? TenantId, ReceptionistConfigAnswers? Answers);

/// <summary>
/// Saves what a business answered about its receptionist. Jeff, 2026-11-20: *"Any and/or every setting needs to be included in the
/// onboarding multi-step form. The application may have preset defaults, but I am not comfortable making decisions on behalf of
/// others."* Until this existed there was no route that wrote a whole config at all - only the single-field consent and booking
/// routes, each behind an owner's token - so fourteen of the sixteen settings could not be answered by anyone, ever.
///
/// <para>
/// <b>Two doors onto one merge, because there are two moments a business answers.</b> During signup the caller is the portal's
/// server key and the tenant must still be <see cref="VoiceTenantStatuses.PendingPayment"/>: a server key must not be able to
/// reconfigure a business that is already running, and the gate also fixes the step's place in the flow, after the business step and
/// before PayPal, so a business sees what it is configuring before it is asked to pay for it. Afterwards the caller is the owner
/// themselves, holding their own token for their own tenant, and there is no status gate - refusing them would mean every answer
/// given at signup were permanent.
/// </para>
///
/// <para>
/// Nothing is validated twice. The behavioural rules live in <see cref="IVoiceStore.SaveConfigAsync"/> - a transfer mode and a
/// caller-ID mode this product has, qualifying questions of a shape it can read, a consent mode it answers to - and a refusal comes
/// back with that store's own code. The two shape rules that cannot live there any more are the number and the address, because the
/// columns hold ciphertext: those moved up into <see cref="ReceptionistConfigAnswers.Seal"/>, which is the last place that holds them
/// in the clear. Moved, not copied.
/// </para>
///
/// <para>
/// <b>One rule is enforced here rather than in the store, because the store cannot see it.</b> Booking turned on needs a calendar that
/// works, which is a call to the business's own calendar and not a shape check. <see cref="BookingSettingController"/> refuses it on the
/// dashboard's own switch and says why: a setting saved as on with nothing behind it shows the owner a receptionist that books while
/// every caller is offered a message instead. That route and this one write the same column, so until this check existed the rule held
/// on one of the two paths to it and a config save went straight past it.
/// </para>
///
/// <para>The store is GeekRepository over internal HTTP; this adds no database access to GeekAPI.</para>
/// </summary>
public sealed class ReceptionistConfigWriter(IVoiceStore store, VoiceCredentialProtector protector, CalendarTools calendar)
{
    /// <summary>
    /// Asked for booking with no calendar behind it. Defined here because this is the service both doors onto the setting go through;
    /// <see cref="BookingSettingController"/> reads it from here so the two cannot answer with different codes.
    /// </summary>
    public const string CalendarRequired = "calendar_required";

    /// <summary>A language with no reviewed caller-facing wording. Its own code, because the portal says something specific about it.</summary>
    public const string LanguageNotOffered = "language_not_offered";

    /// <summary>A step of the signup form. Refused once the business is live, whatever the portal sends.</summary>
    public async Task<Result<VoiceReceptionistConfig>> ApplyDuringSignupAsync(
        ReceptionistConfigStepRequest? request, CancellationToken ct = default)
    {
        if (request?.TenantId is not { } tenantId || tenantId == Guid.Empty)
            return Fail("A tenant is required.", VoiceFailureCodes.InvalidRequest);
        if (request.Answers is null || !request.Answers.AnswersAnything)
            return Fail("Nothing was answered.", VoiceFailureCodes.InvalidRequest);
        if (request.Answers.AnswersThePromptTwice)
            return Fail("The prompt was answered twice, as parts and as a whole.", VoiceFailureCodes.InvalidRequest);

        var tenant = await store.GetTenantAsync(tenantId, ct).ConfigureAwait(false);
        if (!tenant.IsSuccess)
            return Fail(tenant.Error ?? "The tenant could not be read.", tenant.ErrorCode);

        if (!string.Equals(tenant.Value!.Status, VoiceTenantStatuses.PendingPayment, StringComparison.Ordinal))
            return Fail(
                "This business is already active; its settings are changed by its owner.", ActivationFailureCodes.TenantNotPending);

        // The tenant is already read for the gate, so the prompt's assembly is handed the row rather than reading it a second time.
        return await MergeAndSaveAsync(tenantId, request.Answers, tenant.Value, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The owner changing their own settings, at any status. The tenant has already been proved theirs by their token's claim
    /// (OwnerTenantClaim), so this does not re-read the tenant row to decide whether they may write: it would be checking a different
    /// thing and reading the row for nothing.
    /// </summary>
    public async Task<Result<VoiceReceptionistConfig>> ApplyForOwnerAsync(
        Guid tenantId, ReceptionistConfigAnswers? answers, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty)
            return Fail("A tenant is required.", VoiceFailureCodes.InvalidRequest);
        if (answers is null || !answers.AnswersAnything)
            return Fail("Nothing was answered.", VoiceFailureCodes.InvalidRequest);
        if (answers.AnswersThePromptTwice)
            return Fail("The prompt was answered twice, as parts and as a whole.", VoiceFailureCodes.InvalidRequest);

        return await MergeAndSaveAsync(tenantId, answers, null, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The business's current settings, the answers written over them, saved whole. Settings that cannot be read abort the save:
    /// merging onto a default would overwrite answers the business had already given with ones it never gave.
    /// </summary>
    /// <param name="tenant">The tenant row when the caller has already read it, so the prompt's assembly does not read it again.</param>
    private async Task<Result<VoiceReceptionistConfig>> MergeAndSaveAsync(
        Guid tenantId, ReceptionistConfigAnswers answers, VoiceTenant? tenant, CancellationToken ct)
    {
        // What the business said about itself becomes its receptionist's prompt here, where its name, state and industry are readable.
        if (answers.Knowledge is not null)
        {
            if (tenant is null)
            {
                var read = await store.GetTenantAsync(tenantId, ct).ConfigureAwait(false);
                if (!read.IsSuccess) return Fail(read.Error ?? "The tenant could not be read.", read.ErrorCode);
                tenant = read.Value!;
            }

            var prompt = ReceptionistDefaults.PromptFrom(tenant, answers.Knowledge);
            if (!prompt.IsSuccess) return Fail(prompt.Error ?? "The prompt could not be written.", prompt.ErrorCode);
            answers = answers with { SystemPromptTemplate = prompt.Value, Knowledge = null };
        }

        // A language is refused here rather than at the repository, because the repository knows the codes and this knows whether the
        // words exist. ReceptionistLanguages holds every sentence a caller hears in a language - two of them reviewed under
        // Fla. Stat. 934.03 - so a code without an entry is a receptionist that could be set to a language it cannot speak, and the
        // disclosure it would then give is the one nobody understood. Jeff, 2026-11-20: "one could claim one didn't understand."
        if (answers.Language is not null && !ReceptionistLanguages.IsOffered(answers.Language))
            return Fail(
                $"The receptionist cannot be set to {answers.Language}. The languages it can speak are: {string.Join(", ", ReceptionistLanguages.Codes)}.",
                LanguageNotOffered);

        // Turning booking on is the one answer that is refused for a reason outside the answer itself, and only in that direction:
        // turning it off is the business withdrawing permission and nothing may stand in the way of it. The check runs the same call a
        // telephone call would, so a connection that exists and no longer works is not a calendar.
        if (answers.BookingEnabled == true)
        {
            var connected = await calendar
                .CallAsync(tenantId, GoogleCalendarOptions.Provider, "list_calendars", null, ct).ConfigureAwait(false);
            if (!connected.IsSuccess)
                return Fail("Connect a calendar that works before the receptionist can book into it.", CalendarRequired);
        }

        // Sealed first, so a number that is not a number or an unusable encryption key stops the write before the settings are even
        // read. Nothing below this line holds a personal detail in the clear.
        var sealedAnswers = answers.Seal(protector);
        if (!sealedAnswers.IsSuccess)
            return Fail(sealedAnswers.Error ?? "The answers could not be stored.", sealedAnswers.ErrorCode);

        var current = await store.GetConfigAsync(tenantId, ct).ConfigureAwait(false);
        if (!current.IsSuccess)
            return Fail(current.Error ?? "The settings could not be read.", current.ErrorCode);

        return await store.SaveConfigAsync(tenantId, sealedAnswers.Value!.AppliedTo(current.Value!), ct).ConfigureAwait(false);
    }

    /// <summary>A failure raised without a code is the store being unreachable as far as a caller can tell, which is what 503 says.</summary>
    private static Result<VoiceReceptionistConfig> Fail(string error, string? code) =>
        Result<VoiceReceptionistConfig>.Failure(error, code ?? VoiceFailureCodes.StoreUnavailable);
}
