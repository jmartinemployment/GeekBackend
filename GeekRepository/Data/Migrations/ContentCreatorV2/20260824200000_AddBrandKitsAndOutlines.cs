using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentCreatorV2;

public partial class AddBrandKitsAndOutlines : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "SiteAnalysisProfileId",
            schema: "content_creator_v2",
            table: "gcc_v2_jobs",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "gcc_v2_brand_kits",
            schema: "content_creator_v2",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ClientId = table.Column<Guid>(type: "uuid", nullable: true),
                DerivedFromProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                Version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                KitJson = table.Column<string>(type: "text", nullable: false, defaultValue: "{}"),
                VoiceStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "provisional"),
                DerivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                AcceptedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_gcc_v2_brand_kits", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "gcc_v2_outlines",
            schema: "content_creator_v2",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                BriefId = table.Column<Guid>(type: "uuid", nullable: false),
                Version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                OutlineJson = table.Column<string>(type: "text", nullable: false, defaultValue: "{}"),
                HierarchyChildHeadingsJson = table.Column<string>(type: "text", nullable: false, defaultValue: "[]"),
                FrozenAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_gcc_v2_outlines", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_gcc_v2_brand_kits_derived_from_profile_id",
            schema: "content_creator_v2",
            table: "gcc_v2_brand_kits",
            column: "DerivedFromProfileId");

        migrationBuilder.CreateIndex(
            name: "ix_gcc_v2_brand_kits_client_id",
            schema: "content_creator_v2",
            table: "gcc_v2_brand_kits",
            column: "ClientId");

        migrationBuilder.CreateIndex(
            name: "ix_gcc_v2_outlines_brief_id",
            schema: "content_creator_v2",
            table: "gcc_v2_outlines",
            column: "BriefId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "gcc_v2_brand_kits", schema: "content_creator_v2");
        migrationBuilder.DropTable(name: "gcc_v2_outlines", schema: "content_creator_v2");

        migrationBuilder.DropColumn(
            name: "SiteAnalysisProfileId",
            schema: "content_creator_v2",
            table: "gcc_v2_jobs");
    }
}
