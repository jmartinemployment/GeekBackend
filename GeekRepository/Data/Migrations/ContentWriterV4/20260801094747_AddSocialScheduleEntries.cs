using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentWriterV4
{
    /// <inheritdoc />
    public partial class AddSocialScheduleEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "social_schedule_entries",
                schema: "content_writer_v4",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: false),
                    asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    asset_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    scheduled_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_social_schedule_entries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_social_schedule_campaign_when",
                schema: "content_writer_v4",
                table: "social_schedule_entries",
                columns: new[] { "campaign_id", "scheduled_at" });

            migrationBuilder.CreateIndex(
                name: "IX_social_schedule_entries_owner_id",
                schema: "content_writer_v4",
                table: "social_schedule_entries",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_social_schedule_owner_when",
                schema: "content_writer_v4",
                table: "social_schedule_entries",
                columns: new[] { "owner_id", "scheduled_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "social_schedule_entries",
                schema: "content_writer_v4");
        }
    }
}
