using System;
using GeekRepository.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentCreatorV2;

[DbContext(typeof(ContentCreatorV2DbContext))]
[Migration("20260824210000_AddGuardrailRules")]
public partial class AddGuardrailRules : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "gcc_v2_guardrail_rules",
            schema: "content_creator_v2",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Pattern = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                Action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "strip"),
                ReplaceWith = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                Enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                Scope = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                ReasonCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_gcc_v2_guardrail_rules", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_gcc_v2_guardrail_rules_enabled",
            schema: "content_creator_v2",
            table: "gcc_v2_guardrail_rules",
            column: "Enabled");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "gcc_v2_guardrail_rules", schema: "content_creator_v2");
    }
}
