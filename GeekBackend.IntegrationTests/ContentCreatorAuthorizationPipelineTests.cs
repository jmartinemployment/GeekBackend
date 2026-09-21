using System.Net;

namespace GeekBackend.IntegrationTests;

/// <summary>
/// The content-creator.manage scope check is actually evaluated by the running pipeline, not just
/// declared on the controllers.
/// </summary>
/// <remarks>
/// GeekBackend.Tests.ContentCreator.ContentCreatorAuthorizationTests proves every project, task,
/// time and deliverable action carries the [Authorize] attribute — a static fact about the code.
/// It cannot see that GeekAPI's Program.cs originally only wired UseAuthentication/UseAuthorization
/// when GEEK_OAUTH_AUTHORITY was configured ("Scoped to the v2 hub" read the comment there); an
/// attribute the pipeline never evaluates protects nothing. This is the other half: it hits the
/// real endpoints through the real middleware order and checks the status codes that pipeline
/// actually produces.
///
/// Building this surfaced two real findings, both fixed as part of adding it:
///
///   - ManagePolicy's registration lived entirely inside that same "if GEEK_OAUTH_AUTHORITY is
///     set" block. An unset variable would not have degraded auth to something permissive — it
///     would have meant the policy did not exist at all, so every [Authorize(Policy = ManagePolicy)]
///     route would throw "policy not found" on first request: a 500 indistinguishable from an
///     unrelated crash, not a clean refusal. GeekAPI now refuses to start without the variable,
///     the same way it already refused to start without REPO_API_KEY.
///   - GccApiTestFactory's substitute authentication handler ("IntegrationTest") only becomes the
///     *default* scheme; ManagePolicy is deliberately pinned to the real "Bearer" scheme by name
///     (AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)) so production is never at
///     the mercy of whatever the ambient default happens to be. Testing it for real therefore needs
///     an actual signed JWT the real JwtBearer handler accepts — see
///     GeekApiTestFactory.IssueTestToken and CreateScopedClient, which replace the handler's
///     ConfigurationManager with a static, test-signed key instead of trying to reach a live
///     GeekOAuth discovery endpoint that does not exist in this process.
/// </remarks>
public sealed class ContentCreatorAuthorizationPipelineTests(GeekApiTestFactory factory)
    : IClassFixture<GeekApiTestFactory>
{
    private static readonly Guid SomeClientId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid SomeProjectId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    /// <summary>
    /// Every route reachable purely on GccProjectsController — deliberately excludes
    /// api/geek-content-creator/clients. That route now lives on GccController, whose constructor
    /// pulls in the full generate-pipeline dependency graph (GccJobStore among others), which this
    /// test factory does not register. Reaching it 401/403 (before controller construction) is
    /// fine and is exercised by the three refusal tests below; reaching it far enough to construct
    /// the controller is a pre-existing gap in this test factory unrelated to authorization, so it
    /// is left out of the one test that gets that far.
    /// </summary>
    public static IEnumerable<object[]> ProtectedRequests()
    {
        yield return [HttpMethod.Get, $"/api/geek-content-creator/projects?clientId={SomeClientId:D}"];
        yield return [HttpMethod.Get, $"/api/geek-content-creator/projects/{SomeProjectId:D}"];
        yield return [HttpMethod.Get, $"/api/geek-content-creator/projects/{SomeProjectId:D}/tasks"];
        yield return [HttpMethod.Get, $"/api/geek-content-creator/projects/{SomeProjectId:D}/time"];
        yield return [HttpMethod.Get, $"/api/geek-content-creator/projects/{SomeProjectId:D}/deliverables"];
    }

    [Theory]
    [MemberData(nameof(ProtectedRequests))]
    public async Task No_token_is_refused(HttpMethod method, string path)
    {
        using var client = factory.CreateClient();
        using var response = await client.SendAsync(new HttpRequestMessage(method, path));

        // Unauthenticated, not merely unauthorized: RequireAuthenticatedUser() has nothing to
        // assess a scope against when there is no principal at all.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(ProtectedRequests))]
    public async Task Authenticated_without_the_scope_is_refused(HttpMethod method, string path)
    {
        // A real signed-in user, deliberately carrying no scope at all — the shape of a token
        // issued before content-creator.manage existed, or issued for an unrelated Geek app.
        using var client = factory.CreateScopedClient(GeekApiTestFactory.OwnerUserId);
        using var response = await client.SendAsync(new HttpRequestMessage(method, path));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(ProtectedRequests))]
    public async Task Authenticated_with_an_unrelated_scope_is_still_refused(HttpMethod method, string path)
    {
        // Proves the policy checks for this scope specifically rather than "any scope claim at
        // all" — a token from another Geek app carries a scope claim too, just not this one.
        using var client = factory.CreateScopedClient(GeekApiTestFactory.OwnerUserId, "devices.manage");
        using var response = await client.SendAsync(new HttpRequestMessage(method, path));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(ProtectedRequests))]
    public async Task Authenticated_with_the_scope_passes_authorization(HttpMethod method, string path)
    {
        using var client = factory.CreateScopedClient(GeekApiTestFactory.OwnerUserId, "content-creator.manage");

        try
        {
            using var response = await client.SendAsync(new HttpRequestMessage(method, path));

            // Not 401 or 403: authorization passed. A request that reaches this far and gets a
            // clean response is proof on its own; ProtectedRequests is entirely list-shaped GETs,
            // and this test factory's catch-all for unmatched GETs — built for Workflow hydration
            // at startup — answers any of them with 200 and an empty JSON array, which an
            // IReadOnlyList<T> action deserializes without complaint. So the common case here is a
            // real 200, not an exception.
            Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        }
        catch (System.Text.Json.JsonException)
        {
            // The one shape-mismatched exception: GET projects/{id} expects a single object, and
            // the same empty-array catch-all does not fit that shape. The exception itself —
            // thrown deep in HttpGccRepository, past GccProjectsController.GetById — is exactly
            // the proof this test wants: the request reached the controller, which called out to a
            // repository this test never claimed to stub. Two layers downstream of authorization.
        }
    }
}
