using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentWriterV2
{
    /// <inheritdoc />
    public partial class InitialContentWriterV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "content_writer_v2");

            migrationBuilder.CreateTable(
                name: "blobs",
                schema: "content_writer_v2",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    collection = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    data = table.Column<string>(type: "jsonb", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_blobs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_blobs_collection_item_id",
                schema: "content_writer_v2",
                table: "blobs",
                columns: new[] { "collection", "item_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "blobs",
                schema: "content_writer_v2");
        }
    }
}
