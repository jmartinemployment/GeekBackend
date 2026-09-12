using GeekRepository.Data.Entities.ContentCreatorV2;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Data;

internal static class GccV2SharePointConfiguration
{
    public static void ConfigureGccV2SharePoint(this ModelBuilder modelBuilder)
    {
        var connection = modelBuilder.Entity<GccV2SharePointConnection>();
        connection.ToTable("gcc_v2_sharepoint_connections");
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
