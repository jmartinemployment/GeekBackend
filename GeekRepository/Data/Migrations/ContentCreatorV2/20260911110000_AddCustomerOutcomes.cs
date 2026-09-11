using System;
using GeekRepository.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentCreatorV2;

[DbContext(typeof(ContentCreatorV2DbContext))]
[Migration("20260911110000_AddCustomerOutcomes")]
public partial class AddCustomerOutcomes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "gcc_v2_customer_outcomes",
            schema: "content_creator_v2",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OwnerUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                MetricDefinition = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                Baseline = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                Denominator = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                ObservedValue = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                Source = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                EvidenceStatus = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                AttributionMethod = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                AttributionConfidence = table.Column<double>(type: "double precision", nullable: true),
                WorkflowVersionsJson = table.Column<string>(type: "text", nullable: false),
                GeneratedCount = table.Column<int>(type: "integer", nullable: true),
                AcceptedCount = table.Column<int>(type: "integer", nullable: true),
                PublishedCount = table.Column<int>(type: "integer", nullable: true),
                RejectedCount = table.Column<int>(type: "integer", nullable: true),
                ReviewMinutes = table.Column<int>(type: "integer", nullable: true),
                Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_gcc_v2_customer_outcomes", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_gcc_v2_customer_outcomes_OwnerUserId",
            schema: "content_creator_v2",
            table: "gcc_v2_customer_outcomes",
            column: "OwnerUserId");

        migrationBuilder.CreateIndex(
            name: "IX_gcc_v2_customer_outcomes_OwnerUserId_PeriodEnd",
            schema: "content_creator_v2",
            table: "gcc_v2_customer_outcomes",
            columns: new[] { "OwnerUserId", "PeriodEnd" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "gcc_v2_customer_outcomes",
            schema: "content_creator_v2");
    }
}
