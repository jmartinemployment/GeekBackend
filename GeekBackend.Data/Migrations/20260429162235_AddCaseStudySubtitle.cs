using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeekBackend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCaseStudySubtitle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "subtitle",
                table: "case_studies",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "subtitle",
                table: "case_studies");
        }
    }
}
