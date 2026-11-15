using System.Net;
using System.Text;
using GeekAPI.Controllers.Voice;
using GeekAPI.Services.Voice.Onboarding;
using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using GeekApplication.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GeekBackend.Tests.Voice;

/// <summary>
/// Creating the owner's login: only an active tenant gets one, GeekOAuth is asked once with the request GeekAPI was given, a refusal
/// keeps its own code, every other GeekOAuth failure is the service being unavailable, an unset CLIENT_SECRET_GEEKAPI is a refusal
/// before anything is sent, and the password is never logged.
/// </summary>
public sealed class OwnerLoginTests
{
    private const string Password = "correct-horse-battery-staple";
    private static readonly Guid Tenant = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Lines { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add(formatter(state, exception) + (exception?.ToString() ?? string.Empty));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(string Url, string Body)> Seen { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Seen.Add((request.RequestUri!.ToString(), request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken)));
            return respond(request);
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public string? Name { get; private set; }

        public HttpClient CreateClient(string name)
        {
            Name = name;
            return new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("https://auth.example.test/") };
        }
    }

    private sealed class Store(Result<VoiceTenant> tenant) : IVoiceStore
    {
        public Task<Result<VoiceTenant>> GetTenantAsync(Guid tenantId, CancellationToken ct = default) => Task.FromResult(tenant);
        public Task<Result<VoiceTenant>> CreateTenantAsync(CreateVoiceTenantCommand command, CancellationToken ct = default) => throw new NotSupportedException();
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

    private static VoiceTenant MakeTenant(string status) => new(
        Tenant, "Dag Plumbing", "plumbing", false, null, null, DateTimeOffset.UnixEpoch, "US", "FL", null, "owner@example.com", "+15615550100", status, DateTimeOffset.UnixEpoch);

    private static HttpResponseMessage Json(string json, HttpStatusCode status) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class Rig
    {
        public StubHandler Handler { get; }
        public Factory Factory { get; }
        public CapturingLogger<OwnerLoginProvisioner> Log { get; } = new();
        public OwnerLoginProvisioner Provisioner { get; }

        public Rig(Func<HttpRequestMessage, HttpResponseMessage>? answer = null, string status = VoiceTenantStatuses.Active, bool secretSet = true, Result<VoiceTenant>? tenant = null)
        {
            Handler = new StubHandler(answer ?? (_ => Json("{\"userId\":\"user-1\"}", HttpStatusCode.Created)));
            Factory = new Factory(Handler);
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(secretSet ? new Dictionary<string, string?> { ["CLIENT_SECRET_GEEKAPI"] = "s3cret" } : new Dictionary<string, string?>())
                .Build();
            Provisioner = new OwnerLoginProvisioner(new Store(tenant ?? Result<VoiceTenant>.Success(MakeTenant(status))), Factory, config, Log);
        }
    }

    private static CreateOwnerLoginRequest Valid() => new(Tenant, " owner@example.com ", Password, "Dana");

    [Fact]
    public async Task An_active_tenant_gets_a_login_and_geekoauth_is_asked_once_with_the_details()
    {
        var rig = new Rig();
        var result = await rig.Provisioner.CreateAsync(Valid());

        Assert.True(result.IsSuccess);
        Assert.Equal(new OwnerLoginCreated(Tenant, "user-1"), result.Value);
        var seen = Assert.Single(rig.Handler.Seen);
        Assert.Equal("https://auth.example.test/api/internal/tenant-owners", seen.Url);
        Assert.Contains(Tenant.ToString(), seen.Body);
        Assert.Contains("owner@example.com", seen.Body);
        Assert.DoesNotContain(" owner@example.com ", seen.Body);
        Assert.Equal(OwnerLoginProvisioner.ClientName, rig.Factory.Name);
    }

    [Theory]
    [InlineData(VoiceTenantStatuses.PendingPayment)]
    [InlineData(VoiceTenantStatuses.Suspended)]
    [InlineData(VoiceTenantStatuses.Cancelled)]
    public async Task A_tenant_that_is_not_active_gets_no_login_and_nothing_is_sent(string status)
    {
        var rig = new Rig(status: status);
        var result = await rig.Provisioner.CreateAsync(Valid());

        Assert.Equal(OwnerLoginFailureCodes.TenantNotActive, result.ErrorCode);
        Assert.Empty(rig.Handler.Seen);
    }

    [Fact]
    public async Task An_unknown_tenant_keeps_the_stores_code_and_nothing_is_sent()
    {
        var rig = new Rig(tenant: Result<VoiceTenant>.Failure("no such tenant", VoiceFailureCodes.NotFound));
        var result = await rig.Provisioner.CreateAsync(Valid());

        Assert.Equal(VoiceFailureCodes.NotFound, result.ErrorCode);
        Assert.Empty(rig.Handler.Seen);
    }

    [Fact]
    public async Task An_unset_client_secret_is_a_refusal_before_anything_else()
    {
        var rig = new Rig(secretSet: false);
        var result = await rig.Provisioner.CreateAsync(Valid());

        Assert.Equal(OwnerLoginFailureCodes.NotConfigured, result.ErrorCode);
        Assert.Empty(rig.Handler.Seen);
        Assert.Null(rig.Factory.Name);
    }

    public static TheoryData<CreateOwnerLoginRequest?> BadRequests() => new()
    {
        null,
        new CreateOwnerLoginRequest(null, "a@b.co", Password, null),
        new CreateOwnerLoginRequest(Guid.Empty, "a@b.co", Password, null),
        new CreateOwnerLoginRequest(Tenant, null, Password, null),
        new CreateOwnerLoginRequest(Tenant, "not-an-email", Password, null),
        new CreateOwnerLoginRequest(Tenant, "a@b.co", null, null),
        new CreateOwnerLoginRequest(Tenant, "a@b.co", "short", null),
    };

    [Theory]
    [MemberData(nameof(BadRequests))]
    public async Task An_incomplete_request_is_invalid_and_nothing_is_sent(CreateOwnerLoginRequest? request)
    {
        var rig = new Rig();
        var result = await rig.Provisioner.CreateAsync(request);

        Assert.Equal(VoiceFailureCodes.InvalidRequest, result.ErrorCode);
        Assert.Empty(rig.Handler.Seen);
    }

    [Fact]
    public async Task A_conflict_from_geekoauth_is_owner_exists()
    {
        var rig = new Rig(_ => Json("{\"error\":\"That tenant already has an owner.\"}", HttpStatusCode.Conflict));
        var result = await rig.Provisioner.CreateAsync(Valid());

        Assert.Equal(OwnerLoginFailureCodes.OwnerExists, result.ErrorCode);
    }

    [Fact]
    public async Task A_refused_password_keeps_geekoauths_reason()
    {
        var rig = new Rig(_ => Json("{\"error\":\"Passwords must have at least one digit.\"}", HttpStatusCode.BadRequest));
        var result = await rig.Provisioner.CreateAsync(Valid());

        Assert.Equal(OwnerLoginFailureCodes.CredentialsRefused, result.ErrorCode);
        Assert.Equal("Passwords must have at least one digit.", result.Error);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Any_other_geekoauth_status_is_unavailable_and_is_not_retried(HttpStatusCode status)
    {
        var rig = new Rig(_ => Json("{}", status));
        var result = await rig.Provisioner.CreateAsync(Valid());

        Assert.Equal(OwnerLoginFailureCodes.AuthUnavailable, result.ErrorCode);
        Assert.Single(rig.Handler.Seen);
    }

    [Fact]
    public async Task A_created_answer_with_no_user_id_is_unavailable_and_not_a_fake_success()
    {
        var rig = new Rig(_ => Json("{}", HttpStatusCode.Created));
        var result = await rig.Provisioner.CreateAsync(Valid());

        Assert.False(result.IsSuccess);
        Assert.Equal(OwnerLoginFailureCodes.AuthUnavailable, result.ErrorCode);
    }

    [Fact]
    public async Task An_unreachable_geekoauth_is_unavailable()
    {
        var rig = new Rig(_ => throw new HttpRequestException("connection refused"));
        var result = await rig.Provisioner.CreateAsync(Valid());

        Assert.Equal(OwnerLoginFailureCodes.AuthUnavailable, result.ErrorCode);
    }

    [Fact]
    public async Task No_log_line_carries_the_password()
    {
        foreach (var answer in new Func<HttpRequestMessage, HttpResponseMessage>[]
        {
            _ => Json("{\"userId\":\"user-1\"}", HttpStatusCode.Created),
            _ => Json("{\"error\":\"x\"}", HttpStatusCode.BadRequest),
            _ => Json("{}", HttpStatusCode.InternalServerError),
            _ => throw new HttpRequestException("down"),
        })
        {
            var rig = new Rig(answer);
            await rig.Provisioner.CreateAsync(Valid());
            Assert.DoesNotContain(rig.Log.Lines, line => line.Contains(Password));
        }
    }

    [Theory]
    [InlineData(OwnerLoginFailureCodes.CredentialsRefused, 400)]
    [InlineData(VoiceFailureCodes.InvalidRequest, 400)]
    [InlineData(VoiceFailureCodes.NotFound, 404)]
    [InlineData(OwnerLoginFailureCodes.OwnerExists, 409)]
    [InlineData(OwnerLoginFailureCodes.TenantNotActive, 409)]
    [InlineData(OwnerLoginFailureCodes.AuthUnavailable, 503)]
    [InlineData(OwnerLoginFailureCodes.NotConfigured, 503)]
    [InlineData(VoiceFailureCodes.StoreUnavailable, 503)]
    [InlineData(null, 503)]
    public void Each_code_has_its_status(string? code, int status) =>
        Assert.Equal(status, OwnerLoginController.StatusFor(code));

    [Fact]
    public async Task The_controller_answers_201_with_the_tenant_and_user_and_never_the_password()
    {
        var rig = new Rig();
        var controller = new OwnerLoginController(rig.Provisioner)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        var created = Assert.IsAssignableFrom<ObjectResult>(await controller.Create(Valid(), default));

        Assert.Equal(201, created.StatusCode);
        Assert.Equal(new OwnerLoginCreated(Tenant, "user-1"), created.Value);
    }

    [Fact]
    public async Task The_controller_answers_a_null_body_with_400()
    {
        var controller = new OwnerLoginController(new Rig().Provisioner)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        var refused = Assert.IsAssignableFrom<ObjectResult>(await controller.Create(null!, default));

        Assert.Equal(400, refused.StatusCode);
    }
}
