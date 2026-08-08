using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using GeekRepository.Data;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentCreator
{
    [DbContext(typeof(ContentCreatorDbContext))]
    [Migration("20260808_AddGccClient")]
    public partial class AddGccClient : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "gcc_clients",
                schema: "content_creator",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gcc_clients", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_gcc_clients_name_unique",
                schema: "content_creator",
                table: "gcc_clients",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "gcc_clients",
                schema: "content_creator");
        }
    }
}
