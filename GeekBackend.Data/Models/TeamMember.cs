using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class TeamMember
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string DisplayName { get; set; } = null!;

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public string? Passcode { get; set; }

    public string? PermissionSetId { get; set; }

    public string AssignedLocationIds { get; set; } = null!;

    public string? AvatarUrl { get; set; }

    public DateTime? HireDate { get; set; }

    public string Status { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public string? PasswordHash { get; set; }

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public string Role { get; set; } = null!;

    public bool IsActive { get; set; }

    public DateTime? LastLoginAt { get; set; }

    public string? RestaurantGroupId { get; set; }

    public DateTime? TempPasswordExpiresAt { get; set; }

    public string? TempPasswordSetBy { get; set; }

    public bool MustChangePassword { get; set; }

    public DateTime? PasswordChangedAt { get; set; }

    public bool WorkFromHome { get; set; }

    public virtual ICollection<Device1> Device1s { get; set; } = new List<Device1>();

    public virtual MfaSecret? MfaSecret { get; set; }

    public virtual ICollection<MfaTrustedDevice> MfaTrustedDevices { get; set; } = new List<MfaTrustedDevice>();

    public virtual ICollection<PasswordHistory> PasswordHistories { get; set; } = new List<PasswordHistory>();

    public virtual ICollection<PasswordResetToken> PasswordResetTokens { get; set; } = new List<PasswordResetToken>();

    public virtual PermissionSet? PermissionSet { get; set; }

    public virtual Restaurant Restaurant { get; set; } = null!;

    public virtual RestaurantGroup? RestaurantGroup { get; set; }

    public virtual StaffPin? StaffPin { get; set; }

    public virtual StaffTaxInfo? StaffTaxInfo { get; set; }

    public virtual ICollection<TeamMemberJob> TeamMemberJobs { get; set; } = new List<TeamMemberJob>();

    public virtual ICollection<UserRestaurantAccess> UserRestaurantAccesses { get; set; } = new List<UserRestaurantAccess>();

    public virtual ICollection<UserSession> UserSessions { get; set; } = new List<UserSession>();
}
