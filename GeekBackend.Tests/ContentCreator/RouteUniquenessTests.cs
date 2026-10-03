using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// No two actions may claim the same HTTP method and path.
/// </summary>
/// <remarks>
/// This is not tidiness. On 2026-09-21 a second controller was routed at
/// api/geek-content-creator/clients while GccController already owned "clients" under its own
/// prefix. Two actions matched one path, and ASP.NET answers that with an
/// AmbiguousMatchException — a 500 raised *before* authentication runs, so the endpoint did not
/// 401 like an unauthorized route or 404 like a missing one. It looked like a server fault, and it
/// reached production because nothing here checked.
///
/// Routing itself only discovers the clash when a request arrives, which is why this walks the
/// attributes instead: a duplicate is a fact about the code, knowable without a host, a port or a
/// request.
/// </remarks>
public class RouteUniquenessTests
{
    [Fact]
    public void No_two_actions_claim_the_same_method_and_path()
    {
        var duplicates = AllRoutes()
            .GroupBy(r => $"{r.Method} {r.Template}", StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} claimed by: {string.Join(", ", g.Select(r => r.Action))}")
            .ToList();

        Assert.True(
            duplicates.Count == 0,
            "Two or more actions claim the same route. ASP.NET answers such a request with a 500 "
            + "before authentication runs, so it reads as a server fault rather than a routing "
            + "mistake:\n  " + string.Join("\n  ", duplicates));
    }

    /// <summary>
    /// Every action carries an HTTP method attribute.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An action with no <c>[Http*]</c> on a controller with a class-level <c>[Route]</c> is not
    /// unreachable -- it inherits the class route with <b>no method constraint</b>, so it answers every
    /// verb on the controller's base path. Several of them do so at once, and ASP.NET resolves that
    /// with <c>AmbiguousMatchException</c>: a 500 raised before authentication, on the base path, for
    /// any request at all.
    /// </para>
    /// <para>
    /// The uniqueness test above cannot see this. It enumerates routes <i>from</i>
    /// <see cref="IActionHttpMethodProvider"/> attributes, so an action with no attribute contributes
    /// no routes and collides with nothing. On 2026-10-03 nine such actions shipped to production and
    /// the suite stayed green at 1,512 -- the attributes had been deleted and the method bodies left
    /// behind. Absence is the one thing a check built on presence will not report.
    /// </para>
    /// <para>
    /// SignalR hubs are excluded by construction: the walk is over <see cref="ControllerBase"/>, which
    /// a <c>Hub</c> does not derive from. Anything genuinely not an endpoint should carry
    /// <c>[NonAction]</c>, which is how ASP.NET itself is told the same thing.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_action_declares_an_http_method()
    {
        var controllers = typeof(GeekAPI.Controllers.ContentCreator.GccController).Assembly
            .GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract);

        var unrouted = new List<string>();
        foreach (var controller in controllers)
        {
            foreach (var action in controller
                         .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                         .Where(m => !m.IsSpecialName)
                         .Where(m => m.GetCustomAttribute<NonActionAttribute>() is null))
            {
                if (!action.GetCustomAttributes().OfType<IActionHttpMethodProvider>().Any())
                {
                    unrouted.Add($"{controller.Name}.{action.Name}");
                }
            }
        }

        Assert.True(
            unrouted.Count == 0,
            "These actions carry no [Http*] attribute, so each answers every verb on its controller's "
            + "base path and they resolve against each other as a 500 before authentication:\n  "
            + string.Join("\n  ", unrouted));
    }

    private sealed record Route(string Method, string Template, string Action);

    private static IEnumerable<Route> AllRoutes()
    {
        var controllers = typeof(GeekAPI.Controllers.ContentCreator.GccController).Assembly
            .GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract);

        foreach (var controller in controllers)
        {
            var prefix = controller.GetCustomAttribute<RouteAttribute>()?.Template ?? string.Empty;

            var actions = controller
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName);

            foreach (var action in actions)
            {
                foreach (var attribute in action.GetCustomAttributes()
                             .OfType<IActionHttpMethodProvider>())
                {
                    var template = (attribute as IRouteTemplateProvider)?.Template;
                    var full = Combine(prefix, template);

                    foreach (var method in attribute.HttpMethods)
                    {
                        yield return new Route(
                            method,
                            Normalize(full),
                            $"{controller.Name}.{action.Name}");
                    }
                }
            }
        }
    }

    private static string Combine(string prefix, string? template)
    {
        if (string.IsNullOrWhiteSpace(template)) return prefix;
        if (template.StartsWith('/')) return template.TrimStart('/');
        return string.IsNullOrWhiteSpace(prefix) ? template : $"{prefix.TrimEnd('/')}/{template}";
    }

    /// <summary>
    /// Reduce a template to what routing actually matches on.
    /// </summary>
    /// <remarks>
    /// A parameter's name does not distinguish one route from another: "clients/{id:guid}" and
    /// "clients/{clientId:guid}" collide just as surely as two identical strings, so names are
    /// erased.
    ///
    /// Its constraint does distinguish them, and must be kept. "case-studies/{id:guid}" and
    /// "case-studies/{slug}" are two real, unambiguous routes — a GUID goes to the first and
    /// anything else to the second. Erasing constraints reported that healthy pair as a clash,
    /// which is how a check like this loses the right to be believed.
    /// </remarks>
    private static string Normalize(string template)
    {
        var segments = template.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(segment =>
            {
                if (!segment.StartsWith('{') || !segment.EndsWith('}')) return segment;

                var inner = segment[1..^1];
                var constraint = inner.IndexOf(':');
                return constraint < 0 ? "{*}" : $"{{*{inner[constraint..]}}}";
            });
        return string.Join('/', segments);
    }
}
