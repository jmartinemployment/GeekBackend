using System;
using GeekRepository.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentCreatorV2;

[DbContext(typeof(ContentCreatorV2DbContext))]
[Migration("20260913180000_AddIngestionJobRevision")]
public partial class AddIngestionJobRevision : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "Revision",
            schema: "content_creator_v2",
            table: "gcc_v2_context_ingestion_jobs",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "Revision",
            schema: "content_creator_v2",
            table: "gcc_v2_context_ingestion_jobs");
    }
}
