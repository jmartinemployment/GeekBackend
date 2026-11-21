using GeekAPI.Controllers.Voice;
using GeekAPI.Services.Voice;
using GeekAPI.Services.Voice.Onboarding;
using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using GeekApplication.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Time.Testing;

namespace GeekBackend.Tests.Voice;

/// <summary>
/// The three rules of the self-service gate, in the plan's order, and what each leaves behind: a refused signup creates nothing,
/// and an accepted one is exactly one pending tenant carrying the attestation and what the business does.
/// </summary>
public sealed class OnboardingEligibilityTests
{
    private static readonly Guid NewTenant = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 15, 0, 0, TimeSpan.Zero);

    private sealed class RecordingStore(Func<Result<VoiceTenant>>? create = null) : IVoiceStore
    {
        public List<CreateVoiceTenantCommand> Created { get; } = new();

        public Task<Result<VoiceTenant>> CreateTenantAsync(CreateVoiceTenantCommand command, CancellationToken ct = default)
        {
            Created.Add(command);
            return Task.FromResult(create is not null ? create() : Result<VoiceTenant>.Success(Tenant(command)));
        }

        private static VoiceTenant Tenant(CreateVoiceTenantCommand c) => new(NewTenant, c.BusinessName, c.IndustryVertical,
            c.ComplianceAttestedAt, c.CountryCode, c.StateCode, c.ContactEmailEncrypted, c.ContactPhoneEncrypted,
            VoiceTenantStatuses.PendingPayment, Now);

        public Task<Result<VoiceTenant>> GetTenantAsync(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceTenant>> ActivateTenantAsync(Guid tenantId, ActivateVoiceTenantCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceTelephony>> GetTelephonyAsync(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceReceptionistConfig>> GetConfigAsync(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceReceptionistConfig>> SaveConfigAsync(Guid tenantId, VoiceReceptionistConfig config, CancellationToken ct = default) => throw new NotSupportedException();
        // Bring-your-own-storage is not what this test is about; a fake that is asked says there is none.
        public Task<Result<VoiceStorageConnection>> GetStorageConnectionAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(Result<VoiceStorageConnection>.Failure("No storage connection.", VoiceFailureCodes.NotFound));

        public Task<Result<VoiceStorageConnection>> SaveStorageConnectionAsync(Guid tenantId, SaveVoiceStorageConnectionCommand command, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Result<VoiceMcpConnection>> GetMcpConnectionAsync(Guid tenantId, string provider, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceMcpConnection>> SaveMcpConnectionAsync(Guid tenantId, string provider, SaveVoiceMcpConnectionCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceCallStored>> UpsertCallAsync(Guid tenantId, UpsertVoiceCallCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceAppointmentCreated>> CreateAppointmentAsync(Guid tenantId, CreateVoiceAppointmentCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceConnectionRemoved>> DisconnectMcpAsync(Guid tenantId, string provider, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceConnectionRemoved>> DisconnectStorageAsync(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static ValidateEligibilityRequest Signup(
        string? country = "US", bool? attested = true, string? yesTo = null) =>
        new("Boca Cool HVAC", "Heating and air conditioning", country, "FL", "owner@bocacool.example", "+15615550101", attested, Answers(yesTo),
            "Pat Rivera", "Owner", "12 Palm Way, Boca Raton, FL 33432",
            SignupStatements.Declaration, true, SignupStatements.Terms, true,
            "9 Ocean Blvd, Delray Beach, FL 33444", "(561) 555-0199", "pat@example.test");

    /// <summary>The eight questions as the form sends them, each answered no, except the one industry a test says yes to.</summary>
    private static List<IndustryAnswerInput> Answers(string? yesTo = null) =>
        ExcludedIndustries.All.Select(i => new IndustryAnswerInput(i.Code, i.Question, i.Code == yesTo)).ToList();

    private static readonly VisitorOrigin FromUs = new("203.0.113.9", "US");

    /// <summary>
    /// A real protector with a real key: the business's contact address and number are sealed before the tenant is created, so a gate
    /// built without one would test a signup this product cannot perform.
    /// </summary>
    private static readonly string Key = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

    private static readonly VoiceCredentialProtector Protector = new(() => Key);

    private static OnboardingEligibility Gate(RecordingStore store, RecordingSignupAnswerStore? refusals = null) =>
        new(store, refusals ?? new RecordingSignupAnswerStore(), new FakeTimeProvider(Now), Protector);

    /// <summary>The number as it was stored, which is ciphertext, read back the only way there is.</summary>
    private static string StoredPhone(CreateVoiceTenantCommand created) =>
        Protector.Unprotect(created.ContactPhoneEncrypted, VoiceSecretPurposes.TenantContactPhone).Value!;

    private static string StoredEmail(CreateVoiceTenantCommand created) =>
        Protector.Unprotect(created.ContactEmailEncrypted, VoiceSecretPurposes.TenantContactEmail).Value!;

    [Fact]
    public async Task A_passing_signup_becomes_one_pending_tenant_with_its_attestation()
    {
        var store = new RecordingStore();

        var result = await Gate(store).ValidateAsync(Signup(), FromUs);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(new EligibilityAccepted(NewTenant, VoiceTenantStatuses.PendingPayment), result.Value);
        var created = Assert.Single(store.Created);
        Assert.Equal(Now, created.ComplianceAttestedAt);
        Assert.Equal("US", created.CountryCode);
        Assert.Equal("+15615550101", StoredPhone(created));
        Assert.Equal("Heating and air conditioning", created.IndustryVertical);
    }

    [Fact]
    public async Task An_accepted_signup_stores_its_no_to_every_excluded_industry_with_the_visitors_address_and_the_time()
    {
        var store = new RecordingStore();
        var refusals = new RecordingSignupAnswerStore();

        var result = await Gate(store, refusals).ValidateAsync(Signup(), FromUs);

        Assert.True(result.IsSuccess, result.Error);
        var kept = Assert.Single(store.Created).IndustryAnswers;
        Assert.Equal(ExcludedIndustries.All.Select(i => new VoiceIndustryAnswer(i.Code, i.Question, false)), kept.Answers);
        Assert.Equal(Now, kept.AnsweredAt);
        Assert.Equal("203.0.113.9", kept.IpAddress);
        Assert.Equal("US", kept.VisitorCountry);
        Assert.Empty(refusals.Refusals);
    }

    [Fact]
    public async Task What_the_business_does_is_stored_as_typed_without_the_spaces_around_it()
    {
        var store = new RecordingStore();

        var result = await Gate(store).ValidateAsync(Signup() with { BusinessDescription = "  AI for accounting firms " }, FromUs);

        Assert.True(result.IsSuccess, result.Error);
        var created = Assert.Single(store.Created);
        Assert.Equal("AI for accounting firms", created.IndustryVertical);
    }

    [Fact]
    public async Task The_phone_is_stored_as_plus_one_and_ten_digits_however_it_was_typed()
    {
        var store = new RecordingStore();

        var result = await Gate(store).ValidateAsync(Signup() with { ContactPhone = "(561) 555-0101" }, FromUs);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("+15615550101", StoredPhone(Assert.Single(store.Created)));
    }

    [Theory]
    [InlineData("+44 20 7946 0958")]
    [InlineData("555-0101")]
    [InlineData("")]
    [InlineData(null)]
    public async Task A_phone_that_is_not_a_us_number_is_refused_and_creates_nothing(string? phone)
    {
        var store = new RecordingStore();

        var result = await Gate(store).ValidateAsync(Signup() with { ContactPhone = phone }, FromUs);

        Assert.Equal(OnboardingFailureCodes.ContactPhoneNotUs, result.ErrorCode);
        Assert.Empty(store.Created);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task A_signup_that_does_not_say_what_the_business_does_is_refused_and_creates_nothing(string? description)
    {
        var store = new RecordingStore();

        var result = await Gate(store).ValidateAsync(Signup() with { BusinessDescription = description }, FromUs);

        Assert.Equal(OnboardingFailureCodes.BusinessDescriptionRequired, result.ErrorCode);
        Assert.Empty(store.Created);
    }

    public static TheoryData<string, Func<List<IndustryAnswerInput>?>> AnswersThatAreNotTheEightQuestions() => new()
    {
        { "none sent", () => null },
        { "an empty list", () => [] },
        { "one question left out", () => Answers().Skip(1).ToList() },
        { "one answer left blank", () => Answers().Select((a, i) => i == 2 ? a with { Answer = null } : a).ToList() },
        { "an industry nobody asked about", () => Answers().Append(new IndustryAnswerInput("astrology", "Is the business an astrology business?", false)).ToList() },
        { "a question answered twice", () => Answers().Append(Answers()[0]).ToList() },
        { "the question reworded", () => Answers().Select((a, i) => i == 0 ? a with { Question = "HIPAA?" } : a).ToList() },
        { "the retired single HIPAA question", () => Answers().Skip(4).Prepend(new IndustryAnswerInput("hipaa", "Is the business a HIPAA covered entity or business associate, such as a healthcare provider, health plan or a business that handles patient health information for one?", false)).ToList() },
    };

    [Theory]
    [MemberData(nameof(AnswersThatAreNotTheEightQuestions))]
    public async Task Anything_but_an_answer_to_each_of_the_eight_questions_as_worded_is_refused_and_stores_nothing(string why, Func<List<IndustryAnswerInput>?> answers)
    {
        var store = new RecordingStore();
        var refusals = new RecordingSignupAnswerStore();

        var result = await Gate(store, refusals).ValidateAsync(Signup() with { IndustryAnswers = answers() }, FromUs);

        Assert.True(OnboardingFailureCodes.IndustryNotAnswered == result.ErrorCode, why);
        Assert.Empty(store.Created);
        Assert.Empty(refusals.Refusals);
    }

    [Theory]
    [InlineData("CA")]
    [InlineData("us")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Only_the_exact_country_US_passes(string? country)
    {
        var store = new RecordingStore();

        var result = await Gate(store).ValidateAsync(Signup(country: country), FromUs);

        Assert.Equal(OnboardingFailureCodes.CountryNotSupported, result.ErrorCode);
        Assert.Empty(store.Created);
    }

    [Theory]
    [InlineData("hipaa_healthcare_provider")]
    [InlineData("hipaa_health_plan")]
    [InlineData("hipaa_clearinghouse")]
    [InlineData("hipaa_business_associate")]
    [InlineData("securities_broker_dealer")]
    [InlineData("investment_adviser")]
    [InlineData("commodity_futures_derivatives")]
    [InlineData("medicare_tpmo")]
    public async Task A_yes_to_any_one_excluded_industry_is_refused_and_the_yes_is_stored(string industry)
    {
        var store = new RecordingStore();
        var refusals = new RecordingSignupAnswerStore();

        var result = await Gate(store, refusals).ValidateAsync(Signup(yesTo: industry), FromUs);

        Assert.Equal(OnboardingFailureCodes.IndustryExcluded, result.ErrorCode);
        Assert.Equal(ExcludedIndustries.Refusal, result.Error);
        Assert.Empty(store.Created);
        var kept = Assert.Single(refusals.Refusals);
        Assert.Equal(industry, Assert.Single(kept.Answers, a => a.Answer).Industry);
        Assert.Equal(8, kept.Answers.Count);
        Assert.Equal(Now, kept.AnsweredAt);
        Assert.Equal("203.0.113.9", kept.IpAddress);
    }

    [Fact]
    public async Task A_yes_that_cannot_be_stored_is_still_refused_and_says_the_store_failed()
    {
        var store = new RecordingStore();
        var refusals = new RecordingSignupAnswerStore(() => Result<VoiceIndustryAnswersRecorded>.Failure("the store is down", VoiceFailureCodes.StoreUnavailable));

        var result = await Gate(store, refusals).ValidateAsync(Signup(yesTo: "medicare_tpmo"), FromUs);

        Assert.False(result.IsSuccess);
        Assert.Equal(VoiceFailureCodes.StoreUnavailable, result.ErrorCode);
        Assert.Empty(store.Created);
    }

    [Fact]
    public void The_excluded_industries_are_these_eight_questions_each_with_its_own_code()
    {
        string[] codes =
        [
            "hipaa_healthcare_provider", "hipaa_health_plan", "hipaa_clearinghouse", "hipaa_business_associate",
            "securities_broker_dealer", "investment_adviser", "commodity_futures_derivatives", "medicare_tpmo",
        ];

        Assert.Equal(codes, ExcludedIndustries.All.Select(i => i.Code));
        Assert.Equal(IndustryAnswersFixture.Questions.Select(q => q.Question), ExcludedIndustries.All.Select(i => i.Question));
        Assert.All(ExcludedIndustries.All, i => Assert.EndsWith("?", i.Question, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null, "US")]
    [InlineData("203.0.113.9", null)]
    [InlineData("", "US")]
    [InlineData("203.0.113.9", " ")]
    public async Task A_visitor_whose_address_or_country_is_missing_is_refused_as_unverified(string? ip, string? country)
    {
        var store = new RecordingStore();

        var result = await Gate(store).ValidateAsync(Signup(), new VisitorOrigin(ip, country));

        Assert.Equal(OnboardingFailureCodes.VisitorUnverified, result.ErrorCode);
        Assert.Empty(store.Created);
    }

    [Theory]
    [InlineData("CA")]
    [InlineData("MX")]
    [InlineData("GB")]
    public async Task A_visitor_whose_address_is_outside_the_US_is_refused_even_when_the_form_says_US(string country)
    {
        var store = new RecordingStore();

        var result = await Gate(store).ValidateAsync(Signup(country: "US"), new VisitorOrigin("198.51.100.7", country));

        Assert.Equal(OnboardingFailureCodes.VisitorCountryNotSupported, result.ErrorCode);
        Assert.Empty(store.Created);
    }

    [Fact]
    public async Task The_visitor_country_is_read_without_regard_to_case()
    {
        var store = new RecordingStore();

        var result = await Gate(store).ValidateAsync(Signup(), new VisitorOrigin("203.0.113.9", "us"));

        Assert.True(result.IsSuccess, result.Error);
    }

    [Fact]
    public async Task The_declared_country_is_checked_before_the_visitors()
    {
        var store = new RecordingStore();

        var result = await Gate(store).ValidateAsync(Signup(country: "CA"), new VisitorOrigin(null, null));

        Assert.Equal(OnboardingFailureCodes.CountryNotSupported, result.ErrorCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public async Task Anything_but_an_accepted_attestation_is_refused(bool? attested)
    {
        var store = new RecordingStore();

        var result = await Gate(store).ValidateAsync(Signup(attested: attested), FromUs);

        Assert.Equal(OnboardingFailureCodes.AttestationRequired, result.ErrorCode);
        Assert.Empty(store.Created);
    }

    [Fact]
    public async Task An_accepted_signup_stores_who_declared_what_they_agreed_to_with_the_name_title_and_address_sealed()
    {
        var store = new RecordingStore();

        var result = await Gate(store).ValidateAsync(Signup(), FromUs);

        Assert.True(result.IsSuccess, result.Error);
        var kept = Assert.Single(store.Created).Attestation;
        Assert.Equal("Pat Rivera", Protector.Unprotect(kept.AttestorNameEncrypted, VoiceSecretPurposes.SignupAttestorName).Value);
        Assert.Equal("Owner", Protector.Unprotect(kept.AttestorTitleEncrypted, VoiceSecretPurposes.SignupAttestorTitle).Value);
        Assert.Equal("12 Palm Way, Boca Raton, FL 33432", Protector.Unprotect(kept.BusinessAddressEncrypted, VoiceSecretPurposes.SignupBusinessAddress).Value);
        Assert.Equal("9 Ocean Blvd, Delray Beach, FL 33444", Protector.Unprotect(kept.DeclarantAddressEncrypted, VoiceSecretPurposes.SignupDeclarantAddress).Value);
        Assert.Equal("+15615550199", Protector.Unprotect(kept.DeclarantPhoneEncrypted, VoiceSecretPurposes.SignupDeclarantPhone).Value);
        Assert.Equal("pat@example.test", Protector.Unprotect(kept.DeclarantEmailEncrypted, VoiceSecretPurposes.SignupDeclarantEmail).Value);
        Assert.DoesNotContain("pat@example.test", kept.DeclarantEmailEncrypted);
        Assert.DoesNotContain("Pat Rivera", kept.AttestorNameEncrypted);
        Assert.DoesNotContain("5615550199", kept.DeclarantPhoneEncrypted);
        Assert.Equal(SignupStatements.Declaration, kept.DeclarationStatement);
        Assert.Equal(SignupStatements.Terms, kept.TermsStatement);
        Assert.Equal(Now, kept.AttestedAt);
        Assert.Equal("203.0.113.9", kept.IpAddress);
        Assert.Equal("US", kept.VisitorCountry);
    }

    [Theory]
    [InlineData(null, "Owner")]
    [InlineData("", "Owner")]
    [InlineData("   ", "Owner")]
    [InlineData("Pat Rivera", null)]
    [InlineData("Pat Rivera", " ")]
    public async Task A_signup_with_no_named_person_is_refused_and_creates_nothing(string? name, string? title)
    {
        var store = new RecordingStore();

        var result = await Gate(store).ValidateAsync(Signup() with { AttestorName = name, AttestorTitle = title }, FromUs);

        Assert.Equal(OnboardingFailureCodes.AttestorRequired, result.ErrorCode);
        Assert.Empty(store.Created);
    }

    [Fact]
    public async Task A_signup_whose_name_is_longer_than_the_limit_is_refused_and_not_cut_short()
    {
        var store = new RecordingStore();

        var result = await Gate(store).ValidateAsync(Signup() with { AttestorName = new string('a', SignupStatements.MaxFieldLength + 1) }, FromUs);

        Assert.Equal(OnboardingFailureCodes.AttestorRequired, result.ErrorCode);
        Assert.Empty(store.Created);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task A_signup_with_no_business_address_is_refused_and_creates_nothing(string? address)
    {
        var store = new RecordingStore();

        var result = await Gate(store).ValidateAsync(Signup() with { BusinessAddress = address }, FromUs);

        Assert.Equal(OnboardingFailureCodes.BusinessAddressRequired, result.ErrorCode);
        Assert.Empty(store.Created);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task A_signup_with_no_declarant_address_is_refused_and_creates_nothing(string? address)
    {
        var store = new RecordingStore();

        var result = await Gate(store).ValidateAsync(Signup() with { DeclarantAddress = address }, FromUs);

        Assert.Equal(OnboardingFailureCodes.DeclarantAddressRequired, result.ErrorCode);
        Assert.Empty(store.Created);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a number")]
    [InlineData("+442071838750")]
    public async Task A_signup_whose_declarant_telephone_is_not_a_us_number_is_refused_and_creates_nothing(string? phone)
    {
        var store = new RecordingStore();

        var result = await Gate(store).ValidateAsync(Signup() with { DeclarantPhone = phone }, FromUs);

        Assert.Equal(OnboardingFailureCodes.DeclarantPhoneNotUs, result.ErrorCode);
        Assert.Empty(store.Created);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-at-sign")]
    [InlineData("Pat <pat@example.test>")]
    public async Task A_signup_whose_declarant_email_is_not_an_address_is_refused_and_creates_nothing(string? email)
    {
        var store = new RecordingStore();

        var result = await Gate(store).ValidateAsync(Signup() with { DeclarantEmail = email }, FromUs);

        Assert.Equal(OnboardingFailureCodes.DeclarantEmailInvalid, result.ErrorCode);
        Assert.Empty(store.Created);
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(null, null)]
    [InlineData(true, "I declare something else.")]
    [InlineData(true, null)]
    public async Task A_declaration_that_was_not_agreed_to_in_the_words_shown_is_refused(bool? agreed, string? words)
    {
        var store = new RecordingStore();

        var result = await Gate(store).ValidateAsync(
            Signup() with { DeclarationAgreed = agreed, DeclarationStatement = words ?? (agreed == true ? null : SignupStatements.Declaration) }, FromUs);

        Assert.Equal(OnboardingFailureCodes.DeclarationRequired, result.ErrorCode);
        Assert.Empty(store.Created);
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(null, null)]
    [InlineData(true, "I agree to something else.")]
    [InlineData(true, null)]
    public async Task Terms_that_were_not_agreed_to_in_the_words_shown_are_refused(bool? agreed, string? words)
    {
        var store = new RecordingStore();

        var result = await Gate(store).ValidateAsync(
            Signup() with { TermsAgreed = agreed, TermsStatement = words ?? (agreed == true ? null : SignupStatements.Terms) }, FromUs);

        Assert.Equal(OnboardingFailureCodes.TermsRequired, result.ErrorCode);
        Assert.Empty(store.Created);
    }

    [Fact]
    public void The_terms_the_person_agrees_to_say_they_are_subject_to_the_connections_being_verified()
    {
        Assert.Contains("subject to", SignupStatements.Terms);
        Assert.Contains("No payment is taken before that connection is verified", SignupStatements.Terms);
    }

    [Fact]
    public async Task The_rules_answer_in_the_plans_order()
    {
        var store = new RecordingStore();

        // Wrong country, an excluded industry and no attestation at once: the country rule speaks first.
        var first = await Gate(store).ValidateAsync(Signup(country: "CA", attested: false, yesTo: "hipaa_clearinghouse"), FromUs);
        // An excluded industry and no attestation: the industry rule speaks before the attestation rule.
        var second = await Gate(store).ValidateAsync(Signup(attested: false, yesTo: "hipaa_clearinghouse"), FromUs);

        Assert.Equal(OnboardingFailureCodes.CountryNotSupported, first.ErrorCode);
        Assert.Equal(OnboardingFailureCodes.IndustryExcluded, second.ErrorCode);
        Assert.Empty(store.Created);
    }

    [Theory]
    [InlineData(VoiceFailureCodes.InvalidRequest)]
    [InlineData(VoiceFailureCodes.StoreUnavailable)]
    public async Task A_refusal_from_the_store_is_a_refusal_and_never_an_accepted_signup(string code)
    {
        var store = new RecordingStore(() => Result<VoiceTenant>.Failure("the store said no", code));

        var result = await Gate(store).ValidateAsync(Signup(), FromUs);

        Assert.False(result.IsSuccess);
        Assert.Equal(code, result.ErrorCode);
    }

    [Theory]
    [InlineData(OnboardingFailureCodes.CountryNotSupported, 400)]
    [InlineData(OnboardingFailureCodes.BusinessDescriptionRequired, 400)]
    [InlineData(OnboardingFailureCodes.IndustryNotAnswered, 400)]
    [InlineData(OnboardingFailureCodes.ContactPhoneNotUs, 400)]
    [InlineData(OnboardingFailureCodes.IndustryExcluded, 400)]
    [InlineData(OnboardingFailureCodes.AttestationRequired, 400)]
    [InlineData(OnboardingFailureCodes.AttestorRequired, 400)]
    [InlineData(OnboardingFailureCodes.BusinessAddressRequired, 400)]
    [InlineData(OnboardingFailureCodes.DeclarantAddressRequired, 400)]
    [InlineData(OnboardingFailureCodes.DeclarantPhoneNotUs, 400)]
    [InlineData(OnboardingFailureCodes.DeclarantEmailInvalid, 400)]
    [InlineData(OnboardingFailureCodes.DeclarationRequired, 400)]
    [InlineData(OnboardingFailureCodes.TermsRequired, 400)]
    [InlineData(OnboardingFailureCodes.VisitorUnverified, 400)]
    [InlineData(OnboardingFailureCodes.VisitorCountryNotSupported, 400)]
    [InlineData(VoiceFailureCodes.InvalidRequest, 400)]
    [InlineData(VoiceFailureCodes.NotFound, 404)]
    [InlineData(VoiceFailureCodes.Conflict, 409)]
    [InlineData(VoiceFailureCodes.StoreUnavailable, 503)]
    [InlineData("a code nobody has heard of", 503)]
    [InlineData(null, 503)]
    public void The_status_comes_from_the_failure_code(string? code, int status) =>
        Assert.Equal(status, OnboardingController.StatusFor(code));
}

/// <summary>
/// The caps on the public signup routes and on owner sign-in: ten per address per hour, one bucket per address (an IPv6 address counts as
/// its /64), no queue, a 429 with a code, and sign-in counted apart from signup.
/// </summary>
public sealed class OnboardingRateLimitTests
{
    private static DefaultHttpContext From(string? address)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = address is null ? null : System.Net.IPAddress.Parse(address);
        return context;
    }

    private static bool Acquire(System.Threading.RateLimiting.PartitionedRateLimiter<HttpContext> limiter, string? address) =>
        limiter.AttemptAcquire(From(address)).IsAcquired;

    private static System.Threading.RateLimiting.PartitionedRateLimiter<HttpContext> Limiter() =>
        System.Threading.RateLimiting.PartitionedRateLimiter.Create<HttpContext, string>(OnboardingRateLimit.Partition);

    [Fact]
    public void An_address_gets_ten_calls_and_the_eleventh_is_refused()
    {
        using var limiter = Limiter();

        for (var i = 0; i < OnboardingRateLimit.PermitsPerWindow; i++)
            Assert.True(Acquire(limiter, "203.0.113.9"), $"call {i + 1}");

        Assert.False(Acquire(limiter, "203.0.113.9"));
    }

    [Fact]
    public void One_address_being_refused_does_not_refuse_another()
    {
        using var limiter = Limiter();
        for (var i = 0; i < OnboardingRateLimit.PermitsPerWindow; i++) Acquire(limiter, "203.0.113.9");

        Assert.False(Acquire(limiter, "203.0.113.9"));
        Assert.True(Acquire(limiter, "203.0.113.10"));
    }

    [Fact]
    public void Calls_with_no_readable_address_share_one_bucket_and_are_not_unlimited()
    {
        using var limiter = Limiter();
        for (var i = 0; i < OnboardingRateLimit.PermitsPerWindow; i++)
            Assert.True(Acquire(limiter, null));

        Assert.False(Acquire(limiter, null));
    }

    [Fact]
    public async Task A_refusal_is_a_429_with_the_rate_limited_code()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await OnboardingRateLimit.WriteRejection(context, CancellationToken.None);

        Assert.Equal(429, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var json = System.Text.Json.JsonDocument.Parse(context.Response.Body);
        Assert.Equal(OnboardingRateLimit.Code, json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public void Every_address_in_one_IPv6_64_block_shares_one_bucket_and_the_next_block_has_its_own()
    {
        using var limiter = Limiter();
        for (var i = 0; i < OnboardingRateLimit.PermitsPerWindow; i++)
            Assert.True(Acquire(limiter, $"2001:db8:1:2::{i + 1:x}"), $"call {i + 1}");

        Assert.False(Acquire(limiter, "2001:db8:1:2:ffff:ffff:ffff:fffe"));
        Assert.True(Acquire(limiter, "2001:db8:1:3::1"));
    }

    [Fact]
    public void An_IPv4_address_written_in_IPv6_form_counts_as_the_IPv4_address() =>
        Assert.Equal(OnboardingRateLimit.AddressKey(From("203.0.113.9")), OnboardingRateLimit.AddressKey(From("::ffff:203.0.113.9")));

    [Fact]
    public void Signing_in_has_its_own_allowance_that_signup_calls_do_not_use_up()
    {
        var attribute = typeof(OwnerAuthController).GetMethod(nameof(OwnerAuthController.SignIn), [typeof(GeekAPI.Services.Voice.OwnerAuth.OwnerSignInRequest), typeof(CancellationToken)])!
            .GetCustomAttributes(typeof(Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute), inherit: false)
            .Cast<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>().Single();
        Assert.Equal(OnboardingRateLimit.SignInPolicyName, attribute.PolicyName);
        Assert.NotEqual(OnboardingRateLimit.PolicyName, OnboardingRateLimit.SignInPolicyName);

        using var signup = Limiter();
        using var signIn = System.Threading.RateLimiting.PartitionedRateLimiter.Create<HttpContext, string>(OnboardingRateLimit.SignInPartition);
        for (var i = 0; i < OnboardingRateLimit.PermitsPerWindow; i++) Acquire(signup, "203.0.113.9");

        Assert.False(Acquire(signup, "203.0.113.9"));
        Assert.True(Acquire(signIn, "203.0.113.9"));
    }

    [Fact]
    public void The_onboarding_controller_carries_the_cap()
    {
        var attribute = typeof(OnboardingController).GetCustomAttributes(typeof(Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute), inherit: false)
            .Cast<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>().Single();

        Assert.Equal(OnboardingRateLimit.PolicyName, attribute.PolicyName);
    }
}


/// <summary>The visitor's address and country are believed only from a call whose API key was verified, and are read as sent.</summary>
public sealed class OnboardingVisitorTests
{
    private static DefaultHttpContext Call(bool verified, string? ip, string? country)
    {
        var context = new DefaultHttpContext();
        if (verified) context.Items[GeekAPI.Auth.CrmAuthConstants.VerifiedApiKeyItem] = true;
        if (ip is not null) context.Request.Headers[OnboardingVisitor.IpHeader] = ip;
        if (country is not null) context.Request.Headers[OnboardingVisitor.CountryHeader] = country;
        return context;
    }

    [Fact]
    public void A_verified_call_carries_the_forwarded_address_and_country()
    {
        var visitor = OnboardingVisitor.From(Call(true, " 203.0.113.9 ", "US"));

        Assert.Equal(new VisitorOrigin("203.0.113.9", "US"), visitor);
    }

    [Fact]
    public void A_call_without_a_verified_key_is_believed_about_nothing()
    {
        var visitor = OnboardingVisitor.From(Call(false, "203.0.113.9", "US"));

        Assert.Equal(new VisitorOrigin(null, null), visitor);
    }

    [Fact]
    public void An_IPv4_address_sent_in_IPv6_form_is_recorded_as_IPv4()
    {
        Assert.Equal("203.0.113.9", OnboardingVisitor.From(Call(true, "::ffff:203.0.113.9", "US")).IpAddress);
    }

    [Theory]
    [InlineData("not an address")]
    [InlineData("")]
    [InlineData(null)]
    public void An_address_that_is_not_an_IP_address_reads_as_absent(string? ip) =>
        Assert.Null(OnboardingVisitor.From(Call(true, ip, "US")).IpAddress);

    [Fact]
    public void A_missing_country_reads_as_absent() =>
        Assert.Null(OnboardingVisitor.From(Call(true, "203.0.113.9", null)).CountryCode);

    [Fact]
    public void The_cap_counts_each_visitor_address_apart_even_though_every_call_comes_from_the_portal()
    {
        using var limiter = System.Threading.RateLimiting.PartitionedRateLimiter.Create<HttpContext, string>(OnboardingRateLimit.Partition);
        var portal = System.Net.IPAddress.Parse("76.76.21.21");
        HttpContext From(string visitorIp)
        {
            var context = Call(true, visitorIp, "US");
            context.Connection.RemoteIpAddress = portal;
            return context;
        }

        for (var i = 0; i < OnboardingRateLimit.PermitsPerWindow; i++)
            Assert.True(limiter.AttemptAcquire(From("203.0.113.9")).IsAcquired);

        Assert.False(limiter.AttemptAcquire(From("203.0.113.9")).IsAcquired);
        Assert.True(limiter.AttemptAcquire(From("203.0.113.10")).IsAcquired);
    }
}


/// <summary>
/// The yes a visitor gives to an excluded industry on /signup is stored at once, with the noes before it, and with no tenant. Nothing
/// is stored for a visitor who cannot be verified, or for answers that hold no yes or are not this gate's questions.
/// </summary>
public sealed class IndustryRefusalsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 15, 0, 0, TimeSpan.Zero);
    private static readonly VisitorOrigin FromUs = new("203.0.113.9", "US");

    private static IndustryRefusals Service(RecordingSignupAnswerStore store) => new(store, new FakeTimeProvider(Now));

    private static IndustryRefusalRequest UpTo(string yesTo)
    {
        var answers = new List<IndustryAnswerInput>();
        foreach (var industry in ExcludedIndustries.All)
        {
            answers.Add(new IndustryAnswerInput(industry.Code, industry.Question, industry.Code == yesTo));
            if (industry.Code == yesTo) break;
        }

        return new IndustryRefusalRequest(answers);
    }

    [Fact]
    public async Task A_yes_is_stored_with_the_noes_before_it_the_time_and_the_visitors_address()
    {
        var store = new RecordingSignupAnswerStore();

        var result = await Service(store).RecordAsync(UpTo("investment_adviser"), FromUs);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(6, result.Value!.Answers);
        var kept = Assert.Single(store.Refusals);
        Assert.Equal(["hipaa_healthcare_provider", "hipaa_health_plan", "hipaa_clearinghouse", "hipaa_business_associate", "securities_broker_dealer", "investment_adviser"], kept.Answers.Select(a => a.Industry));
        Assert.Equal([false, false, false, false, false, true], kept.Answers.Select(a => a.Answer));
        Assert.Equal("Is the business an investment adviser?", kept.Answers[^1].Question);
        Assert.Equal(Now, kept.AnsweredAt);
        Assert.Equal("203.0.113.9", kept.IpAddress);
        Assert.Equal("US", kept.VisitorCountry);
    }

    [Theory]
    [InlineData(null, "US")]
    [InlineData("203.0.113.9", null)]
    public async Task A_visitor_who_cannot_be_verified_stores_nothing(string? ip, string? country)
    {
        var store = new RecordingSignupAnswerStore();

        var result = await Service(store).RecordAsync(UpTo("hipaa_health_plan"), new VisitorOrigin(ip, country));

        Assert.Equal(OnboardingFailureCodes.VisitorUnverified, result.ErrorCode);
        Assert.Empty(store.Refusals);
    }

    [Fact]
    public async Task Answers_with_no_yes_in_them_are_not_a_refusal_and_store_nothing()
    {
        var store = new RecordingSignupAnswerStore();
        var noes = new IndustryRefusalRequest(ExcludedIndustries.All.Select(i => new IndustryAnswerInput(i.Code, i.Question, false)).ToList());

        var result = await Service(store).RecordAsync(noes, FromUs);

        Assert.Equal(OnboardingFailureCodes.IndustryNotAnswered, result.ErrorCode);
        Assert.Empty(store.Refusals);
    }

    [Fact]
    public async Task A_question_that_is_not_this_gates_stores_nothing()
    {
        var store = new RecordingSignupAnswerStore();
        var reworded = new IndustryRefusalRequest([new IndustryAnswerInput("hipaa_healthcare_provider", "HIPAA?", true)]);

        var missing = await Service(store).RecordAsync(null, FromUs);
        var result = await Service(store).RecordAsync(reworded, FromUs);

        Assert.Equal(OnboardingFailureCodes.IndustryNotAnswered, missing.ErrorCode);
        Assert.Equal(OnboardingFailureCodes.IndustryNotAnswered, result.ErrorCode);
        Assert.Empty(store.Refusals);
    }

    [Fact]
    public async Task A_store_that_is_down_is_a_failure_and_never_a_recorded_answer()
    {
        var store = new RecordingSignupAnswerStore(() => Result<VoiceIndustryAnswersRecorded>.Failure("down", VoiceFailureCodes.StoreUnavailable));

        var result = await Service(store).RecordAsync(UpTo("medicare_tpmo"), FromUs);

        Assert.False(result.IsSuccess);
        Assert.Equal(VoiceFailureCodes.StoreUnavailable, result.ErrorCode);
    }
}
