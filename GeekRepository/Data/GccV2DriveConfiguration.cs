using GeekRepository.Data.Entities.ContentCreatorV2;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Data;

internal static class GccV2DriveConfiguration
{
    public static void ConfigureGccV2Drive(this ModelBuilder modelBuilder)
    {
        var connection = modelBuilder.Entity<GccV2DriveConnection>();
        connection.ToTable("gcc_v2_drive_connections");
        connection.HasKey(x => x.Id);
        connection.Property(x => x.OwnerUserId).IsRequired().HasMaxLength(128);
        connection.Property(x => x.AccountLabel).IsRequired().HasMaxLength(512);
        connection.Property(x => x.Status).IsRequired().HasMaxLength(32);
        connection.Property(x => x.EncryptedRefreshToken).IsRequired();
        connection.Property(x => x.EncryptionIv).IsRequired();
        connection.Property(x => x.EncryptionTag).IsRequired();
        connection.HasIndex(x => new { x.OwnerUserId, x.AccountLabel }).IsUnique();
        connection.HasIndex(x => x.OwnerUserId);
    }
}
