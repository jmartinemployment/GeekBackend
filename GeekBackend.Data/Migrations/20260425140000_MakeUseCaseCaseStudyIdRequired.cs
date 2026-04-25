using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeekBackend.Data.Migrations;

[Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(GeekBackend.Data.Data.AppDbContext))]
[Migration("20260425140000_MakeUseCaseCaseStudyIdRequired")]
public partial class MakeUseCaseCaseStudyIdRequired : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_use_cases_case_studies_case_study_id",
            table: "use_cases");

        migrationBuilder.AlterColumn<int>(
            name: "case_study_id",
            table: "use_cases",
            type: "integer",
            nullable: false,
            oldClrType: typeof(int),
            oldType: "integer",
            oldNullable: true);

        migrationBuilder.AddForeignKey(
            name: "FK_use_cases_case_studies_case_study_id",
            table: "use_cases",
            column: "case_study_id",
            principalTable: "case_studies",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_use_cases_case_studies_case_study_id",
            table: "use_cases");

        migrationBuilder.AlterColumn<int>(
            name: "case_study_id",
            table: "use_cases",
            type: "integer",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "integer");

        migrationBuilder.AddForeignKey(
            name: "FK_use_cases_case_studies_case_study_id",
            table: "use_cases",
            column: "case_study_id",
            principalTable: "case_studies",
            principalColumn: "id",
            onDelete: ReferentialAction.SetNull);
    }
}
