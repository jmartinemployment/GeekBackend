using System.Reflection;
using GeekAPI.Auth;
using GeekAPI.Controllers.ContentCreator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// Every project, client, task, time and deliverable action requires
/// <see cref="ContentCreatorAuthConstants.ManagePolicy"/>.
/// </summary>
/// <remarks>
/// Static, not behavioral: it reads the <c>[Authorize]</c> metadata the way ASP.NET's authorization
/// middleware does, without a running server. That trade is deliberate — a reflective check like
/// this cannot miss a route the way a hand-picked list of HTTP probes can, and RouteUniquenessTests
/// already established that no two actions here fight over one path, so enumerating every action on
/// these two controllers is enumerating every route.
///
/// It does not prove the policy is actually *evaluated* at runtime — GeekAPI only wires
/// UseAuthentication/UseAuthorization when GEEK_OAUTH_AUTHORITY is configured, which is a fact
/// about Program.cs this test cannot see. ContentCreatorAuthorizationPipelineTests covers that half
/// through the real ASP.NET pipeline.
/// </remarks>
public class ContentCreatorAuthorizationTests
{
    private static readonly HashSet<string> ClientActionsOnGccController =
    [
        nameof(GccController.GetClient),
        nameof(GccController.GetClients),
        nameof(GccController.CreateClient),
        nameof(GccController.UpdateClient),
        nameof(GccController.DeleteClient),
    ];

    [Fact]
    public void Every_client_action_on_GccController_requires_the_manage_policy()
    {
        AssertAllRequirePolicy(typeof(GccController), ClientActionsOnGccController);
    }

    [Fact]
    public void Every_action_on_GccProjectsController_requires_the_manage_policy()
    {
        var allActions = typeof(GccProjectsController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName && HasHttpMethodAttribute(m))
            .Select(m => m.Name)
            .ToHashSet();

        // A controller with nothing to check would pass vacuously and hide a regression where every
        // action was accidentally removed or renamed out from under this test.
        Assert.NotEmpty(allActions);

        AssertAllRequirePolicy(typeof(GccProjectsController), allActions);
    }

    private static void AssertAllRequirePolicy(Type controller, IReadOnlySet<string> actionNames)
    {
        var controllerPolicy = controller.GetCustomAttribute<AuthorizeAttribute>()?.Policy;

        var unprotected = new List<string>();
        foreach (var method in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            if (!actionNames.Contains(method.Name)) continue;

            // [AllowAnonymous] on the action wins over any [Authorize] on the controller — that is
            // ASP.NET's own precedence, and a check that did not honour it would pass a route that
            // is actually wide open. Caught by deliberately adding [AllowAnonymous] to one action
            // and confirming this test failed, before it was added.
            if (method.GetCustomAttribute<AllowAnonymousAttribute>() is not null)
            {
                unprotected.Add($"{method.Name} (marked [AllowAnonymous])");
                continue;
            }

            // A policy on the action overrides one on the controller; either satisfies the check,
            // since ASP.NET evaluates whichever is present the same way.
            var effectivePolicy = method.GetCustomAttribute<AuthorizeAttribute>()?.Policy ?? controllerPolicy;

            if (effectivePolicy != ContentCreatorAuthConstants.ManagePolicy)
                unprotected.Add(method.Name);
        }

        Assert.True(
            unprotected.Count == 0,
            $"These actions on {controller.Name} do not require {ContentCreatorAuthConstants.ManagePolicy}: "
            + string.Join(", ", unprotected)
            + ". Every route over project, client, task, time or deliverable data must carry it — "
            + "GeekOAuth is shared across Geek apps, so a valid token alone proves nothing about "
            + "being granted this data.");
    }

    private static bool HasHttpMethodAttribute(MethodInfo method) =>
        method.GetCustomAttributes().OfType<IActionHttpMethodProvider>().Any();
}
