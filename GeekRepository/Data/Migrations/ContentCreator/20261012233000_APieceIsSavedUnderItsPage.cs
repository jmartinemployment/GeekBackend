using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentCreator
{
    /// <summary>
    /// A piece a Repurpose writes from a page -- an email, a social post, an ad -- is saved under that
    /// page, one of each type and name, and its run records which page it was written from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>gcc_generate_jobs.source_page_id</c>: null for a Generate, the page for a Repurpose. The two
    /// share the table because they share its rule, one run on a project at a time. No foreign key: the
    /// row is the record of a run and stays when the page is written again or deleted.
    /// </para>
    /// <para>
    /// The index is the rule "one piece of a type and name under a page", beside
    /// <c>ux_gcc_artifacts_one_page_per_type_and_name</c>, which is the same rule for a project's own
    /// pages. An expression index over lower(btrim(...)), which EF cannot model, so it lives here and
    /// nowhere in the snapshot. Nothing is deleted to make room for it: no code has ever saved a row
    /// with a parent (GeekAPI is the only caller of this service and has never sent one), so there is
    /// no row for it to refuse. If there were two, the index would not be created, the migration would
    /// roll back and this service would refuse to start rather than run with a rule its data breaks.
    /// </para>
    /// </remarks>
    public partial class APieceIsSavedUnderItsPage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "source_page_id",
                schema: "content_creator",
                table: "gcc_generate_jobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX ux_gcc_artifacts_one_piece_per_page_type_and_name
                ON content_creator.gcc_artifacts ("ParentArtifactId", lower(btrim("Type")), lower(btrim("Name")))
                WHERE "ParentArtifactId" IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS content_creator.ux_gcc_artifacts_one_piece_per_page_type_and_name;
                """);

            migrationBuilder.DropColumn(
                name: "source_page_id",
                schema: "content_creator",
                table: "gcc_generate_jobs");
        }
    }
}
