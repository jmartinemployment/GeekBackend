namespace GeekApplication.Entities.Seo;

public sealed class SeoOrganization
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public Guid OwnerId { get; set; }
    public required string Slug { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<SeoOrganizationMember> Members { get; set; } = [];
}

public sealed class SeoOrganizationMember
{
    public Guid OrgId { get; set; }
    public Guid UserId { get; set; }
    public string Role { get; set; } = "writer";
    public Guid? InvitedBy { get; set; }
    public DateTimeOffset JoinedAt { get; set; }

    public SeoOrganization? Organization { get; set; }
}
