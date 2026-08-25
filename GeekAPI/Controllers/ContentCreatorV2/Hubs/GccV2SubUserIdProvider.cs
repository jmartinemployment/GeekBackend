using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;

namespace GeekAPI.Controllers.ContentCreatorV2.Hubs;

/// <summary>Maps the GeekOAuth JWT <c>sub</c> claim to the SignalR user id so <c>Clients.User(id)</c> works.</summary>
public sealed class GccV2SubUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) =>
        connection.User?.FindFirst("sub")?.Value
        ?? connection.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
}
