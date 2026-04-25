using System;
using GeekBackend.Data.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GeekBackend.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260424120000_AddCaseStudies")]
public partial class AddCaseStudies : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "departments",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                description = table.Column<string>(type: "text", nullable: false),
                icon_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_departments", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "case_studies",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                descriptive_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                slug = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "draft"),
                executive_summary = table.Column<string>(type: "text", nullable: false),
                primary_actor = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                trigger = table.Column<string>(type: "text", nullable: false),
                problem_challenge = table.Column<string>(type: "text", nullable: false),
                solution = table.Column<string>(type: "text", nullable: false),
                post_conditions = table.Column<string>(type: "text", nullable: true),
                exceptions = table.Column<string>(type: "text", nullable: true),
                industry_citation = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                published_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_case_studies", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "case_study_actors",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                case_study_id = table.Column<int>(type: "integer", nullable: false),
                actor_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                actor_role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_case_study_actors", x => x.id);
                table.ForeignKey(
                    name: "FK_case_study_actors_case_studies_case_study_id",
                    column: x => x.case_study_id,
                    principalTable: "case_studies",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "case_study_metrics",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                case_study_id = table.Column<int>(type: "integer", nullable: false),
                metric_label = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                metric_value = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                metric_unit = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_case_study_metrics", x => x.id);
                table.ForeignKey(
                    name: "FK_case_study_metrics_case_studies_case_study_id",
                    column: x => x.case_study_id,
                    principalTable: "case_studies",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "case_study_event_flow_steps",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                case_study_id = table.Column<int>(type: "integer", nullable: false),
                step_number = table.Column<int>(type: "integer", nullable: false),
                step_description = table.Column<string>(type: "text", nullable: false),
                step_actor_id = table.Column<int>(type: "integer", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_case_study_event_flow_steps", x => x.id);
                table.ForeignKey(
                    name: "FK_case_study_event_flow_steps_case_studies_case_study_id",
                    column: x => x.case_study_id,
                    principalTable: "case_studies",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_case_study_event_flow_steps_case_study_actors_step_actor_id",
                    column: x => x.step_actor_id,
                    principalTable: "case_study_actors",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "use_cases",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                department_id = table.Column<int>(type: "integer", nullable: false),
                case_study_id = table.Column<int>(type: "integer", nullable: true),
                descriptive_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                slug = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                summary = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_use_cases", x => x.id);
                table.ForeignKey(
                    name: "FK_use_cases_departments_department_id",
                    column: x => x.department_id,
                    principalTable: "departments",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_use_cases_case_studies_case_study_id",
                    column: x => x.case_study_id,
                    principalTable: "case_studies",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex(
            name: "IX_departments_slug",
            table: "departments",
            column: "slug",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_case_studies_slug",
            table: "case_studies",
            column: "slug",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_case_study_actors_case_study_id",
            table: "case_study_actors",
            column: "case_study_id");

        migrationBuilder.CreateIndex(
            name: "IX_case_study_metrics_case_study_id",
            table: "case_study_metrics",
            column: "case_study_id");

        migrationBuilder.CreateIndex(
            name: "IX_case_study_event_flow_steps_case_study_id_step_number",
            table: "case_study_event_flow_steps",
            columns: new[] { "case_study_id", "step_number" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_case_study_event_flow_steps_step_actor_id",
            table: "case_study_event_flow_steps",
            column: "step_actor_id");

        migrationBuilder.CreateIndex(
            name: "IX_use_cases_slug",
            table: "use_cases",
            column: "slug",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_use_cases_department_id",
            table: "use_cases",
            column: "department_id");

        migrationBuilder.CreateIndex(
            name: "IX_use_cases_case_study_id",
            table: "use_cases",
            column: "case_study_id");

        migrationBuilder.InsertData(
            table: "departments",
            columns: new[] { "name", "slug", "description", "sort_order" },
            values: new object[,]
            {
                { "Operations",       "operations",       "Streamline and automate operational workflows.",         1 },
                { "Marketing",        "marketing",        "AI-powered marketing content and campaign automation.",  2 },
                { "Sales",            "sales",            "Lead qualification, follow-up, and pipeline automation.", 3 },
                { "Customer Service", "customer-service", "24/7 AI support, ticket routing, and response automation.", 4 },
                { "Accounting",       "accounting",       "Automated reporting, invoicing, and financial workflows.", 5 },
                { "Human Resources",  "human-resources",  "Hiring automation, onboarding, and employee workflows.",  6 }
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "use_cases");
        migrationBuilder.DropTable(name: "case_study_event_flow_steps");
        migrationBuilder.DropTable(name: "case_study_metrics");
        migrationBuilder.DropTable(name: "case_study_actors");
        migrationBuilder.DropTable(name: "case_studies");
        migrationBuilder.DropTable(name: "departments");
    }
}
