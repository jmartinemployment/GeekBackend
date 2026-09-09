using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentCreatorV2
{
    /// <inheritdoc />
    public partial class AddGrids : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "gcc_v2_grids",
                schema: "content_creator_v2",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Description = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ConfigJson = table.Column<string>(type: "text", nullable: false, defaultValue: "{}"),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gcc_v2_grids", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "gcc_v2_grid_rows",
                schema: "content_creator_v2",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GridId = table.Column<Guid>(type: "uuid", nullable: false),
                    RowIndex = table.Column<int>(type: "integer", nullable: false),
                    InputJson = table.Column<string>(type: "text", nullable: false, defaultValue: "{}"),
                    OutputJson = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Error = table.Column<string>(type: "text", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gcc_v2_grid_rows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_gcc_v2_grid_rows_gcc_v2_grids_GridId",
                        column: x => x.GridId,
                        principalSchema: "content_creator_v2",
                        principalTable: "gcc_v2_grids",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "gcc_v2_grid_runs",
                schema: "content_creator_v2",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GridId = table.Column<Guid>(type: "uuid", nullable: false),
                    Mode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SampleSize = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ActorUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    BudgetPreviewJson = table.Column<string>(type: "text", nullable: false, defaultValue: "{}"),
                    HistoryJson = table.Column<string>(type: "text", nullable: false, defaultValue: "[]"),
                    OutputCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gcc_v2_grid_runs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_gcc_v2_grid_runs_gcc_v2_grids_GridId",
                        column: x => x.GridId,
                        principalSchema: "content_creator_v2",
                        principalTable: "gcc_v2_grids",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_gcc_v2_grid_rows_grid_row_index",
                schema: "content_creator_v2",
                table: "gcc_v2_grid_rows",
                columns: new[] { "GridId", "RowIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_gcc_v2_grid_runs_grid_started",
                schema: "content_creator_v2",
                table: "gcc_v2_grid_runs",
                columns: new[] { "GridId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "ix_gcc_v2_grids_owner_updated",
                schema: "content_creator_v2",
                table: "gcc_v2_grids",
                columns: new[] { "OwnerUserId", "UpdatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "gcc_v2_grid_rows",
                schema: "content_creator_v2");

            migrationBuilder.DropTable(
                name: "gcc_v2_grid_runs",
                schema: "content_creator_v2");

            migrationBuilder.DropTable(
                name: "gcc_v2_grids",
                schema: "content_creator_v2");
        }
    }
}
