using System;
using GeekRepository.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentCreator;

[DbContext(typeof(ContentCreatorDbContext))]
[Migration("20260802210000_AddGccSiteFindings")]
public partial class AddGccSiteFindings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "gcc_site_findings",
            schema: "content_creator",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                SiteAnalysisId = table.Column<Guid>(type: "uuid", nullable: false),
                FindingType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Severity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                AffectedUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                Title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                Summary = table.Column<string>(type: "text", nullable: false),
                DetailsJson = table.Column<string>(type: "text", nullable: false, defaultValue: "{}"),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_gcc_site_findings", x => x.Id);
                table.ForeignKey(
                    name: "FK_gcc_site_findings_gcc_site_analyses_SiteAnalysisId",
                    column: x => x.SiteAnalysisId,
                    principalSchema: "content_creator",
                    principalTable: "gcc_site_analyses",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_gcc_site_findings_site_analysis_id",
            schema: "content_creator",
            table: "gcc_site_findings",
            column: "SiteAnalysisId");

        migrationBuilder.CreateIndex(
            name: "ix_gcc_site_findings_site_analysis_id_finding_type",
            schema: "content_creator",
            table: "gcc_site_findings",
            columns: new[] { "SiteAnalysisId", "FindingType" });

        migrationBuilder.CreateIndex(
            name: "ix_gcc_site_findings_site_analysis_id_severity",
            schema: "content_creator",
            table: "gcc_site_findings",
            columns: new[] { "SiteAnalysisId", "Severity" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "gcc_site_findings",
            schema: "content_creator");
    }
}
