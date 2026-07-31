using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentWriterV3
{
    /// <inheritdoc />
    public partial class AddWorkspaceOwnerId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "owner_id",
                schema: "content_writer_v3",
                table: "workspaces",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "ix_workspaces_owner_id",
                schema: "content_writer_v3",
                table: "workspaces",
                column: "owner_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_workspaces_owner_id",
                schema: "content_writer_v3",
                table: "workspaces");

            migrationBuilder.DropColumn(
                name: "owner_id",
                schema: "content_writer_v3",
                table: "workspaces");
        }
    }
}
