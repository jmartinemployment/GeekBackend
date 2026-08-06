using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using GeekRepository.Data;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentCreator
{
    [DbContext(typeof(ContentCreatorDbContext))]
    [Migration("20260805220000_AddGccCreateDepartment")]
    public partial class AddGccCreateDepartment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Department",
                schema: "content_creator",
                table: "gcc_creates",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "marketing");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Department",
                schema: "content_creator",
                table: "gcc_creates");
        }
    }
}
