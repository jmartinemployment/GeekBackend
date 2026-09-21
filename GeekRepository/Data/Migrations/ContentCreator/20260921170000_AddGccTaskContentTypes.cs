using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using GeekRepository.Data;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentCreator
{
    /// <summary>
    /// A task can be tagged with which v2 content types it relates to.
    /// </summary>
    /// <remarks>
    /// Optional, not enforced. No CHECK constraint ties the column to the frontend's twenty-value
    /// list — that list (src/lib/content-types.ts in content-creator-v2) is still settling, and a
    /// CHECK would mean a migration every time it changes. An empty array is a complete, ordinary
    /// task, not an incomplete one.
    /// </remarks>
    [DbContext(typeof(ContentCreatorDbContext))]
    [Migration("20260921170000_AddGccTaskContentTypes")]
    public partial class AddGccTaskContentTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<string>>(
                name: "content_types",
                schema: "content_creator",
                table: "gcc_tasks",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "content_types",
                schema: "content_creator",
                table: "gcc_tasks");
        }
    }
}
