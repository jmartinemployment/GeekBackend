using GeekAPI.Controllers.Voice;
using GeekAPI.Services.Voice.Onboarding;
using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using GeekApplication.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;

namespace GeekBackend.Tests.Voice;

/// <summary>
/// The four rules of the self-service gate, in the plan's order, and what each leaves behind: a refused signup creates nothing,
/// and an accepted one is exactly one pending tenant carrying the attestation, its IRS business activity code and, where the
/// industry needs it, the addendum.
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

        private static VoiceTenant Tenant(CreateVoiceTenantCommand c) => new(NewTenant, c.BusinessName, c.IndustryVertical, c.BusinessActivityCode, c.RequiresAddendum,
            c.AddendumSignedAt, c.AddendumIpAddress, c.ComplianceAttestedAt, c.CountryCode, c.StateCode, c.ContactEmail, c.ContactPhone,
            VoiceTenantStatuses.PendingPayment, Now);

        public Task<Result<VoiceTenant>> GetTenantAsync(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceTenant>> ActivateTenantAsync(Guid tenantId, ActivateVoiceTenantCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceTelephony>> GetTelephonyAsync(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceReceptionistConfig>> GetConfigAsync(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceReceptionistConfig>> SaveConfigAsync(Guid tenantId, VoiceReceptionistConfig config, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceMcpConnection>> GetMcpConnectionAsync(Guid tenantId, string provider, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceMcpConnection>> SaveMcpConnectionAsync(Guid tenantId, string provider, SaveVoiceMcpConnectionCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceKnowledgeDocumentCreated>> CreateKnowledgeDocumentAsync(Guid tenantId, CreateVoiceKnowledgeDocumentCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<IReadOnlyList<VoiceKnowledgeHit>>> SearchKnowledgeAsync(Guid tenantId, SearchVoiceKnowledgeCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceCallStored>> UpsertCallAsync(Guid tenantId, UpsertVoiceCallCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceAppointmentCreated>> CreateAppointmentAsync(Guid tenantId, CreateVoiceAppointmentCommand command, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static ValidateEligibilityRequest Signup(
        string? activity = "238220", string? country = "US", bool? attested = true, bool? addendumSigned = null) =>
        new("Boca Cool HVAC", activity, country, "FL", "owner@bocacool.example", "+15615550101", attested, addendumSigned);

    private static readonly VisitorOrigin FromUs = new("203.0.113.9", "US");

    private static OnboardingEligibility Gate(RecordingStore store, bool addendums = false) =>
        new(store, new OnboardingOptions(addendums), new FakeTimeProvider(Now));

    [Fact]
    public async Task A_passing_signup_becomes_one_pending_tenant_with_its_attestation()
    {
        var store = new RecordingStore();

        var result = await Gate(store).ValidateAsync(Signup(), FromUs);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(new EligibilityAccepted(NewTenant, VoiceTenantStatuses.PendingPayment), result.Value);
        var created = Assert.Single(store.Created);
        Assert.Equal(Now, created.ComplianceAttestedAt);
        Assert.False(created.RequiresAddendum);
        Assert.Null(created.AddendumSignedAt);
        Assert.Null(created.AddendumIpAddress);
        Assert.Equal("US", created.CountryCode);
        Assert.Equal("+15615550101", created.ContactPhone);
        Assert.Equal("238220", created.BusinessActivityCode);
        Assert.Equal("Plumbing, heating, & air-conditioning contractors", created.IndustryVertical);
    }

    [Theory]
    [InlineData("541510")] // computer systems design: an AI consultancy that serves accounting firms is not an accounting firm
    [InlineData("624410")] // childcare is social assistance, not healthcare and not education
    [InlineData("541600")]
    public async Task A_code_outside_the_excluded_ranges_passes_without_an_addendum(string activity)
    {
        var store = new RecordingStore();

        var result = await Gate(store).ValidateAsync(Signup(activity), FromUs);

        Assert.True(result.IsSuccess, result.Error);
        Assert.False(Assert.Single(store.Created).RequiresAddendum);
    }

    [Fact]
    public async Task The_phone_is_stored_as_plus_one_and_ten_digits_however_it_was_typed()
    {
        var store = new RecordingStore();

        var result = await Gate(store).ValidateAsync(Signup() with { ContactPhone = "(561) 555-0101" }, FromUs);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("+15615550101", Assert.Single(store.Created).ContactPhone);
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
    [InlineData("HVAC")]
    [InlineData("541511")] // NAICS has it; the 2025 Schedule C chart folds it into 541510
    [InlineData("999000")] // "unclassified" would step around every rule
    [InlineData("")]
    [InlineData(null)]
    public async Task A_code_the_chart_does_not_have_is_refused_and_creates_nothing(string? activity)
    {
        var store = new RecordingStore();

        var result = await Gate(store).ValidateAsync(Signup(activity), FromUs);

        Assert.Equal(OnboardingFailureCodes.BusinessActivityUnknown, result.ErrorCode);
        Assert.Empty(store.Created);
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
    [InlineData("621111")] // offices of physicians
    [InlineData("621210")] // offices of dentists
    [InlineData("622000")] // hospitals
    [InlineData("623000")] // nursing and residential care
    [InlineData("541940")] // veterinary services
    public async Task A_healthcare_industry_is_refused_whatever_the_flag_says(string industry)
    {
        foreach (var flag in new[] { false, true })
        {
            var store = new RecordingStore();

            var result = await Gate(store, flag).ValidateAsync(Signup(industry, addendumSigned: true), FromUs);

            Assert.Equal(OnboardingFailureCodes.IndustryExcluded, result.ErrorCode);
            Assert.Empty(store.Created);
        }
    }

    [Theory]
    [InlineData("541100")] // legal services
    [InlineData("541211")] // certified public accountants
    [InlineData("541213")] // tax preparation
    [InlineData("541214")] // payroll services
    [InlineData("522100")] // banking
    [InlineData("524210")] // insurance agencies
    [InlineData("611000")] // educational services
    public async Task An_addendum_industry_is_blocked_while_addendums_are_not_published(string industry)
    {
        var store = new RecordingStore();

        var result = await Gate(store, addendums: false).ValidateAsync(Signup(industry, addendumSigned: true), FromUs);

        Assert.Equal(OnboardingFailureCodes.IndustryAddendumNotAvailable, result.ErrorCode);
        Assert.Empty(store.Created);
    }

    [Fact]
    public async Task With_addendums_published_a_signed_addendum_is_recorded_with_its_time_and_address()
    {
        var store = new RecordingStore();

        var result = await Gate(store, addendums: true).ValidateAsync(Signup("541100", addendumSigned: true), FromUs);

        Assert.True(result.IsSuccess, result.Error);
        var created = Assert.Single(store.Created);
        Assert.True(created.RequiresAddendum);
        Assert.Equal(Now, created.AddendumSignedAt);
        Assert.Equal("203.0.113.9", created.AddendumIpAddress);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public async Task With_addendums_published_an_unsigned_addendum_is_refused(bool? signed)
    {
        var store = new RecordingStore();

        var result = await Gate(store, addendums: true).ValidateAsync(Signup("541211", addendumSigned: signed), FromUs);

        Assert.Equal(OnboardingFailureCodes.AddendumRequired, result.ErrorCode);
        Assert.Empty(store.Created);
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
    public async Task The_rules_answer_in_the_plans_order()
    {
        var store = new RecordingStore();

        // Wrong country, healthcare and no attestation at once: the country rule speaks first.
        var first = await Gate(store).ValidateAsync(Signup("621111", country: "CA", attested: false), FromUs);
        // Healthcare and no attestation: the industry rule speaks before the attestation rule.
        var second = await Gate(store).ValidateAsync(Signup("621111", attested: false), FromUs);
        // An addendum industry and no attestation: the block speaks before the attestation rule.
        var third = await Gate(store, addendums: false).ValidateAsync(Signup("541100", attested: false), FromUs);

        Assert.Equal(OnboardingFailureCodes.CountryNotSupported, first.ErrorCode);
        Assert.Equal(OnboardingFailureCodes.IndustryExcluded, second.ErrorCode);
        Assert.Equal(OnboardingFailureCodes.IndustryAddendumNotAvailable, third.ErrorCode);
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
    [InlineData(OnboardingFailureCodes.BusinessActivityUnknown, 400)]
    [InlineData(OnboardingFailureCodes.ContactPhoneNotUs, 400)]
    [InlineData(OnboardingFailureCodes.IndustryExcluded, 400)]
    [InlineData(OnboardingFailureCodes.IndustryAddendumNotAvailable, 400)]
    [InlineData(OnboardingFailureCodes.AddendumRequired, 400)]
    [InlineData(OnboardingFailureCodes.AttestationRequired, 400)]
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

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("false", false)]
    [InlineData("true", true)]
    [InlineData(" True ", true)]
    public void The_addendum_flag_defaults_to_false_and_reads_true_or_false(string? raw, bool expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [OnboardingOptions.FlagKey] = raw })
            .Build();

        Assert.Equal(expected, OnboardingOptions.From(configuration).EnableIndustryAddendums);
    }

    [Fact]
    public void An_addendum_flag_that_is_not_true_or_false_stops_the_service_from_starting()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [OnboardingOptions.FlagKey] = "yes" })
            .Build();

        Assert.Throws<InvalidOperationException>(() => OnboardingOptions.From(configuration));
    }
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
