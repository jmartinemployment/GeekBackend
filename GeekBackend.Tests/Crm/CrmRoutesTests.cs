using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using GeekAPI.HttpClients;
using GeekApplication.Models.Crm;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeekBackend.Tests.Crm;

/// <summary>
/// The custom-property, NAICS and import calls GeekAPI makes to GeekRepository: each goes to a route
/// GeekRepository's controller actually declares, with the verb it declares, and GeekAPI's own routes
/// match the paths the plan names. A path typed wrongly on one side would otherwise fail only when used.
/// </summary>
public class CrmRoutesTests
{
    private sealed class Recorder : HttpMessageHandler
    {
        public List<(HttpMethod Method, string PathAndQuery, string? Body)> Calls { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls.Add((request.Method, request.RequestUri!.PathAndQuery, request.Content is null ? null : await request.Content.ReadAsStringAsync(ct)));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]", Encoding.UTF8, "application/json") };
        }
    }

    private static readonly Guid Tenant = CrmSeed.GeekAtYourSpotTenantId;
    private static readonly Guid Record = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Definition = Guid.Parse("22222222-2222-4222-8222-222222222222");

    private static (HttpCrmRepository Client, Recorder Recorder) NewClient()
    {
        var recorder = new Recorder();
        return (new HttpCrmRepository(new HttpClient(recorder) { BaseAddress = new Uri("http://repo.test/") }, NullLogger<HttpCrmRepository>.Instance), recorder);
    }

    private static string Normalize(string path) => path[..(path.IndexOf('?', StringComparison.Ordinal) is var q and >= 0 ? q : path.Length)].TrimStart('/');

    /// <summary>A repository route template with its constraints removed and its parameters as braces: tenants/{tenantId}/deals.</summary>
    private static IEnumerable<(string Verb, string Template)> RepositoryRoutes()
    {
        var controller = typeof(GeekRepository.Controllers.Crm.CrmController);
        var root = controller.GetCustomAttribute<RouteAttribute>()!.Template;
        foreach (var method in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            foreach (var attribute in method.GetCustomAttributes<HttpMethodAttribute>())
            {
                var template = System.Text.RegularExpressions.Regex.Replace($"{root}/{attribute.Template}", @"\{(\w+):[^}]+\}", "{$1}");
                yield return (attribute.HttpMethods.Single(), template);
            }
        }
    }

    private static bool Matches(string template, string path)
    {
        var t = template.Split('/');
        var p = path.Split('/');
        return t.Length == p.Length && t.Zip(p).All(pair => pair.First.StartsWith('{') || pair.First == pair.Second);
    }

    [Fact]
    public async Task Every_new_call_goes_to_a_route_and_verb_geekrepository_declares()
    {
        var (client, recorder) = NewClient();
        var values = new Dictionary<string, JsonElement> { ["pms"] = JsonDocument.Parse("\"x\"").RootElement.Clone() };

        await client.SearchNaicsAsync("dental", 6, 10);
        await client.ListPropertyDefinitionsAsync(Tenant, "organization", true, true);
        await client.CreatePropertyDefinitionAsync(new(Tenant, "organization", "pms", "PMS", "text", null, false));
        await client.UpdatePropertyDefinitionAsync(Definition, new(Tenant, "PMS", null, false));
        await client.RetirePropertyDefinitionAsync(Tenant, Definition);
        await client.GetPropertyValuesAsync(Tenant, "organization", Record, false);
        await client.SetPropertyValuesAsync(new(Tenant, "organization", Record, values, false));

        Assert.Equal(7, recorder.Calls.Count);
        var routes = RepositoryRoutes().ToList();
        foreach (var call in recorder.Calls)
        {
            var path = Normalize(call.PathAndQuery);
            Assert.True(routes.Any(r => r.Verb == call.Method.Method && Matches(r.Template, path)),
                $"GeekRepository declares no {call.Method} route for {path}.");
        }
    }

    [Fact]
    public async Task The_calls_carry_their_arguments_in_the_address_or_the_body()
    {
        var (client, recorder) = NewClient();

        await client.SearchNaicsAsync("dental & care", 6, 10);
        await client.GetPropertyValuesAsync(Tenant, "deal", Record, true);
        await client.SetPropertyValuesAsync(new(Tenant, "person", Record, new Dictionary<string, JsonElement>(), false));

        Assert.Equal("/repo/crm/lookups/naics?limit=10&q=dental%20%26%20care&level=6", recorder.Calls[0].PathAndQuery);
        Assert.Equal($"/repo/crm/tenants/{Tenant}/records/deal/{Record}/properties?includeSensitive=true", recorder.Calls[1].PathAndQuery);
        Assert.Contains("\"objectType\":\"person\"", recorder.Calls[2].Body, StringComparison.Ordinal);
        Assert.Contains("\"includeSensitive\":false", recorder.Calls[2].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_import_carries_dry_run_to_geekrepository()
    {
        var (client, recorder) = NewClient();

        await client.ImportProspectsAsync(new(Tenant, "Apollo Batch 1", [], DryRun: true));

        Assert.Contains("\"dryRun\":true", recorder.Calls.Single().Body, StringComparison.Ordinal);
    }

    [Fact]
    public void GeekApi_exposes_the_paths_the_plan_names()
    {
        var controller = typeof(GeekAPI.Controllers.Crm.CrmController);
        var routes = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(m => m.GetCustomAttributes<HttpMethodAttribute>())
            .SelectMany(a => a.HttpMethods.Select(v => $"{v} {System.Text.RegularExpressions.Regex.Replace(a.Template ?? string.Empty, @"\{(\w+):[^}]+\}", "{$1}")}"))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var expected in new[]
                 {
                     "GET lookups/naics",
                     "GET properties/definitions", "POST properties/definitions",
                     "PUT properties/definitions/{definitionId}", "DELETE properties/definitions/{definitionId}",
                     "GET {objectType}/{recordId}/properties", "PUT {objectType}/{recordId}/properties",
                     "POST imports/apollo",
                 })
            Assert.Contains(expected, routes);
    }
}
