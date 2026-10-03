using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GeekAPI.Auth;
using GeekAPI.HttpClients;

namespace GeekAPI.Services.ContentCreatorV2.Generation;

public sealed class GccV2SkillAdminPolicy
{
    private readonly HashSet<Guid> _adminIds;

    public GccV2SkillAdminPolicy(IConfiguration configuration)
    {
        var configured = configuration["GccV2Skills:AdminUserIds"]
            ?? Environment.GetEnvironmentVariable("GEEK_CONTENT_CREATOR_ADMIN_USER_IDS")
            ?? "";
        _adminIds = configured.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => Guid.TryParse(value, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty).ToHashSet();
    }

    public bool IsAuthorized(ICurrentUserContext user) => user.IsAuthenticated && _adminIds.Contains(user.UserId);
    public string ConfigurationHint => "Configure GccV2Skills:AdminUserIds or GEEK_CONTENT_CREATOR_ADMIN_USER_IDS.";
}
