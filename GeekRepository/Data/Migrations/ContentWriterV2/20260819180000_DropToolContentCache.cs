using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentWriterV2
{
    /// <inheritdoc />
    public partial class DropToolContentCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tool_content_cache",
                schema: "content_writer_v2");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tool_content_cache",
                schema: "content_writer_v2",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    normalized_tool_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    display_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    overview_json = table.Column<string>(type: "jsonb", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tool_content_cache", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tool_content_cache_normalized_tool_name",
                schema: "content_writer_v2",
                table: "tool_content_cache",
                column: "normalized_tool_name",
                unique: true);
        }
    }
}
