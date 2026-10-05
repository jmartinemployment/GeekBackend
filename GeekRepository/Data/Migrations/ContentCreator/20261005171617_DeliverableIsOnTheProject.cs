using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentCreator
{
    /// <summary>
    /// A deliverable is a name and a due date on the project (Jeff, 2026-10-04: "Creates, it is
    /// project"), so gcc_deliverables.create_id and type stop being required. Existing rows keep theirs.
    /// </summary>
    /// <remarks>
    /// Down is deliberately empty. Making the columns required again would need a create and a type
    /// for every deliverable recorded without one, and there is none to give: EF's generated Down filled
    /// them with an all-zero id and an empty string, which is inventing data (and the id fails the key).
    /// </remarks>
    public partial class DeliverableIsOnTheProject : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "type",
                schema: "content_creator",
                table: "gcc_deliverables",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<Guid>(
                name: "create_id",
                schema: "content_creator",
                table: "gcc_deliverables",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
