using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentCreatorV2;

public partial class AddPublishRecords : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "gcc_v2_publish_records",
            schema: "content_creator_v2",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CreateId = table.Column<Guid>(type: "uuid", nullable: false),
                JobId = table.Column<Guid>(type: "uuid", nullable: false),
                OwnerUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "blog"),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "draft"),
                ExternalPostId = table.Column<int>(type: "integer", nullable: true),
                Slug = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                PublicUrl = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                Title = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                MetaDescription = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                Error = table.Column<string>(type: "text", nullable: true),
                BodyDocumentJson = table.Column<string>(type: "text", nullable: true),
                IsPublished = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                PublishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_gcc_v2_publish_records", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_gcc_v2_publish_records_create_id",
            schema: "content_creator_v2",
            table: "gcc_v2_publish_records",
            column: "CreateId");

        migrationBuilder.CreateIndex(
            name: "ix_gcc_v2_publish_records_job_id",
            schema: "content_creator_v2",
            table: "gcc_v2_publish_records",
            column: "JobId");

        migrationBuilder.CreateIndex(
            name: "ix_gcc_v2_publish_records_owner_user_id",
            schema: "content_creator_v2",
            table: "gcc_v2_publish_records",
            column: "OwnerUserId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "gcc_v2_publish_records", schema: "content_creator_v2");
    }
}
